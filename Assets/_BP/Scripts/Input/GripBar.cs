using UnityEngine;

namespace BP.Input
{
    /// <summary>
    /// Lišta pod oknem, za kterou se okno chytá a přetahuje.
    ///
    /// Síť se staví za běhu, ne z modelu: lišta má být zaoblená a zároveň
    /// široká a nízká. Kdyby se použila krychle s nerovnoměrným měřítkem
    /// (6 cm × 1,4 cm), rozmázlo by se zaoblení do elipsy — poloměr rohu
    /// se škáluje spolu s tělesem. Proto se síť rovnou generuje ve skutečných
    /// rozměrech a měřítko objektu zůstává jednotkové.
    ///
    /// LIŠTA JE JEN VIZUÁL. Uchopení řeší collider a XRGrabInteractable na
    /// kořeni okna — úchyt, který by zároveň posouval sám sebe, je zpětná
    /// smyčka (viz MenuDragHandle).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    public class GripBar : MonoBehaviour
    {
        [Header("Rozměry v metrech")]
        [Tooltip("Lišta je jen úchyt. Drží se malá schválně: je to jediný prvek " +
                 "panelu, který s úlohou nesouvisí, a čím větší je, tím víc " +
                 "přetahuje pozornost z dlaždic, mezi kterými se hledá.")]
        [SerializeField] private float width = 0.040f;
        [SerializeField] private float height = 0.010f;
        [SerializeField] private float depth = 0.009f;

        [Tooltip("Poloměr rohu. Ořízne se na polovinu kratší strany.")]
        [SerializeField] private float radius = 0.005f;

        [Tooltip("Dílků na jeden roh. Víc než 8 už při této velikosti nikdo nepozná.")]
        [SerializeField] private int cornerSegments = 6;

        private Mesh _mesh;

        private void Awake() => Postavit();

        private void OnValidate()
        {
            width = Mathf.Max(0.001f, width);
            height = Mathf.Max(0.001f, height);
            depth = Mathf.Max(0.001f, depth);
            cornerSegments = Mathf.Clamp(cornerSegments, 1, 16);

            // OnValidate běží i při načtení scény, kdy se se scénou pracovat
            // nesmí; postavení se odloží o snímek, jinak Unity vypíše varování
            // o měnění objektů v nepovolené chvíli.
            if (Application.isPlaying) Postavit();
#if UNITY_EDITOR
            else UnityEditor.EditorApplication.delayCall += BezpecneVEditoru;
#endif
        }

#if UNITY_EDITOR
        private void BezpecneVEditoru()
        {
            if (this == null) return;
            Postavit();
        }
#endif

        /// <summary>
        /// Nastaví rozměry a rovnou přestaví síť. Používá okno s příkazy,
        /// které si lištu vyrábí za běhu a nemá ji jak serializovat.
        /// </summary>
        public void Nastavit(float sirka, float vyska, float hloubka)
        {
            width = sirka;
            height = vyska;
            depth = hloubka;
            radius = Mathf.Min(width, height) * 0.5f;
            Postavit();
        }

        [ContextMenu("Postavit lištu")]
        public void Postavit()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) return;

            var r = Mathf.Min(radius, Mathf.Min(width, height) * 0.5f);
            var obrys = Obrys(width * 0.5f, height * 0.5f, r);

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            var z = depth * 0.5f;

            // Přední a zadní stěna jako vějíř ze středu. Obrys je konvexní,
            // takže vějíř nikdy nevytvoří překřížený trojúhelník.
            Vejir(verts, tris, obrys, -z, false);
            Vejir(verts, tris, obrys, z, true);

            // Plášť: každá hrana obrysu jeden obdélník. Vrcholy se nesdílejí
            // se stěnami, aby hrana zůstala ostrá a stěna plochá.
            for (var i = 0; i < obrys.Length; i++)
            {
                var a = obrys[i];
                var b = obrys[(i + 1) % obrys.Length];

                var zaklad = verts.Count;
                verts.Add(new Vector3(a.x, a.y, -z));
                verts.Add(new Vector3(b.x, b.y, -z));
                verts.Add(new Vector3(b.x, b.y, z));
                verts.Add(new Vector3(a.x, a.y, z));

                // POŘADÍ VRCHOLŮ: takhle míří stěna ven. Obrácené vinutí se
                // pozná těžko — lišta vypadá celá, jen se z boku dívá skrz ni
                // dovnitř, protože přední stěny se odřezávají. Kontrola je
                // znaménko objemu sítě: musí vyjít kladné.
                tris.Add(zaklad); tris.Add(zaklad + 1); tris.Add(zaklad + 2);
                tris.Add(zaklad); tris.Add(zaklad + 2); tris.Add(zaklad + 3);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "GripBar", hideFlags = HideFlags.DontSave };
            }

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            mf.sharedMesh = _mesh;
        }

        /// <summary>Body obrysu proti směru hodinových ručiček, začíná vpravo dole.</summary>
        private Vector2[] Obrys(float halfW, float halfH, float r)
        {
            var body = new System.Collections.Generic.List<Vector2>();

            // Středy čtyř rohových oblouků, pořadí určuje směr obcházení.
            var stredy = new[]
            {
                new Vector2(halfW - r, -halfH + r),
                new Vector2(halfW - r, halfH - r),
                new Vector2(-halfW + r, halfH - r),
                new Vector2(-halfW + r, -halfH + r),
            };

            for (var roh = 0; roh < 4; roh++)
            {
                var od = -Mathf.PI * 0.5f + roh * Mathf.PI * 0.5f;

                for (var i = 0; i <= cornerSegments; i++)
                {
                    var uhel = od + Mathf.PI * 0.5f * i / cornerSegments;
                    body.Add(stredy[roh] + new Vector2(Mathf.Cos(uhel), Mathf.Sin(uhel)) * r);
                }
            }

            return body.ToArray();
        }

        private static void Vejir(System.Collections.Generic.List<Vector3> verts,
            System.Collections.Generic.List<int> tris, Vector2[] obrys, float z, bool dopredu)
        {
            var stred = verts.Count;
            verts.Add(new Vector3(0f, 0f, z));

            foreach (var p in obrys) verts.Add(new Vector3(p.x, p.y, z));

            for (var i = 0; i < obrys.Length; i++)
            {
                var a = stred + 1 + i;
                var b = stred + 1 + (i + 1) % obrys.Length;

                if (dopredu) { tris.Add(stred); tris.Add(a); tris.Add(b); }
                else { tris.Add(stred); tris.Add(b); tris.Add(a); }
            }
        }
    }
}
