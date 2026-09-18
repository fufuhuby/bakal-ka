using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Panel, který provází session: úvodní nabídka, běžící blok
    /// s časomírou, a shrnutí po dokončení bloku.
    ///
    /// Panel mění velikost podle obrazovky — viz ApplyPanelLayout.
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

        [Tooltip("Průvodce tutoriálem. Spouští se třetím tlačítkem v nabídce.")]
        [SerializeField] private TutorialGuide tutorialGuide;

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
                 "na konci session roztáhne. Šířka musí pojmout nejširší řádek: " +
                 "67 znaků × velikost písma × šířka znaku, plus okraje.")]
        [SerializeField] private float resultsWidth = 760f;

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
        [SerializeField] private Color panelColor = PanelStyle.Window;
        [SerializeField] private Color buttonColor = PanelStyle.Positive;

        [Tooltip("Druhé tlačítko (hlasová verze). Jiná barva než klasická, " +
                 "aby si je participant nespletl.")]
        [SerializeField] private Color secondButtonColor = new Color(0.20f, 0.36f, 0.62f, 1f);

        [Tooltip("Třetí tlačítko (tutoriál). Tlumené — není to volba měření, " +
                 "ale nácvik, a nemá si říkat o pozornost stejně jako obě verze.")]
        [SerializeField] private Color thirdButtonColor = PanelStyle.Neutral;

        [Tooltip("Čtvrté tlačítko: tutoriál hlasové verze.")]
        [SerializeField] private Color fourthButtonColor = PanelStyle.Neutral;

        private const string CanvasName = "SessionCanvas";

        /// <summary>
        /// Výška úvodní nabídky — tři tlačítka pod sebou a okraj.
        ///
        /// PROČ ÚVOD NEMÁ ŽÁDNÝ TEXT: instrukci na úvodní obrazovce si
        /// participant buď přečte, nebo ne, a ani v jednom případě se z ní
        /// ovládání nenaučí — ruční uchopení a zaskočení objektu se popsat
        /// nedá. Od toho je tutoriál. Prázdný panel navíc nechává volné
        /// zorné pole, takže se nabídka nepřekrývá s pracovištěm.
        /// </summary>
        private const float IntroHeight = 312f;

        /// <summary>Rozměry tlačítka v úvodní nabídce.</summary>
        private const float IntroButtonWidth = 320f;
        private const float IntroButtonHeight = 54f;
        private const float IntroButtonGap = 14f;

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _timerText;
        private GameObject _panel;
        private Button _actionButton;
        private TextMeshProUGUI _actionLabel;
        private Button _secondButton;
        private TextMeshProUGUI _secondLabel;
        private Button _thirdButton;
        private TextMeshProUGUI _thirdLabel;
        private Button _fourthButton;
        private TextMeshProUGUI _fourthLabel;

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
            trialManager.TutorialEnded += ShowIntro;
        }

        private void OnDisable()
        {
            if (trialManager == null) return;
            trialManager.BlockStarted -= OnBlockStarted;
            trialManager.BlockEnded -= OnBlockEnded;
            trialManager.SessionEnded -= OnSessionEnded;
            trialManager.TutorialEnded -= ShowIntro;
        }

        private void Update()
        {
            if (_timerText == null || timer == null) return;

            // Časomíra je vidět jen za běhu MĚŘENÉHO bloku.
            //
            // V tutoriálu se schovává schválně: nácvik má proběhnout beze
            // spěchu, a běžící stopky tlačí k tomu, aby ho člověk odbyl.
            // Měřit se přitom nepřestává — čas tutoriálu je v logu, jen ho
            // participant nevidí.
            var zobrazit = timer.IsRunning
                           && (trialManager == null || !trialManager.TutorialRunning);

            _timerText.enabled = zobrazit;
            if (!zobrazit) return;

            _timerText.text = BlockTimer.Format(timer.DisplayTime)
                              + (timer.PenaltyCount > 0 ? "  (+" + timer.PenaltyCount + ")" : "");
        }

        // ---- Obrazovky ----

        public void ShowIntro()
        {
            Current = Screen.Intro;
            SetPanel(true, null, null,
                "KLASICKÁ VERZE", "HLASOVÁ VERZE", "TUTORIÁL: MENU", "TUTORIÁL: HLAS");
            ApplyPanelLayout(width, IntroHeight);
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

            // ČÍSLO BLOKU V NADPISU. Bez něj vypadalo shrnutí po prvním
            // i po posledním bloku stejně a nedalo se poznat, kolik jich
            // ještě zbývá — ani při zkoušení, ani při měření s participantem.
            SetPanel(true, "HOTOVÝ BLOK " + (index + 1), body, "DALŠÍ BLOK");

            // Úvod panel zmenšil na dvě tlačítka, shrnutí potřebuje celý.
            ApplyPanelLayout(width, height);
        }

        private void OnSessionEnded() => ShowResults();

        /// <summary>Tabulka bloků právě dokončené session.</summary>
        public void ShowResults()
        {
            Current = Screen.SessionDone;

            var table = trialManager != null
                ? trialManager.BuildResultsTable()
                : "Všechny bloky dokončeny.";

            // Podmínka patří do nadpisu, ne do sloupce — v tabulce by se
            // opakovala ve všech řádcích, protože session je jen jedna.
            var verze = trialManager == null ? ""
                : trialManager.Mode == TrialManager.SessionMode.Voice
                    ? "  ·  HLASOVÁ VERZE" : "  ·  KLASICKÁ VERZE";

            ShowTable("VÝSLEDKY" + verze, table, "NEJLEPŠÍ VÝSLEDKY", "MENU");
        }

        /// <summary>Žebříček napříč všemi uloženými session.</summary>
        public void ShowRanking()
        {
            Current = Screen.Ranking;

            var table = trialManager != null
                ? trialManager.BuildRankingTable()
                : "Žebříček není dostupný.";

            ShowTable("NEJLEPŠÍ VÝSLEDKY", table, "ZPĚT NA VÝSLEDKY", "MENU");
        }

        private void ShowTable(string title, string table, string action, string second = null)
        {
            var mspace = resultsCharWidth.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);

            // Šířka musí být nastavená DŘÍV, než se text změří — v užším
            // rámečku by se řádky tabulky zalomily a výška by vyšla špatně.
            ApplyResultsWidth();
            SetPanel(true, title, "<mspace=" + mspace + "em>" + table, action, second);
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
                // RADĚJI ZMENŠIT NEŽ ZALOMIT. Zalomená tabulka se rozpadne
                // na dvojřádky a přestane být tabulkou; menší písmo se dá
                // přečíst pořád.
                _body.textWrappingMode = TextWrappingModes.NoWrap;
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

            // Tlačítka drží Build() na výšce panelu z editoru; po přepočtu
            // výšky by zůstala viset uprostřed tabulky.
            RadaTlacitek(pr, resultsWidth, h, 34f);
        }

        /// <summary>
        /// Přepočítá panel na danou velikost. Obrazovky se v obsahu liší
        /// natolik, že pevná výška z editoru sedí jen jedné z nich: úvod jsou
        /// dvě tlačítka, shrnutí bloku tři řádky, výsledky celá tabulka.
        ///
        /// Volá se AŽ PO SetPanel — vodorovnou polohu tlačítek řeší SetPanel
        /// podle toho, jestli je druhé tlačítko vidět, a tahle metoda ji nechává
        /// být. Kdyby běžela dřív, přepsal by ji SetPanel a naopak.
        /// </summary>
        private void ApplyPanelLayout(float w, float h)
        {
            if (_panel == null) return;

            var pr = (RectTransform)_panel.transform;
            pr.sizeDelta = new Vector2(w, h);

            var canvas = pr.parent as RectTransform;
            if (canvas != null) canvas.sizeDelta = new Vector2(w, h + 90f);

            var title = pr.Find("Title") as RectTransform;
            if (title != null)
            {
                title.sizeDelta = new Vector2(w - 40f, 46f);
                title.anchoredPosition = new Vector2(0f, h * 0.5f - 34f);
            }

            var body = pr.Find("Body") as RectTransform;
            if (body != null)
            {
                body.sizeDelta = new Vector2(w - 50f, Mathf.Max(0f, h - 145f));
                body.anchoredPosition = new Vector2(0f, 15f);
            }

            // Sazba se vrací z tabulkové zpátky na běžnou — obrazovka
            // s výsledky ji zvětšuje a prostrkává a jinak by to zůstalo.
            if (_body != null)
            {
                _body.fontSize = 17f;
                _body.lineSpacing = 0f;
            }

            // Prázdný panel: tlačítka patří doprostřed, ne k dolní hraně.
            var prazdny = (_title == null || string.IsNullOrEmpty(_title.text))
                          && (_body == null || string.IsNullOrEmpty(_body.text));

            var jmena = new[] { "ActionButton", "SecondButton", "ThirdButton", "FourthButton" };

            if (prazdny)
            {
                // Úvodní nabídka: tlačítka pod sebou. Vedle sebe by se tři
                // nevešla čitelně a v brýlích se hůř míří na úzký obdélník
                // než na široký řádek.
                var krok = IntroButtonHeight + IntroButtonGap;
                var horni = (jmena.Length - 1) * krok * 0.5f;

                for (var i = 0; i < jmena.Length; i++)
                {
                    var rt = pr.Find(jmena[i]) as RectTransform;
                    if (rt == null) continue;

                    rt.sizeDelta = new Vector2(IntroButtonWidth, IntroButtonHeight);
                    rt.anchoredPosition = new Vector2(0f, horni - i * krok);

                    var label = rt.Find("Label") as RectTransform;
                    if (label != null) label.sizeDelta = rt.sizeDelta;
                }

                return;
            }

            // Ostatní obrazovky mají tlačítka v řádku u dolní hrany.
            RadaTlacitek(pr, w, h, 38f);
        }

        /// <summary>
        /// Posadí viditelná tlačítka vedle sebe k dolní hraně panelu.
        ///
        /// POČÍTAJÍ SE JEN VIDITELNÁ. Dřív se všechna posadila na tutéž
        /// souřadnici, což nevadilo, dokud bylo vidět jedno — jakmile přibylo
        /// druhé, leželo přesně na prvním a spodní z nich se nedalo zmáčknout.
        ///
        /// JE TO JEDINÉ MÍSTO, kde se tlačítka umísťují. Obrazovka s výsledky
        /// si výšku panelu počítá zvlášť podle délky tabulky, takže si dřív
        /// posouvala tlačítko po svém — a o druhém nevěděla.
        /// </summary>
        private static void RadaTlacitek(RectTransform pr, float w, float h, float spodniOkraj)
        {
            var viditelna = new List<RectTransform>();

            foreach (var jmeno in new[] { "ActionButton", "SecondButton", "ThirdButton", "FourthButton" })
            {
                var rt = pr.Find(jmeno) as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                viditelna.Add(rt);
            }

            if (viditelna.Count == 0) return;

            const float mezera = 14f;
            const float vyskaT = 48f;

            // Šířka se zmenší, když se tlačítka do panelu nevejdou vedle sebe.
            var sirkaT = Mathf.Min(270f,
                (w - 40f - (viditelna.Count - 1) * mezera) / viditelna.Count);

            var celkem = viditelna.Count * sirkaT + (viditelna.Count - 1) * mezera;
            var x0 = -celkem * 0.5f + sirkaT * 0.5f;

            for (var i = 0; i < viditelna.Count; i++)
            {
                var rt = viditelna[i];
                rt.sizeDelta = new Vector2(sirkaT, vyskaT);
                rt.anchoredPosition = new Vector2(x0 + i * (sirkaT + mezera), -h * 0.5f + spodniOkraj);

                var label = rt.Find("Label") as RectTransform;
                if (label != null) label.sizeDelta = rt.sizeDelta;
            }
        }

        private void SetPanel(bool visible, string title, string body,
            string action, string second = null, string third = null, string fourth = null)
        {
            if (_panel != null) _panel.SetActive(visible);
            if (!visible) return;

            if (_title != null) _title.text = title ?? "";
            if (_body != null) _body.text = body ?? "";

            var hasAction = !string.IsNullOrEmpty(action);
            if (_actionButton != null) _actionButton.gameObject.SetActive(hasAction);
            if (hasAction && _actionLabel != null) _actionLabel.text = action;

            // Druhé tlačítko se ukazuje jen na obrazovkách, které ho mají —
            // v ostatních by matlo, protože by nic nedělalo.
            var hasSecond = !string.IsNullOrEmpty(second);
            if (_secondButton != null) _secondButton.gameObject.SetActive(hasSecond);
            if (hasSecond && _secondLabel != null) _secondLabel.text = second;

            var hasThird = !string.IsNullOrEmpty(third);
            if (_thirdButton != null) _thirdButton.gameObject.SetActive(hasThird);
            if (hasThird && _thirdLabel != null) _thirdLabel.text = third;

            var hasFourth = !string.IsNullOrEmpty(fourth);
            if (_fourthButton != null) _fourthButton.gameObject.SetActive(hasFourth);
            if (hasFourth && _fourthLabel != null) _fourthLabel.text = fourth;
        }

        private void OnSecondAction()
        {
            if (trialManager == null) return;

            // Na obrazovkách s výsledky vede druhé tlačítko zpět do nabídky.
            // Bez něj se po dokončení session nedalo spustit nic dalšího
            // jinak než restartem aplikace — tlačítko tam jen přepínalo mezi
            // vlastní tabulkou a žebříčkem.
            if (Current == Screen.SessionDone || Current == Screen.Ranking)
            {
                ShowIntro();
                return;
            }

            if (Current != Screen.Intro) return;

            trialManager.StartSession(TrialManager.SessionMode.Voice);
        }

        private void OnThirdAction() => SpustitTutorial(false);

        private void OnFourthAction() => SpustitTutorial(true);

        private void SpustitTutorial(bool hlasem)
        {
            if (trialManager == null) return;
            if (Current != Screen.Intro) return;

            trialManager.StartTutorial(hlasem);
            if (tutorialGuide != null) tutorialGuide.Begin(hlasem);
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

                // Z úvodu se session teprve zakládá — a musí se přitom říct,
                // která. Kdyby se rovnou volal StartNextBlock, běželo by
                // pořadí z minulé session a z nabídky by se nedalo přepnout.
                case Screen.Intro:
                    trialManager.StartSession(TrialManager.SessionMode.Classic);
                    return;

                default:
                    // Ze shrnutí bloku vede akce na další blok téže session.
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

            var btn2 = p.Find("SecondButton");
            if (btn2 != null)
            {
                _secondButton = btn2.GetComponent<Button>();
                _secondLabel = Get(btn2, "Label");
                if (_secondButton != null)
                {
                    _secondButton.onClick.RemoveAllListeners();
                    _secondButton.onClick.AddListener(OnSecondAction);
                }
            }

            var btn3 = p.Find("ThirdButton");
            if (btn3 != null)
            {
                _thirdButton = btn3.GetComponent<Button>();
                _thirdLabel = Get(btn3, "Label");
                if (_thirdButton != null)
                {
                    _thirdButton.onClick.RemoveAllListeners();
                    _thirdButton.onClick.AddListener(OnThirdAction);
                }
            }

            var btn4 = p.Find("FourthButton");
            if (btn4 != null)
            {
                _fourthButton = btn4.GetComponent<Button>();
                _fourthLabel = Get(btn4, "Label");
                if (_fourthButton != null)
                {
                    _fourthButton.onClick.RemoveAllListeners();
                    _fourthButton.onClick.AddListener(OnFourthAction);
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
            PanelStyle.ApplyRounded(panel.AddComponent<Image>(), PanelStyle.RadiusWindow,
                PanelStyle.Window);
            _panel = panel;

            var pr = (RectTransform)panel.transform;

            var title = New("Title", pr, new Vector2(0f, height * 0.5f - 34f), new Vector2(width - 40f, 46f));
            Text(title, 30f, TextAlignmentOptions.Center, PanelStyle.Title);

            // Rámeček textu je záměrně vyšší, než kam text sahá — jinak text
            // z rámečku přetéká a chování při delší instrukci je nepředvídatelné.
            // Horní hrana zůstává stejná, takže mezera k tlačítku se nemění.
            var body = New("Body", pr, new Vector2(0f, 15f), new Vector2(width - 50f, height - 145f));
            Text(body, 17f, TextAlignmentOptions.TopLeft, PanelStyle.TextSecondary);

            // Tři tlačítka. Pozice se stejně přepočítá v ApplyPanelLayout
            // podle obrazovky, tady jde jen o to, aby existovala.
            var y = -height * 0.5f + 38f;

            Tlacitko(pr, "ActionButton", new Vector2(0f, y),
                IntroButtonWidth, IntroButtonHeight, buttonColor);
            Tlacitko(pr, "SecondButton", new Vector2(0f, y),
                IntroButtonWidth, IntroButtonHeight, secondButtonColor);
            Tlacitko(pr, "ThirdButton", new Vector2(0f, y),
                IntroButtonWidth, IntroButtonHeight, thirdButtonColor);
            Tlacitko(pr, "FourthButton", new Vector2(0f, y),
                IntroButtonWidth, IntroButtonHeight, fourthButtonColor);

        }

        private void Tlacitko(RectTransform parent, string jmeno, Vector2 pos,
            float w, float h, Color barva)
        {
            var btn = New(jmeno, parent, pos, new Vector2(w, h));
            var img = btn.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, PanelStyle.RadiusButton, barva);
            var b = btn.AddComponent<Button>();
            b.targetGraphic = img;

            var label = New("Label", (RectTransform)btn.transform, Vector2.zero, new Vector2(w, h));
            var tmp = label.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = 22f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 11f;
            tmp.fontSizeMax = 22f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = PanelStyle.TextPrimary;
            tmp.raycastTarget = false;
        }

        private void BuildTimer(RectTransform canvas)
        {
            // Vpravo dole pod panelem.
            var t = New("Timer", canvas,
                new Vector2(width * 0.5f - 90f, -(height * 0.5f) - 10f), new Vector2(180f, 60f));
            Text(t, 34f, TextAlignmentOptions.Right, PanelStyle.Title);
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
