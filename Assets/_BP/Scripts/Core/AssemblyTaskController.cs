using System;
using System.Collections.Generic;
using BP.Input;
using BP.Logging;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BP.Core
{
    /// <summary>
    /// Řídí průběh sestavovací úlohy: přijímá požadavky na vytvoření objektu,
    /// vyhodnocuje umístění, posouvá krok a všechno loguje.
    ///
    /// Nezná rozdíl mezi menu a hlasem — pracuje výhradně přes
    /// IObjectRequestSource. Tím je zaručeno, že se obě podmínky chovají
    /// v celém zbytku pipeline identicky.
    /// </summary>
    public class AssemblyTaskController : MonoBehaviour
    {
        [Header("Závislosti")]
        [SerializeField] private ShapeSpawner spawner;
        [SerializeField] private PlacementValidator validator;

        [Tooltip("Zdroj požadavků. Přiřazuje TrialManager podle podmínky bloku.")]
        [SerializeField] private MonoBehaviour requestSourceBehaviour;

        [Header("Pravidla")]
        [Tooltip("Vynutit pořadí kroků podle čísel v šabloně. " +
                 "Zapnuto = čas na krok je mezi participanty srovnatelný.")]
        [SerializeField] private bool enforceStepOrder = true;

        [Tooltip("Kolik objektů smí být zároveň nezasazených. " +
                 "1 = participant musí umístit, než vytvoří další.")]
        [SerializeField] private int maxPendingObjects = 1;

        [Tooltip("Spustit úlohu hned po Play. Jen pro vývojové zkoušení — " +
                 "v měření spouští bloky TrialManager.")]
        [SerializeField] private bool autoStartOnPlay;

        /// <summary>Aktuální krok (0-based). Číslo zobrazené u šablony je +1.</summary>
        public int CurrentStep { get; private set; }

        public bool IsRunning { get; private set; }
        public bool IsComplete => CurrentStep >= StepCount;

        /// <summary>Počet kroků aktuální šablony. Čte ho souhrn bloku.</summary>
        public int StepCount => validator.Visualizer.Template.StepCount;

        /// <summary>Krok byl dokončen. Parametr je index dokončeného kroku.</summary>
        public event Action<int> StepCompleted;

        /// <summary>Celá struktura odpovídá šabloně.</summary>
        public event Action TaskCompleted;

        /// <summary>Participant vytvořil objekt, který k aktuálnímu kroku nepatří.</summary>
        public event Action<ShapeInstance> WrongObjectCreated;

        /// <summary>
        /// Požadavek byl odmítnut. Parametr je důvod k zobrazení.
        /// Bez tohoto eventu by CREATE mlčel a nebylo by poznat proč.
        /// </summary>
        public event Action<string> RequestBlocked;

        /// <summary>
        /// Objekt byl puštěn, ale neumístil se. Parametry: objekt, vzdálenost
        /// od cíle v metrech. Bez zpětné vazby by participant nevěděl, jestli
        /// byl těsně mimo, nebo úplně vedle.
        /// </summary>
        public event Action<ShapeInstance, float> PlacementRejected;

        private IObjectRequestSource _source;
        private TrialLogger _logger;

        // Zásobník v pořadí vytvoření — STEP BACK maže odzadu.
        private readonly List<ShapeInstance> _created = new List<ShapeInstance>();

        // Objekty, které ještě nebyly přijaty do struktury.
        private readonly List<ShapeInstance> _pending = new List<ShapeInstance>();

        public void Initialize(IObjectRequestSource source, TrialLogger logger)
        {
            Detach();

            _source = source;
            _logger = logger;

            if (_source != null)
            {
                _source.ObjectRequested += OnObjectRequested;
                _source.UndoRequested += OnUndoRequested;
            }
        }

        private void Awake()
        {
            // Zdroj lze nastavit i v inspektoru (pro rychlé zkoušení jedné podmínky).
            if (_source == null && requestSourceBehaviour is IObjectRequestSource s)
                Initialize(s, null);
        }

        private void Start()
        {
            if (autoStartOnPlay) StartTask();
        }

        private void OnDestroy() => Detach();

        private void Detach()
        {
            if (_source == null) return;
            _source.ObjectRequested -= OnObjectRequested;
            _source.UndoRequested -= OnUndoRequested;
            _source = null;
        }

        public void StartTask()
        {
            CurrentStep = 0;
            IsRunning = true;
            _created.Clear();
            _pending.Clear();

            if (_source != null) _source.SetInputEnabled(true);
            if (_logger != null) _logger.Log(LogEvent.TemplateShown, detail: validator.Visualizer.Template.templateId);
        }

        public void StopTask()
        {
            IsRunning = false;
            if (_source != null) _source.SetInputEnabled(false);
        }

        // ---- Požadavky ze vstupu ----

        private void OnObjectRequested(ObjectRequest request)
        {
            // Nespuštěný blok je nejčastější důvod, proč se "nic nedeje" —
            // musí se to říct, ne spolknout.
            if (!IsRunning)
            {
                if (RequestBlocked != null) RequestBlocked("NO BLOCK RUNNING");
                return;
            }

            if (IsComplete)
            {
                if (RequestBlocked != null) RequestBlocked("BLOCK COMPLETE");
                return;
            }

            if (_pending.Count >= maxPendingObjects)
            {
                // Záměrně se nic nevytvoří: jinak by participant mohl zaplavit
                // scénu objekty a data o čase na krok by přestala mít význam.
                if (_logger != null)
                    _logger.Log(LogEvent.Note, step: CurrentStep + 1,
                        detail: "pozadavek ignorovan - neumisteny objekt");

                if (RequestBlocked != null) RequestBlocked("PLACE THE OBJECT FIRST");
                return;
            }

            var instance = spawner.Spawn(request.Shape, request.Color, CurrentStep);
            if (instance == null) return;

            _created.Add(instance);
            _pending.Add(instance);

            if (_logger != null)
                _logger.Log(LogEvent.ObjectSpawned, CurrentStep + 1, request.Shape, request.Color);

            var step = validator.Visualizer.Template.GetStep(CurrentStep);
            if (!instance.Matches(step.shape, step.color))
            {
                if (_logger != null)
                    _logger.Log(LogEvent.WrongObjectCreated, CurrentStep + 1, request.Shape, request.Color,
                        detail: $"ocekavano {step.color} {step.shape}");

                if (WrongObjectCreated != null) WrongObjectCreated(instance);
            }

            HookRelease(instance);
        }

        /// <summary>
        /// Objekt, který je vytvořený ale ještě neumístěný.
        /// Používá ho zpětná vazba, aby uměla ukázat, kdy je dost blízko cíle.
        /// </summary>
        public ShapeInstance PendingObject
            => _pending.Count > 0 ? _pending[_pending.Count - 1] : null;

        private void OnUndoRequested()
        {
            if (!IsRunning) return;

            if (_created.Count == 0)
            {
                if (RequestBlocked != null) RequestBlocked("NOTHING TO UNDO");
                return;
            }

            var last = _created[_created.Count - 1];
            _created.RemoveAt(_created.Count - 1);
            _pending.Remove(last);

            // Když se maže už zasazený objekt, krok se musí vrátit,
            // jinak by se struktura a stav rozešly.
            if (last.IsConfirmed && CurrentStep > 0) CurrentStep--;

            if (_logger != null)
                _logger.Log(LogEvent.UndoUsed, CurrentStep + 1, last.Shape, last.Color);

            UnhookRelease(last);
            spawner.Remove(last);
        }

        // ---- Vyhodnocení umístění ----

        private void HookRelease(ShapeInstance instance)
        {
            var grab = instance.GetComponent<XRGrabInteractable>();
            if (grab == null)
            {
                Debug.LogError($"[AssemblyTaskController] {instance.name} nemá XRGrabInteractable.", instance);
                return;
            }

            grab.selectExited.AddListener(OnReleased);
        }

        private void UnhookRelease(ShapeInstance instance)
        {
            if (instance == null) return;
            var grab = instance.GetComponent<XRGrabInteractable>();
            if (grab != null) grab.selectExited.RemoveListener(OnReleased);
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (!IsRunning || IsComplete) return;

            var instance = args.interactableObject.transform.GetComponent<ShapeInstance>();
            if (instance == null || instance.IsConfirmed) return;

            var result = validator.Evaluate(instance, CurrentStep);

            if (_logger != null)
                _logger.Log(LogEvent.ObjectPlaced, CurrentStep + 1, instance.Shape, instance.Color,
                    result.PositionError, result.RotationError,
                    detail: result.IsAccepted ? "prijato" : (result.IsCorrectObject ? "mimo toleranci" : "spatny objekt"));

            if (!result.IsAccepted)
            {
                if (PlacementRejected != null) PlacementRejected(instance, result.PositionError);
                return;
            }

            _pending.Remove(instance);
            UnhookRelease(instance);

            // Zasazený objekt se zafixuje, aby se struktura nedala rozebrat
            // uchopením. Odebrat ho lze dál, ale jen přes STEP BACK.
            instance.SetFixed(true);

            // Póza se vynutí AŽ PO zafixování. Snap ve validátoru sám nestačí —
            // XRI si drží vlastní cílovou pózu a dokázalo objekt posunout zpět
            // na staging point. Tohle je poslední slovo o výsledné poloze.
            instance.ForcePose(
                validator.Visualizer.GetTargetPosition(CurrentStep),
                validator.Visualizer.GetTargetRotation(CurrentStep));

            var finished = CurrentStep;
            if (_logger != null)
                _logger.Log(LogEvent.StepCompleted, finished + 1, instance.Shape, instance.Color,
                    reactionTime: Time.realtimeSinceStartup - instance.SpawnTime);

            // Krok se posune PŘED vyvoláním eventu. Obráceně by posluchač
            // viděl ještě starý CurrentStep a překreslil by zvýraznění na
            // právě dokončený krok — správný stav by naskočil až o frame později.
            if (enforceStepOrder) CurrentStep++;

            if (StepCompleted != null) StepCompleted(finished);

            if (IsComplete)
            {
                StopTask();
                if (TaskCompleted != null) TaskCompleted();
            }
        }
    }
}
