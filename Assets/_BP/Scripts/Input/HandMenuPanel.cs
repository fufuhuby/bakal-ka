using System;
using System.Collections.Generic;
using BP.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Input
{
    /// <summary>
    /// Vizuál hand-fixed menu podle skice b4: vlevo sloupec barev, vpravo
    /// sloupec tvarů, dole STEP BACK a CREATE, pod nimi řádek pro hlášku.
    ///
    /// Vybraná dlaždice dostane obrys s odsazením — „visibility of system
    /// status": participant musí kdykoli vidět, co má rozjednané, jinak by
    /// chyby vznikaly ze zapomenutí, ne z vlastnosti modality.
    ///
    /// ROZDĚLENÍ STAVBY A NAVĚŠENÍ JE ZÁMĚRNÉ:
    ///   Build() staví hierarchii a spouští se v editoru.
    ///   Bind()  navěšuje posluchače a spouští se za běhu.
    /// Posluchače přidané lambdou se neserializují, takže je nutné je navěsit
    /// za běhu. Zároveň se ale nesmí canvas v Awake rušit a stavět znovu —
    /// TrackedDeviceGraphicRaycaster si registraci drží ve slovníku a při
    /// zrušení canvasu v Awake padá na KeyNotFoundException, čímž přestane
    /// fungovat celá interakce s UI.
    /// </summary>
    public class HandMenuPanel : MonoBehaviour
    {
        [Header("Závislosti")]
        [SerializeField] private MenuRequestSource source;
        [SerializeField] private ShapeLibrary library;
        [SerializeField] private TMP_FontAsset font;

        [Tooltip("Volitelné. Slouží jen k zobrazení hlášky, když je požadavek odmítnut.")]
        [SerializeField] private AssemblyTaskController taskController;

        [Header("Rozměry (px na canvasu)")]
        [SerializeField] private float tileSize = 50f;
        [SerializeField] private float tileSpacing = 4f;
        [SerializeField] private float columnGap = 34f;
        [SerializeField] private float padding = 16f;
        [SerializeField] private float buttonHeight = 56f;
        [SerializeField] private float noticeHeight = 22f;

        [Tooltip("O kolik px je obrys větší než dlaždice (outline with offset).")]
        [SerializeField] private float outlineOffset = 5f;

        [Tooltip("Přepočet px na metry. 0.0005 = dlaždice 2,5 cm.")]
        [SerializeField] private float canvasScale = 0.0005f;

        [Header("Hlášky")]
        [SerializeField] private float noticeDuration = 2f;
        [SerializeField] private Color noticeColor = new Color(1f, 0.72f, 0.2f, 1f);

        [Header("Barvy panelu")]
        [SerializeField] private Color panelColor = new Color(0.10f, 0.11f, 0.13f, 0.92f);
        [SerializeField] private Color tileBackground = new Color(0.92f, 0.92f, 0.94f, 1f);
        [SerializeField] private Color outlineColor = Color.white;
        [SerializeField] private Color buttonColor = new Color(0.24f, 0.26f, 0.30f, 1f);
        [SerializeField] private Color createReadyColor = new Color(0.16f, 0.55f, 0.24f, 1f);

        private const string CanvasName = "MenuCanvas";

        private readonly Dictionary<PaletteColor, GameObject> _colorOutlines =
            new Dictionary<PaletteColor, GameObject>();

        private readonly Dictionary<ShapeType, GameObject> _shapeOutlines =
            new Dictionary<ShapeType, GameObject>();

        private Image _createButtonImage;
        private TextMeshProUGUI _noticeText;
        private TextMeshProUGUI _progressText;
        private float _noticeUntil;
        private bool _noticeSticky;

        private void Awake()
        {
            var canvas = transform.Find(CanvasName) as RectTransform;

            // Canvas se v Awake NEruší — jen se doplní to, co serializace neunese.
            if (canvas == null) Build();
            else Bind(canvas);
        }

        private void OnEnable()
        {
            if (source != null) source.SelectionChanged += Refresh;
            if (taskController != null) taskController.RequestBlocked += OnRequestBlocked;
            Refresh();
        }

        private void OnDisable()
        {
            if (source != null) source.SelectionChanged -= Refresh;
            if (taskController != null) taskController.RequestBlocked -= OnRequestBlocked;
        }

        private void Update()
        {
            // Trvalá hláška nemizí sama — používá se, když je problém stavový
            // (v ruce je špatný objekt) a musí být vidět, dokud ho participant
            // neodstraní. Blikající hláška vedla k desítkám marných pokusů.
            if (_noticeSticky) return;

            if (_noticeText != null && _noticeText.enabled && Time.time > _noticeUntil)
                _noticeText.enabled = false;
        }

        // ---- Stav ----

        public void Refresh()
        {
            if (source == null) return;

            foreach (var kv in _colorOutlines)
                if (kv.Value != null)
                    kv.Value.SetActive(source.SelectedColor.HasValue && source.SelectedColor.Value == kv.Key);

            foreach (var kv in _shapeOutlines)
                if (kv.Value != null)
                    kv.Value.SetActive(source.SelectedShape.HasValue && source.SelectedShape.Value == kv.Key);

            // CREATE zůstane šedé, dokud není vybráno obojí — participant tak
            // nemusí zkoušet, jestli tlačítko vůbec něco udělá.
            if (_createButtonImage != null)
                _createButtonImage.color = source.CanCreate ? createReadyColor : buttonColor;
        }

        private void OnRequestBlocked(string reason) => ShowNotice(reason);

        /// <summary>
        /// Postup blokem, např. „3 / 8". Bez toho participant neví, kolik
        /// zbývá, a nemá jak odhadnout, jak dlouho ještě.
        /// </summary>
        public void SetProgress(int completed, int total)
        {
            if (_progressText == null) return;
            _progressText.text = completed + " / " + total;
        }

        /// <summary>
        /// Hláška pod tlačítky. Anglicky, stejně jako hlasová gramatika.
        /// sticky = zůstane, dokud ji někdo nesmaže (pro stavové problémy).
        /// </summary>
        public void ShowNotice(string message, bool sticky = false)
        {
            if (_noticeText == null) return;

            _noticeText.text = message;
            _noticeText.enabled = true;
            _noticeSticky = sticky;
            _noticeUntil = Time.time + noticeDuration;
        }

        /// <summary>Zhasne hlášku včetně trvalé.</summary>
        public void ClearNotice()
        {
            _noticeSticky = false;
            if (_noticeText != null) _noticeText.enabled = false;
        }

        /// <summary>Je zobrazená trvalá hláška?</summary>
        public bool HasStickyNotice => _noticeSticky;

        // ---- Navěšení na existující hierarchii ----

        /// <summary>
        /// Najde prvky podle jména a navěsí posluchače. Jména jsou dohodnutá
        /// v Build(), takže tenhle krok nezávisí na pořadí potomků.
        /// </summary>
        private void Bind(RectTransform canvas)
        {
            _colorOutlines.Clear();
            _shapeOutlines.Clear();

            foreach (PaletteColor c in Enum.GetValues(typeof(PaletteColor)))
            {
                var outline = canvas.Find("Outline_" + c);
                if (outline != null) _colorOutlines[c] = outline.gameObject;

                var tile = canvas.Find("Color_" + c);
                if (tile == null) continue;

                var btn = tile.GetComponent<Button>();
                if (btn == null) continue;

                var captured = c;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => source.SelectColor(captured));
            }

            foreach (ShapeType s in Enum.GetValues(typeof(ShapeType)))
            {
                var outline = canvas.Find("Outline_" + s);
                if (outline != null) _shapeOutlines[s] = outline.gameObject;

                var tile = canvas.Find("Shape_" + s);
                if (tile == null) continue;

                var btn = tile.GetComponent<Button>();
                if (btn == null) continue;

                var captured = s;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => source.SelectShape(captured));
            }

            BindActionButton(canvas, "Button_CREATE", source.Create, true);
            BindActionButton(canvas, "Button_STEPBACK", source.StepBack, false);

            var notice = canvas.Find("Notice");
            if (notice != null)
            {
                _noticeText = notice.GetComponent<TextMeshProUGUI>();
                if (_noticeText != null) _noticeText.enabled = false;
            }

            var progress = canvas.Find("Progress");
            if (progress != null) _progressText = progress.GetComponent<TextMeshProUGUI>();

            Refresh();
        }

        private void BindActionButton(RectTransform canvas, string name, Action action, bool isCreate)
        {
            var t = canvas.Find(name);
            if (t == null) return;

            var btn = t.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => action());
            }

            if (isCreate) _createButtonImage = t.GetComponent<Image>();
        }

        // ---- Stavba hierarchie ----

        [ContextMenu("Postavit panel")]
        public void Build()
        {
            Clear();

            if (source == null || library == null)
            {
                Debug.LogError("[HandMenuPanel] Chybí source nebo library.", this);
                return;
            }

            var shapes = (ShapeType[])Enum.GetValues(typeof(ShapeType));
            var colors = (PaletteColor[])Enum.GetValues(typeof(PaletteColor));
            var rows = Mathf.Max(shapes.Length, colors.Length);

            const float progressHeight = 26f;

            var gridHeight = rows * tileSize + (rows - 1) * tileSpacing;
            var width = padding * 2f + tileSize * 2f + columnGap;
            var height = padding * 2f + progressHeight + gridHeight
                         + tileSpacing * 3f + buttonHeight + noticeHeight;

            var canvas = CreateCanvas(width, height);
            CreateBackground(canvas, width, height);

            BuildProgress(canvas,
                new Vector2(0f, height * 0.5f - padding * 0.5f - progressHeight * 0.5f),
                width - padding, progressHeight);

            var gridTop = height * 0.5f - padding - progressHeight;
            var leftX = -(columnGap * 0.5f + tileSize * 0.5f);
            var rightX = columnGap * 0.5f + tileSize * 0.5f;

            for (var i = 0; i < colors.Length; i++)
            {
                var y = gridTop - tileSize * 0.5f - i * (tileSize + tileSpacing);
                BuildTile(canvas, "Color_" + colors[i], "Outline_" + colors[i],
                    new Vector2(leftX, y), library.GetDisplayColor(colors[i]), null);
            }

            for (var i = 0; i < shapes.Length; i++)
            {
                var y = gridTop - tileSize * 0.5f - i * (tileSize + tileSpacing);
                BuildTile(canvas, "Shape_" + shapes[i], "Outline_" + shapes[i],
                    new Vector2(rightX, y), tileBackground, library.GetIcon(shapes[i]));
            }

            var buttonY = -height * 0.5f + padding + noticeHeight + buttonHeight * 0.5f;
            var buttonWidth = (width - padding * 2f - tileSpacing) * 0.5f;

            BuildActionButtonVisual(canvas, "Button_STEPBACK", "STEP BACK",
                new Vector2(leftX - (tileSize - buttonWidth) * 0.5f, buttonY), buttonWidth);

            BuildActionButtonVisual(canvas, "Button_CREATE", "CREATE",
                new Vector2(rightX + (tileSize - buttonWidth) * 0.5f, buttonY), buttonWidth);

            BuildNotice(canvas,
                new Vector2(0f, -height * 0.5f + padding * 0.5f + noticeHeight * 0.5f),
                width - padding);

            Bind(canvas);
        }

        [ContextMenu("Smazat panel")]
        public void Clear()
        {
            _colorOutlines.Clear();
            _shapeOutlines.Clear();
            _createButtonImage = null;
            _noticeText = null;

            // Maže se POUZE canvas. Na kořeni menu visí i další věci
            // (úchytová lišta, collider), které se stavbou panelu nesouvisí —
            // mazání všech potomků by je odstranilo taky.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != CanvasName) continue;

                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private RectTransform CreateCanvas(float width, float height)
        {
            var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, height);
            rt.localScale = Vector3.one * canvasScale;
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;

            // Jen TrackedDeviceGraphicRaycaster, BEZ obyčejného GraphicRaycasteru.
            // XRI ho má nahrazovat, ne doplňovat — dva raycastery na jednom
            // canvasu si konkurují.
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            return rt;
        }

        private void CreateBackground(RectTransform canvas, float width, float height)
        {
            var go = NewUIObject("Background", canvas, Vector2.zero, new Vector2(width, height));
            var img = go.AddComponent<Image>();
            img.color = panelColor;
            img.raycastTarget = false;
        }

        private void BuildTile(RectTransform canvas, string tileName, string outlineName,
            Vector2 pos, Color tint, Sprite icon)
        {
            var outlineSize = new Vector2(tileSize + outlineOffset * 2f, tileSize + outlineOffset * 2f);
            var outline = NewUIObject(outlineName, canvas, pos, outlineSize);
            var outlineImg = outline.AddComponent<Image>();
            outlineImg.color = outlineColor;
            outlineImg.raycastTarget = false;
            outline.SetActive(false);

            var go = NewUIObject(tileName, canvas, pos, new Vector2(tileSize, tileSize));
            var img = go.AddComponent<Image>();
            img.color = tint;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            if (icon == null) return;

            // Ikona jako potomek, aby zůstala celá plocha tlačítka klikatelná.
            var iconGo = NewUIObject("Icon", (RectTransform)go.transform, Vector2.zero,
                new Vector2(tileSize * 0.82f, tileSize * 0.82f));
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
        }

        private void BuildActionButtonVisual(RectTransform canvas, string name, string label,
            Vector2 pos, float width)
        {
            var go = NewUIObject(name, canvas, pos, new Vector2(width, buttonHeight));

            var img = go.AddComponent<Image>();
            img.color = buttonColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var textGo = NewUIObject("Label", (RectTransform)go.transform, Vector2.zero,
                new Vector2(width, buttonHeight));
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8f;
            tmp.fontSizeMax = 14f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
        }

        private void BuildProgress(RectTransform canvas, Vector2 pos, float width, float height)
        {
            var go = NewUIObject("Progress", canvas, pos, new Vector2(width, height));

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "-- / --";
            tmp.fontSize = 15f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 9f;
            tmp.fontSizeMax = 17f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.85f, 0.87f, 0.92f, 1f);
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
        }

        private void BuildNotice(RectTransform canvas, Vector2 pos, float width)
        {
            var go = NewUIObject("Notice", canvas, pos, new Vector2(width, noticeHeight));

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 11f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 7f;
            tmp.fontSizeMax = 12f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = noticeColor;
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
            tmp.enabled = false;
        }

        private static GameObject NewUIObject(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return go;
        }
    }
}
