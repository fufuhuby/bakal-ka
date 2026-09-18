using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Vyrobí neutrální značku místa — rámeček z rohů krychle.
    ///
    /// PROČ NE OBRYS NĚKTERÉHO TVARU: značka se používá v bloku, kde je
    /// plánek skrytý. Kdyby měla podobu některého tvaru z knihovny, prozradí
    /// identitu objektu a plánek přestane být k něčemu potřeba — celá
    /// manipulace by se vyprázdnila. Rohový rámeček se nepodobá žádnému
    /// ze sedmi tvarů, takže sděluje KAM, ale ne CO.
    /// </summary>
    public static class MarkerMeshBuilder
    {
        private const string Vystup = "Assets/_BP/Meshes/Wire/Wire_Marker.asset";

        /// <summary>Délka rohové tyčky jako podíl poloviny hrany.</summary>
        private const float Podil = 0.45f;

        /// <summary>
        /// Tloušťka tyčky v JEDNOTKÁCH MESHE, ne v metrech. Mesh je jednotková
        /// krychle a ve scéně se zmenšuje na velikost značky, takže tloušťka
        /// se zmenší spolu s ním: 0,03 při značce 7,5 cm dá ve světě 2,3 mm.
        /// Zadané v metrech by po zmenšení vyšlo 0,17 mm a značka by zmizela.
        /// </summary>
        private const float Tloustka = 0.048f;

        [MenuItem("BP/Generovat neutralni znacku")]
        public static void Generate()
        {
            var vrcholy = new List<Vector3>();
            var trojuhelniky = new List<int>();

            const float p = 0.5f;              // poloviční hrana jednotkové krychle
            var delka = p * Podil;

            for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
            for (var sz = -1; sz <= 1; sz += 2)
            {
                var roh = new Vector3(sx * p, sy * p, sz * p);

                // Z každého rohu vede krátká tyčka podél všech tří os dovnitř.
                Tycka(vrcholy, trojuhelniky, roh, roh + new Vector3(-sx * delka, 0f, 0f));
                Tycka(vrcholy, trojuhelniky, roh, roh + new Vector3(0f, -sy * delka, 0f));
                Tycka(vrcholy, trojuhelniky, roh, roh + new Vector3(0f, 0f, -sz * delka));
            }

            var mesh = new Mesh { name = "Wire_Marker" };
            mesh.SetVertices(vrcholy);
            mesh.SetTriangles(trojuhelniky, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var slozka = System.IO.Path.GetDirectoryName(Vystup);
            if (!AssetDatabase.IsValidFolder(slozka))
                System.IO.Directory.CreateDirectory(slozka);

            var stary = AssetDatabase.LoadAssetAtPath<Mesh>(Vystup);
            if (stary != null) AssetDatabase.DeleteAsset(Vystup);

            AssetDatabase.CreateAsset(mesh, Vystup);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MarkerMeshBuilder] Wire_Marker: " + vrcholy.Count + " vrcholů, "
                      + trojuhelniky.Count / 3 + " trojúhelníků.");
        }

        /// <summary>Hranol mezi dvěma body — tenká tyčka viditelná z každé strany.</summary>
        private static void Tycka(List<Vector3> vrcholy, List<int> trojuhelniky, Vector3 a, Vector3 b)
        {
            var osa = b - a;
            if (osa.sqrMagnitude < 1e-10f) return;

            var smer = osa.normalized;
            var pomocna = Mathf.Abs(Vector3.Dot(smer, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            var u = Vector3.Cross(smer, pomocna).normalized * (Tloustka * 0.5f);
            var v = Vector3.Cross(smer, u).normalized * (Tloustka * 0.5f);

            var zaklad = vrcholy.Count;

            vrcholy.Add(a - u - v); vrcholy.Add(a + u - v);
            vrcholy.Add(a + u + v); vrcholy.Add(a - u + v);
            vrcholy.Add(b - u - v); vrcholy.Add(b + u - v);
            vrcholy.Add(b + u + v); vrcholy.Add(b - u + v);

            int[] steny =
            {
                0,1,5, 0,5,4,
                1,2,6, 1,6,5,
                2,3,7, 2,7,6,
                3,0,4, 3,4,7,
                0,3,2, 0,2,1,
                4,5,6, 4,6,7
            };

            foreach (var i in steny) trojuhelniky.Add(zaklad + i);
        }
    }
}
