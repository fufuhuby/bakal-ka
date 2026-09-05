using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Panel, který provází session: úvod s instrukcí, běžící blok
    /// s časomírou, a shrnutí po dokončení bloku.
    ///
    /// INSTRUKCE NA PANELU MÁ SMYSL PRO MĚŘENÍ: když ji každý participant
    /// dostane stejně, zmizí variance z toho, že ji operátor pokaždé
    /// odříká trochu jinak.
    ///
    /// Panel se staví kódem ze stejného důvodu jako menu s tvary — posluchače
    /// tlačítek přidané lambdou se neserializují, takže se navěšují za běhu.
    /// </summary>
    public class SessionMenu : MonoBehaviour
    {
        public enum Screen
        {
            Intro = 0,
            Running = 1,
            BlockDone = 2,
            SessionDone = 3,
            Ranking = 4
        }

        [Header("Závislosti")]
        [SerializeField] private TrialManager trialManager;
        [SerializeField] private BlockTimer timer;
        [SerializeField] private TMP_FontAsset font;

        [Tooltip("Volitelné. Text časomíry mimo tento panel — aby šla umístit " +
                 "do rohu zorného pole nezávisle na velikosti panelu.")]
        [SerializeField] private TextMeshProUGUI externalTimerText;

        [Header("Rozměry (px na canvasu)")]
        [SerializeField] private float width = 420f;
        [SerializeField] private float height = 300f;
        [SerializeField] private float canvasScale = 0.0009f;

        [Header("Obrazovka výsledků")]
        [Tooltip("Tabulka potřebuje víc místa než instrukce, takže se panel " +
                 "na konci session roztáhne. Šířka musí pojmout 52 znaků řádku.")]
        [SerializeField] private float resultsWidth = 640f;

        [Tooltip("Nejmenší výška panelu s výsledky. Skutečná výška se dopočítá " +
                 "podle počtu bloků, aby pod tabulkou nezůstávalo prázdno.")]
        [SerializeField] private float resultsMinHeight = 240f;
        [SerializeField] private float resultsFontSize = 18f;

        [Tooltip("Přidané prostrkání řádků v procentech výšky písma. " +
                 "Řádky tabulky nalepené na sebe se čtou špatně — oko " +
                 "ztrácí, ve kterém řádku je.")]
        [SerializeField] private float resultsLineSpacing = 16f;

        [Tooltip("Šířka znaku v em pro neproporcionální sazbu tabulky. Bez ní " +
                 "se sloupce rozjedou, protože číslice a písmena mají " +
                 "v proporcionálním fontu různou šířku.")]
        [SerializeField] private float resultsCharWidth = 0.55f;

        [Header("Barvy")]
        [SerializeField] private Color panelColor = new Color(0.09f, 0.10f, 0.12f, 0.94f);
        [SerializeField] private Color buttonColor = new Color(0.16f, 0.55f, 0.24f, 1f);

        [Header("Text instrukce")]
        [TextArea(4, 10)]
        [SerializeField] private string introText =
            "Postav strukturu podle předlohy vlevo.\n\n" +
            "Na panelu vpravo vyber BARVU a TVAR, pak stiskni CREATE.\n" +
            "Objekt přenes na svítící místo uprostřed.\n" +
            "Špatný objekt odstraníš tlačítkem STEP BACK.\n\n" +
            "Když se rozsvítí ČERVENÝ TERČ, dotkni se ho.\n" +
            "Za každý zmeškaný terč se přičítá čas.";

        private const string CanvasName = "SessionCanvas";

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _timerText;
        private GameObject _panel;
        private Button _actionButton;
        private TextMeshProUGUI _actionLabel;

        public Screen Current { get; private set; } = Screen.Intro;

        private void Awake()
        {
            var canvas = transform.Find(CanvasName) as RectTransform;
            if (canvas == null) Build();
            else Bind(canvas);
        }

        private void OnEnable()
        {
            if (trialManager == null) return;
            trialManager.BlockStarted += OnBlockStarted;
            trialManager.BlockEnded += OnBlockEnded;
            trialManager.SessionEnded += OnSessionEnded;
        }

        private void OnDisable()
        {
            if (trialManager == null) return;
            trialManager.BlockStarted -= OnBlockStarted;
            trialManager.BlockEnded -= OnBlockEnded;
            trialManager.SessionEnded -= OnSessionEnded;
        }

        private void Update()
        {
            if (_timerText == null || timer == null) return;

            // Časomíra je vidět jen za běhu bloku.
            _timerText.enabled = timer.IsRunning;
            if (!timer.IsRunning) return;

            _timerText.text = BlockTimer.Format(timer.DisplayTime)
                              + (timer.PenaltyCount > 0 ? "  (+" + timer.PenaltyCount + ")" : "");
        }

        // ---- Obrazovky ----

        public void ShowIntro()
        {
            Current = Screen.Intro;
            SetPanel(true, "PŘIPRAVENO", introText, "START");
        }

        private void OnBlockStarted(BlockDefinition block, int index)
        {
            Current = Screen.Running;
            SetPanel(false, null, null, null);
        }

        private void OnBlockEnded(BlockDefinition block, int index)
        {
            Current = Screen.BlockDone;

            var body = timer != null
                ? "Čas: " + BlockTimer.Format(timer.RawTime)
                  + "\nPostihy: " + timer.PenaltyCount + "×  (" + BlockTimer.Format(timer.PenaltyTime) + ")"
                  + "\nCelkem: " + BlockTimer.Format(timer.DisplayTime)
                : "Blok dokončen.";

            SetPanel(true, "BLOK HOTOV", body, "DALŠÍ BLOK");
        }

        private void OnSessionEnded() => ShowResults();

        /// <summary>Tabulka bloků právě dokončené session.</summary>
        public void ShowResults()
        {
            Current = Screen.SessionDone;

            var table = trialManager != null
                ? trialManager.BuildResultsTable()
                : "Všechny bloky dokončeny.";

            ShowTable("VÝSLEDKY", table, "NEJLEPŠÍ VÝSLEDKY");
        }

        /// <summary>Žebříček napříč všemi uloženými session.</summary>
        public void ShowRanking()
        {
            Current = Screen.Ranking;

            var table = trialManager != null
                ? trialManager.BuildRankingTable()
                : "Žebříček není dostupný.";

            ShowTable("NEJLEPŠÍ VÝSLEDKY", table, "ZPĚT NA VÝSLEDKY");
        }

        private void ShowTable(string title, string table, string action)
        {
            var mspace = resultsCharWidth.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);

            // Šířka musí být nastavená DŘÍV, než se text změří — v užším
            // rámečku by se řádky tabulky zalomily a výška by vyšla špatně.
            ApplyResultsWidth();
            SetPanel(true, title, "<mspace=" + mspace + "em>" + table, action);
            FitResultsHeight();
        }

        /// <summary>
        /// Roztáhne panel na šířku tabulky. Dělá se to až tady, ne při stavbě —
        /// úvodní instrukce v širokém panelu vypadá ztracená a čte se hůř.
        /// Obrazovka výsledků je poslední, takže se rozměr nemusí vracet zpět.
        /// </summary>
        private void ApplyResultsWidth()
        {
            if (_panel == null) return;

            var pr = (RectTransform)_panel.transform;
            pr.sizeDelta = new Vector2(resultsWidth, pr.sizeDelta.y);

            var body = pr.Find("Body") as RectTransform;
            if (body != null) body.sizeDelta = new Vector2(resultsWidth - 50f, body.sizeDelta.y);

            if (_body != null)
            {
                _body.fontSize = resultsFontSize;
                _body.lineSpacing = resultsLineSpacing;
            }
        }

        /// <summary>
        /// Dopočítá výšku panelu podle skutečné výšky tabulky. Pevná výška by
        /// při čtyřech blocích nechala pod tabulkou půl panelu prázdné a při
        /// osmi by tabulku uřízla.
        /// </summary>
        private void FitResultsHeight()
        {
            if (_panel == null || _body == null) return;

            _body.ForceMeshUpdate();

            const float titleArea = 68f;   // nadpis + mezera pod ním

            // Pod tabulkou je na obrazovce výsledků tlačítko, takže se musí
            // počítat s větším spodním okrajem než u pouhého odsazení.
            var bottomPad = _actionButton != null && _actionButton.gameObject.activeSelf ? 86f : 26f;

            var textHeight = _body.textBounds.size.y;
            var h = Mathf.Max(resultsMinHeight, titleArea + textHeight + bottomPad);

            var pr = (RectTransform)_panel.transform;
            pr.sizeDelta = new Vector2(resultsWidth, h);

            var canvas = pr.parent as RectTransform;
            if (canvas != null) canvas.sizeDelta = new Vector2(resultsWidth, h + 90f);

            var title = pr.Find("Title") as RectTransform;
            if (title != null)
            {
                title.sizeDelta = new Vector2(resultsWidth - 40f, 46f);
                title.anchoredPosition = new Vector2(0f, h * 0.5f - 34f);
            }

            var body = pr.Find("Body") as RectTransform;
            if (body != null)
            {
                body.sizeDelta = new Vector2(resultsWidth - 50f, h - titleArea - bottomPad);
                body.anchoredPosition = new Vector2(0f, -(titleArea - bottomPad) * 0.5f);
            }

            // Tlačítko drží Build() na výšce panelu z editoru; po přepočtu
            // výšky by zůstalo viset uprostřed tabulky.
            var btn = pr.Find("ActionButton") as RectTransform;
            if (btn != null)
            {
                btn.sizeDelta = new Vector2(270f, 48f);
                btn.anchoredPosition = new Vector2(0f, -h * 0.5f + 34f);

                var label = btn.Find("Label") as RectTransform;
                if (label != null) label.sizeDelta = btn.sizeDelta;
            }
        }

        private void SetPanel(bool visible, string title, string body, string action)
        {
            if (_panel != null) _panel.SetActive(visible);
            if (!visible) return;

            if (_title != null) _title.text = title ?? "";
            if (_body != null) _body.text = body ?? "";

            var hasAction = !string.IsNullOrEmpty(action);
            if (_actionButton != null) _actionButton.gameObject.SetActive(hasAction);
            if (hasAction && _actionLabel != null) _actionLabel.text = action;
        }

        private void OnAction()
        {
            if (trialManager == null) return;

            switch (Current)
            {
                // Na konci session už není co spouštět, tlačítko tam přepíná
                // mezi vlastními výsledky a žebříčkem.
                case Screen.SessionDone:
                    ShowRanking();
                    return;

                case Screen.Ranking:
                    ShowResults();
                    return;

                default:
                    // Z úvodu i ze shrnutí bloku vede stejná akce: další blok.
                    trialManager.StartNextBlock();
                    return;
            }
        }

        // ---- Stavba a navěšení ----

        private void Bind(RectTransform canvas)
        {
            _panel = canvas.Find("Panel") != null ? canvas.Find("Panel").gameObject : null;
            if (_panel == null) return;

            var p = _panel.transform;
            _title = Get(p, "Title");
            _body = Get(p, "Body");

            var btn = p.Find("ActionButton");
            if (btn != null)
            {
                _actionButton = btn.GetComponent<Button>();
                _actionLabel = Get(btn, "Label");
                if (_actionButton != null)
                {
                    _actionButton.onClick.RemoveAllListeners();
                    _actionButton.onClick.AddListener(OnAction);
                }
            }

            if (externalTimerText != null)
            {
                _timerText = externalTimerText;

                // Vlastní časomíra v panelu by se s tou vnější zdvojila.
                var inner = canvas.Find("Timer");
                if (inner != null) inner.gameObject.SetActive(false);
            }
            else
            {
                var t = canvas.Find("Timer");
                if (t != null) _timerText = t.GetComponent<TextMeshProUGUI>();
            }

            ShowIntro();
        }

        private static TextMeshProUGUI Get(Transform parent, string name)
        {
            var t = parent.Find(name);
            return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
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

            var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            var canvas = (RectTransform)go.transform;
            canvas.sizeDelta = new Vector2(width, height + 90f);
            canvas.localScale = Vector3.one * canvasScale;
            canvas.localPosition = Vector3.zero;
            canvas.localRotation = Quaternion.identity;

            BuildPanel(canvas);
            BuildTimer(canvas);
            Bind(canvas);
        }

        private void BuildPanel(RectTransform canvas)
        {
            var panel = New("Panel", canvas, new Vector2(0f, 45f), new Vector2(width, height));
            panel.AddComponent<Image>().color = panelColor;
            _panel = panel;

            var pr = (RectTransform)panel.transform;

            var title = New("Title", pr, new Vector2(0f, height * 0.5f - 34f), new Vector2(width - 40f, 46f));
            Text(title, 30f, TextAlignmentOptions.Center, new Color(0.95f, 0.96f, 1f, 1f));

            // Rámeček textu je záměrně vyšší, než kam text sahá — jinak text
            // z rámečku přetéká a chování při delší instrukci je nepředvídatelné.
            // Horní hrana zůstává stejná, takže mezera k tlačítku se nemění.
            var body = New("Body", pr, new Vector2(0f, 15f), new Vector2(width - 50f, height - 145f));
            Text(body, 17f, TextAlignmentOptions.TopLeft, new Color(0.84f, 0.86f, 0.92f, 1f));

            var btn = New("ActionButton", pr, new Vector2(0f, -height * 0.5f + 38f), new Vector2(200f, 52f));
            btn.AddComponent<Image>().color = buttonColor;
            var b = btn.AddComponent<Button>();
            b.targetGraphic = btn.GetComponent<Image>();

            var label = New("Label", (RectTransform)btn.transform, Vector2.zero, new Vector2(200f, 52f));
            Text(label, 22f, TextAlignmentOptions.Center, Color.white);
        }

        private void BuildTimer(RectTransform canvas)
        {
            // Vpravo dole pod panelem.
            var t = New("Timer", canvas,
                new Vector2(width * 0.5f - 90f, -(height * 0.5f) - 10f), new Vector2(180f, 60f));
            Text(t, 34f, TextAlignmentOptions.Right, new Color(1f, 0.92f, 0.55f, 1f));
        }

        private void Text(GameObject go, float size, TextAlignmentOptions align, Color color)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            if (font != null) tmp.font = font;
        }

        private static GameObject New(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
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
