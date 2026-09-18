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

        /// <summary>
        /// Změnilo se, které části menu jsou povolené — panel podle toho
        /// zhasíná dlaždice.
        ///
        /// PROČ VLASTNÍ UDÁLOST A NE SelectionChanged: na SelectionChanged
        /// visí průvodce tutoriálem a posouvá podle něj fázi. Kdyby zamykání
        /// menu chodilo stejnou událostí, průvodce by při každém přepnutí
        /// fáze dostal zpětné volání a rozjel by se do kruhu.
        /// </summary>
        public event Action PartsChanged;

        public InteractionCondition Condition => InteractionCondition.Menu;

        public ShapeType? SelectedShape { get; private set; }
        public PaletteColor? SelectedColor { get; private set; }
        public ShapeSize? SelectedSize { get; private set; }

        /// <summary>
        /// Vyžaduje blok i velikost? Nastavuje TrialManager podle úrovně
        /// druhého faktoru. Ve dvouvlastnostním bloku se sloupec velikostí
        /// vůbec nezobrazí, aby menu nenabízelo volbu, která nic nedělá.
        /// </summary>
        public bool RequiresSize { get; private set; }

        /// <summary>Je vybráno všechno potřebné, takže CREATE něco udělá?</summary>
        public bool CanCreate => SelectedShape.HasValue && SelectedColor.HasValue
                                 && (!RequiresSize || SelectedSize.HasValue);

        [Tooltip("Zrušit výběr po vytvoření objektu. Vypnuto = lze vytvořit " +
                 "několik stejných objektů bez opakovaného vybírání.")]
        [SerializeField] private bool clearSelectionAfterCreate;

        /// <summary>
        /// Které části menu jsou právě k dispozici. Používá to VÝHRADNĚ
        /// tutoriál: vede participanta po krocích, a kdyby šlo v kroku
        /// „vyber barvu" klepnout na tvar, průvodce by při následném výběru
        /// barvy přeskočil rovnou přes krok s tvarem a nácvik by o něj přišel.
        ///
        /// V měřených blocích zůstává All — omezovat pořadí voleb by měnilo
        /// samotnou úlohu, jejíž cenu experiment porovnává s hlasem.
        /// </summary>
        public MenuPart AllowedParts { get; private set; } = MenuPart.All;

        private bool _inputEnabled = true;

        public void SetInputEnabled(bool value) => _inputEnabled = value;

        public void SetAllowedParts(MenuPart parts)
        {
            if (AllowedParts == parts) return;

            AllowedParts = parts;
            if (PartsChanged != null) PartsChanged();
        }

        private bool Povoleno(MenuPart part) => _inputEnabled && (AllowedParts & part) != 0;

        public void SelectShape(ShapeType shape)
        {
            if (!Povoleno(MenuPart.Shapes)) return;

            // Opětovný klik na už vybranou dlaždici výběr zruší — participant
            // se tak dostane zpět do neutrálního stavu bez hledání jiné cesty.
            SelectedShape = SelectedShape.HasValue && SelectedShape.Value == shape
                ? (ShapeType?)null
                : shape;

            Raise();
        }

        public void SelectColor(PaletteColor color)
        {
            if (!Povoleno(MenuPart.Colors)) return;

            SelectedColor = SelectedColor.HasValue && SelectedColor.Value == color
                ? (PaletteColor?)null
                : color;

            Raise();
        }

        public void SelectSize(ShapeSize size)
        {
            if (!Povoleno(MenuPart.Sizes)) return;

            SelectedSize = SelectedSize.HasValue && SelectedSize.Value == size
                ? (ShapeSize?)null
                : size;

            Raise();
        }

        /// <summary>
        /// Přepne, jestli blok velikost řeší. Zároveň zruší rozjednanou
        /// volbu velikosti — kdyby zůstala z předchozího bloku, poslala by
        /// se s prvním objektem, aniž by ji participant v tomto bloku vybral.
        /// </summary>
        public void SetRequiresSize(bool value)
        {
            RequiresSize = value;
            SelectedSize = null;
            Raise();
        }

        public void Create()
        {
            if (!Povoleno(MenuPart.Create) || !CanCreate) return;

            // V dvouvlastnostnim bloku jde vzdy zakladni velikost. Objekty
            // tak maji stejnou velikost jako v puvodnim navrhu a obe urovne
            // faktoru se lisi jen poctem voleb, ne velikosti stavby.
            var size = RequiresSize ? SelectedSize.Value : ShapeSizes.Default;

            var request = new ObjectRequest(
                SelectedShape.Value, SelectedColor.Value, size, Time.realtimeSinceStartup);

            if (ObjectRequested != null) ObjectRequested(request);

            if (clearSelectionAfterCreate)
            {
                SelectedShape = null;
                SelectedColor = null;
                SelectedSize = null;
                Raise();
            }
        }

        public void StepBack()
        {
            if (!Povoleno(MenuPart.StepBack)) return;
            if (UndoRequested != null) UndoRequested();
        }

        private void Raise()
        {
            if (SelectionChanged != null) SelectionChanged();
        }
    }
}
