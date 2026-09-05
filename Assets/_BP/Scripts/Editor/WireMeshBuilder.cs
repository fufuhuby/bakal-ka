using System.Collections.Generic;
using System.IO;
using BP.Core;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Vyrobí z tvaru drátový model — jeho hrany jako tenké hranolky.
    ///
    /// PROČ HRANOLKY A NE ČÁRY: Unity umí kreslit topologii Lines, ale ta má
    /// vždy tloušťku jednoho pixelu. Ve VR to znamená blikající vlásečnici,
    /// která se při pohybu hlavy rozpadá. Hranolek má reálnou tloušťku
    /// v metrech a chová se jako geometrie.
    ///
    /// PROČ VLASTNÍ HRUBÉ MESHE: koule z hlavního generátoru má 425 vrcholů,
    /// takže její drátový model by byl nečitelná změť. Pro šablonu se proto
    /// generují hrubší varianty — koule o osmi segmentech vypadá jako
    /// drátěný glóbus a je čitelná na první pohled.
    /// </summary>
    public static class WireMeshBuilder
    {
        private const string OutputFolder = "Assets/_BP/Meshes/Wire";

        /// <summary>Největší rozměr tvaru — musí odpovídat plným tvarům.</summary>
        private const float TargetSize = 0.06f;

        /// <summary>Tloušťka drátu v metrech.</summary>
        private const float WireThickness = 0.0018f;

        [MenuItem("BP/Generovat dratove modely")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputFolder);

            // Hrubé varianty: čitelný drátový model potřebuje málo hran.
            Save(ShapeType.Cube, Coarse.Cube());
            Save(ShapeType.Pyramid, Coarse.Pyramid());
            Save(ShapeType.Octahedron, Coarse.Octahedron());
            Save(ShapeType.Cylinder, Coarse.Cylinder(10));
            Save(ShapeType.Cone, Coarse.Cone(10));
            Save(ShapeType.Sphere, Coarse.Sphere(10, 6));
            Save(ShapeType.Torus, Coarse.Torus(12, 6, 0.40f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BP] Vygenerovány drátové modely do {OutputFolder}, tloušťka {WireThickness * 1000f} mm.");
        }

        private static void Save(ShapeType shape, List<Edge> edges)
        {
            var mesh = BuildWireMesh(edges);
            mesh.name = "Wire_" + shape;

            var path = $"{OutputFolder}/Wire_{shape}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                // Přepsání obsahu zachová GUID, takže se neodpojí reference.
                existing.Clear();
                existing.indexFormat = mesh.indexFormat;
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

        /// <summary>Jedna hrana mezi dvěma body.</summary>
        public struct Edge
        {
            public Vector3 A;
            public Vector3 B;
            public Edge(Vector3 a, Vector3 b) { A = a; B = b; }
        }

        /// <summary>
        /// Poskládá hranolky podél všech hran do jednoho meshe.
        /// Jeden mesh = jedno vykreslení, i když má tvar sto hran.
        /// </summary>
        private static Mesh BuildWireMesh(List<Edge> edges)
        {
            // Normalizace na stejnou velikost jako plné tvary.
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;
            foreach (var e in edges)
            {
                min = Vector3.Min(min, Vector3.Min(e.A, e.B));
                max = Vector3.Max(max, Vector3.Max(e.A, e.B));
            }

            var center = (min + max) * 0.5f;
            var size = max - min;
            var largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var factor = largest > 0.0001f ? TargetSize / largest : 1f;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var half = WireThickness * 0.5f;

            foreach (var e in edges)
            {
                var a = (e.A - center) * factor;
                var b = (e.B - center) * factor;

                var dir = b - a;
                if (dir.sqrMagnitude < 1e-10f) continue;
                dir.Normalize();

                // Libovolná kolmice na směr hrany
                var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
                var side = Vector3.Normalize(Vector3.Cross(dir, up)) * half;
                var vert = Vector3.Normalize(Vector3.Cross(dir, side)) * half;

                var i0 = verts.Count;

                // Čtyřboký hranolek: 4 rohy na každém konci
                verts.Add(a - side - vert); verts.Add(a + side - vert);
                verts.Add(a + side + vert); verts.Add(a - side + vert);
                verts.Add(b - side - vert); verts.Add(b + side - vert);
                verts.Add(b + side + vert); verts.Add(b - side + vert);

                int[] quads =
                {
                    0,1,5,4,  1,2,6,5,  2,3,7,6,  3,0,4,7,   // plášť
                    3,2,1,0,  4,5,6,7                        // konce
                };

                for (var q = 0; q < quads.Length; q += 4)
                {
                    tris.Add(i0 + quads[q]);     tris.Add(i0 + quads[q + 1]); tris.Add(i0 + quads[q + 2]);
                    tris.Add(i0 + quads[q]);     tris.Add(i0 + quads[q + 2]); tris.Add(i0 + quads[q + 3]);
                }
            }

            var m = new Mesh();
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Hrubé varianty tvarů — málo hran, aby byl drát čitelný.</summary>
        private static class Coarse
        {
            public static List<Edge> Cube()
            {
                var v = new[]
                {
                    new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                    new Vector3(.5f,.5f,-.5f),   new Vector3(-.5f,.5f,-.5f),
                    new Vector3(-.5f,-.5f,.5f),  new Vector3(.5f,-.5f,.5f),
                    new Vector3(.5f,.5f,.5f),    new Vector3(-.5f,.5f,.5f)
                };
                int[,] e = { {0,1},{1,2},{2,3},{3,0}, {4,5},{5,6},{6,7},{7,4}, {0,4},{1,5},{2,6},{3,7} };
                return FromIndices(v, e);
            }

            public static List<Edge> Pyramid()
            {
                var v = new[]
                {
                    new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f),
                    new Vector3(.5f,0,.5f),   new Vector3(-.5f,0,.5f),
                    new Vector3(0,1,0)
                };
                int[,] e = { {0,1},{1,2},{2,3},{3,0}, {0,4},{1,4},{2,4},{3,4} };
                return FromIndices(v, e);
            }

            public static List<Edge> Octahedron()
            {
                var v = new[]
                {
                    new Vector3(0,1,0), new Vector3(0,-1,0),
                    new Vector3(1,0,0), new Vector3(0,0,1),
                    new Vector3(-1,0,0), new Vector3(0,0,-1)
                };
                int[,] e = { {2,3},{3,4},{4,5},{5,2}, {0,2},{0,3},{0,4},{0,5}, {1,2},{1,3},{1,4},{1,5} };
                return FromIndices(v, e);
            }

            public static List<Edge> Cylinder(int seg)
            {
                var list = new List<Edge>();
                for (var i = 0; i < seg; i++)
                {
                    var t0 = 2f * Mathf.PI * i / seg;
                    var t1 = 2f * Mathf.PI * (i + 1) / seg;

                    var b0 = new Vector3(Mathf.Cos(t0) * .5f, -.5f, Mathf.Sin(t0) * .5f);
                    var b1 = new Vector3(Mathf.Cos(t1) * .5f, -.5f, Mathf.Sin(t1) * .5f);
                    var u0 = new Vector3(b0.x, .5f, b0.z);
                    var u1 = new Vector3(b1.x, .5f, b1.z);

                    list.Add(new Edge(b0, b1));
                    list.Add(new Edge(u0, u1));
                    list.Add(new Edge(b0, u0));
                }
                return list;
            }

            public static List<Edge> Cone(int seg)
            {
                var list = new List<Edge>();
                var apex = new Vector3(0, .5f, 0);
                for (var i = 0; i < seg; i++)
                {
                    var t0 = 2f * Mathf.PI * i / seg;
                    var t1 = 2f * Mathf.PI * (i + 1) / seg;
                    var b0 = new Vector3(Mathf.Cos(t0) * .5f, -.5f, Mathf.Sin(t0) * .5f);
                    var b1 = new Vector3(Mathf.Cos(t1) * .5f, -.5f, Mathf.Sin(t1) * .5f);

                    list.Add(new Edge(b0, b1));
                    list.Add(new Edge(b0, apex));
                }
                return list;
            }

            public static List<Edge> Sphere(int lon, int lat)
            {
                var list = new List<Edge>();
                System.Func<int, int, Vector3> pt = (x, y) =>
                {
                    var phi = Mathf.PI * y / lat;
                    var theta = 2f * Mathf.PI * x / lon;
                    return new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                       Mathf.Cos(phi),
                                       Mathf.Sin(phi) * Mathf.Sin(theta)) * .5f;
                };

                for (var y = 0; y <= lat; y++)
                for (var x = 0; x < lon; x++)
                {
                    if (y > 0 && y < lat) list.Add(new Edge(pt(x, y), pt(x + 1, y)));   // rovnoběžky
                    if (y < lat) list.Add(new Edge(pt(x, y), pt(x, y + 1)));            // poledníky
                }
                return list;
            }

            public static List<Edge> Torus(int major, int minor, float tube)
            {
                var list = new List<Edge>();
                var r = .5f - tube * .5f;

                System.Func<int, int, Vector3> pt = (i, j) =>
                {
                    var u = 2f * Mathf.PI * i / major;
                    var v = 2f * Mathf.PI * j / minor;
                    var rr = r + tube * .5f * Mathf.Cos(v);
                    return new Vector3(Mathf.Cos(u) * rr, tube * .5f * Mathf.Sin(v), Mathf.Sin(u) * rr);
                };

                for (var i = 0; i < major; i++)
                for (var j = 0; j < minor; j++)
                {
                    list.Add(new Edge(pt(i, j), pt(i + 1, j)));
                    list.Add(new Edge(pt(i, j), pt(i, j + 1)));
                }
                return list;
            }

            private static List<Edge> FromIndices(Vector3[] v, int[,] e)
            {
                var list = new List<Edge>();
                for (var i = 0; i < e.GetLength(0); i++) list.Add(new Edge(v[e[i, 0]], v[e[i, 1]]));
                return list;
            }
        }
    }
}
