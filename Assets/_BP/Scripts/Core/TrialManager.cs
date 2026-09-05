using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BP.Input;
using BP.Logging;
using BP.Secondary;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BP.Core
{
    /// <summary>
    /// Definice jednoho bloku měření.
    /// </summary>
    [Serializable]
    public struct BlockDefinition
    {
        public InteractionCondition condition;
        public LoadCondition load;
        public AssemblyTemplate template;

        [Tooltip("Tréninkový blok se do analýzy nezahrnuje, ale loguje se taky.")]
        public bool isTraining;

        public override string ToString()
            => $"{condition}/{load}/{(template != null ? template.templateId : "?")}"
               + (isTraining ? " (trenink)" : "");
    }

    /// <summary>
    /// Výsledek jednoho odehraného bloku. Drží se v paměti po celou session,
    /// aby šla na konci ukázat souhrnná tabulka.
    ///
    /// SLOŽKY ČASU ZŮSTÁVAJÍ ODDĚLENÉ i tady. Postihy za minuté terče jsou
    /// zpětná vazba pro participanta, ne měřená veličina — kdyby se do
    /// completion time přičetly, byl by hlavní výsledek funkcí sekundární
    /// úlohy a rozdíl mezi podmínkami by se nedal interpretovat.
    /// </summary>
    public struct BlockResult
    {
        public int index;
        public InteractionCondition condition;
        public LoadCondition load;
        public string templateId;
        public bool isTraining;
        public string reason;

        public float rawTime;
        public int penaltyCount;
        public float penaltyTime;

        public int stepsDone;
        public int stepsTotal;
        public int wrongObjects;
        public int rejectedPlacements;

        public bool secondaryRan;
        public int activations;
        public int hits;
        public float hitRate;
        public float meanReactionTime;
    }

    /// <summary>
    /// Výsledek celé session jednoho participanta — jeden řádek žebříčku.
    ///
    /// ŘADÍ SE PODLE CELKOVÉHO ČASU VČETNĚ POSTIHŮ, ne podle čistého.
    /// Čistý čas je měřená veličina, ale jako skóre by odměňoval toho, kdo
    /// terče ignoroval: minutý terč by ho nic nestál. Celkový čas je přesně
    /// to, co participant během bloku viděl na časomíře.
    /// </summary>
    public struct SessionScore
    {
        public string participantId;
        public string soubor;
        public string datum;

        public float cleanTime;
        public float penaltyTime;
        public int penaltyCount;
        public float TotalTime => cleanTime + penaltyTime;

        public int measuredBlocks;
        public bool complete;

        public int activations;
        public int hits;
        public float HitRate => activations > 0 ? (float)hits / activations : 0f;
    }

    /// <summary>
    /// Řídí celou session: pořadí bloků, counterbalancing, spouštění
    /// a zastavování obou úloh, logování a zamykání menu.
    ///
    /// COUNTERBALANCING: pořadí podmínek se mezi participanty střídá
    /// (sudá skupina začíná menu, nepálná hlasem), aby se efekt učení
    /// a únavy rozložil rovnoměrně na obě podmínky. Zároveň se rotuje
    /// přiřazení šablon, aby žádná podmínka nedostávala systematicky
    /// tu samou strukturu.
    ///
    /// V rámci podmínky jde vždy SingleTask před DualTask — baseline musí
    /// být naměřená bez zátěže, aby se z ní dal spočítat dual-task cost.
    /// Pořadí je stejné v obou podmínkách, takže se případný efekt učení
    /// mezi nimi vyruší.
    /// </summary>
    public class TrialManager : MonoBehaviour
    {
        [Header("Participant")]
        [SerializeField] private string participantId = "P01";

        [Tooltip("Skupina counterbalancingu. Sudá = menu první, nepálná = hlas první.")]
        [SerializeField] private int counterbalanceGroup;

        [Header("Bloky")]
        [Tooltip("Bloky v základním pořadí. Counterbalancing je přeskládá podle skupiny.")]
        [SerializeField] private List<BlockDefinition> blocks = new List<BlockDefinition>();

        [Header("Úlohy")]
        [SerializeField] private AssemblyTaskController task;
        [SerializeField] private SecondaryTaskManager secondaryTask;

        [Tooltip("Časomíra bloku. Drží čistý čas a postihy odděleně.")]
        [SerializeField] private BlockTimer timer;
        [Tooltip("Stavební vodítko uprostřed — drátový model aktuálního kroku.")]
        [SerializeField] private TemplateVisualizer templateVisualizer;

        [Tooltip("Předloha vedle stavební plochy — plný barevný model celé struktury. " +
                 "Musí se přepínat spolu s vodítkem, jinak v dalším bloku zůstane " +
                 "viset struktura z bloku předchozího.")]
        [SerializeField] private TemplateVisualizer referenceVisualizer;
        [SerializeField] private ShapeSpawner spawner;

        [Header("Vstupní zdroje podmínek")]
        [SerializeField] private MenuRequestSource menuSource;

        [Tooltip("Zdroj pro hlasovou podmínku. Zatím neimplementován.")]
        [SerializeField] private MonoBehaviour voiceSource;

        [Header("Kalibrace")]
        [Tooltip("Zámek pozice menu. Během bloku se zamkne, mezi bloky odemkne.")]
        [SerializeField] private MenuDragHandle menuLock;

        [Tooltip("Zamykat menu v měřených blocích. Při MĚŘENÍ nechat zapnuté — " +
                 "posouvání během bloku mění vzdálenost ruky k menu a ta se " +
                 "propisuje do completion time. Pro zkoušení lze vypnout.")]
        [SerializeField] private bool lockMenuDuringMeasuredBlocks = true;

        [Header("Zobrazení")]
        [Tooltip("Objekty, které jsou skryté, dokud nezačne první blok — " +
                 "aby participant na úvodní obrazovce viděl jen instrukci " +
                 "a nerozptylovalo ho pracoviště.")]
        [SerializeField] private GameObject[] hiddenUntilStart = System.Array.Empty<GameObject>();

        [Header("Ovládání operátorem")]
        [Tooltip("N = další blok, K = ukončit, R = zopakovat, " +
                 "L = zamknout/odemknout menu, C = přecentrovat pracoviště.")]
        [SerializeField] private bool keyboardControl = true;

        [Tooltip("Pracoviště. Umožní ho přecentrovat před participanta klávesou C.")]
        [SerializeField] private WorkspaceLayout workspace;

        [Tooltip("Rozmístění periferních terčů. Po přecentrování se terče " +
                 "rozloží znovu — excentricita se počítá od skutečné výšky " +
                 "očí, a ta je u každého participanta jiná.")]
        [SerializeField] private SecondaryTargetLayout targetLayout;

        [Tooltip("Ovládat i tlačítky na ovladačích. Nutné, když testuješ sám " +
                 "v headsetu — na klávesnici se nedosáhne. Při měření " +
                 "s participantem VYPNOUT, aby si blok nespustil sám.")]
        [SerializeField] private bool controllerControl = true;

        [Tooltip("Spustit sekundární úlohu v KAŽDÉM bloku, i v single-task. " +
                 "Jen pro zkoušení — nevyžaduje mačkání tlačítek. " +
                 "Při měření VYPNOUT, jinak zmizí baseline bez zátěže " +
                 "a nepůjde spočítat dual-task cost.")]
        [SerializeField] private bool runSecondaryInAllBlocks;

        [Tooltip("Spustit první blok hned po Play. Pro zkoušení pohodlné; " +
                 "při měření s participantem vypnout, aby blok začal až na pokyn.")]
        [SerializeField] private bool autoStartFirstBlock = true;

        /// <summary>Index aktuálního bloku v přeskládaném pořadí. -1 = session neběží.</summary>
        public int CurrentBlockIndex { get; private set; } = -1;

        public bool BlockRunning { get; private set; }
        public bool SessionComplete => CurrentBlockIndex >= _order.Count;

        public event Action<BlockDefinition, int> BlockStarted;
        public event Action<BlockDefinition, int> BlockEnded;
        public event Action SessionEnded;

        /// <summary>Odehrané bloky v pořadí, v jakém proběhly.</summary>
        public IReadOnlyList<BlockResult> Results => _results;

        private readonly List<BlockDefinition> _order = new List<BlockDefinition>();
        private readonly List<BlockResult> _results = new List<BlockResult>();
        private TrialLogger _logger;

        // Chyby se počítají tady, ne v úloze — úloha je bez stavu mezi bloky
        // a čítač v ní by se musel resetovat zvenčí, což je snadné zapomenout.
        private int _wrongObjects;
        private int _rejectedPlacements;

        // Operátorské akce na ovladačích. A/B/X/Y nejsou použité ani k uchopení
        // (to je trigger a grip), ani k obsluze menu (to je poke a ray),
        // takže se nemohou plést do samotné úlohy.
        private InputAction _nextBlockAction;
        private InputAction _recenterAction;
        private InputAction _triggerTargetAction;
        private InputAction _toggleSecondaryAction;

        private void Awake()
        {
            BuildOrder();
        }

        private void OnEnable()
        {
            if (!controllerControl) return;

            _recenterAction = Bind("BP_Recenter", "<XRController>{LeftHand}/primaryButton",
                RecenterWorkspace);
            _nextBlockAction = Bind("BP_NextBlock", "<XRController>{LeftHand}/secondaryButton",
                StartNextBlock);
            _triggerTargetAction = Bind("BP_Target", "<XRController>{RightHand}/primaryButton",
                TriggerSecondaryTargetForTesting);
            _toggleSecondaryAction = Bind("BP_Secondary", "<XRController>{RightHand}/secondaryButton",
                ToggleSecondaryTaskForTesting);
        }

        private void OnDisable()
        {
            Unbind(ref _recenterAction);
            Unbind(ref _nextBlockAction);
            Unbind(ref _triggerTargetAction);
            Unbind(ref _toggleSecondaryAction);
        }

        private static InputAction Bind(string name, string path, Action callback)
        {
            var action = new InputAction(name, InputActionType.Button, path);
            action.performed += _ => callback();
            action.Enable();
            return action;
        }

        private static void Unbind(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }

        private void Start()
        {
            // Postih za minutý terč. Přičítá se jen k zobrazenému času —
            // do čistého času nevstupuje.
            if (secondaryTask != null && timer != null)
                secondaryTask.TargetMissed += _ => timer.AddPenalty();

            // Na úvodní obrazovce je vidět jen instrukce.
            SetWorkspaceVisible(false);

            if (autoStartFirstBlock) StartSession();
        }

        private void OnDestroy()
        {
            if (_logger != null) _logger.Dispose();
        }

        private void Update()
        {
            if (!keyboardControl) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.nKey.wasPressedThisFrame) StartNextBlock();
            if (kb.kKey.wasPressedThisFrame) EndCurrentBlock("ukonceno operatorem");
            if (kb.rKey.wasPressedThisFrame) RepeatCurrentBlock();

            // Ruční override zámku menu — pro ladění a pro případ, kdy si
            // participant potřebuje menu posunout i mimo tréninkový blok.
            if (kb.lKey.wasPressedThisFrame) ToggleMenuLock();
            if (kb.cKey.wasPressedThisFrame) RecenterWorkspace();

            // Zkoušení sekundární úlohy mimo dual-task blok.
            if (kb.tKey.wasPressedThisFrame) TriggerSecondaryTargetForTesting();
            if (kb.sKey.wasPressedThisFrame) ToggleSecondaryTaskForTesting();
        }

        // ---- Pořadí bloků ----

        /// <summary>
        /// Přeskládá bloky podle skupiny counterbalancingu.
        /// Zachovává pravidlo SingleTask → DualTask v rámci každé podmínky.
        /// </summary>
        public void BuildOrder()
        {
            _order.Clear();
            if (blocks.Count == 0) return;

            var menuBlocks = new List<BlockDefinition>();
            var voiceBlocks = new List<BlockDefinition>();

            foreach (var b in blocks)
            {
                if (b.condition == InteractionCondition.Menu) menuBlocks.Add(b);
                else voiceBlocks.Add(b);
            }

            SortSingleBeforeDual(menuBlocks);
            SortSingleBeforeDual(voiceBlocks);

            var menuFirst = counterbalanceGroup % 2 == 0;

            if (menuFirst)
            {
                _order.AddRange(menuBlocks);
                _order.AddRange(voiceBlocks);
            }
            else
            {
                _order.AddRange(voiceBlocks);
                _order.AddRange(menuBlocks);
            }
        }

        private static void SortSingleBeforeDual(List<BlockDefinition> list)
        {
            list.Sort((a, b) =>
            {
                // Trénink vždy první, pak SingleTask, pak DualTask.
                var ka = (a.isTraining ? 0 : 1) * 10 + (int)a.load;
                var kb = (b.isTraining ? 0 : 1) * 10 + (int)b.load;
                return ka.CompareTo(kb);
            });
        }

        public IReadOnlyList<BlockDefinition> Order => _order;

        // ---- Průběh ----

        /// <summary>
        /// Zobrazí nebo skryje pracoviště. Na úvodní obrazovce má být vidět
        /// jen instrukce — šablona, menu a terče by od ní odváděly pozornost
        /// a participant by začal zkoušet dřív, než si ji přečte.
        /// </summary>
        public void SetWorkspaceVisible(bool visible)
        {
            foreach (var go in hiddenUntilStart)
                if (go != null) go.SetActive(visible);
        }

        /// <summary>Okamžitě rozsvítí jeden terč. Jen pro zkoušení.</summary>
        public void TriggerSecondaryTargetForTesting()
        {
            if (secondaryTask == null)
            {
                Debug.LogWarning("[TrialManager] Není přiřazený SecondaryTaskManager.");
                return;
            }

            secondaryTask.ActivateNowForTesting();
            Debug.Log("[TrialManager] Terč aktivován (test).");
        }

        /// <summary>Zapne/vypne sekundární úlohu bez ohledu na blok. Jen pro zkoušení.</summary>
        public void ToggleSecondaryTaskForTesting()
        {
            if (secondaryTask == null) return;

            secondaryTask.ToggleForTesting(_logger);
            Debug.Log("[TrialManager] Sekundární úloha "
                      + (secondaryTask.IsRunning ? "spuštěna" : "zastavena") + " (test).");
        }

        /// <summary>
        /// Přesune pracoviště před participanta. Automatické přecentrování
        /// při startu proběhne dřív, než si participant stihne stoupnout —
        /// tohle je oprava, kterou udělá operátor, až je na místě.
        ///
        /// Během běžícího bloku se to zapíše do logu: mění se geometrie
        /// dosahu, a ta ovlivňuje completion time.
        /// </summary>
        public void RecenterWorkspace()
        {
            if (workspace == null)
            {
                Debug.LogWarning("[TrialManager] Není přiřazený WorkspaceLayout.");
                return;
            }

            workspace.RecenterToHead();

            // Terče se rozloží až po přecentrování. Kdyby zůstaly z původní
            // pozice, měl by každý jinou excentricitu, než jakou návrh
            // předpokládá — a to je právě proměnná, kterou držíme konstantní.
            if (targetLayout != null) targetLayout.Apply();

            if (BlockRunning && _logger != null)
                _logger.Log(LogEvent.Note, detail: "pracoviste precentrovano behem bloku");

            Debug.Log("[TrialManager] Pracoviště přecentrováno.");
        }

        /// <summary>
        /// Přepne zámek pozice menu. Pokud se odemkne během měřeného bloku,
        /// zapíše se to do logu — jinak by v datech zůstala nezaznamenaná
        /// změna geometrie, která ovlivňuje completion time.
        /// </summary>
        public void ToggleMenuLock()
        {
            if (menuLock == null) return;

            var newState = !menuLock.IsLocked;
            menuLock.SetLocked(newState);

            if (BlockRunning && _logger != null)
                _logger.Log(LogEvent.Note,
                    detail: "menu " + (newState ? "zamceno" : "ODEMCENO") + " behem bloku");

            Debug.Log("[TrialManager] Menu " + (newState ? "zamceno" : "odemceno"));
        }

        public void StartSession()
        {
            BuildOrder();
            _results.Clear();
            CurrentBlockIndex = -1;
            StartNextBlock();
        }

        public void StartNextBlock()
        {
            if (BlockRunning)
            {
                Debug.LogWarning("[TrialManager] Blok už běží. Nejdřív ho ukonči (K).");
                return;
            }

            // Pořadí je v neserializovaném seznamu. Rekompilace za běhu (domain
            // reload) ho vyprázdní, aniž by se znovu spustil Awake — bez tohoto
            // by session tiše skončila jako "dokončená".
            if (_order.Count == 0) BuildOrder();

            CurrentBlockIndex++;

            if (CurrentBlockIndex >= _order.Count)
            {
                var path = WriteResultsFile();
                Debug.Log("[TrialManager] Session dokončena." + Environment.NewLine
                          + BuildResultsTable()
                          + (path != null ? Environment.NewLine + "souhrn: " + path : ""));

                if (SessionEnded != null) SessionEnded();
                return;
            }

            RunBlock(_order[CurrentBlockIndex]);
        }

        public void RepeatCurrentBlock()
        {
            if (_order.Count == 0) BuildOrder();

            if (CurrentBlockIndex < 0 || CurrentBlockIndex >= _order.Count)
            {
                Debug.LogWarning("[TrialManager] Není co zopakovat — session nezačala.");
                return;
            }

            if (BlockRunning) EndCurrentBlock("zopakovano");
            RunBlock(_order[CurrentBlockIndex]);
        }

        private void RunBlock(BlockDefinition block)
        {
            if (block.template == null)
            {
                Debug.LogError($"[TrialManager] Blok {block} nemá šablonu.");
                return;
            }

            if (block.condition == InteractionCondition.Voice && voiceSource == null)
            {
                Debug.LogError("[TrialManager] Hlasová podmínka není implementovaná — blok přeskočen.");
                return;
            }

            ClearScene();

            // Terče se rozloží na začátku každého bloku. Participant se mezi
            // bloky posune nebo si sedne a excentricita, kterou návrh drží
            // konstantní, by tím přestala platit.
            if (targetLayout != null) targetLayout.Apply();

            // Šablona bloku — vodítko i předloha. Předloha se přepisovala
            // dřív jen v editoru, takže od druhého bloku ukazovala jinou
            // strukturu, než se měla stavět.
            templateVisualizer.SetTemplate(block.template);
            if (referenceVisualizer != null) referenceVisualizer.SetTemplate(block.template);

            // Log
            if (_logger != null) _logger.Dispose();
            _logger = new TrialLogger();
            _logger.OpenBlock(participantId, block.condition, block.load,
                block.template.templateId, CurrentBlockIndex);

            // Vstupní zdroj podle podmínky
            var source = block.condition == InteractionCondition.Menu
                ? (IObjectRequestSource)menuSource
                : voiceSource as IObjectRequestSource;

            task.Initialize(source, _logger);
            task.TaskCompleted += OnTaskCompleted;

            _wrongObjects = 0;
            _rejectedPlacements = 0;
            task.WrongObjectCreated += OnWrongObject;
            task.PlacementRejected += OnPlacementRejected;

            // V TRÉNINKU zůstává menu odemčené — tam si ho participant narovná
            // do pohodlné pozice (kalibrace). V MĚŘENÉM bloku se zamkne, aby
            // byla vzdálenost ruky k menu po celou dobu měření konstantní.
            if (menuLock != null)
                menuLock.SetLocked(lockMenuDuringMeasuredBlocks && !block.isTraining);

            // Sekundární úloha jen v dual-task bloku. Seed z participanta a bloku,
            // aby stejný participant dostal v obou podmínkách stejnou sekvenci.
            var runSecondary = block.load == LoadCondition.DualTask || runSecondaryInAllBlocks;
            if (runSecondary && secondaryTask != null)
            {
                secondaryTask.StartTask(_logger, ComputeSeed(block));

                if (runSecondaryInAllBlocks && block.load != LoadCondition.DualTask)
                    _logger.Log(LogEvent.Note,
                        detail: "POZOR: sekundarni uloha bezi i v single-task bloku (vyvojove nastaveni)");
            }

            SetWorkspaceVisible(true);

            if (timer != null) timer.StartBlock();

            task.StartTask();
            BlockRunning = true;

            Debug.Log($"[TrialManager] Blok {CurrentBlockIndex + 1}/{_order.Count}: {block}");
            if (BlockStarted != null) BlockStarted(block, CurrentBlockIndex);
        }

        private void OnTaskCompleted() => EndCurrentBlock("uloha dokoncena");

        private void OnWrongObject(ShapeInstance instance) => _wrongObjects++;

        private void OnPlacementRejected(ShapeInstance instance, float error) => _rejectedPlacements++;

        public void EndCurrentBlock(string reason)
        {
            if (!BlockRunning) return;

            BlockRunning = false;
            task.TaskCompleted -= OnTaskCompleted;
            task.WrongObjectCreated -= OnWrongObject;
            task.PlacementRejected -= OnPlacementRejected;

            // Kroky se čtou PŘED StopTask() — ta stav úlohy resetuje a
            // z dokončeného bloku by pak zbylo 0/8.
            var stepsDone = task.CurrentStep;
            var stepsTotal = task.StepCount;

            task.StopTask();

            var summary = reason;

            // Čistý čas a postihy jdou do logu ODDĚLENĚ. Completion time je
            // hlavní závislá proměnná — kdyby v něm byly postihy schované,
            // nešlo by rozlišit pomalejší stavbu od většího počtu minutí.
            if (timer != null)
            {
                timer.StopBlock();
                summary += " | " + timer.GetSummary();
            }

            var secondaryRan = secondaryTask != null && secondaryTask.IsRunning;
            if (secondaryRan)
            {
                summary += " | " + secondaryTask.GetSummary();
                secondaryTask.StopTask();
            }

            if (_logger != null) _logger.CloseBlock(summary);

            // Mezi bloky se menu odemkne, aby si ho participant mohl narovnat.
            if (menuLock != null) menuLock.SetLocked(false);

            var block = CurrentBlockIndex >= 0 && CurrentBlockIndex < _order.Count
                ? _order[CurrentBlockIndex]
                : default(BlockDefinition);

            // Během bloku je vidět pracoviště, mimo blok jen panel.
            // Kdyby zůstalo pracoviště za shrnutím, čte se špatně obojí.
            //
            // Postavená struktura se musí UKLIDIT, ne jen schovat: objekty
            // vznikají za běhu, takže je nemá co zhasnout — v seznamu
            // schovávaných je jen jejich kontejner a ten sám o sobě zůstane
            // plný. Bez tohohle zůstala stavba viset za tabulkou výsledků.
            if (spawner != null) spawner.RemoveAll();
            SetWorkspaceVisible(false);

            _results.Add(new BlockResult
            {
                index = CurrentBlockIndex,
                condition = block.condition,
                load = block.load,
                templateId = block.template != null ? block.template.templateId : "?",
                isTraining = block.isTraining,
                reason = reason,

                rawTime = timer != null ? timer.RawTime : 0f,
                penaltyCount = timer != null ? timer.PenaltyCount : 0,
                penaltyTime = timer != null ? timer.PenaltyTime : 0f,

                stepsDone = stepsDone,
                stepsTotal = stepsTotal,
                wrongObjects = _wrongObjects,
                rejectedPlacements = _rejectedPlacements,

                secondaryRan = secondaryRan,
                activations = secondaryTask != null ? secondaryTask.Activations : 0,
                hits = secondaryTask != null ? secondaryTask.Hits : 0,
                hitRate = secondaryTask != null ? secondaryTask.HitRate : 0f,
                meanReactionTime = secondaryTask != null ? secondaryTask.MeanReactionTime : 0f
            });

            Debug.Log($"[TrialManager] Blok ukončen: {summary}");
            if (BlockEnded != null) BlockEnded(block, CurrentBlockIndex);
        }

        /// <summary>
        /// Seed sekvence terčů. Závisí na participantovi a úrovni zátěže,
        /// NE na podmínce — díky tomu dostane stejný participant v menu
        /// i v hlasové podmínce identické pořadí terčů.
        /// </summary>
        private int ComputeSeed(BlockDefinition block)
        {
            unchecked
            {
                var h = participantId != null ? participantId.GetHashCode() : 0;
                return h * 31 + (int)block.load;
            }
        }

        private void ClearScene()
        {
            if (spawner != null) spawner.RemoveAll();
            if (secondaryTask != null) secondaryTask.StopTask();
        }

        /// <summary>Cesta k logu aktuálního bloku — pro kontrolu, že se data píšou.</summary>
        public string CurrentLogPath => _logger != null ? _logger.FilePath : null;

        /// <summary>Cesta k souhrnnému souboru poslední session, nebo null.</summary>
        public string ResultsPath { get; private set; }

        // ---- Souhrn session ----

        /// <summary>
        /// Tabulka výsledků pro zobrazení v headsetu. Sloupce se zarovnávají
        /// mezerami a text se sází neproporcionálně (TMP tag mspace) — jinak
        /// se sloupce rozjedou a tabulka přestane být čitelná.
        /// </summary>
        public string BuildResultsTable()
        {
            if (_results.Count == 0) return "Žádné odehrané bloky.";

            var sb = new StringBuilder();

            sb.Append(Pad("#", 5)).Append(Pad("Podmínka", 17)).Append(Pad("Čas", 11))
              .Append(Pad("Chyby", 8)).Append(Pad("Terče", 9)).Append("RT")
              .AppendLine();

            sb.AppendLine(new string('-', 56));

            foreach (var r in _results)
            {
                // Trénink je označený přímo ve sloupci podmínky. Dřív ho značila
                // hvězdička u čísla, ale tu bylo nutné vysvětlit pod tabulkou —
                // a tabulka má stát bez doprovodného textu.
                var podminka = r.condition == InteractionCondition.Menu ? "Menu" : "Hlas";
                if (r.isTraining) podminka += " (trénink)";

                sb.Append(Pad((r.index + 1).ToString(), 5))
                  .Append(Pad(podminka, 17))
                  .Append(Pad(BlockTimer.Format(r.rawTime), 11))
                  .Append(Pad(r.wrongObjects.ToString(), 8))
                  .Append(Pad(r.secondaryRan ? r.hits + "/" + r.activations : "—", 9))
                  .Append(r.secondaryRan && r.hits > 0
                      ? r.meanReactionTime.ToString("F2") + "s" : "—")
                  .AppendLine();
            }

            return sb.ToString();
        }

        // ---- Zebricek napric ulozenymi session ----

        /// <summary>
        /// Precte souhrnne soubory vsech drivejsich session a slozi z nich
        /// zebricek. Cte se z disku, ne z pameti — jinak by zebricek existoval
        /// jen do zavreni aplikace a po restartu headsetu by byl prazdny.
        /// </summary>
        public List<SessionScore> ReadRanking()
        {
            var vysledek = new List<SessionScore>();

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "BP_Data");
                if (!Directory.Exists(dir)) return vysledek;

                foreach (var soubor in Directory.GetFiles(dir, "*_souhrn_*.csv"))
                {
                    var skore = ParseSummary(soubor);
                    if (skore.measuredBlocks > 0) vysledek.Add(skore);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[TrialManager] Žebříček se nepodařilo načíst: " + e.Message);
                return vysledek;
            }

            // Z kazdeho participanta jen jeho nejlepsi session — jinak by
            // ten, kdo prisel dvakrat, obsadil zebricek sam.
            var nejlepsi = new Dictionary<string, SessionScore>();
            foreach (var s in vysledek)
            {
                var klic = s.participantId ?? "?";
                SessionScore drivejsi;
                if (!nejlepsi.TryGetValue(klic, out drivejsi) || s.TotalTime < drivejsi.TotalTime)
                    nejlepsi[klic] = s;
            }

            // Dokoncene napred. Nedokoncena session ma kratsi cas prave proto,
            // ze nebyla dokoncena, a jinak by se vyhoupla na spicku zebricku.
            var seznam = new List<SessionScore>(nejlepsi.Values);
            seznam.Sort((a, b) =>
            {
                if (a.complete != b.complete) return a.complete ? -1 : 1;
                return a.TotalTime.CompareTo(b.TotalTime);
            });
            return seznam;
        }

        private static SessionScore ParseSummary(string cesta)
        {
            var s = new SessionScore { soubor = Path.GetFileName(cesta), complete = true };

            foreach (var radek in File.ReadAllLines(cesta))
            {
                if (radek.Length == 0) continue;

                if (radek[0] == '#')
                {
                    var c = radek.Split(';');
                    if (c.Length >= 2 && c[0].Contains("participant")) s.participantId = c[1].Trim();
                    else if (c.Length >= 2 && c[0].Contains("ukonceno")) s.datum = c[1].Trim();
                    continue;
                }

                var p = radek.Split(';');
                if (p.Length < 17 || p[0] == "blok") continue;   // hlavicka
                if (p[1] == "1") continue;                        // trenink se nepocita

                s.cleanTime += Cislo(p[5]);
                s.penaltyCount += (int)Cislo(p[6]);
                s.penaltyTime += Cislo(p[7]);
                s.activations += (int)Cislo(p[13]);
                s.hits += (int)Cislo(p[14]);
                s.measuredBlocks++;

                // Nedokoncena stavba by dala kratky cas a vyhrala by neopravnene.
                if ((int)Cislo(p[8]) < (int)Cislo(p[9])) s.complete = false;
            }

            return s;
        }

        private static float Cislo(string text)
        {
            float v;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        /// <summary>
        /// Tabulka zebricku. Nedokoncene session se ukazuji, ale mimo poradi —
        /// schovat je uplne by budilo dojem, ze se ztratila data.
        /// </summary>
        public string BuildRankingTable(int limit = 8)
        {
            var vse = ReadRanking();
            if (vse.Count == 0) return "Zatím žádné uložené session.";

            var sb = new StringBuilder();

            sb.Append(Pad("#", 4)).Append(Pad("Participant", 13)).Append(Pad("Celkem", 10))
              .Append(Pad("Čistý čas", 12)).Append(Pad("Postihy", 9)).Append("Terče")
              .AppendLine();

            sb.AppendLine(new string('-', 53));

            var poradi = 0;
            var ukazano = 0;

            foreach (var s2 in vse)
            {
                if (ukazano++ >= limit) break;

                // Nedokončená session dostane pomlčku místo pořadí. Krátký čas
                // má právě proto, že nebyla dokončena, takže by jinak vyhrála.
                var znacka = s2.complete ? (++poradi) + "." : "—";

                sb.Append(Pad(znacka, 4))
                  .Append(Pad(s2.participantId ?? "?", 13))
                  .Append(Pad(BlockTimer.Format(s2.TotalTime), 10))
                  .Append(Pad(BlockTimer.Format(s2.cleanTime), 12))
                  .Append(Pad(s2.penaltyCount + "×", 9))
                  .Append(s2.activations > 0 ? Mathf.RoundToInt(s2.HitRate * 100f) + "%" : "—")
                  .AppendLine();
            }

            return sb.ToString();
        }

        private static string Pad(string value, int width)
        {
            if (value == null) value = "";
            return value.Length >= width ? value + " " : value.PadRight(width);
        }

        /// <summary>
        /// Uloží souhrn session vedle blokových logů. Blokové logy jsou
        /// událostní a pro rychlý pohled nepoužitelné; tenhle soubor má jeden
        /// řádek na blok, takže se dá rovnou otevřít v tabulkovém procesoru.
        /// </summary>
        public string WriteResultsFile()
        {
            if (_results.Count == 0) return null;

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "BP_Data");
                Directory.CreateDirectory(dir);

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var path = Path.Combine(dir, participantId + "_souhrn_" + stamp + ".csv");

                var sb = new StringBuilder();
                sb.AppendLine("# participant;" + participantId);
                sb.AppendLine("# skupina;" + counterbalanceGroup);
                sb.AppendLine("# ukonceno;" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture));

                sb.AppendLine(string.Join(";",
                    "blok", "trenink", "podminka", "zatez", "sablona",
                    "cisty_cas_s", "postihu", "postih_s",
                    "kroku_hotovo", "kroku_celkem", "spatnych_objektu", "odmitnutych_pokladek",
                    "sekundarni_bezela", "aktivaci", "zasahu", "hit_rate", "prum_rt_s",
                    "ukonceni"));

                foreach (var r in _results)
                {
                    sb.AppendLine(string.Join(";",
                        (r.index + 1).ToString(CultureInfo.InvariantCulture),
                        r.isTraining ? "1" : "0",
                        r.condition.ToString(),
                        r.load.ToString(),
                        r.templateId,
                        r.rawTime.ToString("F3", CultureInfo.InvariantCulture),
                        r.penaltyCount.ToString(CultureInfo.InvariantCulture),
                        r.penaltyTime.ToString("F1", CultureInfo.InvariantCulture),
                        r.stepsDone.ToString(CultureInfo.InvariantCulture),
                        r.stepsTotal.ToString(CultureInfo.InvariantCulture),
                        r.wrongObjects.ToString(CultureInfo.InvariantCulture),
                        r.rejectedPlacements.ToString(CultureInfo.InvariantCulture),
                        r.secondaryRan ? "1" : "0",
                        r.activations.ToString(CultureInfo.InvariantCulture),
                        r.hits.ToString(CultureInfo.InvariantCulture),
                        r.hitRate.ToString("F3", CultureInfo.InvariantCulture),
                        r.meanReactionTime.ToString("F3", CultureInfo.InvariantCulture),
                        r.reason));
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                ResultsPath = path;
                return path;
            }
            catch (Exception e)
            {
                // Zápis souhrnu nesmí shodit konec session — blokové logy
                // jsou na disku už tak jako tak.
                Debug.LogError("[TrialManager] Souhrn se nepodařilo uložit: " + e.Message);
                return null;
            }
        }
    }
}
