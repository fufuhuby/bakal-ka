using System.Collections.Generic;
using BP.Core;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Geometrie stavby: kde který tvar stojí, aby na sobě doopravdy ležely.
    ///
    /// POČÍTÁ SE, NEOPISUJE. Tvary nemají stejnou výšku — torus je plochý,
    /// ostatní krychlové — takže sada souřadnic spočítaná pro jedno pořadí
    /// tvarů přestane platit, jakmile se pořadí změní. Přepsat v šabloně tvar
    /// a nechat pozici je nejtišší možná chyba, jakou tady jde udělat: stavba
    /// se pořád dá dokončit a validátor ji přijme, jen v ní jsou mezery a
    /// tvary zapadlé do sebe. Participant to ale vidí a řeší, takže se mu to
    /// propíše do času — a do rozdílu mezi podmínkami, který experiment měří.
    ///
    /// Tenhle výpočet je proto jediné místo, kde se pozice berou. Oba
    /// generátory šablon ho volají.
    /// </summary>
    public static class TemplateLayout
    {
        /// <summary>Výška tvaru v metrech při velikosti M.</summary>
        public static float Height(ShapeType shape) => shape == ShapeType.Torus ? 0.024f : 0.060f;

        /// <summary>Šířka tvaru v metrech při velikosti M.</summary>
        public static float Width(ShapeType shape) => 0.060f;

        /// <summary>Mezera mezi ramenem a komínem, aby se o sebe neodíraly.</summary>
        private const float Vule = 0.005f;

        /// <summary>
        /// Spočítá pozice všech kroků.
        ///
        /// komin  — indexy kroků, které stojí na sobě, zdola nahoru.
        /// bocni  — indexy kroků přilepených ze strany.
        /// urovne — pro každý boční krok pořadí kroku v komíně, u kterého sedí.
        ///
        /// Výsledek je vystředěný na kotvu: kroky jen nad kotvou by posadily
        /// stavbu mimo osu, kolem které jsou rozložené periferní terče.
        /// </summary>
        public static Vector3[] Rozlozit(ShapeType[] tvary, ShapeSize[] velikosti,
            IReadOnlyList<int> komin, IReadOnlyList<int> bocni, IReadOnlyList<int> urovne)
        {
            var pocet = tvary.Length;

            var vysky = new float[pocet];
            var sirky = new float[pocet];
            for (var i = 0; i < pocet; i++)
            {
                var meritko = ShapeSizes.Scale(velikosti[i]);
                vysky[i] = Height(tvary[i]) * meritko;
                sirky[i] = Width(tvary[i]) * meritko;
            }

            var pozice = new Vector3[pocet];

            // Komín: každý další tvar dosedne na předchozí.
            var y = 0f;
            foreach (var i in komin)
            {
                y += vysky[i] * 0.5f;
                pozice[i] = new Vector3(0f, y, 0f);
                y += vysky[i] * 0.5f;
            }

            var strana = -1f;

            for (var k = 0; k < bocni.Count; k++)
            {
                var i = bocni[k];
                var uroven = Mathf.Clamp(urovne[Mathf.Min(k, urovne.Count - 1)], 0, komin.Count - 1);
                var soused = komin[uroven];

                var ramenoY = pozice[soused].y;
                var ramenoPolovina = sirky[i] * 0.5f;
                var ramenoVyska = vysky[i] * 0.5f;

                // Odsazení se NEPOČÍTÁ jen podle kroku, u kterého rameno sedí.
                // Sousední krok v komíně může být větší a čouhat dál do strany,
                // takže by se do něj rameno zařízlo. Bere se tedy nejširší krok
                // komína, jehož výška se s ramenem překrývá.
                var prekazka = 0f;
                foreach (var kk in komin)
                {
                    if (Mathf.Abs(pozice[kk].y - ramenoY) >= ramenoVyska + vysky[kk] * 0.5f) continue;
                    prekazka = Mathf.Max(prekazka, sirky[kk] * 0.5f);
                }

                pozice[i] = new Vector3(strana * (prekazka + ramenoPolovina + Vule), ramenoY, 0f);
                strana = -strana;
            }

            var obal = new Bounds(pozice[0], Vector3.zero);
            foreach (var p in pozice) obal.Encapsulate(p);
            var stred = obal.center.y;

            for (var i = 0; i < pocet; i++) pozice[i].y -= stred;

            return pozice;
        }

        /// <summary>
        /// Rozdělí kroky na komín a ramena podle předlohy. Boční krok se pozná
        /// podle nenulového x — tak vznikly všechny ruční šablony.
        /// </summary>
        public static void RozdelitKroky(AssemblyTemplate zdroj,
            out List<int> komin, out List<int> bocni)
        {
            komin = new List<int>();
            bocni = new List<int>();

            for (var i = 0; i < zdroj.StepCount; i++)
            {
                if (Mathf.Abs(zdroj.GetStep(i).localPosition.x) > 0.001f) bocni.Add(i);
                else komin.Add(i);
            }
        }

        /// <summary>
        /// U které úrovně komína sedí které rameno v předloze. Zjišťuje se
        /// z výšek, aby se při přepočtu zachovala silueta stavby.
        /// </summary>
        public static List<int> UrovneRamen(AssemblyTemplate zdroj,
            IReadOnlyList<int> komin, IReadOnlyList<int> bocni)
        {
            var urovne = new List<int>();

            foreach (var i in bocni)
            {
                var cil = zdroj.GetStep(i).localPosition.y;

                var nejlepsi = 0;
                var nejmensi = float.MaxValue;

                for (var k = 0; k < komin.Count; k++)
                {
                    var vzdalenost = Mathf.Abs(zdroj.GetStep(komin[k]).localPosition.y - cil);
                    if (vzdalenost >= nejmensi) continue;

                    nejmensi = vzdalenost;
                    nejlepsi = k;
                }

                urovne.Add(nejlepsi);
            }

            return urovne;
        }
    }
}
