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
            /// <summary>Přehled všech uložených session, dá se v něm rolovat.</summary>
            AllResults = 4,

            /// <summary>Blok je nachystaný a čeká se na potvrzení připravenosti.</summary>
            Ready = 5
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

        [Tooltip("Pevná výška panelu s přehledem všech session. Obsah v něm " +
                 "roluje, protože počet řádků roste s každým měřením.")]
        [SerializeField] private float allResultsHeight = 620f;

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

        [SerializeField] private Color fifthButtonColor = PanelStyle.Neutral;

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
        // Pět tlačítek ve třech patrech plus hlavička s ID.
        private const float IntroHeight = 408f;

        /// <summary>
        /// Úvod je širší než ostatní obrazovky. Popisky jako „NÁCVIK: MENU"
        /// se do úzkého tlačítka nevešly a automatické zmenšování písma je
        /// scvrklo na nečitelnou velikost.
        /// </summary>
        private const float IntroWidth = 470f;

        /// <summary>
        /// Obrazovka připravenosti má vlastní, menší výšku. Panel pro shrnutí
        /// bloku je na dva řádky a tlačítko zbytečně velký a půlka zůstávala
        /// prázdná.
        /// </summary>
        private const float ReadyHeight = 200f;

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
        private Button _fifthButton;
        private TextMeshProUGUI _fifthLabel;

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
            trialManager.BlockReady += OnBlockReady;
            trialManager.BlockStarted += OnBlockStarted;
            trialManager.BlockEnded += OnBlockEnded;
            trialManager.SessionEnded += OnSessionEnded;
            trialManager.TutorialEnded += ShowIntro;
        }

        private void OnDisable()
        {
            if (trialManager == null) return;
            trialManager.BlockReady -= OnBlockReady;
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
                "KLASICKÁ VERZE", "HLASOVÁ VERZE", "KLASICKÁ", "HLASOVÁ",
                "VŠECHNY VÝSLEDKY");
            ApplyPanelLayout(IntroWidth, IntroHeight);
            PostavitHlavicku();
            PostavitPopisNacviku();
            ObnovitHlavicku();
        }

        // ---- Hlavicka uvodni nabidky ----

        private RectTransform _hlavicka;
        private TextMeshProUGUI _idText;
        private TextMeshProUGUI _stavText;

        /// <summary>
        /// Hlavicka s ID participanta a s tim, co uz pod nim bylo odehrano.
        ///
        /// PROC TO PATRI DO NABIDKY: ID se nastavovalo jen v editoru, takze
        /// v bryllich neslo poznat, pod kym se meri. Vsech 70 ulozenych
        /// session ma proto stejne P01 a rozdelit je zpetne nejde. Tohle je
        /// posledni misto, kde se ta chyba da chytit - pred startem bloku.
        /// </summary>
        private void PostavitHlavicku()
        {
            var pr = (RectTransform)_panel.transform;

            // STARÁ HLAVIČKA SE ZAHODÍ A POSTAVÍ ZNOVU, nepoužije se.
            //
            // Posluchače tlačítek se přidávají lambdou a ty se neserializují.
            // Když hlavička zůstane v uložené scéně, po načtení vypadá stejně,
            // ale šipky u ID nic nedělají — a nejde to poznat jinak než tím,
            // že se číslo nemění.
            var stara = pr.Find("Hlavicka");
            if (stara != null) Znicit(stara.gameObject);

            var koren = New("Hlavicka", pr, new Vector2(0f, IntroHeight * 0.5f - 52f),
                new Vector2(IntroWidth - 40f, 78f));
            _hlavicka = (RectTransform)koren.transform;

            var idGo = New("Id", _hlavicka, new Vector2(0f, 10f), new Vector2(160f, 40f));
            Text(idGo, 30f, TextAlignmentOptions.Center, PanelStyle.Title);
            _idText = idGo.GetComponent<TextMeshProUGUI>();
            _idText.fontStyle = FontStyles.Bold;

            SipkaId(_hlavicka, "Min", new Vector2(-118f, 10f), "–", -1);
            SipkaId(_hlavicka, "Plus", new Vector2(118f, 10f), "+", 1);

            var stavGo = New("Stav", _hlavicka, new Vector2(0f, -24f),
                new Vector2(IntroWidth - 60f, 24f));
            Text(stavGo, 15f, TextAlignmentOptions.Center, PanelStyle.TextSecondary);
            _stavText = stavGo.GetComponent<TextMeshProUGUI>();

            // BEZ DĚLICÍ ČÁRY. Hlavičku od tlačítek odděluje dost mezera
            // sama o sobě; čára navíc jen přidávala grafiku, která nic
            // neříká.
        }

        /// <summary>
        /// Slovo „nácvik" nad dvojicí menších tlačítek.
        ///
        /// PROČ NE NA TLAČÍTKÁCH: „NÁCVIK: MENU" je na úzké tlačítko moc
        /// dlouhé — písmo se automaticky zmenšilo tak, že bylo drobnější než
        /// všude jinde. Společný nadpis to slovo řekne jednou a na tlačítka
        /// zbude jen to, čím se od sebe liší. Navíc se tím trefí do stejných
        /// slov jako velká tlačítka nad nimi, takže je dvojice zjevná.
        /// </summary>
        private void PostavitPopisNacviku()
        {
            var pr = (RectTransform)_panel.transform;

            var stary = pr.Find("PopisNacviku");
            if (stary != null) Znicit(stary.gameObject);

            var go = New("PopisNacviku", pr, new Vector2(0f, IntroHeight * 0.5f - 250f),
                new Vector2(IntroWidth - 80f, 22f));
            Text(go, 15f, TextAlignmentOptions.Center, PanelStyle.TextSecondary);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = "TUTORIÁL";
            t.characterSpacing = 6f;
        }

        /// <summary>
        /// Přidá tlačítku obecné cvaknutí rozhraní.
        ///
        /// PROČ RUČNĚ: zvuk věší FeedbackDirector na všechna tlačítka ve
        /// Start(). Šipky u ID, zaškrtávátka i ostatní ovládání přehledu ale
        /// vznikají až při otevření obrazovky, tedy dávno potom — a zůstala
        /// by němá.
        /// </summary>
        private void Ozvucit(Button b)
        {
            if (b == null) return;

            if (_zvuk == null)
                _zvuk = FindFirstObjectByType<BP.Feedback.FeedbackDirector>(
                    FindObjectsInactive.Include);

            if (_zvuk != null) b.onClick.AddListener(_zvuk.PlayUiClick);
        }

        private BP.Feedback.FeedbackDirector _zvuk;

        private void SipkaId(RectTransform rodic, string jmeno, Vector2 pos, string znak, int krok)
        {
            var go = New(jmeno, rodic, pos, new Vector2(40f, 40f));
            var img = go.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, 12f, PanelStyle.Card);

            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => PosunoutId(krok));
            Ozvucit(b);

            var lbl = New("Label", (RectTransform)go.transform, new Vector2(0f, 1f),
                new Vector2(40f, 40f));
            Text(lbl, 22f, TextAlignmentOptions.Center, PanelStyle.TextPrimary);
            lbl.GetComponent<TextMeshProUGUI>().text = znak;
        }

        /// <summary>
        /// Posune ID o jedna. Format je vzdycky "P" a dve cislice, aby se
        /// soubory radily jako text ve stejnem poradi jako cisla.
        /// </summary>
        private void PosunoutId(int krok)
        {
            if (trialManager == null) return;

            var stare = trialManager.ParticipantId;
            var cislo = 1;

            if (!string.IsNullOrEmpty(stare) && stare.Length > 1)
                int.TryParse(stare.Substring(1), out cislo);

            // DVOJCIFERNÝ ZÁPIS ZŮSTÁVÁ DO P99, teprve pak se přidá třetí
            // číslice. Kdyby se přešlo na „P001", rozešlo by se to se všemi
            // dosud uloženými soubory, které mají v názvu „P01" — a přehled
            // by je považoval za jiného participanta.
            cislo = Mathf.Clamp(cislo + krok, 1, 999);
            trialManager.SetParticipantId("P" + (cislo < 100 ? cislo.ToString("00") : cislo.ToString()));

            ObnovitHlavicku();
        }

        /// <summary>
        /// Prepise ID a spocita, co uz ten participant odehral. Pocitaji se
        /// JEN DOKONCENE session - prerusena nic neznamena a jako "hotovo"
        /// by lhala.
        /// </summary>
        private void ObnovitHlavicku()
        {
            if (trialManager == null || _idText == null) return;

            var id = trialManager.ParticipantId;
            _idText.text = id;

            var klasika = 0;
            var hlas = 0;

            foreach (var s in trialManager.ReadAllSessions())
            {
                if (!s.complete) continue;
                if (s.participantId != id) continue;

                if (s.condition == InteractionCondition.Voice) hlas++;
                else klasika++;
            }

            if (_stavText == null) return;

            _stavText.text = klasika == 0 && hlas == 0
                ? "zatím nic neodehrál"
                : "hotovo:  klasická " + klasika + "×     hlasová " + hlas + "×";

            _stavText.color = klasika > 0 && hlas > 0
                ? PanelStyle.Positive
                : PanelStyle.TextSecondary;
        }

        /// <summary>
        /// Blok je nachystaný, ale čas ještě neběží.
        ///
        /// PROČ TA MEZIZASTÁVKA: dřív se stiskem v nabídce naráz objevilo
        /// pracoviště a rozjela časomíra, takže se do naměřeného času počítalo
        /// i rozhlížení. To není interakce — a protože s každým blokem klesá,
        /// smíchalo by se v datech s tím, čím se bloky skutečně liší.
        /// </summary>
        private void OnBlockReady(BlockDefinition block, int index)
        {
            Current = Screen.Ready;

            // STEJNÝ TEXT V OBOU PODMÍNKÁCH. Dřív tu byla věta navíc podle
            // toho, jestli jde o hlas nebo menu — jenže delší instrukce v jedné
            // z podmínek znamená, že si ji tam participant déle čte, a ten čas
            // padne do fáze, která má být pro obě verze stejná.
            SetPanel(true, "BLOK " + (index + 1),
                "Rozhlédni se po pracovišti.\nČas se rozeběhne až stiskem tlačítka.",
                "ZAČÍT");

            ApplyPanelLayout(width, ReadyHeight);

            // TLACITKO HNED POD TEXT. Obecne rozvrzeni pocita s telem na
            // nekolik odstavcu a tlacitko drzi u dolni hrany; u dvou radku
            // z toho zustala mezera pres pul panelu.
            var pr = (RectTransform)_panel.transform;

            var body = pr.Find("Body") as RectTransform;
            if (body != null)
            {
                body.sizeDelta = new Vector2(width - 50f, 46f);
                body.anchoredPosition = new Vector2(0f, ReadyHeight * 0.5f - 85f);
            }

            var akce = pr.Find("ActionButton") as RectTransform;
            if (akce != null) akce.anchoredPosition = new Vector2(0f, -ReadyHeight * 0.5f + 46f);
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

            // NADPIS JE JEN „VÝSLEDKY". Podmínka v něm stála proto, že
            // v tabulce nebyla — jenže sloupec Úloha ji teď nese v každém
            // řádku („hlasová + velikost"), takže v nadpisu už jen
            // zdvojovala to, co je pod ním.
            ShowTable("VÝSLEDKY", table, "MENU");
        }

        /// <summary>
        /// Přehled všech uložených session. Na rozdíl od žebříčku se nic
        /// nefiltruje ani neslučuje — je to výpis toho, co se kdy naměřilo,
        /// včetně přerušených pokusů.
        ///
        /// Dostupný z úvodní nabídky, aby se do něj dalo podívat i bez
        /// odehrání session. Doteď se výsledky daly vidět jen na konci hraní.
        /// </summary>
        public void ShowAllResults()
        {
            Current = Screen.AllResults;

            ApplyResultsWidth();
            SetPanel(true, "VŠECHNY VÝSLEDKY", "", "MENU");
            if (_title != null) _title.text = "VŠECHNY VÝSLEDKY";

            var pr = (RectTransform)_panel.transform;
            pr.sizeDelta = new Vector2(resultsWidth, allResultsHeight);

            var canvasRt = pr.parent as RectTransform;
            if (canvasRt != null)
                canvasRt.sizeDelta = new Vector2(resultsWidth, allResultsHeight + 90f);

            // Nadpis drzi horni hranu panelu. Bez tohohle zustal viset tam,
            // kde byl pri malem panelu - tedy uprostred tabulky.
            var title = pr.Find("Title") as RectTransform;
            if (title != null)
            {
                title.sizeDelta = new Vector2(resultsWidth - 40f, 46f);
                title.anchoredPosition = new Vector2(0f, allResultsHeight * 0.5f - 34f);
            }

            if (_body != null) _body.gameObject.SetActive(false);

            PostavitPrehled(pr);
            NaplnitPrehled();

            RadaTlacitek(pr, resultsWidth, allResultsHeight, 34f);
        }

        // ---- Prehled vsech session ----

        private bool _chciHlas = true;
        private bool _chciKlasiku = true;

        /// <summary>Radit podle casu (nejlepsi nahore), nebo od nejnovejsich?</summary>
        private bool _podleCasu;

        private RectTransform _prehled;
        private RectTransform _obsah;

        /// <summary>Zatrzitka: ctverecek, ktery se vybarvi, kdyz je zapnuto.</summary>
        private readonly List<Image> _zatrzitka = new List<Image>();

        /// <summary>Ramecek kolem zatrzitka - svitne, kdyz je volba zapnuta.</summary>
        private readonly List<Image> _ramecky = new List<Image>();

        private readonly List<TextMeshProUGUI> _popisky = new List<TextMeshProUGUI>();

        /// <summary>
        /// Sloupce prehledu: nadpis a sirka. TABULKA SE SKLADA Z BUNEK, ne
        /// z jednoho textu s mezerami.
        ///
        /// Driv to byl jeden odstavec vysazeny neproporcionalne pres znacku
        /// mspace - a ta natahne rozestupy i uvnitr slov, takze "Celkem" se
        /// cetlo jako "C e l k e m". Vlastni bunky se daji zarovnat na stred
        /// a pismo zustane normalni.
        /// </summary>
        private static readonly (string nadpis, float sirka)[] Sloupce =
        {
            ("Kdy", 120f),
            ("Úloha", 110f),
            ("Kdo", 70f),
            ("Čistý čas", 110f),
            ("S postihy", 110f),
            ("Chyby", 80f),
            ("Terče", 100f),
            ("Stav", 120f)
        };

        private const float RadekVyska = 30f;

        private void PostavitPrehled(RectTransform pr)
        {
            // Stejný důvod jako u hlavičky: uložená kopie by měla mrtvá
            // zaškrtávátka i šipky.
            var stare = pr.Find("Prehled");
            if (stare != null) Znicit(stare.gameObject);

            const float titleArea = 74f;
            const float bottomPad = 86f;
            const float filtrVyska = 40f;
            const float hlavickaVyska = 28f;

            var sirka = resultsWidth - 50f;
            var vyskaCela = allResultsHeight - titleArea - bottomPad;

            var koren = New("Prehled", pr, new Vector2(0f, -(titleArea - bottomPad) * 0.5f),
                new Vector2(sirka, vyskaCela));
            _prehled = (RectTransform)koren.transform;

            // Tabulka je o posuvnik uzsi nez panel a je proti nemu posunuta
            // doleva. Hlavicka musi sedet na TOMTO stredu, ne na stredu
            // panelu - jinak jsou nadpisy o par pixelu vedle hodnot.
            const float SipkaPas = 52f;
            var tabulkaSirka = sirka - SipkaPas;
            var tabulkaX = -SipkaPas * 0.5f;

            // ---- Radek se zatrzitky ----
            var filtrY = vyskaCela * 0.5f - filtrVyska * 0.5f;
            var popisky = new[] { "hlasová", "klasická", "od nejlepších" };
            var sirky = new[] { 150f, 160f, 190f };

            var celkem = 0f;
            foreach (var w in sirky) celkem += w;
            var x = tabulkaX - celkem * 0.5f;

            _zatrzitka.Clear();
            _ramecky.Clear();
            _popisky.Clear();
            for (var i = 0; i < popisky.Length; i++)
            {
                var index = i;
                var w = sirky[i];

                var go = New("Volba" + i, _prehled, new Vector2(x + w * 0.5f, filtrY),
                    new Vector2(w, 30f));

                // Prusvitny podklad jen kvuli tomu, aby slo na skupinu
                // klepnout celou, ne jen na ctverecek.
                var plocha = go.AddComponent<Image>();
                plocha.color = new Color(1f, 1f, 1f, 0.0001f);

                var b = go.AddComponent<Button>();
                b.targetGraphic = plocha;
                b.onClick.AddListener(() =>
                {
                    if (index == 0) _chciHlas = !_chciHlas;
                    else if (index == 1) _chciKlasiku = !_chciKlasiku;
                    else _podleCasu = !_podleCasu;
                    NaplnitPrehled();
                });
                Ozvucit(b);

                var box = New("Box", (RectTransform)go.transform,
                    new Vector2(-w * 0.5f + 13f, 0f), new Vector2(22f, 22f));
                var bi = box.AddComponent<Image>();
                PanelStyle.ApplyRounded(bi, 6f, PanelStyle.Card);
                _ramecky.Add(bi);

                var vypln = New("Vypln", (RectTransform)box.transform, Vector2.zero,
                    new Vector2(12f, 12f));
                var vi = vypln.AddComponent<Image>();
                PanelStyle.ApplyRounded(vi, 3f, PanelStyle.Window);
                _zatrzitka.Add(vi);

                var lbl = New("Label", (RectTransform)go.transform,
                    new Vector2(15f, 0f), new Vector2(w - 34f, 24f));
                Text(lbl, 16f, TextAlignmentOptions.Left, PanelStyle.TextPrimary);
                _popisky.Add(lbl.GetComponent<TextMeshProUGUI>());
                _popisky[_popisky.Count - 1].text = popisky[i];

                x += w;
            }

            // ---- Hlavicka sloupcu ----
            var hlavY = filtrY - filtrVyska * 0.5f - hlavickaVyska * 0.5f;
            var hlavicka = New("Hlavicka", _prehled, new Vector2(tabulkaX, hlavY),
                new Vector2(tabulkaSirka, hlavickaVyska));
            PostavitBunky((RectTransform)hlavicka.transform, null, PanelStyle.TextSecondary, true);

            // Tenka linka pod hlavickou oddeli nadpisy od hodnot.
            var linka = New("Linka", _prehled,
                new Vector2(tabulkaX, hlavY - hlavickaVyska * 0.5f - 2f),
                new Vector2(tabulkaSirka, 1.5f));
            var li = linka.AddComponent<Image>();
            li.color = new Color(1f, 1f, 1f, 0.12f);

            // ---- Rolovaci plocha ----
            var vyrezVyska = vyskaCela - filtrVyska - hlavickaVyska - 12f;
            var vyrezY = hlavY - hlavickaVyska * 0.5f - vyrezVyska * 0.5f - 6f;

            var vyrez = New("Vyrez", _prehled, new Vector2(tabulkaX, vyrezY),
                new Vector2(tabulkaSirka, vyrezVyska));
            vyrez.AddComponent<RectMask2D>();

            var obsah = New("Obsah", (RectTransform)vyrez.transform, Vector2.zero,
                new Vector2(tabulkaSirka, 10f));
            _obsah = (RectTransform)obsah.transform;
            _obsah.anchorMin = new Vector2(0.5f, 1f);
            _obsah.anchorMax = new Vector2(0.5f, 1f);
            _obsah.pivot = new Vector2(0.5f, 1f);
            _obsah.anchoredPosition = Vector2.zero;

            var scroll = vyrez.AddComponent<ScrollRect>();
            scroll.content = _obsah;
            scroll.viewport = (RectTransform)vyrez.transform;
            scroll.horizontal = false;
            scroll.vertical = true;
            _rolovani = scroll;

            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 30f;
            // ŠIPKY MÍSTO POSUVNÍKU.
            //
            // Rolování páčkou přes XRI se ukázalo jako nespolehlivé a tažení
            // jezdce v okně, které visí napevno před obličejem, je nepohodlné:
            // hýbe se ruka i celý panel zároveň. Klepnutí na šipku je jeden
            // uzavřený úkon — posune o stránku a nic dalšího se neděje.
            _sipkaNahoru = Sipka(_prehled, "SipkaNahoru",
                new Vector2(sirka * 0.5f - 26f, vyrezY + vyrezVyska * 0.5f - 24f),
                "▲", 1);
            _sipkaDolu = Sipka(_prehled, "SipkaDolu",
                new Vector2(sirka * 0.5f - 26f, vyrezY - vyrezVyska * 0.5f + 24f),
                "▼", -1);

            // Prázdná tabulka potřebuje větu. Bez ní vypadá odškrtnutí obou
            // voleb jako by se aplikace zasekla nebo ztratila data.
            var prazdno = New("Prazdno", _prehled, new Vector2(tabulkaX, vyrezY),
                new Vector2(tabulkaSirka, 40f));
            Text(prazdno, 17f, TextAlignmentOptions.Center, PanelStyle.TextSecondary);
            _prazdnoText = prazdno.GetComponent<TextMeshProUGUI>();
            _prazdnoText.text = "nic není vybráno";
            prazdno.SetActive(false);
        }

        private ScrollRect _rolovani;
        private GameObject _sipkaNahoru;
        private GameObject _sipkaDolu;
        private TextMeshProUGUI _prazdnoText;

        /// <summary>
        /// Tlačítko pro posun seznamu o stránku. Směr je +1 nahoru, -1 dolů.
        /// </summary>
        private GameObject Sipka(RectTransform rodic, string jmeno, Vector2 pos,
            string znak, int smer)
        {
            var go = New(jmeno, rodic, pos, new Vector2(38f, 38f));
            var img = go.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, 11f, PanelStyle.Card);

            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => Posunout(smer));
            Ozvucit(b);

            var lbl = New("Label", (RectTransform)go.transform, new Vector2(0f, 1f),
                new Vector2(38f, 38f));
            Text(lbl, 18f, TextAlignmentOptions.Center, PanelStyle.TextPrimary);
            lbl.GetComponent<TextMeshProUGUI>().text = znak;

            return go;
        }

        /// <summary>
        /// Posune seznam o 80 % výšky výřezu. Ne o celou: překryv pár řádků
        /// drží souvislost, jinak člověk po každém kliknutí hledá, kde skončil.
        /// </summary>
        private void Posunout(int smer)
        {
            if (_rolovani == null || _obsah == null) return;

            var vyrez = _rolovani.viewport;
            if (vyrez == null) return;

            var rozsah = _obsah.sizeDelta.y - vyrez.rect.height;
            if (rozsah <= 0f) return;

            var krok = vyrez.rect.height * 0.8f;
            var y = _obsah.anchoredPosition.y - smer * krok;

            _obsah.anchoredPosition = new Vector2(_obsah.anchoredPosition.x,
                Mathf.Clamp(y, 0f, rozsah));
        }

        /// <summary>
        /// Rozmisti bunky jednoho radku podle <see cref="Sloupce"/>.
        /// Kdyz je <paramref name="hodnoty"/> null, pisou se nadpisy sloupcu.
        /// </summary>
        private void PostavitBunky(RectTransform radek, string[] hodnoty, Color barva, bool tucne)
        {
            var celkem = 0f;
            foreach (var sl in Sloupce) celkem += sl.sirka;

            var x = -celkem * 0.5f;

            for (var i = 0; i < Sloupce.Length; i++)
            {
                var sirka = Sloupce[i].sirka;
                var go = New("C" + i, radek, new Vector2(x + sirka * 0.5f, 0f),
                    new Vector2(sirka, RadekVyska));

                // NA STRED. Sloupce maji pevnou sirku, takze vystredene
                // hodnoty drzi svislici i pri ruzne dlouhych textech.
                Text(go, 16f, TextAlignmentOptions.Center, barva);

                var t = go.GetComponent<TextMeshProUGUI>();
                t.text = hodnoty == null ? Sloupce[i].nadpis : hodnoty[i];
                t.textWrappingMode = TextWrappingModes.NoWrap;
                if (tucne) t.fontStyle = FontStyles.Bold;

                x += sirka;
            }
        }

        private void NaplnitPrehled()
        {
            if (_obsah == null) return;

            for (var i = _obsah.childCount - 1; i >= 0; i--)
                DestroyImmediate(_obsah.GetChild(i).gameObject);

            var vse = trialManager != null
                ? trialManager.ReadAllSessions()
                : new List<SessionScore>();

            // ZAŠKRTÁVÁTKO ZNAMENÁ PŘESNĚ TO, CO JE VIDĚT. Dřív se „nic
            // zaškrtnutého" chápalo jako „bez filtru" a ukázalo se všechno —
            // úsporné, ale nepředvídatelné: odškrtnutím obou voleb se seznam
            // místo vyprázdnění naopak rozšířil.
            var vybrane = new List<SessionScore>();
            foreach (var s in vse)
            {
                var jeHlas = s.condition == InteractionCondition.Voice;
                if (jeHlas && !_chciHlas) continue;
                if (!jeHlas && !_chciKlasiku) continue;

                vybrane.Add(s);
            }

            if (_podleCasu)
            {
                // Dokoncene napred. Prerusena session ma kratky cas prave
                // proto, ze se nedohrala, a jinak by se vyhoupla na spicku.
                vybrane.Sort((x, y) =>
                {
                    if (x.complete != y.complete) return x.complete ? -1 : 1;
                    return x.TotalTime.CompareTo(y.TotalTime);
                });
            }
            else
            {
                // Jmeno souboru nese datum i cas v poradi, ve kterem se da
                // radit jako text.
                vybrane.Sort((x, y) => string.CompareOrdinal(y.soubor, x.soubor));
            }

            var sirka = _obsah.sizeDelta.x;

            for (var i = 0; i < vybrane.Count; i++)
            {
                var s = vybrane[i];

                var go = New("Radek" + i, _obsah, Vector2.zero, new Vector2(sirka, RadekVyska));

                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -(i + 0.5f) * RadekVyska);

                // Kazdy druhy radek slaby podklad. V dlouhem seznamu oko
                // jinak sklouzne o radek vedle.
                if (i % 2 == 1)
                {
                    var pruh = go.AddComponent<Image>();
                    pruh.color = new Color(1f, 1f, 1f, 0.035f);
                }

                // Prerusena session je ztlumena - at se v seznamu neprehledne,
                // ze jeji cas nic neznamena.
                var barva = s.complete
                    ? PanelStyle.TextPrimary
                    : new Color(PanelStyle.TextSecondary.r, PanelStyle.TextSecondary.g,
                        PanelStyle.TextSecondary.b, 0.55f);

                PostavitBunky(rt, new[]
                {
                    Kdy(s.soubor),
                    s.condition == InteractionCondition.Voice ? "hlasová" : "klasická",
                    s.participantId ?? "?",
                    BlockTimer.Format(s.cleanTime),
                    BlockTimer.Format(s.TotalTime),
                    s.wrongObjects + "×",
                    s.activations > 0 ? s.hits + "/" + s.activations : "—",
                    s.complete ? "dokončeno" : "přerušeno"
                }, barva, false);
            }

            _obsah.sizeDelta = new Vector2(_obsah.sizeDelta.x,
                Mathf.Max(vybrane.Count * RadekVyska, 10f));
            _obsah.anchoredPosition = Vector2.zero;

            // ZAPNUTA VOLBA SE POZNA TREMI ZNAKY NARAZ: vybarveny ramecek,
            // tmavy puntik uvnitr a plne bily popisek. Jeden signal byl malo -
            // prazdny a plny krouzek se od sebe na dalku v bryllich nelisily.
            var stavy = new[] { _chciHlas, _chciKlasiku, _podleCasu };

            for (var i = 0; i < stavy.Length; i++)
            {
                if (i < _zatrzitka.Count) _zatrzitka[i].enabled = stavy[i];

                if (i < _ramecky.Count)
                    _ramecky[i].color = stavy[i] ? PanelStyle.TextPrimary : PanelStyle.Card;

                if (i < _popisky.Count)
                    _popisky[i].color = stavy[i]
                        ? PanelStyle.TextPrimary
                        : new Color(PanelStyle.TextSecondary.r, PanelStyle.TextSecondary.g,
                            PanelStyle.TextSecondary.b, 0.7f);
            }

            // Šipky mají smysl jen tehdy, když se seznam nevejde.
            var vejdeSe = _rolovani != null && _rolovani.viewport != null
                          && _obsah.sizeDelta.y <= _rolovani.viewport.rect.height;

            if (_sipkaNahoru != null) _sipkaNahoru.SetActive(!vejdeSe);
            if (_sipkaDolu != null) _sipkaDolu.SetActive(!vejdeSe);

            if (_prazdnoText != null)
            {
                _prazdnoText.gameObject.SetActive(vybrane.Count == 0);
                _prazdnoText.text = vse.Count == 0
                    ? "zatím žádné uložené session"
                    : "nic není vybráno";
            }

        }

        /// <summary>
        /// Datum a cas z nazvu souboru "P01_souhrn_20260918_204729.csv"
        /// jako "18/9 20:47". Bez tecek - v tabulce se ctou jako konce vet.
        /// </summary>
        private static string Kdy(string soubor)
        {
            if (string.IsNullOrEmpty(soubor)) return "?";

            var casti = soubor.Split('_');
            if (casti.Length < 4) return soubor;

            var d = casti[2];
            var t = casti[3];
            if (d.Length < 8 || t.Length < 6) return soubor;

            int mesic, den;
            if (!int.TryParse(d.Substring(4, 2), out mesic)) return soubor;
            if (!int.TryParse(d.Substring(6, 2), out den)) return soubor;

            return den + "/" + mesic + " " + t.Substring(0, 2) + ":" + t.Substring(2, 2);
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

            var jmena = new[] { "ActionButton", "SecondButton", "ThirdButton", "FourthButton", "FifthButton" };

            if (prazdny)
            {
                // UVODNI NABIDKA MA TRI PATRA PODLE VAHY.
                //
                // Nahore dve velka tlacitka mereni - to je jediny duvod, proc
                // se aplikace spousti. Pod nimi mensi nacviky vedle sebe,
                // protoze nacvik neni mereni a nema si rikat o stejnou
                // pozornost. Uplne dole data. Dokud vypadalo pet tlacitek
                // stejne, dalo se omylem spustit mereni misto nacviku.
                var horni = h * 0.5f;
                const float hlavni = 390f;

                Posadit(pr, jmena[0], new Vector2(0f, horni - 128f), hlavni, 58f);
                Posadit(pr, jmena[1], new Vector2(0f, horni - 194f), hlavni, 58f);

                // Mezera mezi měřením a nácvikem je VĚTŠÍ než mezi tlačítky
                // uvnitř skupin. Bez toho vypadá pět tlačítek jako jeden
                // seznam a rozdělení podle váhy se ztratí.
                const float nacvikSirka = 189f;
                Posadit(pr, jmena[2], new Vector2(-nacvikSirka * 0.5f - 6f, horni - 288f),
                    nacvikSirka, 46f);
                Posadit(pr, jmena[3], new Vector2(nacvikSirka * 0.5f + 6f, horni - 288f),
                    nacvikSirka, 46f);

                Posadit(pr, jmena[4], new Vector2(0f, horni - 350f), hlavni, 46f);

                return;
            }

            // Ostatní obrazovky mají tlačítka v řádku u dolní hrany.
            RadaTlacitek(pr, w, h, 38f);
        }

        /// <summary>
        /// Zruší objekt hned, ne až na konci snímku. Odložené Destroy by
        /// nechalo starou kopii ve scéně ještě chvíli žít a Find by ji našel
        /// místo nové.
        /// </summary>
        private static void Znicit(GameObject go)
        {
            // PŘEJMENOVAT PŘED ZRUŠENÍM. Destroy v play módu odkládá zánik
            // na konec snímku, takže by Find ještě chvíli nacházel mrtvou
            // kopii místo nové — a nová by se místo zobrazení schovala za ni.
            go.name += "_mrtve";

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private static void Posadit(RectTransform pr, string jmeno, Vector2 pos,
            float w, float h)
        {
            var rt = pr.Find(jmeno) as RectTransform;
            if (rt == null) return;

            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = pos;

            // POPISEK JE UŽŠÍ NEŽ TLAČÍTKO. Když má stejný rámeček, sahá text
            // až na okraj a automatické zmenšování se zapne později, než je
            // potřeba — písmo se pak dotýká zaoblených rohů.
            var label = rt.Find("Label") as RectTransform;
            if (label != null) label.sizeDelta = new Vector2(w - 28f, h - 8f);
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

            foreach (var jmeno in new[] { "ActionButton", "SecondButton", "ThirdButton", "FourthButton", "FifthButton" })
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
            string action, string second = null, string third = null, string fourth = null,
            string fifth = null)
        {
            if (_panel != null) _panel.SetActive(visible);
            if (!visible) return;

            // Rolovací plocha patří JEN přehledu. Každá jiná obrazovka ji
            // schová a vrátí obyčejný Body — jinak by zůstala viset přes
            // úvodní nabídku.
            if (_panel != null)
            {
                var rig = _panel.transform.Find("Prehled");
                if (rig != null && Current != Screen.AllResults) rig.gameObject.SetActive(false);

                var hl = _panel.transform.Find("Hlavicka");
                if (hl != null && Current != Screen.Intro) hl.gameObject.SetActive(false);

                var pn = _panel.transform.Find("PopisNacviku");
                if (pn != null && Current != Screen.Intro) pn.gameObject.SetActive(false);
                if (_body != null && Current != Screen.AllResults) _body.gameObject.SetActive(true);
            }

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

            var hasFifth = !string.IsNullOrEmpty(fifth);
            if (_fifthButton != null) _fifthButton.gameObject.SetActive(hasFifth);
            if (hasFifth && _fifthLabel != null) _fifthLabel.text = fifth;
        }

        private void OnSecondAction()
        {
            if (trialManager == null) return;

            // Na obrazovkách s výsledky vede druhé tlačítko zpět do nabídky.
            // Bez něj se po dokončení session nedalo spustit nic dalšího
            // jinak než restartem aplikace — tlačítko tam jen přepínalo mezi
            // vlastní tabulkou a žebříčkem.
            if (Current == Screen.SessionDone || Current == Screen.AllResults)
            {
                ShowIntro();
                return;
            }

            if (Current != Screen.Intro) return;

            trialManager.StartSession(TrialManager.SessionMode.Voice);
        }

        private void OnThirdAction() => SpustitTutorial(false);

        private void OnFourthAction() => SpustitTutorial(true);

        private void OnFifthAction()
        {
            if (Current == Screen.Intro) ShowAllResults();
        }

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
                // Na konci session i v přehledu vede akce zpátky do nabídky —
                // není co dalšího spouštět.
                case Screen.SessionDone:
                case Screen.AllResults:
                    ShowIntro();
                    return;

                // Potvrzení připravenosti: teprve tady se odkryje předloha
                // a začne se měřit.
                case Screen.Ready:
                    trialManager.StartReadyBlock();
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

            var btn5 = p.Find("FifthButton");
            if (btn5 != null)
            {
                _fifthButton = btn5.GetComponent<Button>();
                _fifthLabel = Get(btn5, "Label");
                if (_fifthButton != null)
                {
                    _fifthButton.onClick.RemoveAllListeners();
                    _fifthButton.onClick.AddListener(OnFifthAction);
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
            Tlacitko(pr, "FifthButton", new Vector2(0f, y),
                IntroButtonWidth, IntroButtonHeight, fifthButtonColor);

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
