using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Stálá cedule s vysvětlením úlohy a cíle.
    ///
    /// PROČ TRVALE VISÍ A NEZMIZÍ: úvodní panel se po startu bloku schová
    /// a participant se k němu už nedostane. Kdo si během stavby přestane
    /// být jistý, co má vlastně dělat, nemá se kde podívat — a začne
    /// experimentovat, což se propíše do completion time. Cedule stojí
    /// stranou, takže nepřekáží, ale je pořád po ruce.
    ///
    /// Text je zarovnaný VLEVO. Souvislý odstavec na střed se čte pomaleji,
    /// protože oko nemá pevnou hranu, ke které se vrací.
    /// </summary>
    public class InfoBoard : MonoBehaviour
    {
        [Header("Obsah")]
        [SerializeField] private string title = "O CO JDE";

        [TextArea(6, 20)]
        [SerializeField] private string body = "";

        [Header("Vzhled")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float width = 480f;
        [SerializeField] private float height = 420f;
        [SerializeField] private float canvasScale = 0.0007f;
        [SerializeField] private Color panelColor = PanelStyle.Window;
        [SerializeField] private Color titleColor = PanelStyle.Title;
        [SerializeField] private Color bodyColor = PanelStyle.TextSecondary;

        [Tooltip("Natočit ceduli k participantovi. Bere polohu hlavy při stavbě.")]
        [SerializeField] private bool faceHead = true;

        private const string CanvasName = "InfoCanvas";

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;

        private void Awake()
        {
            var canvas = transform.Find(CanvasName) as RectTransform;

            // Canvas se v Awake NERUŠÍ — TrackedDeviceGraphicRaycaster si drží
            // registraci a při zrušení canvasu v Awake padá interakce s UI.
            if (canvas == null) Build();
            else Bind(canvas);

            Refresh();
        }

        /// <summary>Přepíše text za běhu — pro ladění znění.</summary>
        public void SetText(string nadpis, string telo)
        {
            title = nadpis;
            body = telo;
            Refresh();
        }

        private void Refresh()
        {
            if (_title != null) _title.text = title ?? "";
            if (_body != null) _body.text = body ?? "";
        }

        private void Bind(RectTransform canvas)
        {
            var t = canvas.Find("Nadpis");
            if (t != null) _title = t.GetComponent<TextMeshProUGUI>();

            var b = canvas.Find("Telo");
            if (b != null) _body = b.GetComponent<TextMeshProUGUI>();
        }

        [ContextMenu("Postavit ceduli")]
        public void Build()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (c.name != CanvasName) continue;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }

            var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var canvas = (RectTransform)go.transform;
            canvas.sizeDelta = new Vector2(width, height);
            canvas.localScale = Vector3.one * canvasScale;
            canvas.localPosition = Vector3.zero;
            canvas.localRotation = Quaternion.identity;

            var bg = Novy("Pozadi", canvas, Vector2.zero, new Vector2(width, height));
            PanelStyle.ApplyRounded(bg.AddComponent<Image>(), PanelStyle.RadiusWindow,
                PanelStyle.Window);

            const float pad = 24f;
            const float nadpisH = 38f;
            var top = height * 0.5f - pad;

            _title = Text(Novy("Nadpis", canvas, new Vector2(0f, top - nadpisH * 0.5f),
                new Vector2(width - pad * 2f, nadpisH)), 26f,
                TextAlignmentOptions.Left, titleColor);

            var teloH = height - pad * 2f - nadpisH - 10f;
            _body = Text(Novy("Telo", canvas,
                new Vector2(0f, top - nadpisH - 10f - teloH * 0.5f),
                new Vector2(width - pad * 2f, teloH)), 18f,
                TextAlignmentOptions.TopLeft, bodyColor);

            transform.localRotation = Quaternion.identity;

            if (faceHead && Camera.main != null)
            {
                var smer = transform.position - Camera.main.transform.position;
                if (smer.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(smer, Vector3.up);
            }

            Bind(canvas);
            Refresh();
        }

        private TextMeshProUGUI Text(GameObject go, float velikost,
            TextAlignmentOptions zarovnani, Color barva)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();

            // Font jako první: sazba i obrys zapisují do materiálu fontu.
            if (font != null) tmp.font = font;
            tmp.fontSize = velikost;
            tmp.alignment = zarovnani;
            tmp.color = barva;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.lineSpacing = 8f;
            return tmp;
        }

        private static GameObject Novy(string jmeno, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(jmeno, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return go;
        }
    }
}
