using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Generátor 7 tvarů pro experiment. Unity umí z primitivů jen kvádr,
    /// kuličku a válec — torus, jehlan, oktahedron a kužel se musí vyrobit.
    ///
    /// KLÍČOVÉ: všechny meshe se na konci normalizují na stejnou velikost
    /// obalového tělesa a vycentrují na počátek. Kdyby měl jeden tvar jiný
    /// rozměr než ostatní, byl by systematicky snazší nebo těžší na uchopení
    /// a umístění — a tvar by se stal skrytou proměnnou v datech.
    /// </summary>
    public static class PrimitiveMeshBuilder
    {
        private const string OutputFolder = "Assets/_BP/Meshes";

        /// <summary>Největší rozměr každého tvaru v metrech.</summary>
        private const float TargetSize = 0.06f;

        /// <summary>Segmenty po obvodu u kulatých tvarů.</summary>
        /// <summary>
        /// Dílků po obvodu. Rozhoduje o tom, jestli je na obrysu vidět
        /// mnohoúhelník — stínování může být hladké, ale silueta je pořád
        /// z úseček. Při 6 cm a pohledu z půl metru je 24 dílků na hraně
        /// rozeznatelnosti, 32 už ne.
        /// </summary>
        private const int RadialSegments = 32;

        [MenuItem("BP/Generovat meshe tvaru")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputFolder);

            Save("Shape_Cube", BuildCube());
            Save("Shape_Sphere", BuildSphere(RadialSegments, 16));
            Save("Shape_Cylinder", BuildCylinder(RadialSegments));
            Save("Shape_Cone", BuildCone(RadialSegments));
            // Tubus 0.40: silnější než původních 0.32, aby byl torus objemově
            // srovnatelný s ostatními tvary a stejně dobře uchopitelný.
            // Nad 0.50 se díra uzavře úplně (poloměr díry = 0.5 - tubeRatio).
            Save("Shape_Torus", BuildTorus(RadialSegments, 20, 0.40f));
            Save("Shape_Pyramid", BuildPyramid());
            Save("Shape_Octahedron", BuildOctahedron());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BP] Vygenerovano 7 meshu do " + OutputFolder + ", cilova velikost " + (TargetSize * 100f) + " cm.");
        }

        private static void Save(string name, Mesh mesh)
        {
            Normalize(mesh);
            mesh.name = name;

            var path = OutputFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                // Přepsání obsahu existujícího assetu zachová GUID,
                // takže se neodpojí reference z prefabů a ShapeLibrary.
                existing.Clear();
                existing.vertices = mesh.vertices;
                existing.normals = mesh.normals;
                existing.triangles = mesh.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
        }

        /// <summary>Vycentruje na počátek a nascaluje tak, aby největší rozměr byl TargetSize.</summary>
        private static void Normalize(Mesh mesh)
        {
            var verts = mesh.vertices;
            if (verts.Length == 0) return;

            var min = verts[0];
            var max = verts[0];
            foreach (var v in verts)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            var center = (min + max) * 0.5f;
            var size = max - min;
            var largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var factor = largest > 0.0001f ? TargetSize / largest : 1f;

            for (var i = 0; i < verts.Length; i++)
                verts[i] = (verts[i] - center) * factor;

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // ---- Tvary s ostrými hranami: vrcholy se nesdílejí, aby normály nezaoblily hrany ----

        private static Mesh BuildCube()
        {
            var v = new[]
            {
                new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f),
                new Vector3(.5f, .5f, -.5f),   new Vector3(-.5f, .5f, -.5f),
                new Vector3(-.5f, -.5f, .5f),  new Vector3(.5f, -.5f, .5f),
                new Vector3(.5f, .5f, .5f),    new Vector3(-.5f, .5f, .5f)
            };

            var quads = new[]
            {
                new[] { 0, 3, 2, 1 }, new[] { 5, 6, 7, 4 }, new[] { 4, 7, 3, 0 },
                new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 4, 0, 1, 5 }
            };

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var q in quads) AddQuad(verts, tris, v[q[0]], v[q[1]], v[q[2]], v[q[3]]);

            return Assemble(verts, tris);
        }

        private static Mesh BuildPyramid()
        {
            var a = new Vector3(-.5f, 0f, -.5f);
            var b = new Vector3(.5f, 0f, -.5f);
            var c = new Vector3(.5f, 0f, .5f);
            var d = new Vector3(-.5f, 0f, .5f);
            var apex = new Vector3(0f, 1f, 0f);

            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Podstava musí koukat DOLŮ. Obrácené pořadí ji otočilo nahoru,
            // takže se zespodu odřízla a do jehlanu bylo vidět skrz dno.
            AddQuad(verts, tris, a, b, c, d);
            AddTri(verts, tris, a, b, apex);
            AddTri(verts, tris, b, c, apex);
            AddTri(verts, tris, c, d, apex);
            AddTri(verts, tris, d, a, apex);

            return Assemble(verts, tris);
        }

        private static Mesh BuildOctahedron()
        {
            var top = new Vector3(0, 1, 0);
            var bottom = new Vector3(0, -1, 0);
            var e = new[]
            {
                new Vector3(1, 0, 0), new Vector3(0, 0, 1),
                new Vector3(-1, 0, 0), new Vector3(0, 0, -1)
            };

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (var i = 0; i < 4; i++)
            {
                var n = (i + 1) % 4;
                AddTri(verts, tris, e[i], e[n], top);
                AddTri(verts, tris, e[n], e[i], bottom);
            }

            return Assemble(verts, tris);
        }

        // ---- Kulaté tvary: prstence se sdílejí, RecalculateNormals je vyhladí ----

        private static Mesh BuildSphere(int lon, int lat)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (var y = 0; y <= lat; y++)
            {
                var phi = Mathf.PI * y / lat;
                for (var x = 0; x <= lon; x++)
                {
                    var theta = 2f * Mathf.PI * x / lon;
                    verts.Add(new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta)) * 0.5f);
                }
            }

            for (var y = 0; y < lat; y++)
            {
                for (var x = 0; x < lon; x++)
                {
                    var i0 = y * (lon + 1) + x;
                    var i1 = i0 + 1;
                    var i2 = i0 + lon + 1;
                    var i3 = i2 + 1;
                    // POZOR NA POŘADÍ: tenhle pás měl vinutí obrácené proti
                    // zbytku knihovny, takže RecalculateNormals() otočil
                    // všechny normály dovnitř. Přední stěny se pak odřízly
                    // a skrz povrch byla vidět vnitřní strana tělesa.
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i1); tris.Add(i3); tris.Add(i2);
                }
            }

            return Assemble(verts, tris);
        }

        private static Mesh BuildCylinder(int seg)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (var y = 0; y <= 1; y++)
            {
                for (var i = 0; i <= seg; i++)
                {
                    var t = 2f * Mathf.PI * i / seg;
                    verts.Add(new Vector3(Mathf.Cos(t) * 0.5f, y - 0.5f, Mathf.Sin(t) * 0.5f));
                }
            }

            for (var i = 0; i < seg; i++)
            {
                var b = i;
                var tp = i + seg + 1;
                tris.Add(b); tris.Add(tp); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(tp); tris.Add(tp + 1);
            }

            AddCap(verts, tris, seg, 0.5f, true);
            AddCap(verts, tris, seg, -0.5f, false);

            return Assemble(verts, tris);
        }

        private static Mesh BuildCone(int seg)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // PLÁŠŤ SDÍLÍ VRCHOLY PO OBVODU, hrot ne.
            //
            // Dřív měl každý trojúhelník pláště vlastní tři vrcholy, takže
            // RecalculateNormals nemělo co průměrovat a kužel byl fazetovaný
            // jako broušený kámen. Když se prstenec u podstavy sdílí, normála
            // v každém jeho vrcholu vyjde jako průměr obou sousedních stěn
            // a plášť se stíní plynule.
            //
            // Hrot zůstává rozdělený: v jediném bodě se sbíhají všechny stěny
            // a jejich průměrná normála míří rovnou vzhůru, což by špičku
            // rozmazalo do kulata.
            var prstenec = verts.Count;

            for (var i = 0; i < seg; i++)
            {
                var t = 2f * Mathf.PI * i / seg;
                verts.Add(new Vector3(Mathf.Cos(t) * 0.5f, -0.5f, Mathf.Sin(t) * 0.5f));
            }

            for (var i = 0; i < seg; i++)
            {
                var a = prstenec + i;
                var b = prstenec + (i + 1) % seg;

                var hrot = verts.Count;
                verts.Add(new Vector3(0f, 0.5f, 0f));

                // Pořadí odpovídá tomu, jak vinula AddTri(b0, b1, vrchol).
                tris.Add(a); tris.Add(hrot); tris.Add(b);
            }

            AddCap(verts, tris, seg, -0.5f, false);

            return Assemble(verts, tris);
        }

        private static Mesh BuildTorus(int major, int minor, float tubeRatio)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var r = 0.5f - tubeRatio * 0.5f;

            for (var i = 0; i <= major; i++)
            {
                var u = 2f * Mathf.PI * i / major;
                var dir = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));

                for (var j = 0; j <= minor; j++)
                {
                    var vAng = 2f * Mathf.PI * j / minor;
                    var rr = r + tubeRatio * 0.5f * Mathf.Cos(vAng);
                    verts.Add(new Vector3(dir.x * rr, tubeRatio * 0.5f * Mathf.Sin(vAng), dir.z * rr));
                }
            }

            for (var i = 0; i < major; i++)
            {
                for (var j = 0; j < minor; j++)
                {
                    var i0 = i * (minor + 1) + j;
                    var i1 = i0 + 1;
                    var i2 = i0 + minor + 1;
                    var i3 = i2 + 1;
                    // POZOR NA POŘADÍ: tenhle pás měl vinutí obrácené proti
                    // zbytku knihovny, takže RecalculateNormals() otočil
                    // všechny normály dovnitř. Přední stěny se pak odřízly
                    // a skrz povrch byla vidět vnitřní strana tělesa.
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i1); tris.Add(i3); tris.Add(i2);
                }
            }

            return Assemble(verts, tris);
        }

        // ---- Pomocné ----

        private static void AddCap(List<Vector3> verts, List<int> tris, int seg, float y, bool up)
        {
            var center = new Vector3(0f, y, 0f);
            for (var i = 0; i < seg; i++)
            {
                var t0 = 2f * Mathf.PI * i / seg;
                var t1 = 2f * Mathf.PI * (i + 1) / seg;
                var p0 = new Vector3(Mathf.Cos(t0) * 0.5f, y, Mathf.Sin(t0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(t1) * 0.5f, y, Mathf.Sin(t1) * 0.5f);

                if (up) AddTri(verts, tris, center, p0, p1);
                else AddTri(verts, tris, center, p1, p0);
            }
        }

        private static void AddTri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
        {
            var i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
        }

        private static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        private static Mesh Assemble(List<Vector3> verts, List<int> tris)
        {
            var m = new Mesh();
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
