using System;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Jediné místo, kde se tvar + barva mapují na konkrétní prefab a materiál.
    /// Menu, hlasový modul i zobrazení šablony čtou odsud, takže obě podmínky
    /// pracují s naprosto identickými objekty.
    /// </summary>
    [CreateAssetMenu(menuName = "BP/Shape Library", fileName = "ShapeLibrary")]
    public class ShapeLibrary : ScriptableObject
    {
        [Serializable]
        public struct ShapeEntry
        {
            public ShapeType shape;
            public GameObject prefab;

            [Tooltip("Ikona do menu (skica b4).")]
            public Sprite icon;

            [Tooltip("Drátový model pro zobrazení šablony. Hrubší varianta tvaru — " +
                     "drátový model z plného meshe by byl nečitelná změť.")]
            public Mesh wireMesh;
        }

        [Serializable]
        public struct ColorEntry
        {
            public PaletteColor color;

            [Tooltip("Neprůhledný materiál pro reálný objekt.")]
            public Material solidMaterial;

            [Tooltip("Barva pro UI dlaždici v menu a pro ghost preview.")]
            [ColorUsage(false, true)]
            public Color displayColor;
        }

        [SerializeField] private ShapeEntry[] shapes = Array.Empty<ShapeEntry>();
        [SerializeField] private ColorEntry[] colors = Array.Empty<ColorEntry>();

        [Header("Velikosti")]
        [Tooltip("Stupnice velikosti v pořadí S, M, L. Tři čtverečky, z nichž " +
                 "je zvýrazněný ten, o kterou velikost jde. Čte to menu, okno " +
                 "s hlasovými povely i odznak u plánku — proto to leží tady " +
                 "a ne v každé z těch tří komponent zvlášť. Kdyby se rozešly, " +
                 "znamenal by týž obrázek v každé podmínce něco jiného.")]
        [SerializeField] private Sprite[] sizeIcons = Array.Empty<Sprite>();

        [Header("Ghost / preview")]
        [Tooltip("Průhledný materiál se světlými konturami pro náhled (skica b2).")]
        public Material ghostMaterial;

        [Tooltip("Materiál pro zobrazení objektů v šabloně.")]
        public Material templateMaterial;

        public ShapeEntry[] Shapes => shapes;
        public ColorEntry[] Colors => colors;

        public GameObject GetPrefab(ShapeType shape)
        {
            foreach (var e in shapes)
                if (e.shape == shape) return e.prefab;

            Debug.LogError($"[ShapeLibrary] Chybí prefab pro tvar {shape}.", this);
            return null;
        }

        /// <summary>Drátový model tvaru pro zobrazení šablony.</summary>
        public Mesh GetWireMesh(ShapeType shape)
        {
            foreach (var e in shapes)
                if (e.shape == shape) return e.wireMesh;
            return null;
        }

        public Sprite GetIcon(ShapeType shape)
        {
            foreach (var e in shapes)
                if (e.shape == shape) return e.icon;
            return null;
        }

        /// <summary>
        /// Stupnice pro danou velikost, nebo null, když ikony nejsou nastavené.
        /// Volající si v tom případě vykreslí písmeno jako dřív — nastavení
        /// ikon je zlepšení čitelnosti, ne podmínka běhu.
        /// </summary>
        public Sprite GetSizeIcon(ShapeSize size)
        {
            var poradi = (int)size;
            if (sizeIcons == null || poradi < 0 || poradi >= sizeIcons.Length) return null;
            return sizeIcons[poradi];
        }

        public Material GetMaterial(PaletteColor color)
        {
            foreach (var e in colors)
                if (e.color == color) return e.solidMaterial;

            Debug.LogError($"[ShapeLibrary] Chybí materiál pro barvu {color}.", this);
            return null;
        }

        public Color GetDisplayColor(PaletteColor color)
        {
            foreach (var e in colors)
                if (e.color == color) return e.displayColor;
            return Color.magenta;
        }

        /// <summary>
        /// Ověří, že knihovna je kompletní — spouštěno při startu bloku,
        /// ať se chybějící asset neprojeví až uprostřed testování s participantem.
        /// </summary>
        public bool Validate(out string problem)
        {
            foreach (ShapeType s in Enum.GetValues(typeof(ShapeType)))
            {
                var found = false;
                foreach (var e in shapes)
                    if (e.shape == s && e.prefab != null) { found = true; break; }

                if (!found) { problem = $"Chybí prefab pro tvar {s}."; return false; }
            }

            foreach (PaletteColor c in Enum.GetValues(typeof(PaletteColor)))
            {
                var found = false;
                foreach (var e in colors)
                    if (e.color == c && e.solidMaterial != null) { found = true; break; }

                if (!found) { problem = $"Chybí materiál pro barvu {c}."; return false; }
            }

            if (ghostMaterial == null) { problem = "Chybí ghost materiál."; return false; }

            problem = null;
            return true;
        }
    }
}
