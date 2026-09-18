using System.Collections.Generic;
using BP.Core;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Vyrobí šablony pro hlasovou verzi z těch klasických.
    ///
    /// PROČ NESTAČILY STÁVAJÍCÍ: klasická session jede T1, T5 a T3. Ze šesti
    /// existujících šablon zbývají T2, T4 a T6 — jenže T4 vznikla z T1 a T6
    /// z T3, takže mají stejné tvary i barvy a liší se jen velikostmi.
    /// Participant by v hlasové verzi stavěl to, co už jednou postavil,
    /// a completion time by měřil zapamatování místo ceny ovládání.
    ///
    /// CO SE MĚNÍ A CO NE: kostra zůstává — stejný počet kroků, stejné
    /// velikosti, stejná silueta (komín plus dvě ramena ve stejných patrech).
    /// Prohodí se jen PŘIŘAZENÍ tvarů a barev. Obtížnost tím zůstává
    /// srovnatelná (to je podmínka pro porovnání podmínek), ale stavba je jiná.
    ///
    /// POZICE SE PŘEPOČÍTÁVAJÍ, NEOPISUJÍ. Torus je plochý (2,4 cm) a ostatní
    /// tvary vysoké 6 cm, takže jakmile se pořadí tvarů posune, souřadnice
    /// z předlohy přestanou sedět: pod jedním tvarem zůstane mezera a jiný se
    /// zaboří do souseda. Poprvé to tak opravdu vzniklo — V1 měla 18 mm mezeru
    /// pod druhým tvarem a 18 mm překryv nahoře.
    ///
    /// Permutace se HLEDÁ, nezadává. Posun o pevné číslo může u některé
    /// dvojice šablon vyjít tak, že se krok trefí do stejného tvaru i barvy
    /// jako jinde — tady se projdou všechny dvojice posunů a vezme se první,
    /// která nemá s klasickými šablonami ani mezi sebou jediný shodný krok.
    /// </summary>
    public static class VoiceTemplateBuilder
    {
        private const string Folder = "Assets/_BP/Templates";

        /// <summary>Z čeho se bere kostra a do čeho se ukládá výsledek.</summary>
        private static readonly (string zdroj, string cil)[] Mapa =
        {
            ("Template_T1", "V1"),
            ("Template_T5", "V2"),
            ("Template_T3", "V3"),
        };

        /// <summary>Šablony klasické session — s těmi se výsledek nesmí potkat.</summary>
        private static readonly string[] Klasicke = { "Template_T1", "Template_T5", "Template_T3" };

        [MenuItem("BP/Generovat hlasove sablony")]
        public static void Generate()
        {
            var zdroje = new List<AssemblyTemplate>();
            foreach (var m in Mapa)
            {
                var z = Load(m.zdroj);
                if (z == null)
                {
                    Debug.LogError("[VoiceTemplateBuilder] Chybí zdrojová šablona " + m.zdroj);
                    return;
                }
                zdroje.Add(z);
            }

            var pocetBarev = System.Enum.GetValues(typeof(PaletteColor)).Length;
            var pocetTvaru = System.Enum.GetValues(typeof(ShapeType)).Length;

            var posunBarev = -1;
            var posunTvaru = -1;

            // Posun 0 by nechal vlastnost beze změny, proto se začíná od 1.
            for (var pb = 1; pb < pocetBarev && posunBarev < 0; pb++)
            for (var pt = 1; pt < pocetTvaru; pt++)
            {
                if (!Vyhovuje(zdroje, pb, pt, pocetBarev, pocetTvaru)) continue;

                posunBarev = pb;
                posunTvaru = pt;
                break;
            }

            if (posunBarev < 0)
            {
                Debug.LogError("[VoiceTemplateBuilder] Nenašel jsem permutaci bez překryvu. "
                               + "Zkontroluj, jestli klasické šablony nepoužívají "
                               + "všechny kombinace tvarů a barev.");
                return;
            }

            for (var i = 0; i < Mapa.Length; i++)
            {
                var cesta = Folder + "/Template_" + Mapa[i].cil + ".asset";
                var cil = AssetDatabase.LoadAssetAtPath<AssemblyTemplate>(cesta);

                if (cil == null)
                {
                    cil = ScriptableObject.CreateInstance<AssemblyTemplate>();
                    AssetDatabase.CreateAsset(cil, cesta);
                }

                Naplnit(cil, zdroje[i], Mapa[i].cil, posunBarev, posunTvaru, pocetBarev, pocetTvaru);
                EditorUtility.SetDirty(cil);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[VoiceTemplateBuilder] Hotovo — V1, V2, V3. "
                      + "Posun barev " + posunBarev + ", posun tvarů " + posunTvaru + ".");
        }

        /// <summary>
        /// Nemá permutace ani jeden shodný krok s klasickými šablonami
        /// a nevyrobí dvě stejné šablony mezi sebou?
        /// </summary>
        private static bool Vyhovuje(List<AssemblyTemplate> zdroje,
            int posunBarev, int posunTvaru, int pocetBarev, int pocetTvaru)
        {
            var klasicke = new List<AssemblyTemplate>();
            foreach (var jm in Klasicke)
            {
                var t = Load(jm);
                if (t != null) klasicke.Add(t);
            }

            // Kroky se porovnávají po indexech: participant staví v pevném
            // pořadí, takže „stejný krok" znamená stejné pořadí i objekt.
            foreach (var zdroj in zdroje)
            foreach (var jina in klasicke)
            {
                var n = Mathf.Min(zdroj.StepCount, jina.StepCount);
                for (var i = 0; i < n; i++)
                {
                    var a = zdroj.GetStep(i);
                    var b = jina.GetStep(i);

                    if (Posun(a.shape, posunTvaru, pocetTvaru) == b.shape
                        && Posun(a.color, posunBarev, pocetBarev) == b.color)
                        return false;
                }
            }

            return true;
        }

        private static ShapeType Posun(ShapeType s, int o, int n)
            => (ShapeType)(((int)s + o) % n);

        private static PaletteColor Posun(PaletteColor c, int o, int n)
            => (PaletteColor)(((int)c + o) % n);

        private static void Naplnit(AssemblyTemplate cil, AssemblyTemplate zdroj, string id,
            int posunBarev, int posunTvaru, int pocetBarev, int pocetTvaru)
        {
            var so = new SerializedObject(cil);

            so.FindProperty("templateId").stringValue = id;
            so.FindProperty("usesSizes").boolValue = zdroj.usesSizes;
            so.FindProperty("isTrainingTemplate").boolValue = false;
            so.FindProperty("positionTolerance").floatValue = zdroj.positionTolerance;
            so.FindProperty("rotationTolerance").floatValue = zdroj.rotationTolerance;

            var steps = so.FindProperty("steps");
            steps.arraySize = zdroj.StepCount;

            var pocet = zdroj.StepCount;

            var tvary = new ShapeType[pocet];
            var velikosti = new ShapeSize[pocet];

            for (var i = 0; i < pocet; i++)
            {
                var z = zdroj.GetStep(i);
                tvary[i] = Posun(z.shape, posunTvaru, pocetTvaru);
                velikosti[i] = z.size;
            }

            // Silueta se přebírá z předlohy: co je komín, co rameno a v jakém
            // patře rameno sedí. Výšky se pak dopočítají pro NOVÉ tvary.
            List<int> komin, bocni;
            TemplateLayout.RozdelitKroky(zdroj, out komin, out bocni);
            var urovne = TemplateLayout.UrovneRamen(zdroj, komin, bocni);

            var pozice = TemplateLayout.Rozlozit(tvary, velikosti, komin, bocni, urovne);

            for (var i = 0; i < pocet; i++)
            {
                var z = zdroj.GetStep(i);
                var s = steps.GetArrayElementAtIndex(i);

                s.FindPropertyRelative("shape").enumValueIndex = (int)tvary[i];
                s.FindPropertyRelative("color").enumValueIndex =
                    (int)Posun(z.color, posunBarev, pocetBarev);

                s.FindPropertyRelative("size").enumValueIndex = (int)z.size;
                s.FindPropertyRelative("localPosition").vector3Value = pozice[i];
                s.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
                s.FindPropertyRelative("uniformScale").floatValue = 1f;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AssemblyTemplate Load(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyTemplate"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<AssemblyTemplate>(p);
                if (a != null && a.name == name) return a;
            }
            return null;
        }
    }
}
