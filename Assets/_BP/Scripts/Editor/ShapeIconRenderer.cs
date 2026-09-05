using System.IO;
using BP.Core;
using UnityEditor;
using UnityEngine;

namespace BP.EditorTools
{
    /// <summary>
    /// Vyrenderuje ikony tvarů do PNG a naimportuje je jako sprity pro menu (skica b4).
    ///
    /// Renderuje se z jednoho pevného úhlu a s jedním materiálem, takže všech
    /// sedm ikon má stejnou vizuální váhu. Kdyby některý tvar vypadal v menu
    /// výrazněji než ostatní, přitahoval by pozornost a ovlivnil dobu hledání
    /// v menu — což je právě ta veličina, kterou experiment měří.
    /// </summary>
    public static class ShapeIconRenderer
    {
        private const string OutputFolder = "Assets/_BP/Icons";
        private const int Resolution = 256;

        /// <summary>Izometrický úhel, aby byla vidět trojrozměrnost tvaru.</summary>
        private static readonly Vector3 ViewAngle = new Vector3(22f, -34f, 0f);

        [MenuItem("BP/Generovat ikony tvaru")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputFolder);

            var library = AssetDatabase.LoadAssetAtPath<ShapeLibrary>("Assets/_BP/ShapeLibrary.asset");
            if (library == null)
            {
                Debug.LogError("[BP] Nenalezena ShapeLibrary.");
                return;
            }

            var iconMaterial = CreateIconMaterial();
            var paths = new System.Collections.Generic.List<string>();

            foreach (ShapeType shape in System.Enum.GetValues(typeof(ShapeType)))
            {
                var prefab = library.GetPrefab(shape);
                if (prefab == null) continue;

                var mf = prefab.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var png = Render(mf.sharedMesh, iconMaterial);
                var path = $"{OutputFolder}/Icon_{shape}.png";
                File.WriteAllBytes(path, png);
                paths.Add(path);
            }

            Object.DestroyImmediate(iconMaterial);
            AssetDatabase.Refresh();

            foreach (var p in paths) ConfigureAsSprite(p);

            AssetDatabase.Refresh();
            WireIntoLibrary(library);

            Debug.Log($"[BP] Vygenerováno {paths.Count} ikon do {OutputFolder}.");
        }

        private static Material CreateIconMaterial()
        {
            // Lit, ne Unlit: plochá výplň dá jen siluetu a kvádr, jehlan
            // i oktahedron pak vypadají jako nerozlišitelné tmavé kaňky.
            // Stínování je to, co dělá tvar rozeznatelným.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader);
            m.SetColor("_BaseColor", new Color(0.42f, 0.44f, 0.48f, 1f));
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        private static byte[] Render(Mesh mesh, Material material)
        {
            var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 8;

            var camGo = new GameObject("__IconCam__");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = ~0;
            cam.targetTexture = rt;
            cam.nearClipPlane = 0.001f;
            cam.farClipPlane = 10f;

            // Objekt se vykreslí daleko od scény, ať se do ikony nepletou
            // jiné objekty, které v ní zůstaly.
            var origin = new Vector3(0f, -500f, 0f);

            var subject = new GameObject("__IconSubject__");
            subject.transform.position = origin;
            subject.transform.rotation = Quaternion.Euler(ViewAngle);
            subject.AddComponent<MeshFilter>().sharedMesh = mesh;
            subject.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Vlastní světlo pro render: ikona nesmí záležet na osvětlení scény,
            // jinak by se její vzhled měnil podle toho, co je právě ve scéně.
            var lightGo = new GameObject("__IconLight__");
            lightGo.transform.position = origin;
            lightGo.transform.rotation = Quaternion.Euler(38f, 205f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = Color.white;
            light.shadows = LightShadows.None;

            // Ortho velikost podle obalového tělesa + rezerva, aby se tvar
            // nikdy neodřízl a všechny ikony měly stejný odstup od okraje.
            var radius = mesh.bounds.extents.magnitude;
            cam.orthographicSize = radius * 1.15f;
            cam.transform.position = origin - Vector3.forward * (radius * 4f);
            cam.transform.rotation = Quaternion.identity;

            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var png = tex.EncodeToPNG();

            // Pořadí úklidu: nejdřív odpojit RenderTexture od kamery,
            // teprve pak rušit objekty. Obráceně se přistupuje k už zničené kameře.
            cam.targetTexture = null;
            Object.DestroyImmediate(subject);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);

            return png;
        }

        private static void ConfigureAsSprite(string path)
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

        private static void WireIntoLibrary(ShapeLibrary library)
        {
            var so = new SerializedObject(library);
            var shapes = so.FindProperty("shapes");

            for (var i = 0; i < shapes.arraySize; i++)
            {
                var el = shapes.GetArrayElementAtIndex(i);
                var shape = (ShapeType)el.FindPropertyRelative("shape").enumValueIndex;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{OutputFolder}/Icon_{shape}.png");
                el.FindPropertyRelative("icon").objectReferenceValue = sprite;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }
    }
}
