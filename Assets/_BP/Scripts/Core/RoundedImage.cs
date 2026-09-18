using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Paměť zaoblení pro desku, která se staví v editoru a ukládá do scény.
    ///
    /// PROČ TO VŮBEC MUSÍ EXISTOVAT: sprite se zaoblenými rohy se generuje
    /// za běhu a má HideFlags.DontSave, aby po sobě nenechával smetí
    /// v projektu. Do souboru scény se tedy nezapíše — inventář postavený
    /// v editoru si po uložení a načtení odkaz na sprite nenese a vykreslí
    /// se s ostrými rohy. Okno s hlasovými příkazy problém nemá, protože se
    /// staví celé za běhu.
    ///
    /// Řešením není sprite uložit jako asset: pak by se musel udržovat
    /// obrázek v projektu a poloměry by přestaly jít měnit z kódu. Levnější
    /// je zapamatovat si jedno číslo a tvar po načtení scény obnovit.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class RoundedImage : MonoBehaviour
    {
        [Tooltip("Poloměr rohu v jednotkách canvasu. Zapisuje ho PanelStyle.")]
        [SerializeField] private float radius = PanelStyle.RadiusPanel;

        private void Awake()
        {
            var img = GetComponent<Image>();

            // Obnovuje se JEN tvar. Barvu a raycastTarget nese scéna a mohly
            // se mezitím změnit za běhu (zelené CREATE, zhasnutá dlaždice).
            if (img != null) PanelStyle.Tvarovat(img, radius);
        }

        /// <summary>Zapíše poloměr. Tvar nastavuje PanelStyle sám.</summary>
        public void Zapamatovat(float hodnota) => radius = hodnota;
    }
}
