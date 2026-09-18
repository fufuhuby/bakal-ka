using System;
using BP.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BP.Input
{
    /// <summary>
    /// DOČASNÝ vývojový zdroj požadavků, ovládaný klávesnicí.
    ///
    /// Slouží k odladění celé smyčky (spawn → uchopení → snap → krok → log)
    /// ještě před tím, než existuje hand-fixed menu nebo hlasový modul.
    /// Do měření s participanty se NIKDY nepoužije — v logu by se objevila
    /// podmínka Menu, ale vstup by byl klávesnice.
    ///
    /// Klávesnice:
    ///   Mezerník  = vytvořit SPRÁVNÝ objekt pro aktuální krok
    ///   W         = vytvořit ZÁMĚRNĚ ŠPATNÝ objekt (test error metriky)
    ///   Backspace = step back (undo)
    ///
    /// Ovladače (aby šlo zkoušet s nasazeným headsetem):
    ///   A (pravý)  = správný objekt
    ///   B (pravý)  = step back
    ///   X (levý)   = záměrně špatný objekt
    /// </summary>
    public class DebugRequestSource : MonoBehaviour, IObjectRequestSource
    {
        [SerializeField] private AssemblyTaskController controller;
        [SerializeField] private PlacementValidator validator;

        public event Action<ObjectRequest> ObjectRequested;
        public event Action UndoRequested;

        public InteractionCondition Condition => InteractionCondition.Menu;

        private bool _enabled = true;

        // Akce na ovladačích: s nasazeným headsetem se na klávesnici nedosáhne.
        private InputAction _correctAction;
        private InputAction _wrongAction;
        private InputAction _undoAction;

        public void SetInputEnabled(bool value) => _enabled = value;

        private void OnEnable()
        {
            _correctAction = new InputAction("BP_DebugCorrect", InputActionType.Button,
                "<XRController>{RightHand}/primaryButton");
            _undoAction = new InputAction("BP_DebugUndo", InputActionType.Button,
                "<XRController>{RightHand}/secondaryButton");
            _wrongAction = new InputAction("BP_DebugWrong", InputActionType.Button,
                "<XRController>{LeftHand}/primaryButton");

            _correctAction.performed += _ => { if (_enabled) RequestCorrect(); };
            _undoAction.performed += _ => { if (_enabled) RequestUndo(); };
            _wrongAction.performed += _ => { if (_enabled) RequestWrong(); };

            _correctAction.Enable();
            _undoAction.Enable();
            _wrongAction.Enable();
        }

        private void OnDisable()
        {
            if (_correctAction != null) { _correctAction.Disable(); _correctAction.Dispose(); }
            if (_undoAction != null) { _undoAction.Disable(); _undoAction.Dispose(); }
            if (_wrongAction != null) { _wrongAction.Disable(); _wrongAction.Dispose(); }
        }

        private void Update()
        {
            if (!_enabled) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) RequestCorrect();
            if (kb.wKey.wasPressedThisFrame) RequestWrong();
            if (kb.backspaceKey.wasPressedThisFrame) RequestUndo();
        }

        /// <summary>Vytvoří objekt, který šablona v aktuálním kroku očekává.</summary>
        public void RequestCorrect()
        {
            TemplateStep step;
            if (!TryGetCurrentStep(out step)) return;
            Emit(step.shape, step.color, step.size);
        }

        /// <summary>
        /// Vytvoří objekt posunutý o jeden tvar i barvu — spolehlivě špatný,
        /// takže se dá otestovat logování WrongObjectCreated a recovery přes undo.
        /// </summary>
        public void RequestWrong()
        {
            TemplateStep step;
            if (!TryGetCurrentStep(out step)) return;

            var shape = (ShapeType)(((int)step.shape + 1) % Enum.GetValues(typeof(ShapeType)).Length);
            var color = (PaletteColor)(((int)step.color + 1) % Enum.GetValues(typeof(PaletteColor)).Length);
            Emit(shape, color, step.size);
        }

        public void RequestUndo()
        {
            if (UndoRequested != null) UndoRequested();
        }

        private bool TryGetCurrentStep(out TemplateStep step)
        {
            step = default(TemplateStep);

            if (controller == null || validator == null) return false;

            var template = validator.Visualizer.Template;
            if (template == null) return false;

            var i = controller.CurrentStep;
            if (i < 0 || i >= template.StepCount) return false;

            step = template.GetStep(i);
            return true;
        }

        private void Emit(ShapeType shape, PaletteColor color, ShapeSize size)
        {
            if (ObjectRequested != null)
                ObjectRequested(new ObjectRequest(shape, color, size, Time.realtimeSinceStartup));
        }
    }
}
