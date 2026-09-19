using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BP.Input;
using BP.Logging;
using BP.Secondary;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BP.Core
{
    /// <summary>
    /// Definice jednoho bloku měření.
    /// </summary>
    [Serializable]
    public struct BlockDefinition
    {
        public InteractionCondition condition;
        public LoadCondition load;
        public AssemblyTemplate template;

        [Tooltip("Tréninkový blok se do analýzy nezahrnuje, ale loguje se taky.")]
        public bool isTraining;

        [Tooltip("Předloha je skrytá a odkrývá se jen na vyžádání tlačítkem. " +
                 "Manipulace patří na BLOK, ne na šablonu — šablona popisuje " +
                 "strukturu, tohle je způsob, jakým se k ní participant dostává.")]
        public bool referenceOnDemand;

        public override string ToString()
            => $"{condition}/{load}/{(template != null ? template.templateId : "?")}"
               + (isTraining ? " (trenink)" : "");
    }

    /// <summary>
    /// Výsledek jednoho odehraného bloku. Drží se v paměti po celou session,
    /// aby šla na konci ukázat souhrnná tabulka.
    ///
    /// SLOŽKY ČASU ZŮSTÁVAJÍ ODDĚLENÉ i tady. Postihy za minuté terče jsou
    /// zpětná vazba pro participanta, ne měřená veličina — kdyby se do
    /// completion time přičetly, byl by hlavní výsledek funkcí sekundární
    /// úlohy a rozdíl mezi podmínkami by se nedal interpretovat.
    /// </summary>
    public struct BlockResult
    {
        public int index;
        public InteractionCondition condition;
        public LoadCondition load;
        public string templateId;
        public bool isTraining;

        /// <summary>Řešil blok i velikosti? Odlišuje druhý blok od prvního.</summary>
        public bool usesSizes;
        public string reason;

        public float rawTime;
        public int penaltyCount;
        public float penaltyTime;

        public int stepsDone;
        public int stepsTotal;
        public int wrongObjects;
        public int rejectedPlacements;

        public bool secondaryRan;
        public int activations;
        public int hits;
        public float hitRate;
        public float meanReactionTime;

        public bool referenceOnDemand;
        public int revealCount;
        public float revealTime;
    }

    /// <summary>
    /// Výsledek celé session jednoho participanta — jeden řádek žebříčku.
    ///
    /// ŘADÍ SE PODLE CELKOVÉHO ČASU VČETNĚ POSTIHŮ, ne podle čistého.
    /// Čistý čas je měřená veličina, ale jako skóre by odměňoval toho, kdo
    /// terče ignoroval: minutý terč by ho nic nestál. Celkový čas je přesně
    /// to, co participant během bloku viděl na časomíře.
    /// </summary>
    public struct SessionScore
    {
        public string participantId;
        public string soubor;
        public string datum;

        /// <summary>
        /// Hlasová, nebo klasická. Session je vždycky jen jedna podmínka —
        /// druhá se hraje zvlášť — takže se dá vzít z prvního měřeného bloku.
        /// </summary>
        public InteractionCondition condition;

        public float cleanTime;
        public float penaltyTime;
        public int penaltyCount;

        /// <summary>
        /// Kolikrát participant vytvořil objekt, který do předlohy nepatřil —
        /// špatný tvar, barva nebo velikost. Sčítá se přes měřené bloky.
        /// </summary>
        public int wrongObjects;
        public float TotalTime => cleanTime + penaltyTime;

        public int measuredBlocks;
        public bool complete;

        public int activations;
        public int hits;
        public float HitRate => activations > 0 ? (float)hits / activations : 0f;
    }

    /// <summary>
    /// Řídí celou session: pořadí bloků, counterbalancing, spouštění
    /// a zastavování obou úloh, logování a zamykání menu.
    ///
    /// COUNTERBALANCING: pořadí podmínek se mezi participanty střídá
    /// (sudá skupina začíná menu, nepálná hlasem), aby se efekt učení
    /// a únavy rozložil rovnoměrně na obě podmínky. Zároveň se rotuje
    /// přiřazení šablon, aby žádná podmínka nedostávala systematicky
    /// tu samou strukturu.
    ///
    /// V rámci podmínky jde vždy SingleTask před DualTask — baseline musí
    /// být naměřená bez zátěže, aby se z ní dal spočítat dual-task cost.
    /// Pořadí je stejné v obou podmínkách, takže se případný efekt učení
    /// mezi nimi vyruší.
    /// </summary>
    public class TrialManager : MonoBehaviour
    {
        [Header("Participant")]
        [SerializeField] private string participantId = "P01";

        [Tooltip("Skupina counterbalancingu. Sudá = menu první, nepálná = hlas první.")]
        [SerializeField] private int counterbalanceGroup;

        [Header("Bloky")]
        [Tooltip("Bloky v základním pořadí. Counterbalancing je přeskládá podle skupiny.")]
        [SerializeField] private List<BlockDefinition> blocks = new List<BlockDefinition>();

        [Tooltip("Tutoriál z hlavního menu. Běží jako blok, ale do výsledků " +
                 "ani do pořadí session nevstupuje — je to nácvik ovládání, " +
                 "ne měření.")]
        [SerializeField] private BlockDefinition tutorialBlock;

        [Tooltip("Tutoriál hlasové verze. Musí mít condition = Voice, jinak by " +
                 "se v něm ukázal inventář místo okna s příkazy.")]
        [SerializeField] private BlockDefinition voiceTutorialBlock;

        [Tooltip("Kolik terčů je v tutoriálu ve hře. Sedm najednou je pro " +
                 "první seznámení moc.")]
        [SerializeField] private int tutorialTargetCount = 3;

        [Header("Úlohy")]
        [SerializeField] private AssemblyTaskController task;
        [SerializeField] private SecondaryTaskManager secondaryTask;

        [Tooltip("Časomíra bloku. Drží čistý čas a postihy odděleně.")]
        [SerializeField] private BlockTimer timer;
        [Tooltip("Stavební vodítko uprostřed — drátový model aktuálního kroku.")]
        [SerializeField] private TemplateVisualizer templateVisualizer;

        [Tooltip("Předloha vedle stavební plochy — plný barevný model celé struktury. " +
                 "Musí se přepínat spolu s vodítkem, jinak v dalším bloku zůstane " +
                 "viset struktura z bloku předchozího.")]
        [SerializeField] private TemplateVisualizer referenceVisualizer;
        [SerializeField] private ShapeSpawner spawner;

        [Header("Vstupní zdroje podmínek")]
        [SerializeField] private MenuRequestSource menuSource;

        [Tooltip("Zdroj pro hlasovou podmínku — mikrofon, Whisper, rozbor " +
                 "českého povelu.")]
        [SerializeField] private VoiceRequestSource voiceSource;

        [Tooltip("Kořen inventáře (MenuAnchor). V HLASOVÉM bloku se schová — " +
                 "kdyby zůstal, mohl by z něj participant objekt naklepat " +
                 "rukou a podmínky by se přestaly lišit v tom jediném, v čem " +
                 "se lišit mají.")]
        [SerializeField] private GameObject menuRoot;

        [Tooltip("Okno s hlasovými příkazy. Stojí na místě inventáře a nahrazuje " +
                 "jeho roli nápovědy — bez něj by si participant musel slovník " +
                 "pamatovat a rozdíl mezi podmínkami by zčásti měřil paměť.")]
        [SerializeField] private VoiceHelpPanel voiceHelp;

        [Header("Kalibrace")]
        [Tooltip("Zámek pozice menu. Během bloku se zamkne, mezi bloky odemkne.")]
        [SerializeField] private MenuDragHandle menuLock;

        [Tooltip("Totéž pro okno s hlasovými příkazy. Obě podmínky musí mít " +
                 "rozhraní během měření stejně pevně na místě.")]
        [SerializeField] private MenuDragHandle voiceHelpLock;

        [Tooltip("Zamykat menu v měřených blocích. Při MĚŘENÍ nechat zapnuté — " +
                 "posouvání během bloku mění vzdálenost ruky k menu a ta se " +
                 "propisuje do completion time. Pro zkoušení lze vypnout.")]
        [SerializeField] private bool lockMenuDuringMeasuredBlocks = true;

        [Header("Zobrazení")]
        [Tooltip("Objekty, které jsou skryté, dokud nezačne první blok — " +
                 "aby participant na úvodní obrazovce viděl jen instrukci " +
                 "a nerozptylovalo ho pracoviště.")]
        [SerializeField] private GameObject[] hiddenUntilStart = System.Array.Empty<GameObject>();

        [Header("Ovládání operátorem")]
        [Tooltip("1 = klasická session, 2 = hlasová session, N = další blok, " +
                 "K = ukončit, R = zopakovat, L = zamknout/odemknout menu, " +
                 "C = přecentrovat pracoviště, T = rozsvítit terč, " +
                 "S = zapnout/vypnout sekundární úlohu.")]
        [SerializeField] private bool keyboardControl = true;

        [Tooltip("Pracoviště. Umožní ho přecentrovat před participanta klávesou C.")]
        [SerializeField] private WorkspaceLayout workspace;

        [Tooltip("Rozmístění periferních terčů. Po přecentrování se terče " +
                 "rozloží znovu — excentricita se počítá od skutečné výšky " +
                 "očí, a ta je u každého participanta jiná.")]
        [SerializeField] private SecondaryTargetLayout targetLayout;

        [Tooltip("Předloha na vyžádání. Zapíná se per blok příznakem " +
                 "referenceOnDemand.")]
        [SerializeField] private ReferenceOnDemand referenceOnDemand;

        [Tooltip("Ovládat i tlačítky na ovladačích. Nutné, když testuješ sám " +
                 "v headsetu — na klávesnici se nedosáhne. Při měření " +
                 "s participantem VYPNOUT, aby si blok nespustil sám.")]
        [SerializeField] private bool controllerControl = true;

        [Tooltip("Spustit sekundární úlohu v KAŽDÉM bloku, i v single-task. " +
                 "Jen pro zkoušení — nevyžaduje mačkání tlačítek. " +
                 "Při měření VYPNOUT, jinak zmizí baseline bez zátěže " +
                 "a nepůjde spočítat dual-task cost.")]
        [SerializeField] private bool runSecondaryInAllBlocks;

        [Tooltip("Spustit první blok hned po Play. Pro zkoušení pohodlné; " +
                 "při měření s participantem vypnout, aby blok začal až na pokyn.")]
        [SerializeField] private bool autoStartFirstBlock = true;

        /// <summary>Index aktuálního bloku v přeskládaném pořadí. -1 = session neběží.</summary>
        public int CurrentBlockIndex { get; private set; } = -1;

        public bool BlockRunning { get; private set; }
        public bool SessionComplete => CurrentBlockIndex >= _order.Count;

        public event Action<BlockDefinition, int> BlockStarted;
        public event Action<BlockDefinition, int> BlockEnded;
        public event Action SessionEnded;

        /// <summary>Tutoriál skončil — menu se má vrátit na úvodní obrazovku.</summary>
        public event Action TutorialEnded;

        /// <summary>Běží právě tutoriál, ne měřený blok?</summary>
        public bool TutorialRunning { get; private set; }

        /// <summary>Odehrané bloky v pořadí, v jakém proběhly.</summary>
        public IReadOnlyList<BlockResult> Results => _results;

        /// <summary>Kolik bloků má session celkem — pro zobrazení postupu.</summary>
        public int BlockCount
        {
            get
            {
                if (_order.Count == 0) BuildOrder();
                return _order.Count;
            }
        }

        private readonly List<BlockDefinition> _order = new List<BlockDefinition>();
        private readonly List<BlockResult> _results = new List<BlockResult>();
        private TrialLogger _logger;

        /// <summary>
        /// Otevřený log běžícího bloku, nebo null. Čte to zpětná vazba, aby
        /// si do dat zapsala, v jaké verzi ozvučení session běžela — bez toho
        /// by u starších souborů nešlo poznat, jestli měl participant zvuk.
        /// </summary>
        public TrialLogger Logger => _logger;

        // Chyby se počítají tady, ne v úloze — úloha je bez stavu mezi bloky
        // a čítač v ní by se musel resetovat zvenčí, což je snadné zapomenout.
        private int _wrongObjects;
        private int _rejectedPlacements;

        // Operátorské akce na ovladačích. A/B/X/Y nejsou použité ani k uchopení
        // (to je trigger a grip), ani k obsluze menu (to je poke a ray),
        // takže se nemohou plést do samotné úlohy.
        private InputAction _nextBlockAction;
        private InputAction _recenterAction;
        private InputAction _triggerTargetAction;
        private InputAction _toggleSecondaryAction;

        /// <summary>
        /// ID participanta, pod kterým se ukládají data. Nastavuje se
        /// z úvodní nabídky, protože zapomenuté ID je nejdražší chyba, jakou
        /// při měření můžeš udělat: data se smíchají s cizími a rozdělit
        /// zpátky už nejdou.
        /// </summary>
        public string ParticipantId => participantId;

        private const string KlicId = "BP_participantId";

        public void SetParticipantId(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            participantId = value;

            // PŘEŽIJE RESTART APLIKACE. Hodnota ze scény by se po každém
            // spuštění vrátila na P01 a měřilo by se pod cizím ID, aniž by
            // si toho kdokoli všiml.
            PlayerPrefs.SetString(KlicId, value);
            PlayerPrefs.Save();

            BuildOrder();
        }

        private void Awake()
        {
            var ulozene = PlayerPrefs.GetString(KlicId, "");
            if (!string.IsNullOrEmpty(ulozene)) participantId = ulozene;

            BuildOrder();
            NapojitHlas();
        }

        private void OnEnable()
        {
            if (!controllerControl) return;

            _recenterAction = Bind("BP_Recenter", "<XRController>{LeftHand}/primaryButton",
                RecenterWorkspace);
            _nextBlockAction = Bind("BP_NextBlock", "<XRController>{LeftHand}/secondaryButton",
                StartNextBlock);
            _triggerTargetAction = Bind("BP_Target", "<XRController>{RightHand}/primaryButton",
                TriggerSecondaryTargetForTesting);
            _toggleSecondaryAction = Bind("BP_Secondary", "<XRController>{RightHand}/secondaryButton",
                ToggleSecondaryTaskForTesting);
        }

        private void OnDisable()
        {
            Unbind(ref _recenterAction);
            Unbind(ref _nextBlockAction);
            Unbind(ref _triggerTargetAction);
            Unbind(ref _toggleSecondaryAction);
        }

        private static InputAction Bind(string name, string path, Action callback)
        {
            var action = new InputAction(name, InputActionType.Button, path);
            action.performed += _ => callback();
            action.Enable();
            return action;
        }

        private static void Unbind(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }

        private void Start()
        {
            // Postih za minutý terč. Přičítá se jen k zobrazenému času —
            // do čistého času nevstupuje.
            if (secondaryTask != null && timer != null)
                secondaryTask.TargetMissed += _ => timer.AddPenalty();

            // Na úvodní obrazovce je vidět jen instrukce.
            SetWorkspaceVisible(false);

            if (autoStartFirstBlock) StartSession();
        }

        private void OnDestroy()
        {
            if (_logger != null) _logger.Dispose();
        }

        private void Update()
        {
            if (!keyboardControl) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // Spuštění session z klávesnice. V editoru se na tlačítka v nabídce
            // bez brýlí nedá kliknout — hlasovou verzi by tam jinak nešlo
            // vyzkoušet jinak než voláním z kódu.
            if (kb.digit1Key.wasPressedThisFrame) StartSession(SessionMode.Classic);
            if (kb.digit2Key.wasPressedThisFrame) StartSession(SessionMode.Voice);

            if (kb.nKey.wasPressedThisFrame) StartNextBlock();
            if (kb.kKey.wasPressedThisFrame) EndCurrentBlock("ukonceno operatorem");
            if (kb.rKey.wasPressedThisFrame) RepeatCurrentBlock();

            // Ruční override zámku menu — pro ladění a pro případ, kdy si
            // participant potřebuje menu posunout i mimo tréninkový blok.
            if (kb.lKey.wasPressedThisFrame) ToggleMenuLock();
            if (kb.cKey.wasPressedThisFrame) RecenterWorkspace();

            // Zkoušení sekundární úlohy mimo dual-task blok.
            if (kb.tKey.wasPressedThisFrame) TriggerSecondaryTargetForTesting();
            if (kb.sKey.wasPressedThisFrame) ToggleSecondaryTaskForTesting();
        }

        /// <summary>
        /// Ukáže to rozhraní, které k podmínce patří, a schová to druhé.
        ///
        /// Menu a okno s příkazy se VYLUČUJÍ. Kdyby byl v hlasovém bloku vidět
        /// inventář, dal by se objekt vytvořit klepnutím a podmínka by přestala
        /// být hlasová; kdyby v klasickém bloku viselo okno s příkazy, mátlo by,
        /// protože mluvit tam nejde.
        /// </summary>
        private void PrepnoutRozhrani(BlockDefinition block)
        {
            var hlas = block.condition == InteractionCondition.Voice;

            if (menuRoot != null) menuRoot.SetActive(!hlas);

            // Vstup do menu se zamyká i tak. Skrytý objekt sice klepnout nejde,
            // ale kdyby se někdy zobrazoval z jiného důvodu, nesmí do dat
            // propustit požadavek z nesprávné podmínky.
            if (menuSource != null) menuSource.SetInputEnabled(!hlas);

            // Obě podmínky musí o velikostech vědět totéž, jinak by hlas
            // v bloku bez velikostí propustil požadavek, který menu vyrobit
            // nemůže.
            var sVelikostmi = block.template != null && block.template.usesSizes;
            if (voiceSource != null) voiceSource.SetRequiresSize(sVelikostmi);

            if (voiceHelp == null) return;

            voiceHelp.Configure(sVelikostmi, block.referenceOnDemand);
            voiceHelp.SetVisible(hlas);
        }

        /// <summary>
        /// Napojí hlasové vyžádání plánku. Dělá se to jednou, ne při každém
        /// bloku — opakované přihlašování by událost spustilo tolikrát,
        /// kolikrát blok začal, a jedno vyslovení by odkrylo plánek víckrát.
        /// </summary>
        private void NapojitHlas()
        {
            if (voiceSource == null || _hlasNapojen) return;

            voiceSource.RevealRequested += OnVoiceReveal;
            _hlasNapojen = true;
        }

        private bool _hlasNapojen;

        /// <summary>Který z tutoriálů právě běží. Terče z něj berou seed.</summary>
        private BlockDefinition _tutorialBlok;

        private void OnVoiceReveal()
        {
            if (referenceOnDemand != null) referenceOnDemand.Reveal();
        }

        // ---- Režim session ----

        /// <summary>
        /// Co se z nabídky spustilo. Klasika a hlas se měří ZVLÁŠŤ, tedy jako
        /// dvě samostatné session, ne jako dvě poloviny jedné.
        ///
        /// PROČ NE JEDNA SESSION S OBĚMA PODMÍNKAMI: dohromady by to byl
        /// víc než dvacetiminutový běh v brýlích a únava by se přičetla
        /// k druhé podmínce v pořadí. Rozdělené session se dají odehrát
        /// s pauzou a pořadí se vyváží mezi participanty tím, koho pustíš
        /// nejdřív na kterou.
        /// </summary>
        public enum SessionMode
        {
            /// <summary>Jen bloky s ovládáním přes menu.</summary>
            Classic = 0,

            /// <summary>Jen bloky s hlasovým ovládáním.</summary>
            Voice = 1,

            /// <summary>Obojí za sebou. Pro vyvážený běh, až na to dojde.</summary>
            Both = 2
        }

        [Header("Session")]
        [Tooltip("Který režim se spustí, když session začne bez výběru " +
                 "z nabídky (klávesou nebo z inspektoru).")]
        [SerializeField] private SessionMode defaultMode = SessionMode.Classic;

        /// <summary>Režim, ve kterém běží nebo naposledy běžela session.</summary>
        public SessionMode Mode { get; private set; }

        /// <summary>
        /// Spustí session v daném režimu od začátku. Volá nabídka.
        /// </summary>
        public void StartSession(SessionMode mode)
        {
            if (BlockRunning)
            {
                Debug.LogWarning("[TrialManager] Blok už běží — session se nespouští znovu.");
                return;
            }

            Mode = mode;
            BuildOrder();

            // Výsledky se musí vyprázdnit, jinak by tabulka na konci mísila
            // bloky z klasické a hlasové session dohromady.
            _results.Clear();

            CurrentBlockIndex = -1;
            StartNextBlock();
        }

        // ---- Pořadí bloků ----

        /// <summary>
        /// Přeskládá bloky podle skupiny counterbalancingu.
        /// Zachovává pravidlo SingleTask → DualTask v rámci každé podmínky.
        /// </summary>
        public void BuildOrder()
        {
            _order.Clear();
            if (blocks.Count == 0) return;

            var menuBlocks = new List<BlockDefinition>();
            var voiceBlocks = new List<BlockDefinition>();

            foreach (var b in blocks)
            {
                if (b.condition == InteractionCondition.Menu) menuBlocks.Add(b);
                else voiceBlocks.Add(b);
            }

            SortSingleBeforeDual(menuBlocks);
            SortSingleBeforeDual(voiceBlocks);

            // Režim rozhoduje, které bloky se do pořadí vůbec dostanou.
            // Vyvažování uvnitř režimu se tím neruší — jen se netýká
            // podmínky, která v téhle session neběží.
            if (Mode == SessionMode.Classic)
            {
                _order.AddRange(menuBlocks);
                return;
            }

            if (Mode == SessionMode.Voice)
            {
                _order.AddRange(voiceBlocks);
                return;
            }

            var menuFirst = counterbalanceGroup % 2 == 0;

            if (menuFirst)
            {
                _order.AddRange(menuBlocks);
                _order.AddRange(voiceBlocks);
            }
            else
            {
                _order.AddRange(voiceBlocks);
                _order.AddRange(menuBlocks);
            }
        }

        private static void SortSingleBeforeDual(List<BlockDefinition> list)
        {
            list.Sort((a, b) =>
            {
                // Trénink vždy první, pak SingleTask, pak DualTask.
                var ka = (a.isTraining ? 0 : 1) * 10 + (int)a.load;
                var kb = (b.isTraining ? 0 : 1) * 10 + (int)b.load;
                return ka.CompareTo(kb);
            });
        }

        public IReadOnlyList<BlockDefinition> Order => _order;

        // ---- Průběh ----

        /// <summary>
        /// Zobrazí nebo skryje pracoviště. Na úvodní obrazovce má být vidět
        /// jen instrukce — šablona, menu a terče by od ní odváděly pozornost
        /// a participant by začal zkoušet dřív, než si ji přečte.
        /// </summary>
        public void SetWorkspaceVisible(bool visible)
        {
            foreach (var go in hiddenUntilStart)
                if (go != null) go.SetActive(visible);
        }

        /// <summary>Okamžitě rozsvítí jeden terč. Jen pro zkoušení.</summary>
        public void TriggerSecondaryTargetForTesting()
        {
            if (secondaryTask == null)
            {
                Debug.LogWarning("[TrialManager] Není přiřazený SecondaryTaskManager.");
                return;
            }

            secondaryTask.ActivateNowForTesting();
            Debug.Log("[TrialManager] Terč aktivován (test).");
        }

        /// <summary>Zapne/vypne sekundární úlohu bez ohledu na blok. Jen pro zkoušení.</summary>
        public void ToggleSecondaryTaskForTesting()
        {
            if (secondaryTask == null) return;

            secondaryTask.ToggleForTesting(_logger);
            Debug.Log("[TrialManager] Sekundární úloha "
                      + (secondaryTask.IsRunning ? "spuštěna" : "zastavena") + " (test).");
        }

        /// <summary>
        /// Přesune pracoviště před participanta. Automatické přecentrování
        /// při startu proběhne dřív, než si participant stihne stoupnout —
        /// tohle je oprava, kterou udělá operátor, až je na místě.
        ///
        /// Během běžícího bloku se to zapíše do logu: mění se geometrie
        /// dosahu, a ta ovlivňuje completion time.
        /// </summary>
        public void RecenterWorkspace()
        {
            if (workspace == null)
            {
                Debug.LogWarning("[TrialManager] Není přiřazený WorkspaceLayout.");
                return;
            }

            workspace.RecenterToHead();

            // Terče se rozloží až po přecentrování. Kdyby zůstaly z původní
            // pozice, měl by každý jinou excentricitu, než jakou návrh
            // předpokládá — a to je právě proměnná, kterou držíme konstantní.
            if (targetLayout != null) targetLayout.Apply();

            if (BlockRunning && _logger != null)
                _logger.Log(LogEvent.Note, detail: "pracoviste precentrovano behem bloku");

            Debug.Log("[TrialManager] Pracoviště přecentrováno.");
        }

        /// <summary>
        /// Přepne zámek pozice menu. Pokud se odemkne během měřeného bloku,
        /// zapíše se to do logu — jinak by v datech zůstala nezaznamenaná
        /// změna geometrie, která ovlivňuje completion time.
        /// </summary>
        public void ToggleMenuLock()
        {
            if (menuLock == null) return;

            var newState = !menuLock.IsLocked;
            menuLock.SetLocked(newState);

            if (BlockRunning && _logger != null)
                _logger.Log(LogEvent.Note,
                    detail: "menu " + (newState ? "zamceno" : "ODEMCENO") + " behem bloku");

            Debug.Log("[TrialManager] Menu " + (newState ? "zamceno" : "odemceno"));
        }

        /// <summary>Session ve výchozím režimu — klávesa, autostart, inspektor.</summary>
        public void StartSession() => StartSession(defaultMode);

        /// <summary>
        /// Spustí tutoriál. Nemění index bloku ani seznam výsledků — po
        /// dokončení se menu vrátí na úvodní obrazovku a session může začít
        /// od začátku, jako by se nic nestalo.
        /// </summary>
        public void StartTutorial() => StartTutorial(false);

        /// <summary>
        /// Spustí tutoriál pro jednu z podmínek.
        ///
        /// OBĚ PODMÍNKY MAJÍ MÍT NÁCVIK. Kdyby ho měla jen jedna, měřil by
        /// rozdíl mezi nimi zčásti to, že do druhé jde participant studený —
        /// tedy první kontakt s nenacvičeným rozhraním, ne cenu modality.
        /// </summary>
        public void StartTutorial(bool hlasem)
        {
            if (BlockRunning)
            {
                Debug.LogWarning("[TrialManager] Nejdřív ukonči běžící blok.");
                return;
            }

            _tutorialBlok = hlasem ? voiceTutorialBlock : tutorialBlock;

            if (_tutorialBlok.template == null)
            {
                Debug.LogError("[TrialManager] Tutoriál nemá šablonu ("
                               + (hlasem ? "hlasový" : "klasický") + ").");
                return;
            }

            TutorialRunning = true;
            RunBlock(_tutorialBlok);

            // Terče začínají VYPNUTÉ. Když naskočí hned, participant se učí
            // vytvářet objekty a zároveň ho něco vyrušuje — obojí naráz se
            // učí špatně. Zapne je průvodce, až stavbu vysvětlí.
            if (secondaryTask != null)
            {
                secondaryTask.StopTask();
                secondaryTask.SetActiveTargetCount(0);
            }
        }

        public void StartNextBlock()
        {
            if (BlockRunning)
            {
                Debug.LogWarning("[TrialManager] Blok už běží. Nejdřív ho ukonči (K).");
                return;
            }

            // Pořadí je v neserializovaném seznamu. Rekompilace za běhu (domain
            // reload) ho vyprázdní, aniž by se znovu spustil Awake — bez tohoto
            // by session tiše skončila jako "dokončená".
            if (_order.Count == 0) BuildOrder();

            CurrentBlockIndex++;

            if (CurrentBlockIndex >= _order.Count)
            {
                var path = WriteResultsFile();
                Debug.Log("[TrialManager] Session dokončena." + Environment.NewLine
                          + BuildResultsTable()
                          + (path != null ? Environment.NewLine + "souhrn: " + path : ""));

                if (SessionEnded != null) SessionEnded();
                return;
            }

            RunBlock(_order[CurrentBlockIndex]);
        }

        public void RepeatCurrentBlock()
        {
            if (_order.Count == 0) BuildOrder();

            if (CurrentBlockIndex < 0 || CurrentBlockIndex >= _order.Count)
            {
                Debug.LogWarning("[TrialManager] Není co zopakovat — session nezačala.");
                return;
            }

            if (BlockRunning) EndCurrentBlock("zopakovano");
            RunBlock(_order[CurrentBlockIndex]);
        }

        private void RunBlock(BlockDefinition block)
        {
            if (block.template == null)
            {
                Debug.LogError($"[TrialManager] Blok {block} nemá šablonu.");
                return;
            }

            if (block.condition == InteractionCondition.Voice && voiceSource == null)
            {
                Debug.LogError("[TrialManager] Hlasová podmínka není implementovaná — blok přeskočen.");
                return;
            }

            ClearScene();

            // Pracoviště se posadí před participanta na začátku KAŽDÉHO bloku.
            // Přecentrování po startu scény se může minout (tracking hlavy
            // ještě neběží) a participant se mezi bloky posune nebo si sedne.
            // Blok začíná stiskem START, kdy se dívá na panel před sebou —
            // to je jediný okamžik, kdy je jeho orientace spolehlivě známá.
            if (workspace != null) workspace.RecenterToHead();

            // Terče se rozloží AŽ POTOM. Excentricita se počítá od polohy
            // hlavy, takže před přecentrováním by seděla na starou pozici.
            if (targetLayout != null) targetLayout.Apply();

            // Šablona bloku — vodítko i předloha. Předloha se přepisovala
            // dřív jen v editoru, takže od druhého bloku ukazovala jinou
            // strukturu, než se měla stavět.
            // Vodítko nesmí prozradit identitu objektu v bloku, kde je plánek
            // na vyžádání — jinak by se dalo stavět bez plánku a manipulace
            // by byla prázdná. Váže se to na stejný příznak schválně: dvě
            // nezávislá nastavení by se dala nastavit rozporně.
            templateVisualizer.UseNeutralGuide = block.referenceOnDemand;

            templateVisualizer.SetTemplate(block.template);
            if (referenceVisualizer != null) referenceVisualizer.SetTemplate(block.template);

            // Log
            if (_logger != null) _logger.Dispose();
            _logger = new TrialLogger();
            _logger.OpenBlock(participantId, block.condition, block.load,
                block.template.templateId, CurrentBlockIndex);

            // Vstupní zdroj podle podmínky
            var source = block.condition == InteractionCondition.Menu
                ? (IObjectRequestSource)menuSource
                : voiceSource;

            // Kolik vlastnosti blok resi, urcuje SABLONA — jeden zdroj pravdy.
            // Kdyby to bylo zvlast na bloku, mohl by nastat rozpor: menu by
            // velikost nabizelo a sablona ji neresila, nebo naopak.
            if (menuSource != null) menuSource.SetRequiresSize(block.template.usesSizes);

            // Hlas si loguje sám — přepis, doba rozpoznání i důvod odmítnutí
            // jsou věci, které o sobě nikdo jiný neví.
            if (voiceSource != null) voiceSource.SetLogger(_logger);

            task.Initialize(source, _logger);
            task.TaskCompleted += OnTaskCompleted;

            _wrongObjects = 0;
            _rejectedPlacements = 0;
            task.WrongObjectCreated += OnWrongObject;
            task.PlacementRejected += OnPlacementRejected;

            // V TRÉNINKU zůstává menu odemčené — tam si ho participant narovná
            // do pohodlné pozice (kalibrace). V MĚŘENÉM bloku se zamkne, aby
            // byla vzdálenost ruky k menu po celou dobu měření konstantní.
            var zamknout = lockMenuDuringMeasuredBlocks && !block.isTraining;
            if (menuLock != null) menuLock.SetLocked(zamknout);
            if (voiceHelpLock != null) voiceHelpLock.SetLocked(zamknout);

            // Sekundární úloha jen v dual-task bloku. Seed z participanta a bloku,
            // aby stejný participant dostal v obou podmínkách stejnou sekvenci.
            var runSecondary = block.load == LoadCondition.DualTask || runSecondaryInAllBlocks;
            if (runSecondary && secondaryTask != null)
            {
                secondaryTask.StartTask(_logger, ComputeSeed(block));

                if (runSecondaryInAllBlocks && block.load != LoadCondition.DualTask)
                    _logger.Log(LogEvent.Note,
                        detail: "POZOR: sekundarni uloha bezi i v single-task bloku (vyvojove nastaveni)");
            }

            SetWorkspaceVisible(true);
            PrepnoutRozhrani(block);

            // AŽ POTOM, co je pracoviště vidět: zviditelnění by jinak
            // předlohu zase odkrylo a mechanika by v prvním okamžiku selhala.
            if (referenceOnDemand != null)
                referenceOnDemand.Configure(_logger, block.referenceOnDemand,
                    block.condition == InteractionCondition.Voice);

            if (timer != null) timer.StartBlock();

            task.StartTask();
            BlockRunning = true;

            Debug.Log($"[TrialManager] Blok {CurrentBlockIndex + 1}/{_order.Count}: {block}");
            if (BlockStarted != null) BlockStarted(block, CurrentBlockIndex);
        }

        private void OnTaskCompleted()
        {
            // V tutoriálu dostavěním nic nekončí — po stavbě ještě přijde
            // vysvětlení terčů. O ukončení rozhoduje průvodce.
            if (TutorialRunning) return;

            EndCurrentBlock("uloha dokoncena");
        }

        /// <summary>
        /// Zapne terče uprostřed tutoriálu. Do té doby jsou vypnuté, aby
        /// participanta nerozptylovaly, dokud se učí vytvářet objekty.
        /// </summary>
        public void EnableTutorialTargets()
        {
            if (secondaryTask == null) return;

            secondaryTask.StartTask(_logger, ComputeSeed(_tutorialBlok));

            // Spodní terče, aby se nepřekrývaly s boxíkem instrukce.
            secondaryTask.SetActiveTargetsLowest(tutorialTargetCount);
        }

        /// <summary>Ukončí tutoriál. Volá průvodce, až projde všechny fáze.</summary>
        public void FinishTutorial()
        {
            if (!TutorialRunning) return;
            EndCurrentBlock("tutorial dokoncen");
        }

        private void OnWrongObject(ShapeInstance instance) => _wrongObjects++;

        private void OnPlacementRejected(ShapeInstance instance, float error) => _rejectedPlacements++;

        public void EndCurrentBlock(string reason)
        {
            if (!BlockRunning) return;

            BlockRunning = false;
            task.TaskCompleted -= OnTaskCompleted;
            task.WrongObjectCreated -= OnWrongObject;
            task.PlacementRejected -= OnPlacementRejected;

            // Kroky se čtou PŘED StopTask() — ta stav úlohy resetuje a
            // z dokončeného bloku by pak zbylo 0/8.
            var stepsDone = task.CurrentStep;
            var stepsTotal = task.StepCount;

            task.StopTask();

            var summary = reason;

            // Čistý čas a postihy jdou do logu ODDĚLENĚ. Completion time je
            // hlavní závislá proměnná — kdyby v něm byly postihy schované,
            // nešlo by rozlišit pomalejší stavbu od většího počtu minutí.
            if (timer != null)
            {
                timer.StopBlock();
                summary += " | " + timer.GetSummary();
            }

            var secondaryRan = secondaryTask != null && secondaryTask.IsRunning;
            if (secondaryRan)
                summary += " | " + secondaryTask.GetSummary();

            // StopTask se volá BEZ OHLEDU na to, jestli úloha běžela. Terč
            // rozsvícený mimo blok (testovací klávesou) by jinak zůstal
            // svítit, a po skrytí pracoviště se jeho časový limit zastaví —
            // zůstal by rozsvícený natrvalo a zablokoval by další aktivace.
            if (secondaryTask != null) secondaryTask.StopTask();

            var odkryti = 0;
            var odkrytiCas = 0f;
            if (referenceOnDemand != null && referenceOnDemand.IsActive)
            {
                summary += " | " + referenceOnDemand.GetSummary();
                odkryti = referenceOnDemand.RevealCount;
                odkrytiCas = referenceOnDemand.TotalRevealTime;
            }

            // Vypnout vzdy: tlacitko nema co delat v bloku, ktery mechaniku
            // neresi, a predloha se musi vratit do bezneho rezimu.
            if (referenceOnDemand != null) referenceOnDemand.Configure(null, false);
            if (voiceHelp != null) voiceHelp.SetVisible(false);

            if (_logger != null) _logger.CloseBlock(summary);

            // Mezi bloky se menu odemkne, aby si ho participant mohl narovnat.
            if (menuLock != null) menuLock.SetLocked(false);
            if (voiceHelpLock != null) voiceHelpLock.SetLocked(false);

            var block = CurrentBlockIndex >= 0 && CurrentBlockIndex < _order.Count
                ? _order[CurrentBlockIndex]
                : default(BlockDefinition);

            // Během bloku je vidět pracoviště, mimo blok jen panel.
            // Kdyby zůstalo pracoviště za shrnutím, čte se špatně obojí.
            //
            // Postavená struktura se musí UKLIDIT, ne jen schovat: objekty
            // vznikají za běhu, takže je nemá co zhasnout — v seznamu
            // schovávaných je jen jejich kontejner a ten sám o sobě zůstane
            // plný. Bez tohohle zůstala stavba viset za tabulkou výsledků.
            if (spawner != null) spawner.RemoveAll();
            SetWorkspaceVisible(false);

            if (TutorialRunning)
            {
                // Tutoriál se do výsledků nezapisuje a index bloku nechává být.
                // Terče se vrátí na plný počet, jinak by si omezení odnesl
                // do prvního měřeného bloku.
                TutorialRunning = false;
                if (secondaryTask != null) secondaryTask.SetActiveTargetCount(-1);

                Debug.Log("[TrialManager] Tutoriál ukončen: " + summary);
                if (TutorialEnded != null) TutorialEnded();
                return;
            }

            _results.Add(new BlockResult
            {
                index = CurrentBlockIndex,
                condition = block.condition,
                load = block.load,
                templateId = block.template != null ? block.template.templateId : "?",
                isTraining = block.isTraining,
                usesSizes = block.template != null && block.template.usesSizes,
                reason = reason,

                rawTime = timer != null ? timer.RawTime : 0f,
                penaltyCount = timer != null ? timer.PenaltyCount : 0,
                penaltyTime = timer != null ? timer.PenaltyTime : 0f,

                stepsDone = stepsDone,
                stepsTotal = stepsTotal,
                wrongObjects = _wrongObjects,
                rejectedPlacements = _rejectedPlacements,

                secondaryRan = secondaryRan,
                activations = secondaryTask != null ? secondaryTask.Activations : 0,
                hits = secondaryTask != null ? secondaryTask.Hits : 0,
                hitRate = secondaryTask != null ? secondaryTask.HitRate : 0f,
                meanReactionTime = secondaryTask != null ? secondaryTask.MeanReactionTime : 0f,

                referenceOnDemand = block.referenceOnDemand,
                revealCount = odkryti,
                revealTime = odkrytiCas
            });

            Debug.Log($"[TrialManager] Blok ukončen: {summary}");
            if (BlockEnded != null) BlockEnded(block, CurrentBlockIndex);
        }

        /// <summary>
        /// Seed sekvence terčů. Závisí na participantovi a úrovni zátěže,
        /// NE na podmínce — díky tomu dostane stejný participant v menu
        /// i v hlasové podmínce identické pořadí terčů.
        /// </summary>
        private int ComputeSeed(BlockDefinition block)
        {
            unchecked
            {
                var h = participantId != null ? participantId.GetHashCode() : 0;
                return h * 31 + (int)block.load;
            }
        }

        private void ClearScene()
        {
            if (spawner != null) spawner.RemoveAll();
            if (secondaryTask != null) secondaryTask.StopTask();
        }

        /// <summary>Cesta k logu aktuálního bloku — pro kontrolu, že se data píšou.</summary>
        public string CurrentLogPath => _logger != null ? _logger.FilePath : null;

        /// <summary>Cesta k souhrnnému souboru poslední session, nebo null.</summary>
        public string ResultsPath { get; private set; }

        // ---- Souhrn session ----

        /// <summary>
        /// Tabulka výsledků pro zobrazení v headsetu. Sloupce se zarovnávají
        /// mezerami a text se sází neproporcionálně (TMP tag mspace) — jinak
        /// se sloupce rozjedou a tabulka přestane být čitelná.
        /// </summary>
        public string BuildResultsTable()
        {
            // TRÉNINK SE NEVYPISUJE. Do analýzy nejde a v tabulce jen mate:
            // jeho čas je vždycky nejhorší, protože se člověk teprve učí,
            // a participant si ho pak porovnává s měřenými bloky, jako by
            // patřily k sobě. Do logu se samozřejmě zapisuje dál.
            var merene = _results.FindAll(x => !x.isTraining);

            if (merene.Count == 0) return "Žádné odehrané bloky.";

            var sb = new StringBuilder();

            // PODMÍNKA NENÍ SLOUPEC, ALE NADPIS. Klasika a hlas jsou dvě
            // samostatné session, takže by ve všech řádcích stálo totéž.
            // Sloupec navíc místo toho nese, ČÍM SE BLOKY LIŠÍ — bez toho
            // vypadá první řádek nevysvětlitelně nejlepší, přestože v něm
            // participant jenom stavěl a nic ho nerušilo.
            // DVA ČASY VEDLE SEBE. „Čistý" je doba stavby, „s postihy" k ní
            // přičítá pět vteřin za každý minutý terč — tedy to, co
            // participantovi běželo na časomíře. Vedle sebe je z nich hned
            // vidět, kolik ho stálo rozdělení pozornosti; samotný součet by
            // to schoval a samotný čistý čas by to zamlčel.
            sb.Append(Pad("Blok", 7)).Append(Pad("Úloha", 21))
              .Append(Pad("Čistý čas", 12)).Append(Pad("S postihy", 12))
              .Append(Pad("Chyby", 8)).Append(Pad("Terče", 9)).Append(Pad("Reakce", 10))
              .Append("Plánek")
              .AppendLine();

            sb.AppendLine(new string('-', 84));

            // Bloky se čísluji od jedné v rámci tabulky, ne podle pořadí
            // v session — po vynechání tréninku by jinak začínala dvojkou.
            for (var i = 0; i < merene.Count; i++)
            {
                var r = merene[i];

                // ČÍM SE BLOK LIŠÍ OD PŘEDCHOZÍHO. První je holá stavba,
                // druhý k ní přidává velikosti, třetí skrytý plánek. Bez toho
                // vypadá první řádek nevysvětlitelně nejlepší, přestože v něm
                // participant dělal ze všech bloků nejmíň.
                var uloha = r.condition == InteractionCondition.Menu ? "klasická" : "hlasová";
                if (r.usesSizes) uloha += " + velikost";
                else if (r.referenceOnDemand) uloha += " + plánek";

                sb.Append(Pad((i + 1).ToString(), 7))
                  .Append(Pad(uloha, 21))
                  .Append(Pad(BlockTimer.Format(r.rawTime), 12))
                  .Append(Pad(BlockTimer.Format(r.rawTime + r.penaltyTime), 12))
                  .Append(Pad(r.wrongObjects.ToString(), 8))
                  .Append(Pad(r.secondaryRan ? r.hits + "/" + r.activations : "—", 9))
                  .Append(Pad(r.secondaryRan && r.hits > 0
                      ? r.meanReactionTime.ToString("F2") + " s" : "—", 10))
                  .Append(r.referenceOnDemand ? r.revealCount + "×" : "—")
                  .AppendLine();
            }

            return sb.ToString();
        }

        // ---- Zebricek napric ulozenymi session ----

        /// <summary>
        /// Přečte souhrnné soubory všech dřívějších session. Čte se z disku,
        /// ne z paměti — jinak by přehled existoval jen do zavření aplikace
        /// a po restartu headsetu by byl prázdný.
        ///
        /// NIC SE NESLUČUJE ANI NEFILTRUJE. Řazení i výběr si dělá obrazovka,
        /// která to zobrazuje; tady jde jen o to, co je na disku.
        /// </summary>
        public List<SessionScore> ReadAllSessions()
        {
            var vysledek = new List<SessionScore>();

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "BP_Data");
                if (!Directory.Exists(dir)) return vysledek;

                foreach (var soubor in Directory.GetFiles(dir, "*_souhrn_*.csv"))
                {
                    var skore = ParseSummary(soubor);
                    if (skore.measuredBlocks > 0) vysledek.Add(skore);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[TrialManager] Výsledky se nepodařilo načíst: " + e.Message);
            }

            return vysledek;
        }

        private static SessionScore ParseSummary(string cesta)
        {
            var s = new SessionScore { soubor = Path.GetFileName(cesta), complete = true };

            foreach (var radek in File.ReadAllLines(cesta))
            {
                if (radek.Length == 0) continue;

                if (radek[0] == '#')
                {
                    var c = radek.Split(';');
                    if (c.Length >= 2 && c[0].Contains("participant")) s.participantId = c[1].Trim();
                    else if (c.Length >= 2 && c[0].Contains("ukonceno")) s.datum = c[1].Trim();
                    continue;
                }

                var p = radek.Split(';');
                if (p.Length < 17 || p[0] == "blok") continue;   // hlavicka
                if (p[1] == "1") continue;                        // trenink se nepocita

                // Podminka z prvniho mereneho bloku. Dal uz se nemeni —
                // hlasova a klasicka verze jsou dve oddelene session.
                if (s.measuredBlocks == 0)
                    s.condition = p[2] == "Voice"
                        ? InteractionCondition.Voice : InteractionCondition.Menu;

                s.cleanTime += Cislo(p[5]);
                s.penaltyCount += (int)Cislo(p[6]);
                s.penaltyTime += Cislo(p[7]);
                s.wrongObjects += (int)Cislo(p[10]);
                s.activations += (int)Cislo(p[13]);
                s.hits += (int)Cislo(p[14]);
                s.measuredBlocks++;

                // Nedokoncena stavba by dala kratky cas a vyhrala by neopravnene.
                if ((int)Cislo(p[8]) < (int)Cislo(p[9])) s.complete = false;
            }

            return s;
        }

        private static float Cislo(string text)
        {
            float v;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        private static string Pad(string value, int width)
        {
            if (value == null) value = "";
            return value.Length >= width ? value + " " : value.PadRight(width);
        }

        /// <summary>
        /// Uloží souhrn session vedle blokových logů. Blokové logy jsou
        /// událostní a pro rychlý pohled nepoužitelné; tenhle soubor má jeden
        /// řádek na blok, takže se dá rovnou otevřít v tabulkovém procesoru.
        /// </summary>
        public string WriteResultsFile()
        {
            if (_results.Count == 0) return null;

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "BP_Data");
                Directory.CreateDirectory(dir);

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var path = Path.Combine(dir, participantId + "_souhrn_" + stamp + ".csv");

                var sb = new StringBuilder();
                sb.AppendLine("# participant;" + participantId);
                sb.AppendLine("# skupina;" + counterbalanceGroup);
                sb.AppendLine("# ukonceno;" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture));

                sb.AppendLine(string.Join(";",
                    "blok", "trenink", "podminka", "zatez", "sablona",
                    "cisty_cas_s", "postihu", "postih_s",
                    "kroku_hotovo", "kroku_celkem", "spatnych_objektu", "odmitnutych_pokladek",
                    "sekundarni_bezela", "aktivaci", "zasahu", "hit_rate", "prum_rt_s",
                    "predloha_na_vyzadani", "odkryti", "odkryti_s",
                    "ukonceni"));

                foreach (var r in _results)
                {
                    sb.AppendLine(string.Join(";",
                        (r.index + 1).ToString(CultureInfo.InvariantCulture),
                        r.isTraining ? "1" : "0",
                        r.condition.ToString(),
                        r.load.ToString(),
                        r.templateId,
                        r.rawTime.ToString("F3", CultureInfo.InvariantCulture),
                        r.penaltyCount.ToString(CultureInfo.InvariantCulture),
                        r.penaltyTime.ToString("F1", CultureInfo.InvariantCulture),
                        r.stepsDone.ToString(CultureInfo.InvariantCulture),
                        r.stepsTotal.ToString(CultureInfo.InvariantCulture),
                        r.wrongObjects.ToString(CultureInfo.InvariantCulture),
                        r.rejectedPlacements.ToString(CultureInfo.InvariantCulture),
                        r.secondaryRan ? "1" : "0",
                        r.activations.ToString(CultureInfo.InvariantCulture),
                        r.hits.ToString(CultureInfo.InvariantCulture),
                        r.hitRate.ToString("F3", CultureInfo.InvariantCulture),
                        r.meanReactionTime.ToString("F3", CultureInfo.InvariantCulture),
                        r.referenceOnDemand ? "1" : "0",
                        r.revealCount.ToString(CultureInfo.InvariantCulture),
                        r.revealTime.ToString("F1", CultureInfo.InvariantCulture),
                        r.reason));
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                ResultsPath = path;
                return path;
            }
            catch (Exception e)
            {
                // Zápis souhrnu nesmí shodit konec session — blokové logy
                // jsou na disku už tak jako tak.
                Debug.LogError("[TrialManager] Souhrn se nepodařilo uložit: " + e.Message);
                return null;
            }
        }
    }
}
