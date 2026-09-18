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
    /// sloupec tvarů, dole ZPĚT a VYTVOŘIT, pod nimi řádek pro hlášku.
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

        [Header("Ikony akčních tlačítek")]
        [Tooltip("Kladivo na tlačítku vytvoření. Generuje BP/Generovat ikony tlacitek.")]
        [SerializeField] private Sprite createIcon;

        [Tooltip("Šipka do kolečka na tlačítku zpět.")]
        [SerializeField] private Sprite undoIcon;

        [Header("Rozměry (px na canvasu)")]
        [SerializeField] private float tileSize = 50f;
        [SerializeField] private float tileSpacing = 4f;
        [Tooltip("Mezera mezi sloupcem barev a sloupcem tvarů.")]
        [SerializeField] private float columnGap = 14f;
        [SerializeField] private float padding = 16f;
        [SerializeField] private float noticeHeight = 22f;

        [Tooltip("O kolik px je obrys větší než dlaždice (outline with offset).")]
        [SerializeField] private float outlineOffset = 5f;

        [Tooltip("Přepočet px na metry. 0.0005 = dlaždice 2,5 cm.")]
        [SerializeField] private float canvasScale = 0.0005f;

        [Header("Hlášky")]
        [SerializeField] private float noticeDuration = 2f;
        [SerializeField] private Color noticeColor = PanelStyle.Warn;

        [Header("Barvy panelu")]
        [Tooltip("Barvy i zaoblení přebírá PanelStyle, stejně jako okno s hlasovými příkazy.")]
        [SerializeField] private Color outlineColor = Color.white;

        private const string CanvasName = "MenuCanvas";

        private readonly Dictionary<PaletteColor, GameObject> _colorOutlines =
            new Dictionary<PaletteColor, GameObject>();

        private readonly Dictionary<ShapeType, GameObject> _shapeOutlines =
            new Dictionary<ShapeType, GameObject>();

        private readonly Dictionary<ShapeSize, GameObject> _sizeOutlines =
            new Dictionary<ShapeSize, GameObject>();

        private readonly Dictionary<ShapeSize, GameObject> _sizeTiles =
            new Dictionary<ShapeSize, GameObject>();

        // Dlazdice barev a tvaru se drzi kvuli zamykani v tutorialu.
        private readonly Dictionary<PaletteColor, Button> _colorButtons =
            new Dictionary<PaletteColor, Button>();

        private readonly Dictionary<ShapeType, Button> _shapeButtons =
            new Dictionary<ShapeType, Button>();

        private Button _createButton;
        private Button _stepBackButton;

        private GameObject _sizeStrip;

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
            if (source != null)
            {
                source.SelectionChanged += Refresh;
                source.PartsChanged += Refresh;
            }
            if (taskController != null) taskController.RequestBlocked += OnRequestBlocked;
            Refresh();
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.SelectionChanged -= Refresh;
                source.PartsChanged -= Refresh;
            }
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

            // Zamky jako PRVNI. Prepnuti Button.interactable spousti barevny
            // prechod, ktery prebarvi cilovou grafiku na normalColor — kdyby
            // beho tady az nakonec, smazalo by to zelene CREATE nastavene nize.
            RefreshLocks();

            foreach (var kv in _colorOutlines)
                if (kv.Value != null)
                    kv.Value.SetActive(source.SelectedColor.HasValue && source.SelectedColor.Value == kv.Key);

            foreach (var kv in _shapeOutlines)
                if (kv.Value != null)
                    kv.Value.SetActive(source.SelectedShape.HasValue && source.SelectedShape.Value == kv.Key);

            // Ve dvouvlastnostnim bloku prilepek cely zhasne — i s pozadim.
            // Nabizet volbu, ktera nic nedela, by participanta ucilo klikat
            // naprazdno; prazdne misto v panelu je stejne matouci.
            if (_sizeStrip != null) _sizeStrip.SetActive(source.RequiresSize);

            foreach (var kv in _sizeTiles)
                if (kv.Value != null) kv.Value.SetActive(source.RequiresSize);

            foreach (var kv in _sizeOutlines)
                if (kv.Value != null)
                    kv.Value.SetActive(source.RequiresSize
                        && source.SelectedSize.HasValue && source.SelectedSize.Value == kv.Key);

            // CREATE zůstane šedé, dokud není vybráno obojí — participant tak
            // nemusí zkoušet, jestli tlačítko vůbec něco udělá.
            if (_createButtonImage != null)
                _createButtonImage.color = source.CanCreate ? PanelStyle.Positive : PanelStyle.Neutral;
        }

        /// <summary>
        /// Zhasne dlaždice, které tutoriál v aktuálním kroku zamkl.
        ///
        /// PROČ NESTAČÍ IGNOROVAT KLIK: dlaždice, která vypadá stejně jako
        /// ostatní a nic nedělá, vypadá jako rozbitá aplikace. Zhasnutá
        /// dlaždice sděluje, že teď na řadě není, a participant se vrátí
        /// k tomu, co po něm boxík chce.
        /// </summary>
        private void RefreshLocks()
        {
            var barvy = (source.AllowedParts & MenuPart.Colors) != 0;
            var tvary = (source.AllowedParts & MenuPart.Shapes) != 0;
            var velikosti = (source.AllowedParts & MenuPart.Sizes) != 0;

            foreach (var kv in _colorButtons)
                if (kv.Value != null) kv.Value.interactable = barvy;

            foreach (var kv in _shapeButtons)
                if (kv.Value != null) kv.Value.interactable = tvary;

            foreach (var kv in _sizeTiles)
            {
                if (kv.Value == null) continue;
                var b = kv.Value.GetComponent<Button>();
                if (b != null) b.interactable = velikosti;
            }

            if (_createButton != null)
                _createButton.interactable = (source.AllowedParts & MenuPart.Create) != 0;

            if (_stepBackButton != null)
                _stepBackButton.interactable = (source.AllowedParts & MenuPart.StepBack) != 0;
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
        /// Hláška pod tlačítky.
        ///
        /// ČESKY, stejně jako hlasové povely. Dřív byla anglicky, protože se
        /// počítalo s anglickou gramatikou rozpoznávání; ta je ale česká,
        /// takže míchat jazyky už nemá důvod a jen to přidává práci navíc.
        ///
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
            _colorButtons.Clear();
            _shapeButtons.Clear();
            _sizeOutlines.Clear();
            _sizeTiles.Clear();

            var strip = canvas.Find("SizeStrip");
            _sizeStrip = strip != null ? strip.gameObject : null;

            foreach (PaletteColor c in Enum.GetValues(typeof(PaletteColor)))
            {
                var outline = canvas.Find("Outline_" + c);
                if (outline != null) _colorOutlines[c] = outline.gameObject;

                var tile = canvas.Find("Color_" + c);
                if (tile == null) continue;

                var btn = tile.GetComponent<Button>();
                if (btn == null) continue;

                _colorButtons[c] = btn;

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

                _shapeButtons[s] = btn;

                var captured = s;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => source.SelectShape(captured));
            }

            foreach (ShapeSize z in Enum.GetValues(typeof(ShapeSize)))
            {
                var jmeno = "Size_" + z;

                var outline = canvas.Find("Outline_" + jmeno);
                if (outline != null) _sizeOutlines[z] = outline.gameObject;

                var tile = canvas.Find(jmeno);
                if (tile == null) continue;
                _sizeTiles[z] = tile.gameObject;

                var btn = tile.GetComponent<Button>();
                if (btn == null) continue;

                var captured = z;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => source.SelectSize(captured));
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

            if (isCreate)
            {
                _createButtonImage = t.GetComponent<Image>();
                _createButton = btn;
            }
            else _stepBackButton = btn;
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

            var sizes = (ShapeSize[])Enum.GetValues(typeof(ShapeSize));

            var gridHeight = rows * tileSize + (rows - 1) * tileSpacing;

            // Hlavni panel ma dva sloupce, barvu a tvar. Velikosti jsou
            // PRILEPEK vpravo s vlastnim pozadim — v bloku, ktery velikosti
            // neresi, se prilepek cely zhasne a panel nema prazdne misto.
            var width = padding * 2f + tileSize * 2f + columnGap;
            var height = padding * 2f + progressHeight + gridHeight
                         + tileSpacing * 3f + tileSize + noticeHeight;

            // Canvas je sirsi nez panel, aby se do nej vesel prilepek vpravo.
            // Pozadi hlavniho panelu zustava uzke.
            var canvas = CreateCanvas(width + (tileSize + 10f * 3f) * 2f, height);
            CreateBackground(canvas, width, height);

            BuildProgress(canvas,
                new Vector2(0f, height * 0.5f - padding * 0.5f - progressHeight * 0.5f),
                width - padding, progressHeight);

            var gridTop = height * 0.5f - padding - progressHeight;
            var leftX = -(columnGap * 0.5f + tileSize * 0.5f);
            var rightX = columnGap * 0.5f + tileSize * 0.5f;

            // Prilepek sedi tesne za pravou hranou hlavniho panelu.
            const float sizeGap = 10f;
            var sizeX = width * 0.5f + sizeGap + tileSize * 0.5f;

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
                    new Vector2(rightX, y), PanelStyle.Plate, library.GetIcon(shapes[i]));
            }

            // Pozadi prilepku se stavi PRED dlazdicemi, aby zustalo pod nimi —
            // poradi potomku urcuje poradi vykresleni.
            var stripHeight = sizes.Length * tileSize + (sizes.Length - 1) * tileSpacing;
            var strip = NewUIObject("SizeStrip", canvas,
                new Vector2(sizeX, gridTop - stripHeight * 0.5f),
                new Vector2(tileSize + sizeGap * 2f, stripHeight + sizeGap * 2f));
            PanelStyle.ApplyRounded(strip.AddComponent<Image>(), PanelStyle.RadiusPanel,
                PanelStyle.Window);

            for (var i = 0; i < sizes.Length; i++)
            {
                var y = gridTop - tileSize * 0.5f - i * (tileSize + tileSpacing);
                BuildSizeTile(canvas, sizes[i], new Vector2(sizeX, y));
            }

            var buttonY = -height * 0.5f + padding + noticeHeight + tileSize * 0.5f;

            // Tlačítko je přesně tak široké jako sloupec nad ním a stojí na
            // jeho ose. Dřív bylo širší a přesahovalo do mezery mezi sloupci,
            // takže panel měl dvě různé svislé mřížky a nic na sebe nesedělo.
            BuildActionButtonVisual(canvas, "Button_STEPBACK", "ZPĚT", undoIcon,
                new Vector2(leftX, buttonY), tileSize);

            BuildActionButtonVisual(canvas, "Button_CREATE", "VYTVOŘIT", createIcon,
                new Vector2(rightX, buttonY), tileSize);

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
            _colorButtons.Clear();
            _shapeButtons.Clear();
            _createButtonImage = null;
            _createButton = null;
            _stepBackButton = null;
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
            PanelStyle.ApplyRounded(go.AddComponent<Image>(), PanelStyle.RadiusPanel, PanelStyle.Window);
        }

        private void BuildTile(RectTransform canvas, string tileName, string outlineName,
            Vector2 pos, Color tint, Sprite icon)
        {
            var outlineSize = new Vector2(tileSize + outlineOffset * 2f, tileSize + outlineOffset * 2f);
            var outline = NewUIObject(outlineName, canvas, pos, outlineSize);
            // Obrys má větší poloměr než dlaždice přesně o odsazení, jinak
            // by se rohy rozcházely a rámeček by u rohu vypadal silnější.
            PanelStyle.ApplyRounded(outline.AddComponent<Image>(),
                PanelStyle.RadiusTile + outlineOffset, outlineColor);
            outline.SetActive(false);

            var go = NewUIObject(tileName, canvas, pos, new Vector2(tileSize, tileSize));
            var img = go.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, PanelStyle.RadiusTile, tint);

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

        /// <summary>
        /// Dlaždice velikosti. Nese text (S / L / XL), ne ikonu — velikost
        /// nakreslená jako různě velký tvar by se v dlaždici 2,5 cm nedala
        /// odlišit, a rozhodovala by čitelnost místo modality.
        /// </summary>
        private void BuildSizeTile(RectTransform canvas, ShapeSize size, Vector2 pos)
        {
            var jmeno = "Size_" + size;

            var outlineSize = new Vector2(tileSize + outlineOffset * 2f, tileSize + outlineOffset * 2f);
            var outline = NewUIObject("Outline_" + jmeno, canvas, pos, outlineSize);
            PanelStyle.ApplyRounded(outline.AddComponent<Image>(),
                PanelStyle.RadiusTile + outlineOffset, outlineColor);
            outline.SetActive(false);

            var go = NewUIObject(jmeno, canvas, pos, new Vector2(tileSize, tileSize));
            var img = go.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, PanelStyle.RadiusTile, PanelStyle.Plate);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            // OBRÁZEK MÍSTO PÍSMENE. „M" má člověk z triček spojené s medium,
            // takže při hlasovém ovládání musel z písmene vyrobit slovo
            // „střední“ — překlad, který v menu neexistuje, protože se na
            // dlaždici jen klepne. Ten rozdíl by se schoval do naměřeného
            // času a tvářil se jako cena hlasu.
            var stupnice = library != null ? library.GetSizeIcon(size) : null;

            if (stupnice != null)
            {
                var ikonaGo = NewUIObject("Ikona", (RectTransform)go.transform, Vector2.zero,
                    new Vector2(tileSize * 0.74f, tileSize * 0.74f));
                var ikona = ikonaGo.AddComponent<Image>();
                ikona.sprite = stupnice;
                ikona.preserveAspect = true;
                ikona.raycastTarget = false;
                ikona.color = PanelStyle.Window;
                return;
            }

            var textGo = NewUIObject("Label", (RectTransform)go.transform, Vector2.zero,
                new Vector2(tileSize, tileSize));
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = ShapeSizes.Label(size);
            tmp.fontSize = 26f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10f;
            tmp.fontSizeMax = 30f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = PanelStyle.Window;
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
        }

        /// <summary>
        /// Akční tlačítko. ČTVEREC stejné velikosti jako dlaždice barev
        /// a tvarů, aby panel držel jednu mřížku.
        ///
        /// OBRÁZEK, NE SLOVO. „VYTVOŘIT" se do 2,5 cm vejde jen při velikosti
        /// písma, u které se popisek musí luštit — a luštění je čas navíc
        /// v podmínce, jejíž rychlost se porovnává s hlasem. Silueta se pozná
        /// na jedno mrknutí a nese ji celá plocha tlačítka, ne tenký tah.
        ///
        /// Text zůstává jako záloha: kdyby ikona chyběla, tlačítko pořád
        /// řekne, co dělá, místo aby vypadalo prázdné.
        /// </summary>
        private void BuildActionButtonVisual(RectTransform canvas, string name, string label,
            Sprite icon, Vector2 pos, float strana)
        {
            var go = NewUIObject(name, canvas, pos, new Vector2(strana, strana));

            var img = go.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, PanelStyle.RadiusButton, PanelStyle.Neutral);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            if (icon != null)
            {
                var ikonaGo = NewUIObject("Ikona", (RectTransform)go.transform, Vector2.zero,
                    new Vector2(strana * 0.74f, strana * 0.74f));
                var ikona = ikonaGo.AddComponent<Image>();
                ikona.sprite = icon;
                ikona.preserveAspect = true;
                ikona.raycastTarget = false;
                ikona.color = PanelStyle.TextPrimary;
                return;
            }

            var textGo = NewUIObject("Label", (RectTransform)go.transform, Vector2.zero,
                new Vector2(strana, strana - 6f));
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8f;
            tmp.fontSizeMax = 9.5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = PanelStyle.TextPrimary;
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
            tmp.color = PanelStyle.Title;
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
