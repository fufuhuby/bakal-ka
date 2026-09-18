using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Nakreslí ikony akčních tlačítek inventáře do PNG.
    ///
    /// PROČ KRESLENÍ A NE OBRÁZEK ODNĚKUD: ikona musí sedět s ostatními
    /// prvky panelu na sílu tahu a na velikost, jinak jedno tlačítko opticky
    /// převáží druhé a přitahuje pozornost — a doba hledání v menu je přesně
    /// ta veličina, kterou experiment měří. Když se kreslí, je síla tahu
    /// jedno číslo pro obě ikony.
    ///
    /// PROČ NE PÍSMENO Z FONTU: „VYTVOŘIT" se do dlaždice 2,5 cm nevejde
    /// čitelně a zmenšené písmo je na dvě sekundy hledání navíc.
    ///
    /// Kreslí se s přesamplováním 4×4, ne přes SDF — tvary jsou ploché
    /// siluety bez měřítkování, takže stačí hrubá síla a hrana vyjde hladká.
    /// </summary>
    public static class ActionIconRenderer
    {
        private const string OutputFolder = "Assets/_BP/Icons";
        private const int Resolution = 256;
        private const int Samples = 4;

        [MenuItem("BP/Generovat ikony tlacitek")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputFolder);

            Ulozit("Icon_Undo", Zpet);
            Ulozit("Icon_Create", Kladivo);
            Ulozit("Icon_CreatePlus", Plus);
            Ulozit("Icon_Plan", Oko);

            for (var i = 0; i < 3; i++)
            {
                var stupen = i;
                Ulozit("Icon_Size_" + "SML"[i], p => Stupnice(p, stupen));
            }

            AssetDatabase.Refresh();
            foreach (var n in new[] { "Icon_Undo", "Icon_Create", "Icon_CreatePlus", "Icon_Plan",
                                      "Icon_Size_S", "Icon_Size_M", "Icon_Size_L" })
                NastavitJakoSprite(OutputFolder + "/" + n + ".png");

            AssetDatabase.Refresh();
            Debug.Log("[BP] Vygenerovany ikony tlacitek do " + OutputFolder + ".");
        }

        private static void Ulozit(string jmeno, Func<Vector2, bool> uvnitr)
            => Ulozit(jmeno, p => uvnitr(p) ? 1f : 0f);

        /// <summary>
        /// Varianta, kde tvar vrací KRYTÍ, ne jen ano/ne. Potřebuje ji
        /// stupnice velikostí: nesvítící dílky se kreslí týmž tahem, jen
        /// slabším, a to se do jednoho obrázku vejde jedině přes alfu.
        /// </summary>
        private static void Ulozit(string jmeno, Func<Vector2, float> kryti)
        {
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            var px = new Color32[Resolution * Resolution];

            for (var y = 0; y < Resolution; y++)
            for (var x = 0; x < Resolution; x++)
            {
                var soucet = 0f;

                for (var sy = 0; sy < Samples; sy++)
                for (var sx = 0; sx < Samples; sx++)
                {
                    // Souřadnice -1..1, aby se tvary psaly nezávisle na rozlišení.
                    var u = (x + (sx + 0.5f) / Samples) / Resolution * 2f - 1f;
                    var v = (y + (sy + 0.5f) / Samples) / Resolution * 2f - 1f;
                    soucet += kryti(new Vector2(u, v));
                }

                var a = (byte)(255f * soucet / (Samples * Samples));
                px[y * Resolution + x] = new Color32(255, 255, 255, a);
            }

            tex.SetPixels32(px);
            tex.Apply();

            File.WriteAllBytes(OutputFolder + "/" + jmeno + ".png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ---- Tvary ----

        /// <summary>
        /// Šipka do kolečka. Oblouk skoro celý dokola a hrot na konci —
        /// mezera v kroužku je to, co odlišuje „vrátit" od „načítá se".
        /// </summary>
        private static bool Zpet(Vector2 p)
        {
            const float polomer = 0.52f;
            const float tah = 0.19f;

            // Oblouk 320° proti směru hodinových ručiček s mezerou nahoře.
            // KONEC JE VLEVO NAHOŘE a hrot míří doleva: tak vypadá „zpět".
            // Když hrot skončil vpravo dole, čtl se tvar jako „načíst znovu".
            var uhel = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            if (uhel < 0f) uhel += 360f;

            var naObvodu = Mathf.Abs(p.magnitude - polomer) <= tah * 0.5f;
            if (naObvodu && (uhel >= 150f || uhel <= 110f)) return true;

            // Hrot na konci oblouku, mířící po tečně dál dokola.
            const float konec = 110f * Mathf.Deg2Rad;
            var stred = new Vector2(Mathf.Cos(konec), Mathf.Sin(konec)) * polomer;
            var tecna = new Vector2(-Mathf.Sin(konec), Mathf.Cos(konec));
            var kolmo = new Vector2(tecna.y, -tecna.x);

            return VTrojuhelniku(p,
                stred + tecna * 0.30f,
                stred + kolmo * 0.24f,
                stred - kolmo * 0.24f);
        }

        /// <summary>Kladivo: hlava nahoře, násada dolů, mírně natočené.</summary>
        private static bool Kladivo(Vector2 p)
        {
            // Natočení o 22°, aby silueta nevyšla jako písmeno T.
            var q = Otocit(p, -22f);

            // Násada.
            if (Obdelnik(q, new Vector2(0f, -0.20f), new Vector2(0.095f, 0.58f), 0.06f)) return true;

            // Hlava.
            if (Obdelnik(q, new Vector2(0.06f, 0.52f), new Vector2(0.46f, 0.20f), 0.07f)) return true;

            // Pařát na levé straně hlavy — bez něj čte silueta jako palička.
            if (Obdelnik(q, new Vector2(-0.44f, 0.46f), new Vector2(0.11f, 0.26f), 0.06f)) return true;

            return false;
        }

        /// <summary>
        /// Oko — tlačítko, které odkryje plánek.
        ///
        /// PROČ OKO A NE TŘEBA VÝKRES: to tlačítko nic nemění, jen na chvíli
        /// něco ukáže. Oko je na „podívat se" nejrozšířenější značka, jakou
        /// lidi znají z telefonu (zobrazit heslo), takže se nemusí učit.
        ///
        /// Tvar vznikne průnikem dvou kruhů — tak se kreslí čočka. Obrys je
        /// rozdíl většího a menšího průniku, zornička je kruh uprostřed.
        /// </summary>
        private static bool Oko(Vector2 p)
        {
            const float posun = 0.62f;   // jak daleko od sebe jsou středy kruhů
            const float vnejsi = 0.95f;
            const float vnitrni = 0.83f;

            var horni = new Vector2(0f, posun);
            var dolni = new Vector2(0f, -posun);

            var vCocce = (p - dolni).magnitude <= vnejsi && (p - horni).magnitude <= vnejsi;
            var vJadre = (p - dolni).magnitude <= vnitrni && (p - horni).magnitude <= vnitrni;

            if (vCocce && !vJadre) return true;          // obrys oka
            return p.magnitude <= 0.17f;                 // zornička
        }

        /// <summary>
        /// Stupnice velikosti: tři čtverce vedle sebe, ten platný svítí.
        ///
        /// PROČ CELÁ STUPNICE A NE JEN JEDEN ČTVEREC: samotný malý čtverec
        /// nikdo nepozná jako „malý", protože nemá s čím srovnávat. Když jsou
        /// vidět všechny tři a jeden z nich svítí, je to jednoznačné i bez
        /// reference — a to je podstatné u odznaku v plánku, kde se velikosti
        /// neukazují vedle sebe.
        ///
        /// PROČ NE PÍSMENA: „M" má člověk z triček spojené s medium, takže
        /// při hlasovém ovládání musí z písmene vyrobit slovo „střední“.
        /// Ten překlad je krok navíc, který v menu neexistuje — klepnutím se
        /// písmeno nepřekládá — a schoval by se do naměřeného času jako cena
        /// hlasu. Obrázek se nepřekládá.
        /// </summary>
        private static float Stupnice(Vector2 p, int stupen)
        {
            // Tři čtverce rostoucí velikosti, posazené na společnou základnu.
            var poloviny = new[] { 0.14f, 0.21f, 0.28f };
            var stredyX = new[] { -0.66f, -0.10f, 0.52f };
            const float zaklad = -0.42f;

            for (var i = 0; i < 3; i++)
            {
                var stred = new Vector2(stredyX[i], zaklad + poloviny[i]);
                if (!Obdelnik(p, stred, new Vector2(poloviny[i], poloviny[i]), 0.05f)) continue;

                // Nesvítící dílky zůstávají vidět, jen slabě — jsou to měřítko,
                // ne volba.
                return i == stupen ? 1f : 0.28f;
            }

            return 0f;
        }

        /// <summary>Znaménko plus. Náhradní varianta pro tlačítko vytvoření.</summary>
        private static bool Plus(Vector2 p)
            => Obdelnik(p, Vector2.zero, new Vector2(0.58f, 0.125f), 0.05f)
               || Obdelnik(p, Vector2.zero, new Vector2(0.125f, 0.58f), 0.05f);

        // ---- Pomocné ----

        private static Vector2 Otocit(Vector2 p, float stupne)
        {
            var r = stupne * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
        }

        private static bool Obdelnik(Vector2 p, Vector2 stred, Vector2 polovina, float radius)
        {
            var d = new Vector2(Mathf.Abs(p.x - stred.x), Mathf.Abs(p.y - stred.y));
            var vnitrek = polovina - new Vector2(radius, radius);

            var dx = Mathf.Max(d.x - vnitrek.x, 0f);
            var dy = Mathf.Max(d.y - vnitrek.y, 0f);

            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool VTrojuhelniku(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Znamenko(p, a, b);
            var d2 = Znamenko(p, b, c);
            var d3 = Znamenko(p, c, a);

            var zaporne = d1 < 0f || d2 < 0f || d3 < 0f;
            var kladne = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(zaporne && kladne);
        }

        private static float Znamenko(Vector2 p, Vector2 a, Vector2 b)
            => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        private static void NastavitJakoSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }
}
