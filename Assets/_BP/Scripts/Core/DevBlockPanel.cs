using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Ovládací panel pro vývoj a testování — přeskakování bloků přímo
    /// z headsetu, bez klávesnice.
    ///
    /// PROČ SAMOSTATNÝ PANEL A NE TLAČÍTKO V SESSION MENU: session menu vidí
    /// i participant a jeho tlačítka jsou součástí protokolu. Operátorské
    /// funkce musí být vizuálně i prostorově oddělené, aby si je nikdo
    /// nespletl s úlohou.
    ///
    /// PŘED MĚŘENÍM SE MUSÍ VYPNOUT. Participant, který si přeskočí blok,
    /// znehodnotí celou session — proto je panel červený a přeskočení se
    /// zapisuje do logu.
    /// </summary>
    public class DevBlockPanel : MonoBehaviour
    {
        [Header("Závislosti")]
        [SerializeField] private TrialManager trialManager;
        [SerializeField] private TMP_FontAsset font;

        [Header("Zobrazení")]
        [Tooltip("VYPNOUT PŘED MĚŘENÍM. Zapnuté = participant si může " +
                 "přeskočit blok a data ze session jsou k ničemu.")]
        [SerializeField] private bool showPanel = true;

        [Tooltip("Umístění vůči rodiči. Panel visí v prostoru vedle plánku, " +
                 "ne na hlavě — připnutý k hlavě mizí ze zorného pole vždycky, " +
                 "když se člověk dívá na pracoviště.")]
        [SerializeField] private Vector3 localPosition = new Vector3(-0.62f, 1.15f, 0.30f);

        [Tooltip("Natočit panel k participantovi. Bere polohu hlavy při stavbě.")]
        [SerializeField] private bool faceHead = true;

        [SerializeField] private float width = 260f;
        [SerializeField] private float canvasScale = 0.0007f;

        [Header("Barvy")]
        [Tooltip("Výrazně odlišná od panelu session, aby nešlo zaměnit.")]
        [SerializeField] private Color panelColor = new Color(0.38f, 0.08f, 0.08f, 0.92f);
        [SerializeField] private Color buttonColor = new Color(0.62f, 0.16f, 0.16f, 1f);

        private const string CanvasName = "DevCanvas";
        private const float ButtonHeight = 46f;
        private const float Padding = 12f;

        private TextMeshProUGUI _status;

        private void Awake()
        {
            var canvas = transform.Find(CanvasName) as RectTransform;

            // Canvas se v Awake NERUŠÍ a nestaví znovu — TrackedDeviceGraphicRaycaster
            // si registraci drží ve slovníku a při zrušení canvasu v Awake
            // přestane fungovat interakce s UI v celé scéně.
            if (canvas == null) Build();
            else Bind(canvas);

            ApplyVisibility();
        }

        private void OnEnable()
        {
            if (trialManager == null) return;
            trialManager.BlockStarted += OnBlockChanged;
            trialManager.BlockEnded += OnBlockChanged;
            trialManager.SessionEnded += RefreshStatus;
        }

        private void OnDisable()
        {
            if (trialManager == null) return;
            trialManager.BlockStarted -= OnBlockChanged;
            trialManager.BlockEnded -= OnBlockChanged;
            trialManager.SessionEnded -= RefreshStatus;
        }

        private void OnBlockChanged(BlockDefinition block, int index) => RefreshStatus();

        private void ApplyVisibility()
        {
            var canvas = transform.Find(CanvasName);
            if (canvas != null) canvas.gameObject.SetActive(showPanel);
        }

        /// <summary>Zapne nebo vypne panel za běhu — pro operátora.</summary>
        public void SetVisible(bool value)
        {
            showPanel = value;
            ApplyVisibility();
        }

        private void RefreshStatus()
        {
            if (_status == null || trialManager == null) return;

            // Pred prvnim blokem je index -1; vypsat "blok 0" by matlo.
            if (trialManager.CurrentBlockIndex < 0)
            {
                _status.text = "pred startem  (" + trialManager.BlockCount + " bloku)";
                return;
            }

            _status.text = trialManager.SessionComplete
                ? "session dokoncena"
                : "blok " + (trialManager.CurrentBlockIndex + 1) + " / " + trialManager.BlockCount
                  + (trialManager.BlockRunning ? "  bezi" : "  stoji");
        }

        // ---- Akce ----

        private void DalsiBlok()
        {
            if (trialManager == null) return;

            // Běžící blok se musí nejdřív ukončit, jinak StartNextBlock
            // odmítne pokračovat. Důvod jde do logu, ať je v datech vidět,
            // že blok neskončil splněním úlohy.
            if (trialManager.BlockRunning) trialManager.EndCurrentBlock("preskoceno vyvojarem");

            trialManager.StartNextBlock();
            RefreshStatus();
        }

        private void Zopakovat()
        {
            if (trialManager == null) return;
            trialManager.RepeatCurrentBlock();
            RefreshStatus();
        }

        private void OdZacatku()
        {
            if (trialManager == null) return;
            if (trialManager.BlockRunning) trialManager.EndCurrentBlock("restart vyvojarem");
            trialManager.StartSession();
            RefreshStatus();
        }

        // ---- Stavba a navěšení ----

        private void Bind(RectTransform canvas)
        {
            var s = canvas.Find("Status");
            if (s != null) _status = s.GetComponent<TextMeshProUGUI>();

            Navesit(canvas, "Btn_Dalsi", DalsiBlok);
            Navesit(canvas, "Btn_Znovu", Zopakovat);
            Navesit(canvas, "Btn_Restart", OdZacatku);

            RefreshStatus();
        }

        private static void Navesit(RectTransform canvas, string jmeno, UnityEngine.Events.UnityAction akce)
        {
            var t = canvas.Find(jmeno);
            if (t == null) return;

            var b = t.GetComponent<Button>();
            if (b == null) return;

            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(akce);
        }

        [ContextMenu("Postavit panel")]
        public void Build()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (c.name != CanvasName) continue;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }

            const float statusHeight = 30f;
            var height = Padding * 2f + statusHeight + ButtonHeight * 3f + 8f * 3f;

            var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            var canvas = (RectTransform)go.transform;
            canvas.sizeDelta = new Vector2(width, height);
            canvas.localScale = Vector3.one * canvasScale;
            canvas.localPosition = Vector3.zero;
            canvas.localRotation = Quaternion.identity;

            var bg = Novy("Pozadi", canvas, Vector2.zero, new Vector2(width, height));
            var img = bg.AddComponent<Image>();
            img.color = panelColor;
            img.raycastTarget = false;

            var top = height * 0.5f - Padding;

            var st = Novy("Status", canvas, new Vector2(0f, top - statusHeight * 0.5f),
                new Vector2(width - Padding * 2f, statusHeight));
            var stt = st.AddComponent<TextMeshProUGUI>();

            // Font jako první: vlastnosti obrysu i sazby zapisují do materiálu
            // fontu a bez něj materiál neexistuje.
            if (font != null) stt.font = font;
            stt.text = "VYVOJ";
            stt.fontSize = 19f;
            stt.alignment = TextAlignmentOptions.Center;
            stt.color = new Color(1f, 0.86f, 0.86f, 1f);
            stt.raycastTarget = false;
            _status = stt;

            var y = top - statusHeight - 8f - ButtonHeight * 0.5f;
            Tlacitko(canvas, "Btn_Dalsi", "DALSI BLOK", y);
            y -= ButtonHeight + 8f;
            Tlacitko(canvas, "Btn_Znovu", "ZOPAKOVAT BLOK", y);
            y -= ButtonHeight + 8f;
            Tlacitko(canvas, "Btn_Restart", "OD ZACATKU", y);

            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.identity;

            // Canvas miri dopredu OD divaka, takze forward smeruje od hlavy
            // k panelu - stejne jako u menu s tvary.
            if (faceHead && Camera.main != null)
            {
                var smer = transform.position - Camera.main.transform.position;
                if (smer.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(smer, Vector3.up);
            }

            Bind(canvas);
        }

        private void Tlacitko(RectTransform canvas, string jmeno, string popis, float y)
        {
            var go = Novy(jmeno, canvas, new Vector2(0f, y),
                new Vector2(width - Padding * 2f, ButtonHeight));
            var img = go.AddComponent<Image>();
            img.color = buttonColor;

            var b = go.AddComponent<Button>();
            b.targetGraphic = img;

            var label = Novy("Label", (RectTransform)go.transform, Vector2.zero,
                new Vector2(width - Padding * 2f, ButtonHeight));
            var tmp = label.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = popis;
            tmp.fontSize = 20f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10f;
            tmp.fontSizeMax = 22f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
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
