using BP.Input;
using BP.Secondary;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Zpětná vazba k hlavní úloze — spojuje stav úlohy s tím, co participant vidí.
    ///
    /// Řeší čtyři mezery, které jinak vyrábějí chyby nesouvisející s modalitou:
    ///   1) na kterém kroku jsem (zvýrazněný krok v šabloně)
    ///   2) kolik zbývá (postup na panelu)
    ///   3) jsem dost blízko, aby objekt zaskočil (zesílené svícení kroku)
    ///   4) vytvořil jsem špatný objekt (hláška)
    ///
    /// Je to čistě pohled — nic nerozhoduje a nic neloguje. Stejná vrstva
    /// se pak použije i pro hlasovou podmínku, takže zpětná vazba k hlavní
    /// úloze bude v obou podmínkách identická a nemůže se stát skrytou
    /// nezávislou proměnnou.
    /// </summary>
    public class TaskFeedbackView : MonoBehaviour
    {
        [SerializeField] private AssemblyTaskController task;
        [SerializeField] private PlacementValidator validator;
        [SerializeField] private TemplateVisualizer templateVisualizer;
        [SerializeField] private HandMenuPanel menuPanel;

        [Tooltip("Volitelné. Slouží k zobrazení instrukce, když se rozsvítí terč.")]
        [SerializeField] private SecondaryTaskManager secondaryTask;

        [Tooltip("Velká hláška v pozadí. Čitelná periferně, bez otočení hlavy.")]
        [SerializeField] private StatusBanner banner;

        [Tooltip("Plánek na vyžádání. Když je zapnutý, hláška o špatném " +
                 "objektu SMÍ říct jen to, že je špatný — ne co je správně.")]
        [SerializeField] private ReferenceOnDemand referenceOnDemand;

        [Tooltip("Jak často se kontroluje blízkost k cíli (sekundy). " +
                 "Nemusí to být každý frame.")]
        [SerializeField] private float snapCheckInterval = 0.06f;

        [Tooltip("Zobrazovat instrukci a výsledek u terčů. Pro zkoušení zapnuto; " +
                 "při měření VYPNOUT — instrukci má participant dostat v tréninku, " +
                 "ne ji během měřeného bloku číst z panelu.")]
        [SerializeField] private bool showSecondaryInstruction = true;

        private int _shownStep = -1;
        private bool _snapReady;
        private float _nextSnapCheck;

        // Sleduje se i verze stavby šablony, ne jen číslo kroku. Na začátku
        // bloku se šablona postaví znovu s čistými materiály, ale krok zůstane
        // 0 — kdyby se hlídal jen krok, zvýraznění by se neobnovilo. Reference
        // na šablonu nestačí, protože při restartu téhož bloku je stejná.
        private int _shownBuildVersion = -1;

        private void OnEnable()
        {
            if (task == null) return;

            task.StepCompleted += OnStepCompleted;
            task.WrongObjectCreated += OnWrongObject;
            task.TaskCompleted += OnTaskCompleted;
            task.RequestBlocked += OnRequestBlocked;
            task.PlacementRejected += OnPlacementRejected;

            if (secondaryTask != null)
            {
                secondaryTask.TargetActivated += OnTargetActivated;
                secondaryTask.TargetHit += OnTargetHit;
                secondaryTask.TargetMissed += OnTargetMissed;
            }
        }

        private void OnDisable()
        {
            if (task == null) return;

            task.StepCompleted -= OnStepCompleted;
            task.WrongObjectCreated -= OnWrongObject;
            task.TaskCompleted -= OnTaskCompleted;
            task.RequestBlocked -= OnRequestBlocked;
            task.PlacementRejected -= OnPlacementRejected;

            if (secondaryTask != null)
            {
                secondaryTask.TargetActivated -= OnTargetActivated;
                secondaryTask.TargetHit -= OnTargetHit;
                secondaryTask.TargetMissed -= OnTargetMissed;
            }
        }

        // ---- Sekundární úloha ----

        /// <summary>
        /// Instrukce k terči. Bez ní se participantovi rozsvítí červená tečka
        /// a nemá odkud vědět, že se jí má dotknout.
        ///
        /// Do samotného měření se to vypne — instrukci má participant dostat
        /// v tréninku, ne ji čtením panelu řešit během měřeného bloku.
        /// </summary>
        private void OnTargetActivated(SecondaryTarget target)
        {
            if (menuPanel == null || !showSecondaryInstruction) return;
            menuPanel.ShowNotice("DOTKNI SE ČERVENÉHO TERČE", true);
        }

        private void OnTargetHit(SecondaryTarget target, float reactionTime)
        {
            if (banner != null) banner.ShowPositive("ZÁSAH");
            if (menuPanel == null || !showSecondaryInstruction) return;
            menuPanel.ShowNotice("ZÁSAH  " + reactionTime.ToString("F2") + " s");
        }

        private void OnTargetMissed(SecondaryTarget target)
        {
            if (banner != null) banner.ShowNegative("POZDĚ");
            if (menuPanel == null || !showSecondaryInstruction) return;
            menuPanel.ShowNotice("POZDĚ");
        }

        private void Update()
        {
            if (task == null || templateVisualizer == null) return;

            // Krok se mohl posunout i jinak než přes event (start bloku, undo),
            // a šablona se mohla mezitím vyměnit za jinou.
            if (task.CurrentStep != _shownStep
                || templateVisualizer.BuildVersion != _shownBuildVersion)
            {
                _shownStep = task.CurrentStep;
                _shownBuildVersion = templateVisualizer.BuildVersion;
                _snapReady = false;
                RefreshAll();
            }

            if (Time.time < _nextSnapCheck) return;
            _nextSnapCheck = Time.time + snapCheckInterval;

            UpdateSnapAffordance();
            UpdateStickyNotice();
        }

        /// <summary>Překreslí šablonu i postup podle aktuálního stavu úlohy.</summary>
        public void RefreshAll()
        {
            if (task == null || templateVisualizer == null) return;

            var template = templateVisualizer.Template;
            if (template == null) return;

            templateVisualizer.ApplyProgress(task.CurrentStep);

            if (menuPanel != null)
                menuPanel.SetProgress(Mathf.Clamp(task.CurrentStep, 0, template.StepCount),
                    template.StepCount);
        }

        private void UpdateSnapAffordance()
        {
            if (validator == null || task.IsComplete) return;

            var pending = task.PendingObject;
            var ready = pending != null && validator.IsWithinSnapRange(pending, task.CurrentStep);

            if (ready == _snapReady) return;

            _snapReady = ready;
            templateVisualizer.SetSnapAffordance(task.CurrentStep, ready);
        }

        private void OnStepCompleted(int step) => RefreshAll();

        /// <summary>
        /// Rozlišuje „těsně mimo" od „úplně vedle". Kdyby hláška byla stejná,
        /// participant by nevěděl, jestli má doladit polohu, nebo objekt
        /// přenést jinam — a v datech by se to projevilo jako opakované
        /// neúspěšné pokusy bez zjevné příčiny.
        /// </summary>
        private void OnPlacementRejected(ShapeInstance instance, float distance)
        {
            if (menuPanel == null) return;

            var template = templateVisualizer.Template;
            if (template == null || task.CurrentStep >= template.StepCount) return;

            var expected = template.GetStep(task.CurrentStep);

            // NEJDŘÍV se řeší identita objektu, teprve pak poloha.
            // Obrácené pořadí posílalo participanta „přiblížit se", i když byl
            // 3 mm od cíle se špatnou barvou — a ten pak marně zkoušel znovu
            // a znovu. Hláška je trvalá, protože je to stavový problém:
            // sama od sebe nezmizí, dokud objekt nezmizí.
            if (!instance.Matches(expected.shape, expected.color, expected.size))
            {
                menuPanel.ShowNotice(HlaskaSpatnyObjekt(expected), true);
                return;
            }

            if (distance <= template.positionTolerance * 3f)
                menuPanel.ShowNotice("SKORO, PŘISUŇ BLÍŽ");
            else
                menuPanel.ShowNotice("NENÍ NA MÍSTĚ, KOUKNI NA ZVÝRAZNĚNÝ KROK");
        }

        private void OnWrongObject(ShapeInstance instance)
        {
            if (menuPanel == null) return;

            var template = templateVisualizer.Template;
            if (template == null || task.CurrentStep >= template.StepCount) return;

            var expected = template.GetStep(task.CurrentStep);
            menuPanel.ShowNotice(HlaskaSpatnyObjekt(expected), true);
        }

        /// <summary>
        /// Hláška o špatném objektu.
        ///
        /// PROČ SE V JEDNOM BLOKU NEDOŘÍKÁ: v bloku se skrytým plánkem se
        /// měří, kolikrát si participant plánek vyžádá. Kdyby mu panel po
        /// každé chybě sám napsal „má být červená kostka", je nejlevnější strategie
        /// vytvořit cokoliv, přečíst si odpověď a plánek neotevřít vůbec —
        /// a metrika odkrytí přestane měřit potřebu podívat se.
        ///
        /// V ostatních blocích plánek visí na očích, takže dořečená hláška
        /// nic neprozrazuje a jen šetří hledání.
        /// </summary>
        private string HlaskaSpatnyObjekt(TemplateStep expected)
        {
            if (referenceOnDemand != null && referenceOnDemand.IsActive)
                return "ŠPATNÝ OBJEKT, DEJ ZPĚT";

            // Název se bere ze společného seznamu, aby hláška mluvila týmiž
            // slovy jako okno s příkazy a jako povely, na které hlas slyší.
            return "ŠPATNĚ, DEJ ZPĚT. MÁ BÝT "
                   + (Nazvy.Barva(expected.color, expected.shape)
                      + " " + Nazvy.Tvar(expected.shape)).ToUpperInvariant();
        }

        /// <summary>
        /// Trvalá hláška o špatném objektu se drží, dokud špatný objekt
        /// existuje, a zmizí sama, jakmile ho participant odstraní.
        /// Řeší se to stavově, ne dalšími eventy — je to spolehlivější.
        /// </summary>
        private void UpdateStickyNotice()
        {
            if (menuPanel == null || !menuPanel.HasStickyNotice) return;

            // Dokud svítí terč, drží se jeho instrukce — nesmí ji přepsat
            // ani smazat úklid hlášek od hlavní úlohy.
            if (secondaryTask != null && secondaryTask.AnyTargetActive) return;

            var template = templateVisualizer.Template;
            if (template == null || task.CurrentStep >= template.StepCount)
            {
                menuPanel.ClearNotice();
                return;
            }

            var pending = task.PendingObject;
            var expected = template.GetStep(task.CurrentStep);

            if (pending == null || pending.Matches(expected.shape, expected.color, expected.size))
                menuPanel.ClearNotice();
        }

        private void OnRequestBlocked(string reason)
        {
            // Hlášku už zobrazuje panel sám; tady se jen sjednotí stav šablony,
            // aby zvýraznění nezůstalo viset po odmítnutém požadavku.
            RefreshAll();
        }

        private void OnTaskCompleted()
        {
            // Banner nese krátká slova. Delší text by při této velikosti
            // přesáhl zorné pole — podrobnosti patří na panel menu.
            if (banner != null) banner.ShowPositive("HOTOVO");

            if (menuPanel != null)
            {
                var total = templateVisualizer.Template != null
                    ? templateVisualizer.Template.StepCount : 0;
                menuPanel.SetProgress(total, total);
                menuPanel.ShowNotice("BLOK HOTOV");
            }

            // Hotová struktura se celá potlačí, ať je vidět, že se nic nečeká.
            if (templateVisualizer.Template != null)
                for (var i = 0; i < templateVisualizer.Template.StepCount; i++)
                    templateVisualizer.SetStepState(i, StepVisualState.Done);
        }
    }
}
