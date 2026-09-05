using System;
using BP.Core;
using UnityEngine;

namespace BP.Input
{
    /// <summary>
    /// Podmínka A — hand-fixed menu (skica b4).
    ///
    /// Participant vybere barvu, vybere tvar a stiskne CREATE; objekt se
    /// zjeví ve scéně. STEP BACK maže poslední objekt.
    ///
    /// Výběr je záměrně dvoukrokový (barva + tvar), protože přesně to
    /// generuje vizuální prohledávání menu — mechanismus, jehož cenu
    /// experiment měří proti hlasovému příkazu.
    /// </summary>
    public class MenuRequestSource : MonoBehaviour, IObjectRequestSource
    {
        public event Action<ObjectRequest> ObjectRequested;
        public event Action UndoRequested;

        /// <summary>Změnil se výběr — panel podle toho překresluje obrysy.</summary>
        public event Action SelectionChanged;

        public InteractionCondition Condition => InteractionCondition.Menu;

        public ShapeType? SelectedShape { get; private set; }
        public PaletteColor? SelectedColor { get; private set; }

        /// <summary>Je vybráno obojí, takže CREATE něco udělá?</summary>
        public bool CanCreate => SelectedShape.HasValue && SelectedColor.HasValue;

        [Tooltip("Zrušit výběr po vytvoření objektu. Vypnuto = lze vytvořit " +
                 "několik stejných objektů bez opakovaného vybírání.")]
        [SerializeField] private bool clearSelectionAfterCreate;

        private bool _inputEnabled = true;

        public void SetInputEnabled(bool value) => _inputEnabled = value;

        public void SelectShape(ShapeType shape)
        {
            if (!_inputEnabled) return;

            // Opětovný klik na už vybranou dlaždici výběr zruší — participant
            // se tak dostane zpět do neutrálního stavu bez hledání jiné cesty.
            SelectedShape = SelectedShape.HasValue && SelectedShape.Value == shape
                ? (ShapeType?)null
                : shape;

            Raise();
        }

        public void SelectColor(PaletteColor color)
        {
            if (!_inputEnabled) return;

            SelectedColor = SelectedColor.HasValue && SelectedColor.Value == color
                ? (PaletteColor?)null
                : color;

            Raise();
        }

        public void Create()
        {
            if (!_inputEnabled || !CanCreate) return;

            var request = new ObjectRequest(
                SelectedShape.Value, SelectedColor.Value, Time.realtimeSinceStartup);

            if (ObjectRequested != null) ObjectRequested(request);

            if (clearSelectionAfterCreate)
            {
                SelectedShape = null;
                SelectedColor = null;
                Raise();
            }
        }

        public void StepBack()
        {
            if (!_inputEnabled) return;
            if (UndoRequested != null) UndoRequested();
        }

        private void Raise()
        {
            if (SelectionChanged != null) SelectionChanged();
        }
    }
}
