using System.Collections.Generic;
using BP.Core;
using BP.Input;
using BP.Logging;
using BP.Secondary;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace BP.Feedback
{
    /// <summary>
    /// Jediné místo, které rozhoduje, co se kdy ozve a kdy to zabzučí.
    ///
    /// PROČ CENTRÁLNĚ: kdyby si každý skript přehrával svůj zvuk sám,
    /// ozvučení by nešlo vypnout jedním přepínačem a při ladění by se
    /// nedalo říct, kolik zvuků vlastně aplikace má. Tady jsou vidět
    /// všechny naráz a všechny visí na událostech, které už v projektu
    /// existovaly — kvůli zvuku se nemuselo přepsat nic jiného.
    ///
    /// ZVUK TADY NENÍ OZDOBA, JE TO PROMĚNNÁ. Platí dvě pravidla, bez
    /// kterých by ozvučení rozbilo měření:
    ///
    /// 1) ROZSVÍCENÍ TERČE SE V MĚŘENÍ NEOZVE. Sekundární úloha stojí na
    ///    tom, že terče mají řízenou excentricitu 32° a zachytávají se
    ///    periferním viděním. Zvuk má všesměrový záběr — kdyby terč pípl,
    ///    excentricita by přestala hrát roli, reakční časy by se srovnaly
    ///    napříč terči a hlavní objektivní míra by přestala měřit to,
    ///    co má. V tutoriálu se ozvat smí (viz targetOnsetInTutorial):
    ///    tam se nic neměří a participant se má naučit, co terč je.
    ///
    /// 2) OZVUČENÍ JE V OBOU PODMÍNKÁCH STEJNÉ. Nic tady nesahá na
    ///    InteractionCondition. Kdyby menu znělo jinak než hlas, rozdíl
    ///    mezi podmínkami by se mísil s rozdílem v ozvučení.
    ///
    /// VĚDOMÝ VEDLEJŠÍ ÚČINEK: zvuk zaskočení objektu ušetří participantovi
    /// kontrolní pohled po každém položení. Zraková zátěž tím klesne, ale
    /// v OBOU podmínkách stejně, takže hlavní porovnání to neposune — jen
    /// zmenší celkový dual-task cost. Patří to do metodiky práce.
    /// </summary>
    public class FeedbackDirector : MonoBehaviour
    {
        [Header("Závislosti")]
        [SerializeField] private TrialManager trialManager;
        [SerializeField] private AssemblyTaskController task;
        [SerializeField] private ShapeSpawner spawner;
        [SerializeField] private MenuRequestSource menuSource;
        [SerializeField] private SecondaryTaskManager secondaryTask;
        [SerializeField] private ReferenceOnDemand referenceOnDemand;


        [Header("Hlavní přepínače")]
        [Tooltip("Zvuk celkově. Vypnuté = tichá session, pro případ, že by " +
                 "se ozvučení ukázalo jako problém.")]
        [SerializeField] private bool enableAudio = true;

        [Tooltip("Haptika celkově.\n\n" +
                 "POZOR: bzučí POUZE ovladače. Při hand trackingu Quest " +
                 "nevibruje nijak. Pokud část participantů pojede na rukou " +
                 "a část na ovladačích, dostanou různou zpětnou vazbu a je " +
                 "to systematická chyba — vstup musí být pro celý experiment " +
                 "jeden.")]
        [SerializeField] private bool enableHaptics = true;

        [Range(0f, 1f)]
        [SerializeField] private float masterVolume = 0.7f;

        [Tooltip("Nechat terč ozvat se při rozsvícení, ale JEN v tutoriálu. " +
                 "V měřených blocích by to zabilo periferní vidění jako " +
                 "mechanismus — viz komentář u třídy.")]
        [SerializeField] private bool targetOnsetInTutorial = true;

        [Tooltip("Ozvučit stisk každého tlačítka v rozhraní (START, TUTORIAL, " +
                 "DALŠÍ BLOK, tlačítka průvodce, vývojářský panel). Tlačítka, " +
                 "která už vlastní zvuk mají, se přeskakují — viz BezVlastnihoZvuku.")]
        [SerializeField] private bool hookAllButtons = true;

        // ---- Zvuky ----

        private AudioSource _source;

        private AudioClip _tileSelect;
        private AudioClip _tileLocked;
        private AudioClip _create;
        private AudioClip _stepBack;
        private AudioClip _snap;
        private AudioClip _reject;
        private AudioClip _wrongObject;
        private AudioClip _taskDone;
        private AudioClip _targetHit;
        private AudioClip _targetMiss;
        private AudioClip _targetOnset;
        private AudioClip _reveal;
        private AudioClip _blockEnd;
        private AudioClip _uiClick;

        private readonly List<HapticImpulsePlayer> _haptics = new List<HapticImpulsePlayer>();
        private MenuPart _lastParts = MenuPart.All;
        private int _lastSelectionHash;

        private void Awake()
        {
            _source = gameObject.GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();

            // Bez prostorovosti: zpětná vazba se má ozvat „u hlavy", ne
            // z místa objektu. Prostorový zvuk by u periferního terče
            // navíc prozradil směr, a to je přesně to, co se nesmí stát.
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _source.loop = false;

            BuildClips();
        }

        private void OnEnable()
        {
            if (task != null)
            {
                task.StepCompleted += OnStepCompleted;
                task.TaskCompleted += OnTaskCompleted;
                task.WrongObjectCreated += OnWrongObject;
                task.PlacementRejected += OnPlacementRejected;
            }

            if (spawner != null) spawner.Spawned += OnSpawned;

            if (menuSource != null)
            {
                menuSource.SelectionChanged += OnSelectionChanged;
                menuSource.PartsChanged += OnPartsChanged;
                menuSource.UndoRequested += OnUndo;
            }

            if (secondaryTask != null)
            {
                secondaryTask.TargetActivated += OnTargetActivated;
                secondaryTask.TargetHit += OnTargetHit;
                secondaryTask.TargetMissed += OnTargetMissed;
            }

            if (trialManager != null)
            {
                trialManager.BlockStarted += OnBlockStarted;
                trialManager.BlockEnded += OnBlockEnded;
            }

            if (referenceOnDemand != null) referenceOnDemand.Revealed += PlayReveal;
        }

        private void OnDisable()
        {
            if (task != null)
            {
                task.StepCompleted -= OnStepCompleted;
                task.TaskCompleted -= OnTaskCompleted;
                task.WrongObjectCreated -= OnWrongObject;
                task.PlacementRejected -= OnPlacementRejected;
            }

            if (spawner != null) spawner.Spawned -= OnSpawned;

            if (menuSource != null)
            {
                menuSource.SelectionChanged -= OnSelectionChanged;
                menuSource.PartsChanged -= OnPartsChanged;
                menuSource.UndoRequested -= OnUndo;
            }

            if (secondaryTask != null)
            {
                secondaryTask.TargetActivated -= OnTargetActivated;
                secondaryTask.TargetHit -= OnTargetHit;
                secondaryTask.TargetMissed -= OnTargetMissed;
            }

            if (trialManager != null)
            {
                trialManager.BlockStarted -= OnBlockStarted;
                trialManager.BlockEnded -= OnBlockEnded;
            }

            if (referenceOnDemand != null) referenceOnDemand.Revealed -= PlayReveal;
        }

        // ---- Reakce ----

        private void OnSelectionChanged()
        {
            // SelectionChanged chodí i z SetRequiresSize při přepnutí bloku,
            // kdy participant na nic neklepl. Cvakat by se tam nemělo, tak
            // se hlídá, že se výběr opravdu změnil.
            var hash = Hash(menuSource);
            if (hash == _lastSelectionHash) return;

            _lastSelectionHash = hash;
            Play(_tileSelect);
        }

        private void OnPartsChanged()
        {
            // Zamčení samo o sobě je tichá věc. Zvuk „teď ne" dává smysl
            // až při pokusu klepnout na zamčenou dlaždici — ten se ale
            // v menu nedá odchytit, protože Button.interactable klik spolkne.
            // Zůstává tedy jen změna stavu, a ta se nehlásí.
            _lastParts = menuSource.AllowedParts;
        }

        private void OnSpawned(ShapeInstance instance)
        {
            Play(_create);
        }

        private void OnUndo()
        {
            Play(_stepBack);
        }

        private void OnStepCompleted(int step)
        {
            Play(_snap);
            Buzz(0.5f, 0.08f);
        }

        private void OnTaskCompleted()
        {
            Play(_taskDone);
        }

        private void OnWrongObject(ShapeInstance instance)
        {
            Play(_wrongObject);
            Buzz(0.4f, 0.12f);
        }

        private void OnPlacementRejected(ShapeInstance instance, float error)
        {
            Play(_reject);
            Buzz(0.4f, 0.04f);
            Buzz(0.4f, 0.04f, 0.09f);
        }

        private void OnTargetActivated(SecondaryTarget target)
        {
            // JEDINÉ místo, kde se rozhoduje o zvuku rozsvícení. Podmínka
            // musí zůstat takhle úzká — viz pravidlo 1 v komentáři třídy.
            if (!targetOnsetInTutorial) return;
            if (trialManager == null || !trialManager.TutorialRunning) return;

            Play(_targetOnset);
        }

        private void OnTargetHit(SecondaryTarget target, float reaction)
        {
            Play(_targetHit);
            Buzz(0.7f, 0.05f);
        }

        private void OnTargetMissed(SecondaryTarget target)
        {
            Play(_targetMiss);
            Buzz(0.3f, 0.15f);
        }

        private void OnBlockStarted(BlockDefinition block, int index)
        {
            // Nastavení se zapisuje do dat, ne jen do inspektoru. Bez toho
            // by u staršího souboru nešlo poznat, jestli participant zvuk
            // vůbec měl, a session by nešly srovnávat.
            if (trialManager != null && trialManager.Logger != null)
                trialManager.Logger.Log(LogEvent.Note, detail: Describe());
        }

        private void OnBlockEnded(BlockDefinition block, int index)
        {
            Play(_blockEnd);
        }

        /// <summary>Odkrytí plánku tlačítkem SHOW PLAN.</summary>
        public void PlayReveal()
        {
            Play(_reveal);
        }

        // ---- Tlačítka rozhraní ----

        /// <summary>
        /// Navěsí zvuk na všechna tlačítka ve scéně.
        ///
        /// PROČ AŽ VE Start(): panely se staví kódem v Awake a jejich Bind()
        /// volá onClick.RemoveAllListeners(). Kdyby se zvuk věšel dřív,
        /// stavba panelu by ho zase strhla.
        /// </summary>
        private void Start()
        {
            if (!hookAllButtons) return;

            var tlacitka = FindObjectsByType<Button>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var pocet = 0;
            foreach (var b in tlacitka)
            {
                if (b == null || !BezVlastnihoZvuku(b.gameObject.name)) continue;

                b.onClick.AddListener(PlayUiClick);
                pocet++;
            }

            _hookedButtons = pocet;
        }

        /// <summary>
        /// Má tohle tlačítko dostat obecné cvaknutí?
        ///
        /// Vyřazená jsou ta, která už svůj zvuk mají. CREATE se ozve až
        /// ve chvíli, kdy objekt opravdu vznikne — obecné cvaknutí navrch
        /// by znělo jako dva zvuky za sebou. U dlaždic je to totéž.
        /// </summary>
        private static bool BezVlastnihoZvuku(string jmeno)
        {
            if (jmeno.StartsWith("Color_")) return false;
            if (jmeno.StartsWith("Shape_")) return false;
            if (jmeno.StartsWith("Size_")) return false;
            if (jmeno == "Button_CREATE") return false;
            if (jmeno == "Button_STEPBACK") return false;
            if (jmeno == "Btn_Reveal") return false;
            return true;
        }

        public void PlayUiClick()
        {
            Play(_uiClick);
        }

        private int _hookedButtons;

        // ---- Přehrání ----

        private void Play(AudioClip clip)
        {
            if (!enableAudio || clip == null || _source == null) return;
            _source.PlayOneShot(clip, masterVolume);
        }

        /// <summary>
        /// Impulz do ovladačů. Přehrávače se hledají až při prvním použití
        /// a znovu, když seznam zmizí — XRI při přepnutí ruce/ovladače
        /// vymění celou větev a uložený odkaz by zůstal na mrtvé.
        /// </summary>
        private void Buzz(float amplitude, float duration, float delay = 0f)
        {
            if (!enableHaptics) return;

            if (_haptics.Count == 0) RefreshHaptics();

            for (var i = 0; i < _haptics.Count; i++)
            {
                var p = _haptics[i];
                if (p == null || !p.isActiveAndEnabled) continue;

                if (delay <= 0f) p.SendHapticImpulse(amplitude, duration);
                else StartCoroutine(BuzzLater(p, amplitude, duration, delay));
            }
        }

        private System.Collections.IEnumerator BuzzLater(HapticImpulsePlayer p,
            float amplitude, float duration, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (p != null && p.isActiveAndEnabled) p.SendHapticImpulse(amplitude, duration);
        }

        private void RefreshHaptics()
        {
            _haptics.Clear();
            _haptics.AddRange(FindObjectsByType<HapticImpulsePlayer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        // ---- Stavba zvuků ----

        private void BuildClips()
        {
            // Frekvence a délky jsou tady schválně jako čísla na jednom
            // místě: takhle se dají opsat do metodiky práce a někdo jiný
            // je zopakuje bez přístupu k projektu.

            _tileSelect = Sfx.Click("tile", 0.030f, 0.35f, 1);
            _tileLocked = Sfx.Tone("locked", 300f, 300f, 0.040f, 0.20f);

            _create = Sfx.Tone("create", 660f, 990f, 0.080f, 0.50f);
            _stepBack = Sfx.Tone("stepback", 660f, 440f, 0.080f, 0.45f);

            _snap = Sfx.Sequence("snap", new[] { 784f, 1047f }, 0.060f, 0.55f);
            _taskDone = Sfx.Sequence("done", new[] { 523f, 659f, 784f }, 0.110f, 0.55f);
            _blockEnd = Sfx.Sequence("blockend", new[] { 587f, 440f }, 0.130f, 0.45f);

            _reject = Sfx.Tone("reject", 220f, 200f, 0.140f, 0.50f, true);
            _wrongObject = Sfx.Tone("wrong", 165f, 150f, 0.200f, 0.50f, true);

            _targetHit = Sfx.Tone("hit", 1320f, 1320f, 0.060f, 0.55f);
            _targetMiss = Sfx.Tone("miss", 196f, 175f, 0.250f, 0.45f, true);
            _targetOnset = Sfx.Tone("onset", 990f, 990f, 0.050f, 0.40f);

            _reveal = Sfx.Tone("reveal", 587f, 587f, 0.090f, 0.35f);
            _uiClick = Sfx.Tone("ui", 520f, 780f, 0.070f, 0.45f);
        }

        private static int Hash(MenuRequestSource s)
        {
            var a = s.SelectedShape.HasValue ? (int)s.SelectedShape.Value + 1 : 0;
            var b = s.SelectedColor.HasValue ? (int)s.SelectedColor.Value + 1 : 0;
            var c = s.SelectedSize.HasValue ? (int)s.SelectedSize.Value + 1 : 0;
            return a * 10000 + b * 100 + c;
        }

        /// <summary>Souhrn nastavení do logu.</summary>
        public string Describe()
        {
            return string.Format(
                "zvuk={0} hlasitost={1:F2} haptika={2} tercVTutorialu={3} tlacitek={4}",
                enableAudio ? "ano" : "ne",
                masterVolume,
                enableHaptics ? "ano" : "ne",
                targetOnsetInTutorial ? "ano" : "ne",
                _hookedButtons);
        }
    }
}
