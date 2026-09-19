using BP.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Předloha na vyžádání — v bloku, kde je zapnutá, je plánek skrytý
    /// a odkrývá se jen na stisk tlačítka, na pevně danou dobu.
    ///
    /// PROČ TO V PRÁCI JE: hypotéza tvrdí, že hlas uvolní ruce A OČI. Dokud
    /// je plánek trvale na očích, zatížení zraku se nikde neměří — participant
    /// se dívá, kolik chce, zadarmo. Když se musí o pohled říct, dostane
    /// pohled cenu a začne se chovat jako zdroj, o který se soutěží.
    ///
    /// CO SE Z TOHO MĚŘÍ: kolikrát si participant plánek vyžádal a jak dlouho
    /// ho celkem měl. Očekávání je, že v menu podmínce si ho vyžádá častěji —
    /// po každém pohledu stranou ztratí místo v mřížce a musí hledat znovu.
    ///
    /// POZOR NA SPRAVEDLNOST PODMÍNEK: odkrytí stojí jedno stisknutí tlačítka,
    /// tedy akci rukou. To je zatím stejné pro obě podmínky. Až vznikne hlasový
    /// modul, musí jít plánek vyžádat i hlasem — jinak by hlasová podmínka
    /// platila rukou za něco, co jinak řeší hlasem, a výhoda by se uměle smazala.
    /// </summary>
    public class ReferenceOnDemand : MonoBehaviour
    {
        [Header("Závislosti")]
        [Tooltip("Kotva předlohy, která se skrývá a odkrývá.")]
        [SerializeField] private GameObject reference;

        [SerializeField] private TMP_FontAsset font;

        [Tooltip("Ikona oka. Generuje BP/Generovat ikony tlacitek.")]
        [SerializeField] private Sprite planIcon;

        [Header("Chování")]
        [Tooltip("Jak dlouho zůstane předloha odkrytá po stisknutí (sekundy). " +
                 "HODNOTU JE NUTNÉ ODPILOTOVAT: příliš dlouhé okno znamená, že " +
                 "si participant vyžádá plánek jednou a má ho pořád — a rozdíl " +
                 "mezi podmínkami zmizí.")]
        [SerializeField] private float revealDuration = 2f;

        [Header("Jak se plánek ukáže")]
        [Tooltip("Zvětšení plánku po dobu, kdy je mechanika zapnutá. Plánek " +
                 "je jinak zmenšený na 60 % a ve dvousekundovém okně se nedá " +
                 "přečíst — zdánlivá výška 12 stupňů je na osm kroků málo.")]
        [SerializeField] private float revealScale = 1.7f;

        [Tooltip("Kam se plánek přesune, když je mechanika zapnutá. Blíž ke " +
                 "stavbě, aby oko nemuselo skákat přes 23 stupňů a zpátky. " +
                 "Nulový vektor = nechat na místě.")]
        [SerializeField] private Vector3 revealLocalPosition = new Vector3(-0.30f, 1.24f, 0.30f);

        [Header("Tlačítko")]
        [Tooltip("Umístění vůči rodiči. Patří k předloze, aby se participant " +
                 "díval tím směrem, kterým se pak dívá na plánek.")]
        [SerializeField] private Vector3 buttonLocalPosition = new Vector3(-0.34f, 0.98f, 0.30f);

        [SerializeField] private float width = 240f;
        [Tooltip("Vyšší než dřív — nese dva řádky a ikonu, ne jedno slovo.")]
        [SerializeField] private float height = 96f;
        [SerializeField] private float canvasScale = 0.0007f;
        [SerializeField] private Color buttonColor = PanelStyle.Info;

        [Tooltip("Natočit tlačítko k participantovi. Bere polohu hlavy při stavbě.")]
        [SerializeField] private bool faceHead = true;

        private const string CanvasName = "RevealCanvas";

        /// <summary>
        /// Plánek se právě odkryl. Odebírá zpětná vazba, aby si Core
        /// nemuselo tahat odkaz na ozvučení — závislost by pak vedla
        /// špatným směrem.
        /// </summary>
        public event System.Action Revealed;

        /// <summary>Je mechanika v tomto bloku zapnutá?</summary>
        public bool IsActive { get; private set; }

        /// <summary>Kolikrát si participant plánek vyžádal.</summary>
        public int RevealCount { get; private set; }

        /// <summary>
        /// Kolik sekund měl plánek celkem odkrytý — VČETNĚ okna, které
        /// právě běží. Počítá se až při čtení, takže na pořadí volání
        /// při ukončování bloku nezáleží: souhrn vyjde správně, ať se čte
        /// před uzavřením okna nebo po něm.
        /// </summary>
        public float TotalRevealTime =>
            _closedTime + (_revealed ? Time.realtimeSinceStartup - _revealStart : 0f);

        private TrialLogger _logger;
        private Vector3 _puvodniPozice;
        private Vector3 _puvodniMeritko;
        private bool _pozicePrevzata;
        private float _hideAt;
        private float _revealStart;
        private float _closedTime;
        private bool _revealed;
        private TextMeshProUGUI _label;

        private void Awake()
        {
            var canvas = transform.Find(CanvasName) as RectTransform;

            // Canvas se v Awake NERUŠÍ — TrackedDeviceGraphicRaycaster si drží
            // registraci a při zrušení canvasu v Awake padá interakce s UI.
            if (canvas == null) Build();
            else Bind(canvas);

            ShowButton(false);
        }

        private void Update()
        {
            if (!IsActive || !_revealed) return;
            if (Time.realtimeSinceStartup < _hideAt) return;

            Skryt();
        }

        /// <summary>
        /// Zapne nebo vypne mechaniku pro tento blok. Volá TrialManager.
        /// Musí se volat AŽ POTOM, co se zviditelní pracoviště — jinak by
        /// zviditelnění předlohu zase odkrylo.
        /// </summary>
        public void Configure(TrialLogger logger, bool active) => Configure(logger, active, false);

        /// <summary>
        /// Zapne nebo vypne mechaniku pro tento blok.
        /// </summary>
        /// <param name="hlasem">
        /// V hlasové podmínce se plánek vyžaduje ŘEČÍ, ne tlačítkem. Tlačítko
        /// se proto schová a místo něj se ukáže krátká věta, co říct — kdyby
        /// tlačítko zůstalo, dalo by se odkrytí koupit rukou a metrika by
        /// přestala měřit cenu pohledu v té podmínce, pro kterou je určená.
        /// </param>
        public void Configure(TrialLogger logger, bool active, bool hlasem)
        {
            _hlasem = hlasem;
            // Kdyz blok konci behem odkryteho okna, cas se musi doscitat
            // TED - jinak by se posledni odkryti do souctu vubec nedostalo.
            if (_revealed) Skryt();

            _logger = logger;
            IsActive = active;

            RevealCount = 0;
            _closedTime = 0f;
            _revealed = false;

            ShowButton(active);

            if (reference != null)
            {
                var t = reference.transform;

                // Původní stav se zapamatuje při PRVNÍM zapnutí a pak už se
                // jen obnovuje — jinak by se při druhém zapnutí uložil stav
                // už zvětšeného plánku a ten by rostl s každým blokem.
                if (!_pozicePrevzata)
                {
                    _puvodniPozice = t.localPosition;
                    _puvodniMeritko = t.localScale;
                    _pozicePrevzata = true;
                }

                if (active)
                {
                    t.localScale = _puvodniMeritko * revealScale;
                    if (revealLocalPosition.sqrMagnitude > 0.0001f)
                        t.localPosition = revealLocalPosition;

                    // Mimo tuhle mechaniku zůstává předloha vidět tak, jak byla —
                    // o její viditelnosti pak rozhoduje jen skrývání pracoviště.
                    reference.SetActive(false);
                }
                else
                {
                    t.localPosition = _puvodniPozice;
                    t.localScale = _puvodniMeritko;
                }
            }

            RefreshLabel();
        }

        /// <summary>
        /// Dočasně zakáže odkrývání, aniž by se měnilo nastavení bloku.
        ///
        /// POUŽÍVÁ SE PŘI ČEKÁNÍ NA START. Blok už je nachystaný, ale čas
        /// neběží — a kdyby si participant mohl prohlédnout plánek dřív,
        /// nezapočítalo by se to odkrytí a měřená veličina by se dala obejít.
        /// </summary>
        public void SetBlocked(bool value)
        {
            _zablokovano = value;
            NastavitOvladatelnost();
        }

        private bool _zablokovano;

        /// <summary>Odkryje předlohu na dobu okna. Navěšeno na tlačítko.</summary>
        public void Reveal()
        {
            if (_zablokovano || !IsActive || reference == null) return;

            // OPĚTOVNÝ STISK BĚHEM ODKRYTÍ NEDĚLÁ NIC. Dřív okno prodlužoval,
            // aby se mačkáním nenafoukl počet vyžádání — jenže tím šlo držet
            // plánek trvale otevřený na jedno jediné započtené odkrytí.
            // A trvale viditelný plánek znamená, že blok neměří nic: celý
            // jeho smysl je v tom, kolikrát se participant potřebuje podívat.
            //
            // Počet se tím nafouknout dá, ale jen tak, že se čeká na zavření
            // a mačká znovu — a to už je poctivý údaj: opravdu se podíval
            // znovu a stálo ho to čas.
            if (_revealed) return;

            _hideAt = Time.realtimeSinceStartup + revealDuration;

            _revealed = true;
            _revealStart = Time.realtimeSinceStartup;
            RevealCount++;
            reference.SetActive(true);

            if (_logger != null)
                _logger.Log(LogEvent.ReferenceRevealed, detail: "poradi=" + RevealCount);

            if (Revealed != null) Revealed();

            RefreshLabel();
        }

        private void Skryt()
        {
            _revealed = false;

            // Scita se SKUTECNY cas, ne delka okna. Okno se da prodlouzit
            // opetovnym stiskem, takze pausalni delka by lhala.
            _closedTime += Time.realtimeSinceStartup - _revealStart;

            if (reference != null) reference.SetActive(false);
            RefreshLabel();
        }

        /// <summary>Souhrn do řádku BlockEnd.</summary>
        public string GetSummary()
        {
            return string.Format("odkryti={0}x celkem={1:F1}s", RevealCount, TotalRevealTime);
        }

        // ---- Zobrazení ----

        private void ShowButton(bool value)
        {
            var canvas = transform.Find(CanvasName);
            if (canvas != null) canvas.gameObject.SetActive(value);

            NastavitOvladatelnost();
            RefreshLabel();
        }

        /// <summary>
        /// V hlasovém bloku se tlačítko nedá zmáčknout.
        ///
        /// ZŮSTÁVÁ ALE VIDĚT, protože na něm stojí instrukce „ŘEKNI: UKAŽ
        /// PLÁN" a protože obě podmínky mají mít stejně zaplněné zorné pole.
        /// Kdyby v hlasové verzi zmizelo, lišila by se nejen cesta k plánku,
        /// ale i to, co má participant před sebou.
        ///
        /// PROČ TO VŮBEC VADILO: plánek se ve třetím bloku vyžaduje hlasem
        /// a počítá se, kolikrát. Když šel otevřít i klepnutím, dal se ten
        /// povel celý obejít a měřená veličina přestala měřit.
        /// </summary>
        private void NastavitOvladatelnost()
        {
            var canvas = transform.Find(CanvasName);
            if (canvas == null) return;

            var t = canvas.Find("Btn_Reveal");
            if (t == null) return;

            var b = t.GetComponent<Button>();
            if (b != null) b.interactable = !_hlasem && !_zablokovano;

            // Samotné vypnutí Buttonu nestačí — paprsek XRI míří na Image
            // a ten by ho pořád chytal, takže by se na tlačítko dalo „klikat"
            // bez odezvy a vypadalo by to jako zaseknutá aplikace.
            var img = t.GetComponent<Image>();
            if (img != null)
            {
                img.raycastTarget = !_hlasem;

                // BARVA MUSÍ ODPOVÍDAT TOMU, CO TLAČÍTKO UMÍ. Modrá slibuje
                // klepnutí; v hlasovém bloku se ale klepnout nedá, takže by
                // participant marně mířil. Tmavá deska se čte jako cedule,
                // ne jako tlačítko.
                img.color = _hlasem ? PanelStyle.Card : PanelStyle.Info;
            }
        }

        private bool _hlasem;

        private void RefreshLabel()
        {
            if (_label == null) return;

            // HORNÍ ŘÁDEK ŘÍKÁ, CO DĚLAT; DOLNÍ, KOLIKRÁT UŽ SE TO STALO.
            // Počítadlo je v obou podmínkách stejné — ve třetím bloku je počet
            // odkrytí měřená veličina a participant má vidět totéž bez ohledu
            // na to, jestli se k plánku dostává rukou, nebo hlasem.
            // HLAS MÁ V NADPISU SLOVA, KTERÁ SE MAJÍ ŘÍCT, ne pokyn „řekni".
            // Pokyn patří na druhý řádek: nadpis je to, co si participant
            // přečte a zopakuje, a čím je kratší, tím větším písmem vyjde.
            if (_revealed) _label.text = "PLÁNEK JE VIDĚT";
            else _label.text = _hlasem ? "„UKAŽ PLÁN“" : "UKÁZAT PLÁN";

            if (_pocetText == null) return;

            // BEZ SLOVES V MINULÉM ČASE. „Zatím ses nedíval“ je mužský rod
            // a participantky by četly tvar, který na ně nesedí.
            var pocet = RevealCount == 0 ? "zatím neodkryto" : "odkryto " + RevealCount + "×";

            // Dokud se ani jednou neodkrylo, nese druhý řádek u hlasu jen
            // pokyn — obojí naráz se tam nevejde a zalomilo by se to.
            if (!_hlasem || _revealed) _pocetText.text = pocet;
            else if (RevealCount == 0) _pocetText.text = "řekni nahlas";
            // MEZERY MÍSTO ODDĚLOVAČE. Tečka uprostřed řádku se čte jako
            // konec věty tam, kde žádný není.
            else _pocetText.text = "řekni nahlas     " + pocet;
        }

        private TextMeshProUGUI _pocetText;

        // ---- Stavba a navěšení ----

        private void Bind(RectTransform canvas)
        {
            var t = canvas.Find("Btn_Reveal");
            if (t == null) return;

            var label = t.Find("Label");
            if (label != null) _label = label.GetComponent<TextMeshProUGUI>();

            var pocet = t.Find("Pocet");
            if (pocet != null) _pocetText = pocet.GetComponent<TextMeshProUGUI>();

            var b = t.GetComponent<Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(Reveal);
            }

            RefreshLabel();
        }

        [ContextMenu("Postavit tlačítko")]
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
            canvas.sizeDelta = new Vector2(width, height);
            canvas.localScale = Vector3.one * canvasScale;
            canvas.localPosition = Vector3.zero;
            canvas.localRotation = Quaternion.identity;

            var btn = Novy("Btn_Reveal", canvas, Vector2.zero, new Vector2(width, height));
            var img = btn.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(img, PanelStyle.RadiusButton, PanelStyle.Info);

            var b = btn.AddComponent<Button>();
            b.targetGraphic = img;

            // ROZVRŽENÍ: oko vlevo, vedle něj dva řádky.
            //
            // Dřív to byl jeden řádek „UKÁZAT PLÁN  (3)" a to číslo v závorce
            // nikdo nečetl jako počet odkrytí — vypadalo jako klávesová zkratka
            // nebo pořadí. Když stojí na vlastním řádku a je u něj slovo,
            // není co luštit.
            var ikonaGo = Novy("Ikona", (RectTransform)btn.transform,
                new Vector2(-width * 0.5f + 42f, 0f), new Vector2(46f, 46f));
            var ikona = ikonaGo.AddComponent<Image>();
            ikona.sprite = planIcon;
            ikona.preserveAspect = true;
            ikona.raycastTarget = false;
            ikona.color = PanelStyle.TextPrimary;

            var sirkaTextu = width - 96f;
            var stredTextu = 24f;

            var label = Novy("Label", (RectTransform)btn.transform,
                new Vector2(stredTextu, 13f), new Vector2(sirkaTextu, 32f));
            var tmp = label.AddComponent<TextMeshProUGUI>();

            // Font jako první: sazba i obrys zapisují do materiálu fontu.
            if (font != null) tmp.font = font;
            tmp.text = "UKÁZAT PLÁN";
            tmp.fontSize = 22f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10f;
            tmp.fontSizeMax = 24f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = PanelStyle.TextPrimary;
            tmp.raycastTarget = false;
            _label = tmp;

            var pocet = Novy("Pocet", (RectTransform)btn.transform,
                new Vector2(stredTextu, -15f), new Vector2(sirkaTextu, 26f));
            var tmp2 = pocet.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp2.font = font;
            tmp2.text = "";
            tmp2.fontSize = 16f;
            tmp2.enableAutoSizing = true;
            tmp2.fontSizeMin = 8f;
            tmp2.fontSizeMax = 17f;
            tmp2.alignment = TextAlignmentOptions.Left;
            tmp2.color = PanelStyle.TextSecondary;
            tmp2.raycastTarget = false;

            // RADĚJI ZMENŠIT NEŽ ZALOMIT. Dvouřádkový spodek by tlačítko
            // rozhodil a přetekl by z rámečku.
            tmp2.textWrappingMode = TextWrappingModes.NoWrap;
            _pocetText = tmp2;

            transform.localPosition = buttonLocalPosition;
            transform.localRotation = Quaternion.identity;

            if (faceHead && Camera.main != null)
            {
                var smer = transform.position - Camera.main.transform.position;
                if (smer.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(smer, Vector3.up);
            }

            Bind(canvas);
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
