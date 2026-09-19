using System;
using System.Collections.Generic;
using BP.Logging;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BP.Secondary
{
    /// <summary>
    /// Řídí sekundární úlohu (skica b3) — objektivní míru zbytkové kapacity.
    ///
    /// TŘI VĚCI, KTERÉ JSOU TU ZÁMĚRNÉ A METODOLOGICKY PODSTATNÉ:
    ///
    /// 1) VÝSTUPEM JSOU POMĚRY, NE POČTY. Hlasová podmínka trvá per objekt
    ///    déle, takže se v ní participant dožije většího počtu aktivací.
    ///    Srovnávat absolutní počty zásahů mezi podmínkami by proto bylo
    ///    zavádějící — porovnává se hit rate a reakční čas.
    ///
    /// 2) INTERVAL AKTIVACÍ JE PEVNĚ ROZPTÝLENÝ, ne vázaný na dění v hlavní
    ///    úloze. Kdyby terče naskakovaly například po každém umístění, byla
    ///    by expozice v obou podmínkách jiná a nešlo by ji srovnat.
    ///
    /// 3) SEKVENCE JE SEEDOVANÁ. Stejný participant dostane v obou podmínkách
    ///    stejné pořadí terčů, takže se rozdíl nedá přičíst tomu, že jednomu
    ///    naskočily náhodou horší pozice.
    /// </summary>
    public class SecondaryTaskManager : MonoBehaviour
    {
        [Header("Terče")]
        [SerializeField] private SecondaryTarget[] targets = Array.Empty<SecondaryTarget>();

        [Tooltip("Body, kterými se dá terč dotknout — hroty ovladačů, případně " +
                 "prsty. Neaktivní modalita se automaticky ignoruje.")]
        [SerializeField] private Transform[] touchers = Array.Empty<Transform>();

        [Header("Časování")]
        [Tooltip("Okno odezvy v sekundách. Skica b3 uvádí ~2,5 s — HODNOTU JE NUTNÉ ODPILOTOVAT: " +
                 "příliš dlouhé okno vyrobí strop (100 % v obou podmínkách) a rozdíl zmizí.")]
        [SerializeField] private float responseWindow = 2.5f;

        [Tooltip("Nejkratší prodleva mezi aktivacemi.")]
        [SerializeField] private float minInterval = 8f;

        [Tooltip("Nejdelší prodleva mezi aktivacemi.")]
        [SerializeField] private float maxInterval = 15f;

        [Tooltip("Prodleva před první aktivací, ať se participant nejdřív rozkoukná.")]
        [SerializeField] private float initialDelay = 6f;

        [Header("Tutoriál")]
        [Tooltip("Prodlevy v tutoriálu. V měřených blocích je dlouhá pauza " +
                 "žádoucí — terč má rušit nepravidelně a nepředvídatelně. " +
                 "V tutoriálu se ale participant teprve učí, že se na terč " +
                 "sahá, a čekat na druhý terč až patnáct vteřin znamená stát " +
                 "a nevědět, jestli se něco nepokazilo.")]
        [SerializeField] private float tutorialMinInterval = 3f;

        [SerializeField] private float tutorialMaxInterval = 5f;

        /// <summary>Běží zrychlené prodlevy pro tutoriál?</summary>
        private bool _tutorialTempo;

        /// <summary>
        /// Zapne zrychlené prodlevy. Volá TutorialGuide při startu a vypíná
        /// je na konci, aby měřené bloky běžely na původních hodnotách.
        /// </summary>
        public void SetTutorialTempo(bool value) => _tutorialTempo = value;

        [Header("Reprodukovatelnost")]
        [Tooltip("Seed sekvence. Stejný seed = stejné pořadí terčů. " +
                 "Nastavuje TrialManager z ID participanta.")]
        [SerializeField] private int seed = 1;

        [Tooltip("Nikdy neaktivovat dvakrát za sebou tentýž terč.")]
        [SerializeField] private bool avoidImmediateRepeat = true;

        [Header("Dotykové body")]
        [Tooltip("Dohledat dotykové body i za běhu podle toho, která modalita " +
                 "je právě živá. Pevný seznam výše se opírá o serializované " +
                 "odkazy, a když XRI během session přepne z ovladačů na ruce " +
                 "(nebo naopak), může v něm zůstat jen neaktivní větev — " +
                 "terče se pak rozsvěcují, ale nejde je trefit.")]
        [SerializeField] private bool autoDiscoverTouchers = true;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Kolik terčů je ve hře. -1 = všechny. Tutoriál jich používá míň,
        /// aby se participant neztratil v sedmi místech naráz.
        ///
        /// Terče mimo výběr se SKRYJÍ, ne jen vyřadí z losování — svítící
        /// terč, na který se nesmí reagovat, by učil špatný návyk.
        /// </summary>
        public void SetActiveTargetCount(int count)
        {
            if (count < 0)
            {
                PouzitVyber(null);
                return;
            }

            var vyber = new HashSet<int>();
            for (var i = 0; i < count && i < targets.Length; i++) vyber.Add(i);
            PouzitVyber(vyber);
        }

        /// <summary>
        /// Nechá ve hře jen N nejníže položených terčů.
        ///
        /// PROČ ZROVNA SPODNÍ: v tutoriálu visí před participantem boxík
        /// s instrukcí. Terče z horní části prstence se přes něj překrývají
        /// a text přestane být čitelný zrovna ve chvíli, kdy podle něj má
        /// jednat. Dole je volno. V měřených blocích se tohle nepoužívá —
        /// tam musí být excentricita rozložená rovnoměrně kolem osy pohledu.
        /// </summary>
        public void SetActiveTargetsLowest(int count)
        {
            var poradi = new List<int>(targets.Length);
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] != null) poradi.Add(i);

            poradi.Sort((a, b) => targets[a].transform.position.y
                .CompareTo(targets[b].transform.position.y));

            var vyber = new HashSet<int>();
            for (var i = 0; i < count && i < poradi.Count; i++) vyber.Add(poradi[i]);

            PouzitVyber(vyber);
        }

        /// <summary>
        /// Terče mimo výběr se SKRYJÍ, ne jen vyřadí z losování — svítící
        /// terč, na který se nesmí reagovat, by učil špatný návyk.
        /// </summary>
        private void PouzitVyber(HashSet<int> vyber)
        {
            _activeSet = vyber;

            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                var zapnuty = vyber == null || vyber.Contains(i);
                if (!zapnuty) targets[i].ResetToIdle();
                targets[i].gameObject.SetActive(zapnuty);
            }
        }

        /// <summary>Indexy terčů ve hře. null = všechny.</summary>
        private HashSet<int> _activeSet;

        // Souhrn za blok
        public int Activations { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }

        /// <summary>Podíl zasažených terčů. To je metrika do analýzy, ne počty.</summary>
        public float HitRate => Activations > 0 ? (float)Hits / Activations : 0f;

        /// <summary>Průměrný reakční čas zásahů v sekundách.</summary>
        public float MeanReactionTime => Hits > 0 ? _reactionTimeSum / Hits : 0f;

        public event Action<SecondaryTarget, float> TargetHit;
        public event Action<SecondaryTarget> TargetMissed;

        /// <summary>Terč se právě rozsvítil — navěšuje se na to instrukce.</summary>
        public event Action<SecondaryTarget> TargetActivated;

        private TrialLogger _logger;
        private System.Random _random;
        private float _nextActivation;
        private float _reactionTimeSum;
        private int _lastIndex = -1;

        private readonly List<Transform> _effectiveTouchers = new List<Transform>();

        private void Awake()
        {
            RefreshTouchers();

            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                targets[i].Index = i;
                targets[i].Hit += OnHit;
                targets[i].Missed += OnMissed;
            }
        }

        /// <summary>
        /// Sestaví seznam dotykových bodů: pevně přiřazené plus ty, které
        /// se najdou v rigu za běhu. Dohledávání je tu proto, že přiřazený
        /// seznam odpovídá stavu při stavbě scény — ne tomu, co má
        /// participant v ruce ve třetím bloku.
        /// </summary>
        private void RefreshTouchers()
        {
            _effectiveTouchers.Clear();

            foreach (var t in touchers)
                if (t != null) _effectiveTouchers.Add(t);

            if (autoDiscoverTouchers)
            {
                // I neaktivní: aktivitu řeší až samotná kontrola dotyku,
                // takže se seznam nemusí přestavovat při každém přepnutí.
                foreach (var poke in FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var tr = poke.transform;
                    if (!_effectiveTouchers.Contains(tr)) _effectiveTouchers.Add(tr);
                }
            }

            var pole = _effectiveTouchers.ToArray();
            foreach (var t in targets)
                if (t != null) t.SetTouchers(pole);
        }

        /// <summary>Kolik dotykových bodů je právě teď živých.</summary>
        private int CountLiveTouchers()
        {
            var n = 0;
            foreach (var t in _effectiveTouchers)
                if (t != null && t.gameObject.activeInHierarchy) n++;
            return n;
        }

        private void OnDestroy()
        {
            foreach (var t in targets)
            {
                if (t == null) continue;
                t.Hit -= OnHit;
                t.Missed -= OnMissed;
            }
        }

        private void Update()
        {
            // Hlídač běží i mimo spuštěnou úlohu — terč se dá rozsvítit
            // testovací klávesou, a takový terč by jinak nikdo neuvolnil.
            ReleaseStuckTargets();

            if (!IsRunning) return;
            if (Time.realtimeSinceStartup < _nextActivation) return;

            ActivateRandomTarget();
            ScheduleNext();
        }

        /// <summary>
        /// Uvolní terč, který zůstal viset v rozsvíceném nebo potvrzeném stavu.
        ///
        /// PROČ TO MUSÍ EXISTOVAT: časový limit terče řeší jeho vlastní Update,
        /// a ten se nevykonává, když je objekt skrytý — mezi bloky se celé
        /// pracoviště skrývá. Terč tak může uvíznout rozsvícený. A protože se
        /// nikdy neaktivují dva terče zároveň, jeden uvíznutý terč zablokuje
        /// VŠECHNY další aktivace na zbytek session. Chyba se pak projeví jako
        /// „terče v dalších blocích přestaly fungovat".
        ///
        /// Uvolnění se loguje. Kdyby k tomu docházelo, je z logu vidět který
        /// terč a jak dlouho visel — bez toho se to hledá naslepo.
        /// </summary>
        private void ReleaseStuckTargets()
        {
            var limit = responseWindow + 2f;

            for (var i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t == null || t.State == TargetState.Idle) continue;
                if (t.TimeInState <= limit) continue;

                var stav = t.State;
                t.ResetToIdle();

                Debug.LogWarning("[SecondaryTaskManager] Terč " + i + " uvízl ve stavu "
                                 + stav + " po " + t.TimeInState.ToString("F1")
                                 + " s — uvolněn hlídačem.", this);

                if (_logger != null)
                    _logger.Log(LogEvent.Note, detail: "terc=" + i + " uvizl ve stavu " + stav);
            }
        }

        /// <summary>Spustí sekundární úlohu. Volá TrialManager na začátku dual-task bloku.</summary>
        public void StartTask(TrialLogger logger, int sequenceSeed)
        {
            _logger = logger;
            _random = new System.Random(sequenceSeed);

            Activations = 0;
            Hits = 0;
            Misses = 0;
            _reactionTimeSum = 0f;
            _lastIndex = -1;

            foreach (var t in targets) if (t != null) t.ResetToIdle();

            // Na zacatku bloku se seznam prestavi: participant muze mit
            // v ruce neco jineho nez v bloku predchozim.
            RefreshTouchers();

            // I první terč přijde v tutoriálu dřív — čekat na úvodní prodlevu
            // se stejnou trpělivostí jako v měření nemá smysl, když se
            // participant teprve učí, co má dělat.
            _nextActivation = Time.realtimeSinceStartup
                + (_tutorialTempo ? tutorialMinInterval : initialDelay);
            IsRunning = true;
        }

        public void StopTask()
        {
            IsRunning = false;
            foreach (var t in targets) if (t != null) t.ResetToIdle();
        }

        /// <summary>Ukazuje se terč právě teď? Pro kontrolu při ladění.</summary>
        public bool AnyTargetActive
        {
            get
            {
                foreach (var t in targets)
                    if (t != null && t.State == TargetState.Active) return true;
                return false;
            }
        }

        /// <summary>
        /// Okamžitě aktivuje jeden terč. Pro zkoušení — čekat 6 s na první
        /// aktivaci a pak 8–15 s na další se při ladění nedá.
        /// Nevyžaduje spuštěnou úlohu.
        /// </summary>
        public void ActivateNowForTesting()
        {
            if (_random == null) _random = new System.Random(seed);
            ActivateRandomTarget();
        }

        /// <summary>
        /// Zapne nebo vypne úlohu bez ohledu na blok. Jen pro zkoušení —
        /// při měření o spuštění rozhoduje TrialManager podle úrovně zátěže.
        /// </summary>
        public void ToggleForTesting(TrialLogger logger)
        {
            if (IsRunning) StopTask();
            else StartTask(logger, seed);
        }

        private void ActivateRandomTarget()
        {
            if (targets.Length == 0) return;

            // VŽDY JEN JEDEN AKTIVNÍ TERČ. Skica b3 mluví o jednom terči,
            // který se rozsvítí — dva zároveň by rozdělily pozornost jinak
            // a reakční čas by přestal být srovnatelný mezi aktivacemi.
            if (AnyTargetActive) return;

            // Sesbírají se jen terče v klidu — aktivní ani doznívající se nepřepíše.
            var candidates = new List<int>(targets.Length);
            for (var i = 0; i < targets.Length; i++)
            {
                if (_activeSet != null && !_activeSet.Contains(i)) continue;
                if (targets[i] == null || targets[i].State != TargetState.Idle) continue;
                if (avoidImmediateRepeat && i == _lastIndex && targets.Length > 1) continue;
                candidates.Add(i);
            }

            if (candidates.Count == 0) return;

            var pick = candidates[_random.Next(candidates.Count)];
            _lastIndex = pick;

            targets[pick].Activate(responseWindow);

            // Aktivace se pocita az podle skutecneho stavu terce. Activate()
            // umi pozadavek odmitnout, a kdyby se pocitalo naslepo, rostl by
            // jmenovatel hit rate o aktivace, ktere participant nikdy nevidel.
            if (targets[pick].State != TargetState.Active) return;

            Activations++;

            // Terc rozsviceny bez jedineho ziveho dotykoveho bodu se NEDA
            // trefit. Bez tohohle zaznamu se to v datech jevi jako ztrata
            // pozornosti, prestoze slo o poruchu vstupu - a hit rate by tim
            // dostal posun, ktery nema nic spolecneho se zatezi.
            var ziveBody = CountLiveTouchers();
            if (ziveBody == 0)
            {
                RefreshTouchers();
                ziveBody = CountLiveTouchers();
            }

            if (ziveBody == 0)
            {
                Debug.LogWarning("[SecondaryTaskManager] Terč " + pick + " se rozsvítil, ale " +
                                 "není živý žádný dotykový bod — nedá se trefit.", this);

                if (_logger != null)
                    _logger.Log(LogEvent.Note, detail: "POZOR: zadny zivy dotykovy bod, terc=" + pick);
            }

            if (_logger != null)
                _logger.Log(LogEvent.SecondaryTargetActivated,
                    detail: "terc=" + pick + " dotykovychBodu=" + ziveBody);

            if (TargetActivated != null) TargetActivated(targets[pick]);
        }

        private void ScheduleNext()
        {
            var min = _tutorialTempo ? tutorialMinInterval : minInterval;
            var max = _tutorialTempo ? tutorialMaxInterval : maxInterval;

            var span = max - min;
            var offset = span > 0f ? (float)_random.NextDouble() * span : 0f;
            _nextActivation = Time.realtimeSinceStartup + min + offset;
        }

        private void OnHit(SecondaryTarget target, float reactionTime)
        {
            Hits++;
            _reactionTimeSum += reactionTime;

            if (_logger != null)
                _logger.Log(LogEvent.SecondaryTargetHit,
                    reactionTime: reactionTime, detail: "terc=" + target.Index);

            if (TargetHit != null) TargetHit(target, reactionTime);
        }

        private void OnMissed(SecondaryTarget target)
        {
            Misses++;

            if (_logger != null)
                _logger.Log(LogEvent.SecondaryTargetMissed, detail: "terc=" + target.Index);

            if (TargetMissed != null) TargetMissed(target);
        }

        /// <summary>
        /// Diagnostika dosahu: jak blízko je nejbližší ruka ke každému terči.
        /// Slouží k ověření, že participant na terče vůbec dosáhne.
        /// </summary>
        public string DescribeReach()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                var d = targets[i].GetNearestToucherDistance();
                sb.Append(i).Append('=')
                  .Append(d > 100f ? "-" : (d * 100f).ToString("F0") + "cm")
                  .Append("  ");
            }
            sb.Append("| prah dotyku ")
              .Append(targets.Length > 0 && targets[0] != null
                  ? (targets[0].TouchRadius * 100f).ToString("F0") + " cm" : "?");
            return sb.ToString();
        }

        /// <summary>Souhrn pro řádek BlockEnd v logu.</summary>
        public string GetSummary()
        {
            return string.Format(
                "aktivaci={0} zasahu={1} minutych={2} hitRate={3:F3} prumRT={4:F3}s",
                Activations, Hits, Misses, HitRate, MeanReactionTime);
        }
    }
}
