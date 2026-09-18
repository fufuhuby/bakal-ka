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
            // Osm spojnic. Čtyři stačily na to, aby byl tvar poznat, ale
            // plášť pak působil prázdně vedle koule a torusu, které mají čar
            // víc. Kruhy jsou na počtu spojnic nezávislé, takže je zahuštění
            // nijak nezhrubne.
            Save(ShapeType.Cylinder, Coarse.Cylinder(8));
            Save(ShapeType.Cone, Coarse.Cone(8));

            // MÉNĚ SEGMENTŮ, NEŽ BY SE ZDÁLO. Koule i torus byly při dvanácti
            // a deseti dílcích na šesti centimetrech jen změť čar — drát je
            // silný 1,8 mm, takže se sousední tahy slily. Osm a šest dílců
            // dá pořád poznat, že je to koule, a jednotlivé čáry jdou od sebe
            // rozeznat.
            // KOULE: čtyři poledníky a tři rovnoběžky, drát tenčí než
            // u hranatých tvarů. Má sedm čar dlouhých přes celý obvod, kdežto
            // krychle dvanáct krátkých hran — při stejné tloušťce by koule
            // nesla skoro o polovinu víc barvy a v předloze by opticky
            // převažovala nad ostatními tvary.
            Save(ShapeType.Sphere, Coarse.Sphere(4, 3), 0.0014f);
            // TORUS MÁ TENČÍ DRÁT. Je vysoký jen 24 mm, takže 1,8 mm je
            // u něj 7,5 % rozměru, kdežto u krychle 3 % — příčné kroužky se
            // pak slily do klubka. Při jednom milimetru drží stejnou váhu
            // jako ostatní tvary.
            //
            // KOLIK ČAR: šest příčných kroužků a čtyři podélné.
            //
            // Podélné čáry vycházejí po obvodu trubky rovnoměrně, takže při
            // čtyřech vedou vnějškem, vrchem, vnitřkem a spodkem — dvě z nich
            // obkreslují obrys a díru, další dvě běží po hřbetu a po břiše
            // malých kroužků. Osmi a víc kroužky se tvar slévá do klubka,
            // šest je hranice čitelnosti.
            Save(ShapeType.Torus, Coarse.Torus(6, 4, 0.40f), 0.0010f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BP] Vygenerovány drátové modely do {OutputFolder}, tloušťka {WireThickness * 1000f} mm.");
        }

        private static void Save(ShapeType shape, List<Edge> edges, float thickness = WireThickness)
        {
            var mesh = BuildWireMesh(edges, thickness);
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
        private static Mesh BuildWireMesh(List<Edge> edges, float thickness = WireThickness)
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

            // KOSTRA SE ZMENŠÍ O TLOUŠŤKU DRÁTU, ne na plnou velikost tvaru.
            // Hranolek se osazuje na hranu, takže polovina jeho tloušťky
            // trčí ven. Když se kostra normalizovala rovnou na 60 mm, měl
            // drátový model 61,8 mm a o těch 0,9 mm na každé straně zajížděl
            // do sousedního tvaru. U krychle to bylo vidět nejvíc: její horní
            // stěna leží celá v rovině dotyku, kdežto koule nebo jehlan se
            // téhle roviny dotýkají jen bodem, kde není do čeho zajet.
            // Hlídá se KAŽDÁ OSA, ne jen ta největší. Tvar má po normalizaci
            // rozměr TargetSize * size[i] / largest; drát k němu přidá svou
            // tloušťku celou, bez ohledu na to, jak je ta osa krátká. U torusu
            // je svislý rozměr jen 40 % vodorovného, takže na něj 1,2 mm drátu
            // dopadne třikrát tíž — a model by byl vyšší než plný tvar a zajel
            // by do souseda nad sebou.
            var factor = 1f;

            if (largest > 0.0001f)
            {
                factor = TargetSize / largest;

                foreach (var osa in new[] { size.x, size.y, size.z })
                {
                    if (osa <= 0.0001f) continue;
                    factor = Mathf.Min(factor, TargetSize / largest - thickness / osa);
                }
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var half = thickness * 0.5f;

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

            /// <summary>
            /// Drátový válec: kruh dole, kruh nahoře a svislé čáry mezi nimi.
            ///
            /// POČET SVISLÝCH ČAR A HLADKOST KRUHŮ ZVLÁŠŤ. Dřív se kruh
            /// kreslil z tolika dílků, kolik bylo svislic — osm svislic tedy
            /// znamenalo osmiúhelníkovou podstavu a přidat kulatost šlo jen
            /// tak, že se přidaly další svislé čáry.
            /// </summary>
            public static List<Edge> Cylinder(int svislych)
            {
                var list = new List<Edge>();

                Kruh(list, -.5f, .5f, KruhovaHladkost);
                Kruh(list, .5f, .5f, KruhovaHladkost);

                for (var i = 0; i < svislych; i++)
                {
                    var t = 2f * Mathf.PI * i / svislych;
                    var x = Mathf.Cos(t) * .5f;
                    var z = Mathf.Sin(t) * .5f;
                    list.Add(new Edge(new Vector3(x, -.5f, z), new Vector3(x, .5f, z)));
                }

                return list;
            }

            /// <summary>Drátový kužel: kruh dole a čáry k vrcholu.</summary>
            public static List<Edge> Cone(int svislych)
            {
                var list = new List<Edge>();
                var vrchol = new Vector3(0, .5f, 0);

                Kruh(list, -.5f, .5f, KruhovaHladkost);

                for (var i = 0; i < svislych; i++)
                {
                    var t = 2f * Mathf.PI * i / svislych;
                    list.Add(new Edge(
                        new Vector3(Mathf.Cos(t) * .5f, -.5f, Mathf.Sin(t) * .5f), vrchol));
                }

                return list;
            }

            /// <summary>Dílků na jeden kruh. Nepřidávají čáry, jen je vyhlazují.</summary>
            private const int KruhovaHladkost = 44;

            private static void Kruh(List<Edge> list, float y, float r, int dilku)
            {
                for (var i = 0; i < dilku; i++)
                {
                    var t0 = 2f * Mathf.PI * i / dilku;
                    var t1 = 2f * Mathf.PI * (i + 1) / dilku;
                    list.Add(new Edge(
                        new Vector3(Mathf.Cos(t0) * r, y, Mathf.Sin(t0) * r),
                        new Vector3(Mathf.Cos(t1) * r, y, Mathf.Sin(t1) * r)));
                }
            }

            /// <summary>
            /// Drátová koule: poledníky od pólu k pólu a rovnoběžky kolem dokola.
            ///
            /// POČET ČAR A JEJICH HLADKOST ZVLÁŠŤ, ze stejného důvodu jako
            /// u torusu. Při pravidelné mřížce byla rovnoběžka nakreslená
            /// z tolika dílků, kolik bylo poledníků — osm poledníků tedy
            /// znamenalo osmiúhelníkovou rovnoběžku. Přidat kulatost se
            /// nedalo jinak než přidat čáry, a naopak.
            /// </summary>
            public static List<Edge> Sphere(int poledniku, int rovnobezek)
            {
                const int hladkost = 44;

                var list = new List<Edge>();

                System.Func<float, float, Vector3> bod = (phi, theta) =>
                    new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                Mathf.Cos(phi),
                                Mathf.Sin(phi) * Mathf.Sin(theta)) * .5f;

                // Poledníky: půlkruh od severního pólu k jižnímu.
                for (var m = 0; m < poledniku; m++)
                {
                    var theta = Mathf.PI * m / poledniku;   // půlka stačí, druhá je táž kružnice

                    for (var i = 0; i < hladkost; i++)
                    {
                        var p0 = 2f * Mathf.PI * i / hladkost;
                        var p1 = 2f * Mathf.PI * (i + 1) / hladkost;
                        list.Add(new Edge(bod(p0, theta), bod(p1, theta)));
                    }
                }

                // Rovnoběžky: kružnice v pevné výšce, rozložené mezi póly.
                for (var k = 1; k <= rovnobezek; k++)
                {
                    var phi = Mathf.PI * k / (rovnobezek + 1);

                    for (var i = 0; i < hladkost; i++)
                    {
                        var t0 = 2f * Mathf.PI * i / hladkost;
                        var t1 = 2f * Mathf.PI * (i + 1) / hladkost;
                        list.Add(new Edge(bod(phi, t0), bod(phi, t1)));
                    }
                }

                return list;
            }

            /// <summary>
            /// Drátový torus: příčné kroužky kolem trubky a podélné čáry dokola.
            ///
            /// HUSTOTA ČAR A JEJICH HLADKOST JSOU DVĚ RŮZNÉ VĚCI. Dřív se
            /// kreslila pravidelná mřížka, takže každé zjemnění křivky přidalo
            /// i čáru navíc: při dvanácti dílcích byl torus změť a při osmi
            /// zase hranatý mnohoúhelník. Tady určuje počet ČAR parametr
            /// a jejich HLADKOST konstanta, takže jde mít pár čar, a přesto
            /// kulatých.
            /// </summary>
            public static List<Edge> Torus(int prstencu, int podelnych, float tube)
            {
                // Dílků na jednu čáru. Nepřidávají čáry, jen je vyhlazují.
                const int hladkostPrstence = 22;
                const int hladkostObvodu = 44;

                var list = new List<Edge>();
                var r = .5f - tube * .5f;

                System.Func<float, float, Vector3> bod = (u, v) =>
                {
                    var rr = r + tube * .5f * Mathf.Cos(v);
                    return new Vector3(Mathf.Cos(u) * rr, tube * .5f * Mathf.Sin(v), Mathf.Sin(u) * rr);
                };

                // Příčné kroužky: říkají, že je to trubka.
                for (var i = 0; i < prstencu; i++)
                {
                    var u = 2f * Mathf.PI * i / prstencu;

                    for (var j = 0; j < hladkostPrstence; j++)
                    {
                        var v0 = 2f * Mathf.PI * j / hladkostPrstence;
                        var v1 = 2f * Mathf.PI * (j + 1) / hladkostPrstence;
                        list.Add(new Edge(bod(u, v0), bod(u, v1)));
                    }
                }

                // Podélné čáry: ty nesou obrys, takže musí být hladké.
                for (var m = 0; m < podelnych; m++)
                {
                    var v = 2f * Mathf.PI * m / podelnych;

                    for (var i = 0; i < hladkostObvodu; i++)
                    {
                        var u0 = 2f * Mathf.PI * i / hladkostObvodu;
                        var u1 = 2f * Mathf.PI * (i + 1) / hladkostObvodu;
                        list.Add(new Edge(bod(u0, v), bod(u1, v)));
                    }
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
