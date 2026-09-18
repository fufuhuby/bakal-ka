using System.Collections.Generic;
using BP.Core;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Generuje třívlastnostní šablony — tvar, barva a velikost.
    ///
    /// POZICE SE POČÍTAJÍ, NEZAPISUJÍ RUČNĚ. Komín se skládá podle skutečných
    /// výšek tvarů: každý další tvar dosedne na předchozí. U jedné velikosti
    /// to dává přesně ty pozice, které měly ručně psané šablony (3, 9, 15,
    /// 21, 27 a 31,2 cm) — jen se to teď dopočítá i pro míchané velikosti,
    /// kde by ruční hodnoty přestaly platit a tvary by se do sebe zabořily.
    ///
    /// TVARY A BARVY SE PŘEBÍRAJÍ z dvouvlastnostních šablon. Obě úrovně
    /// druhého faktoru tak mají stejnou pestrost tvarů i barev a liší se
    /// jen počtem voleb, které musí participant udělat.
    /// </summary>
    public static class SizedTemplateBuilder
    {
        private const string Folder = "Assets/_BP/Templates";

        /// <summary>
        /// Rozvržení velikostí. Každá šablona použije všechny tři velikosti
        /// a v součtu podobně často, aby se obtížnost mezi šablonami nelišila.
        /// Index odpovídá kroku.
        /// </summary>
        private static readonly Dictionary<string, ShapeSize[]> SizePlans =
            new Dictionary<string, ShapeSize[]>
            {
                { "T4", new[]{ ShapeSize.L, ShapeSize.S,  ShapeSize.M,  ShapeSize.L,
                               ShapeSize.S,  ShapeSize.M,  ShapeSize.S,  ShapeSize.M } },
                { "T5", new[]{ ShapeSize.M,  ShapeSize.L, ShapeSize.S,  ShapeSize.M,
                               ShapeSize.L, ShapeSize.S,  ShapeSize.M,  ShapeSize.S } },
                { "T6", new[]{ ShapeSize.S,  ShapeSize.M,  ShapeSize.L, ShapeSize.S,
                               ShapeSize.M,  ShapeSize.L, ShapeSize.M,  ShapeSize.S } },
            };

        /// <summary>
        /// Ke kterým krokům komína se přilepí boční tvary. Dva RŮZNÉ indexy
        /// dají nesymetrickou stavbu.
        ///
        /// PROČ TO TADY JE: zdrojové šablony mají obě ramena ve stejné výšce,
        /// takže mají všechny stejnou souměrnou siluetu. Když z nich vznikla
        /// třívlastnostní verze se stejnými tvary i barvami, byl blok
        /// s velikostmi na pohled k nerozeznání od bloku bez nich — a to je
        /// horší než kosmetická vada: participant tu stavbu už jednou
        /// postavil, takže se v ní podruhé neorientuje, ale VZPOMÍNÁ SI.
        /// Completion time pak měří zapamatování, ne cenu ovládání.
        ///
        /// Indexy jsou pořadí v komíně (0 = spodní), ne indexy kroků.
        /// </summary>
        private static readonly Dictionary<string, int[]> SideLevels =
            new Dictionary<string, int[]>
            {
                { "T4", new[]{ 1, 4 } },
                { "T5", new[]{ 4, 1 } },
                { "T6", new[]{ 0, 3 } },
            };

        [MenuItem("BP/Generovat trivlastnostni sablony")]
        public static void Generate()
        {
            var zdroje = new[] { "Template_T1", "Template_T2", "Template_T3" };
            var cile = new[] { "T4", "T5", "T6" };

            for (var k = 0; k < zdroje.Length; k++)
            {
                var zdroj = Load(zdroje[k]);
                if (zdroj == null)
                {
                    Debug.LogError("[SizedTemplateBuilder] Chybí zdrojová šablona " + zdroje[k]);
                    continue;
                }

                var id = cile[k];
                var cesta = Folder + "/Template_" + id + ".asset";
                var cil = AssetDatabase.LoadAssetAtPath<AssemblyTemplate>(cesta);

                if (cil == null)
                {
                    cil = ScriptableObject.CreateInstance<AssemblyTemplate>();
                    AssetDatabase.CreateAsset(cil, cesta);
                }

                Fill(cil, zdroj, id, SizePlans[id]);
                EditorUtility.SetDirty(cil);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SizedTemplateBuilder] Hotovo — T4, T5, T6.");
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

        private static void Fill(AssemblyTemplate cil, AssemblyTemplate zdroj, string id, ShapeSize[] plan)
        {
            var so = new SerializedObject(cil);
            so.FindProperty("templateId").stringValue = id;
            so.FindProperty("usesSizes").boolValue = true;
            so.FindProperty("isTrainingTemplate").boolValue = false;
            so.FindProperty("positionTolerance").floatValue = zdroj.positionTolerance;
            so.FindProperty("rotationTolerance").floatValue = zdroj.rotationTolerance;

            var pocet = zdroj.StepCount;
            var steps = so.FindProperty("steps");
            steps.arraySize = pocet;

            var tvary = new ShapeType[pocet];
            for (var i = 0; i < pocet; i++) tvary[i] = zdroj.GetStep(i).shape;

            // Silueta z předlohy, patra ramen z tabulky výše — ta je celý
            // důvod, proč blok s velikostmi nevypadá jako blok bez nich.
            List<int> komin, bocni;
            TemplateLayout.RozdelitKroky(zdroj, out komin, out bocni);

            var urovne = new List<int>(SideLevels.ContainsKey(id) ? SideLevels[id] : new[]{ 2, 2 });
            var pozice = TemplateLayout.Rozlozit(tvary, plan, komin, bocni, urovne);

            for (var i = 0; i < pocet; i++)
            {
                var s = steps.GetArrayElementAtIndex(i);
                s.FindPropertyRelative("shape").enumValueIndex = (int)tvary[i];
                s.FindPropertyRelative("color").enumValueIndex = (int)zdroj.GetStep(i).color;
                s.FindPropertyRelative("size").enumValueIndex = (int)plan[i];
                s.FindPropertyRelative("localPosition").vector3Value = pozice[i];
                s.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
                s.FindPropertyRelative("uniformScale").floatValue = 1f;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

    }
}
