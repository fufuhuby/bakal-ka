using BP.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Vede participanta tutoriálem krok za krokem.
    ///
    /// PROČ VEDENÝ A NE JEN KRATŠÍ KOLO: úvodní instrukce je text, který si
    /// část lidí nepřečte nebo si ho nezapamatuje. Když se pak v prvním
    /// měřeném bloku učí ovládání za běhu, propíše se to do completion time —
    /// a to je hlavní závislá proměnná. Vedený nácvik srovná výchozí úroveň.
    ///
    /// JEDNA VĚC V JEDNU CHVÍLI. Nejdřív se naučí vytvořit a položit objekt,
    /// teprve pak přijdou terče. Kdyby terče naskakovaly už během stavby,
    /// učí se dvě věci naráz a ani jednu pořádně — a právě souběh obou úloh
    /// je to, co se v experimentu měří, takže se na něj musí připravit vědomě.
    ///
    /// KAŽDÁ FÁZE ČEKÁ NA SKUTEČNOU AKCI, ne na čas. Kdyby výzvy jen běžely
    /// po sobě, pomalejší člověk by je přeskákal a nenaučil se nic.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        private enum Faze
        {
            Neaktivni = 0,
            Uvod = 1,
            VyberBarvu = 2,
            VyberTvar = 3,
            Stiskni = 4,

            // Hlasová větev. Nácvik má stejnou stavbu jako u menu, jen
            // vytvoření objektu je jeden krok místo tří — zbylé dva se
            // věnují tomu, co u hlasu navíc potřebuje: vrácení špatně
            // rozpoznaného objektu.
            Rekni = 10,
            Zpet = 11,
            RekniZnovu = 12,

            Poloz = 5,
            Dostav = 6,
            TerceUvod = 7,
            TercePraxe = 8,
            Hotovo = 9
        }

        [Header("Závislosti")]
        [SerializeField] private TrialManager trialManager;
        [SerializeField] private MenuRequestSource menuSource;
        [SerializeField] private AssemblyTaskController task;
        [SerializeField] private ShapeSpawner spawner;
        [SerializeField] private BP.Secondary.SecondaryTaskManager secondaryTask;

        [Tooltip("Zdroj hlasu. Průvodce z něj potřebuje jen vědět, že padl " +
                 "povel „zpět\" — vytvoření objektu pozná přes spawner stejně " +
                 "jako u menu.")]
        [SerializeField] private BP.Input.VoiceRequestSource voiceSource;

        [Tooltip("Šablona bloku. Průvodce z ní vybírá tvar, který v ní NENÍ, " +
                 "aby měl participant co říct schválně špatně.")]
        [SerializeField] private TemplateVisualizer templateVisualizer;

        [Tooltip("Cedule s vysvětlením práce. Visí JEN po dobu tutoriálu. " +
                 "V měřených blocích nesmí být vidět: text prozrazuje, že terče " +
                 "měří zbytkovou kapacitu a že se porovnává souběh obou úloh. " +
                 "Kdo to ví, začne terče hlídat víc, než by přirozeně dělal, " +
                 "a dual-task cost vyjde menší, než ve skutečnosti je.")]
        [SerializeField] private GameObject infoBoard;

        [SerializeField] private TMP_FontAsset font;

        [Header("Nácvik terčů")]
        [Tooltip("Kolik terčů musí participant trefit, než tutoriál skončí.")]
        [SerializeField] private int requiredHits = 2;

        [Header("Panel")]
        [Tooltip("Umístění vůči rodiči. Nad stavební plochou, aby byl text " +
                 "v zorném poli i při pohledu na menu.")]
        [SerializeField] private Vector3 panelLocalPosition = new Vector3(0.02f, 1.52f, 0.32f);

        [SerializeField] private float width = 520f;
        [SerializeField] private float height = 250f;
        [SerializeField] private float canvasScale = 0.0007f;
        [SerializeField] private Color panelColor = PanelStyle.Window;
        [SerializeField] private Color buttonColor = PanelStyle.Positive;

        [Tooltip("Natočit panel k participantovi. Bere polohu hlavy při stavbě.")]
        [SerializeField] private bool faceHead = true;

        private const string CanvasName = "TutorialCanvas";
        private const int PocetKroku = 5;

        private Faze _faze = Faze.Neaktivni;
        private TextMeshProUGUI _krok;
        private TextMeshProUGUI _nadpis;
        private TextMeshProUGUI _telo;
        private Button _tlacitko;
        private TextMeshProUGUI _tlacitkoText;
        private int _zasahy;

        /// <summary>Nacvičuje se hlasová verze, ne menu.</summary>
        private bool _hlasem;

        /// <summary>Objekt, který v šabloně není — nabídne se k vyslovení naschvál.</summary>
        private ShapeType _spatnyTvar;
        private PaletteColor _spatnaBarva;

        /// <summary>Byl objekt vytvořený v kroku 1 opravdu špatný?</summary>
        private bool _prvniBylSpatny;

        private void Awake()
        {
            var canvas = transform.Find(CanvasName) as RectTransform;

            // Canvas se v Awake NERUŠÍ — TrackedDeviceGraphicRaycaster si drží
            // registraci a při zrušení canvasu v Awake padá interakce s UI.
            if (canvas == null) Build();
            else Bind(canvas);

            Zobrazit(false);
            ZobrazitCeduli(false);
        }

        private void OnEnable()
        {
            if (trialManager != null) trialManager.TutorialEnded += OnKonec;
            if (menuSource != null) menuSource.SelectionChanged += OnVyber;
            if (spawner != null) spawner.Spawned += OnVytvoren;
            if (task != null)
            {
                task.StepCompleted += OnKrokHotov;
                task.TaskCompleted += OnStavbaHotova;
                task.ObjectUndone += OnObjektVracen;
            }
            if (secondaryTask != null) secondaryTask.TargetHit += OnTercTrefen;
        }

        private void OnDisable()
        {
            if (trialManager != null) trialManager.TutorialEnded -= OnKonec;
            if (menuSource != null) menuSource.SelectionChanged -= OnVyber;
            if (spawner != null) spawner.Spawned -= OnVytvoren;
            if (task != null)
            {
                task.StepCompleted -= OnKrokHotov;
                task.TaskCompleted -= OnStavbaHotova;
                task.ObjectUndone -= OnObjektVracen;
            }
            if (secondaryTask != null) secondaryTask.TargetHit -= OnTercTrefen;
        }

        /// <summary>Spustí vedení. Volá menu spolu se spuštěním tutoriálu.</summary>
        public void Begin(bool hlasem = false)
        {
            _hlasem = hlasem;
            _zasahy = 0;
            _prvniBylSpatny = false;

            if (hlasem) VybratSpatnyObjekt();
            _faze = Faze.Uvod;
            Zobrazit(true);
            ZobrazitCeduli(true);
            Odemknout();
            Prekreslit();
        }

        // ---- Reakce na skutečné akce ----

        private void OnVyber()
        {
            if (_faze == Faze.VyberBarvu && menuSource.SelectedColor.HasValue)
            {
                Prejit(Faze.VyberTvar);
                return;
            }

            if (_faze == Faze.VyberTvar && menuSource.SelectedShape.HasValue)
                Prejit(Faze.Stiskni);
        }

        private void OnVytvoren(ShapeInstance instance)
        {
            if (_faze == Faze.Stiskni) Prejit(Faze.Poloz);

            // U hlasu se objekt poprvé jen vyrobí, aby se pak dal vrátit;
            // teprve napodruhé se pokládá.
            else if (_faze == Faze.Rekni)
            {
                _prvniBylSpatny = !JeToCoMaByt(instance);
                Prejit(Faze.Zpet);
            }
            else if (_faze == Faze.RekniZnovu) Prejit(Faze.Poloz);
        }

        /// <summary>Odpovídá objekt kroku, který je právě na řadě?</summary>
        private bool JeToCoMaByt(ShapeInstance instance)
        {
            if (templateVisualizer == null || templateVisualizer.Template == null) return true;
            if (task == null || task.CurrentStep >= templateVisualizer.Template.StepCount) return true;

            var krok = templateVisualizer.Template.GetStep(task.CurrentStep);
            return instance.Matches(krok.shape, krok.color, krok.size);
        }

        /// <summary>
        /// Vybere tvar a barvu, které se v šabloně nikde nevyskytují.
        ///
        /// PROČ SE ŘÍKÁ ŠPATNĚ NASCHVÁL: nácvik vrácení má ukázat přesně tu
        /// situaci, kvůli které povel „zpět" existuje — hlas se spletl a je
        /// potřeba to spravit. Když participant napoprvé řekl správný tvar,
        /// vypadalo blokované pokládání jako vada aplikace: objekt je
        /// v pořádku, ale do stavby ho to nepustí.
        /// </summary>
        private void VybratSpatnyObjekt()
        {
            _spatnyTvar = ShapeType.Cone;
            _spatnaBarva = PaletteColor.Magenta;

            var sablona = templateVisualizer != null ? templateVisualizer.Template : null;
            if (sablona == null) return;

            foreach (ShapeType t in System.Enum.GetValues(typeof(ShapeType)))
            {
                var pouzity = false;
                for (var i = 0; i < sablona.StepCount; i++)
                    if (sablona.GetStep(i).shape == t) { pouzity = true; break; }

                if (pouzity) continue;
                _spatnyTvar = t;
                break;
            }

            foreach (PaletteColor c in System.Enum.GetValues(typeof(PaletteColor)))
            {
                var pouzita = false;
                for (var i = 0; i < sablona.StepCount; i++)
                    if (sablona.GetStep(i).color == c) { pouzita = true; break; }

                if (pouzita) continue;
                _spatnaBarva = c;
                break;
            }
        }

        /// <summary>Co smí hlas v aktuální fázi. Obdoba zámků menu.</summary>
        private BP.Core.MenuPart PovoleneHlasem()
        {
            switch (_faze)
            {
                // Než se klikne na ZAČÍT, nemá smysl přijímat nic.
                case Faze.Uvod:
                    return BP.Core.MenuPart.None;

                // Krok „řekni barvu a tvar": vrácení zpět tu ještě nebylo
                // vysvětlené, takže se nepřijímá.
                case Faze.Rekni:
                case Faze.RekniZnovu:
                    return BP.Core.MenuPart.Create;

                // Krok „vrať objekt": jediné, co se má stát, je „zpět".
                // Kdyby šlo místo toho nadiktovat další objekt, průvodce by
                // na tomhle kroku uvázl a participant by nevěděl proč.
                case Faze.Zpet:
                    return BP.Core.MenuPart.StepBack;

                // Od položení prvního objektu dál si vede sám.
                default:
                    return BP.Core.MenuPart.All;
            }
        }

        /// <summary>
        /// Krok „vrať objekt" se posune AŽ PO SKUTEČNÉM VRÁCENÍ, ne po
        /// vyslovení povelu. Kdyby stačilo slovo, prošel by krok i tehdy,
        /// když se nic nevrátilo — a nácvik by neproběhl.
        /// </summary>
        private void OnObjektVracen(ShapeInstance instance)
        {
            if (_faze == Faze.Zpet) Prejit(Faze.RekniZnovu);
        }

        private void OnKrokHotov(int krok)
        {
            // Po prvním umístění už participant ovládání zná — zbytek
            // dostaví sám. Vést ho i u druhého a třetího objektu by nácvik
            // jen protahovalo a nic nového by nepřidalo.
            if (_faze == Faze.Poloz) Prejit(Faze.Dostav);
        }

        private void OnStavbaHotova()
        {
            if (_faze == Faze.Neaktivni || _faze == Faze.Hotovo) return;
            Prejit(Faze.TerceUvod);
        }

        private void OnTercTrefen(BP.Secondary.SecondaryTarget terc, float reakce)
        {
            if (_faze != Faze.TercePraxe) return;

            _zasahy++;
            if (_zasahy >= requiredHits) Prejit(Faze.Hotovo);
            else Prekreslit();
        }

        private void OnKonec()
        {
            _faze = Faze.Neaktivni;
            Zobrazit(false);
            ZobrazitCeduli(false);

            // Menu se MUSÍ odemknout, i když tutoriál skončí předčasně
            // (přeskočením přes vývojářský panel) — jinak by do měřeného
            // bloku šel participant se zamčenou polovinou panelu.
            if (menuSource != null) menuSource.SetAllowedParts(MenuPart.All);
            if (voiceSource != null) voiceSource.SetAllowedParts(MenuPart.All);
            if (task != null) task.SetPlacementEnabled(true);
        }

        // ---- Tlačítko ----

        private void OnTlacitko()
        {
            switch (_faze)
            {
                case Faze.Uvod:
                    Prejit(_hlasem ? Faze.Rekni : Faze.VyberBarvu);
                    break;

                case Faze.TerceUvod:
                    // Terče se zapínají až tady, ne na začátku tutoriálu.
                    if (trialManager != null) trialManager.EnableTutorialTargets();
                    Prejit(Faze.TercePraxe);
                    break;

                case Faze.Hotovo:
                    if (trialManager != null) trialManager.FinishTutorial();
                    break;
            }
        }

        private void Prejit(Faze nova)
        {
            _faze = nova;
            Odemknout();
            Prekreslit();
        }

        /// <summary>
        /// Zpřístupní jen tu část menu, kterou aktuální krok vyžaduje.
        ///
        /// PROČ: bez toho stačilo v kroku „vyber barvu" klepnout omylem na
        /// tvar. Průvodce pak po výběru barvy přešel na krok s tvarem, kde
        /// už tvar vybraný byl, a rovnou přeskočil dál — participant se
        /// o kroku vůbec nedozvěděl. Zamykání tohle vylučuje konstrukčně,
        /// místo aby se spoléhalo na to, že se netrefí vedle.
        ///
        /// V měřených blocích se nic nezamyká; menu se vrací do plného
        /// stavu v OnKonec.
        /// </summary>
        private void Odemknout()
        {
            // HLASOVÁ VĚTEV ZAMYKÁ TAKY, jen jiný vstup. Dřív se u hlasu
            // nezamykalo nic, takže se dala celá stavba nadiktovat naráz
            // a z vedeného nácviku nezbylo nic.
            if (_hlasem)
            {
                if (voiceSource != null) voiceSource.SetAllowedParts(PovoleneHlasem());

                // V kroku, kde se nacvičuje vrácení, se objekt nesmí dát
                // položit — jinak by se krok uzavřel a povel „zpět" by si
                // participant nezkusil.
                if (task != null) task.SetPlacementEnabled(_faze != Faze.Zpet);
                return;
            }

            if (menuSource == null) return;

            switch (_faze)
            {
                case Faze.VyberBarvu:
                    menuSource.SetAllowedParts(MenuPart.Colors);
                    break;

                case Faze.VyberTvar:
                    menuSource.SetAllowedParts(MenuPart.Shapes);
                    break;

                case Faze.Stiskni:
                    menuSource.SetAllowedParts(MenuPart.Create);
                    break;

                case Faze.Uvod:
                    menuSource.SetAllowedParts(MenuPart.None);
                    break;

                // Od položení prvního objektu dál si participant vede sám —
                // zbytek stavby je normální úloha včetně STEP BACK.
                default:
                    menuSource.SetAllowedParts(MenuPart.All);
                    break;
            }
        }

        // ---- Text ----

        private void Prekreslit()
        {
            switch (_faze)
            {
                case Faze.Uvod:
                    Vypsat("", _hlasem ? "Tutoriál hlasu" : "Tutoriál",
                        "Naučíš se postavit strukturu podle předlohy a reagovat "
                        + "na terče. Celý nácvik má pět kroků a nikam se nespěchá.",
                        "ZAČÍT");
                    break;

                // ---- Hlasová větev ----

                case Faze.Rekni:
                    Vypsat(Krok(1), "Řekni to schválně špatně",
                        "Objekt se vytvoří tím, že nahlas řekneš barvu a tvar. "
                        + "Sloveso není potřeba a mluvit můžeš kdykoli, na nic "
                        + "se nemačká. Napřed si ale zkusíš, co dělat, když se "
                        + "rozpoznávání splete: řekni <b>„"
                        + Nazvy.Barva(_spatnaBarva, _spatnyTvar) + " "
                        + Nazvy.Tvar(_spatnyTvar) + "“</b>. Takový objekt v předloze není.",
                        null);
                    break;

                case Faze.Zpet:
                    // TENHLE KROK JE U HLASU PODSTATNÝ. Rozpoznávání se občas
                    // splete a participant musí vědět, jak to spraví, dřív než
                    // se to stane v měřeném bloku. V menu je na to vidět
                    // tlačítko, tady se to musí vyslovit.
                    Vypsat(Krok(2), "Vrať objekt zpět",
                        _prvniBylSpatny
                            ? "Vznikl objekt, který v předloze není, takže ho do stavby "
                              + "nedáš. Přesně tak to vypadá, když se hlas splete. "
                              + "Zbav se ho: řekni <b>„zpět“</b>."
                            : "Tenhle objekt je správný, ale pokládat budeš až za chvíli. "
                              + "Nejdřív si zkus vrácení, které budeš potřebovat, když se "
                              + "hlas splete: řekni <b>„zpět“</b>.",
                        null);
                    break;

                case Faze.RekniZnovu:
                    Vypsat(Krok(3), "Teď ten správný",
                        "Objekt zmizel. Podívej se do předlohy, co má být dole, "
                        + "a řekni jeho barvu a tvar. Ten už půjde položit.",
                        null);
                    break;

                case Faze.VyberBarvu:
                    Vypsat(Krok(1), "Vyber barvu",
                        "V levém sloupci klepni na barvu, kterou má mít první objekt.",
                        null);
                    break;

                case Faze.VyberTvar:
                    Vypsat(Krok(2), "Vyber tvar",
                        "V pravém sloupci panelu klepni na tvar objektu.",
                        null);
                    break;

                case Faze.Stiskni:
                    // Tlačítka nesou ikony, ne slova, takže se na ně tutoriál
                    // musí odvolat obrázkem. Napsat sem název, který na
                    // tlačítku nestojí, by participanta poslalo hledat text,
                    // co tam není.
                    Vypsat(Krok(3), "Stiskni kladivo",
                        "Máš vybranou barvu i tvar, takže tlačítko s kladivem zezelenalo. "
                        + "Stiskni ho a objekt se vytvoří před tebou. "
                        + "Kdyby ses spletl, tlačítko se šipkou vedle něj poslední objekt odstraní. "
                        + "Vždy můžeš mít v prostoru pouze jeden objekt.",
                        null);
                    break;

                case Faze.Poloz:
                    Vypsat(Krok(4), "Polož objekt",
                        "Uchop objekt do ruky a přenes ho na místo uprostřed. "
                        + "Když je dost blízko, sám zaskočí na správnou pozici.",
                        null);
                    break;

                case Faze.Dostav:
                    Vypsat(Krok(4), "Dostav zbytek",
                        "Výborně. Zbývají dva objekty. Postav je stejným způsobem "
                        + "podle předlohy vlevo.",
                        null);
                    break;

                case Faze.TerceUvod:
                    Vypsat(Krok(5), "Terče",
                        "Struktura je hotová. Poslední věc: během stavby se občas "
                        + "rozsvítí ČERVENÝ TERČ. Máš krátkou chvíli, aby ses ho dotkl "
                        + "rukou. Za každý zmeškaný terč se přičítá čas. Zkus si to.",
                        "ZKUSIT");
                    break;

                case Faze.TercePraxe:
                    Vypsat(Krok(5), "Dotkni se terče",
                        "Počkej, až se některý terč rozsvítí červeně, a dotkni se ho. "
                        + "Trefeno " + _zasahy + " z " + requiredHits + ".",
                        null);
                    break;

                case Faze.Hotovo:
                    Vypsat("", "Hotovo",
                        "Umíš vytvořit objekt, položit ho a reagovat na terče. "
                        + "V měření poběží obojí zároveň: stav a sleduj terče.",
                        "ZPĚT DO MENU");
                    break;
            }
        }

        private static string Krok(int i) => "KROK " + i + " / " + PocetKroku;

        private void Vypsat(string krok, string nadpis, string telo, string tlacitko)
        {
            if (_krok != null) _krok.text = krok ?? "";
            if (_nadpis != null) _nadpis.text = nadpis ?? "";
            if (_telo != null) _telo.text = telo ?? "";

            var ma = !string.IsNullOrEmpty(tlacitko);
            if (_tlacitko != null) _tlacitko.gameObject.SetActive(ma);
            if (ma && _tlacitkoText != null) _tlacitkoText.text = tlacitko;
        }

        private void ZobrazitCeduli(bool value)
        {
            if (infoBoard != null) infoBoard.SetActive(value);
        }

        private void Zobrazit(bool value)
        {
            var canvas = transform.Find(CanvasName);
            if (canvas != null) canvas.gameObject.SetActive(value);
        }

        // ---- Stavba a navěšení ----

        private void Bind(RectTransform canvas)
        {
            _krok = Najdi(canvas, "Krok");
            _nadpis = Najdi(canvas, "Nadpis");
            _telo = Najdi(canvas, "Telo");

            var b = canvas.Find("Tlacitko");
            if (b != null)
            {
                _tlacitko = b.GetComponent<Button>();
                _tlacitkoText = Najdi(b, "Label");
                if (_tlacitko != null)
                {
                    _tlacitko.onClick.RemoveAllListeners();
                    _tlacitko.onClick.AddListener(OnTlacitko);
                }
            }
        }

        private static TextMeshProUGUI Najdi(Transform parent, string jmeno)
        {
            var t = parent.Find(jmeno);
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
            canvas.sizeDelta = new Vector2(width, height);
            canvas.localScale = Vector3.one * canvasScale;
            canvas.localPosition = Vector3.zero;
            canvas.localRotation = Quaternion.identity;

            var bg = Novy("Pozadi", canvas, Vector2.zero, new Vector2(width, height));
            PanelStyle.ApplyRounded(bg.AddComponent<Image>(), PanelStyle.RadiusWindow,
                PanelStyle.Window);

            const float pad = 22f;
            const float krokH = 22f;
            const float nadpisH = 38f;
            const float tlacitkoH = 46f;

            var top = height * 0.5f - pad;

            _krok = Text(Novy("Krok", canvas, new Vector2(0f, top - krokH * 0.5f),
                new Vector2(width - pad * 2f, krokH)), 16f, TextAlignmentOptions.Center,
                PanelStyle.TextSecondary);

            _nadpis = Text(Novy("Nadpis", canvas, new Vector2(0f, top - krokH - nadpisH * 0.5f),
                new Vector2(width - pad * 2f, nadpisH)), 30f, TextAlignmentOptions.Center,
                PanelStyle.Title);

            // Tělo dostane všechno místo mezi nadpisem a tlačítkem. Kdyby mělo
            // pevnou výšku, delší vysvětlení by z rámečku přeteklo.
            var teloH = height - pad * 2f - krokH - nadpisH - tlacitkoH - 16f;
            _telo = Text(Novy("Telo", canvas,
                new Vector2(0f, top - krokH - nadpisH - 8f - teloH * 0.5f),
                new Vector2(width - pad * 2f, teloH)), 19f, TextAlignmentOptions.Center,
                PanelStyle.TextPrimary);

            var btn = Novy("Tlacitko", canvas,
                new Vector2(0f, -height * 0.5f + pad + tlacitkoH * 0.5f),
                new Vector2(220f, tlacitkoH));
            var bimg = btn.AddComponent<Image>();
            PanelStyle.ApplyRoundedButton(bimg, PanelStyle.RadiusButton, PanelStyle.Positive);
            _tlacitko = btn.AddComponent<Button>();
            _tlacitko.targetGraphic = bimg;

            _tlacitkoText = Text(Novy("Label", (RectTransform)btn.transform, Vector2.zero,
                new Vector2(220f, tlacitkoH)), 21f, TextAlignmentOptions.Center,
                PanelStyle.TextPrimary);

            transform.localPosition = panelLocalPosition;
            transform.localRotation = Quaternion.identity;

            if (faceHead && Camera.main != null)
            {
                var smer = transform.position - Camera.main.transform.position;
                if (smer.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(smer, Vector3.up);
            }

            Bind(canvas);
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
