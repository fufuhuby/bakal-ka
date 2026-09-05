using System;
using System.Collections.Generic;
using BP.Logging;
using UnityEngine;

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

        [Header("Reprodukovatelnost")]
        [Tooltip("Seed sekvence. Stejný seed = stejné pořadí terčů. " +
                 "Nastavuje TrialManager z ID participanta.")]
        [SerializeField] private int seed = 1;

        [Tooltip("Nikdy neaktivovat dvakrát za sebou tentýž terč.")]
        [SerializeField] private bool avoidImmediateRepeat = true;

        public bool IsRunning { get; private set; }

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

        private void Awake()
        {
            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                targets[i].Index = i;
                targets[i].SetTouchers(touchers);
                targets[i].Hit += OnHit;
                targets[i].Missed += OnMissed;
            }
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
            if (!IsRunning) return;
            if (Time.realtimeSinceStartup < _nextActivation) return;

            ActivateRandomTarget();
            ScheduleNext();
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

            _nextActivation = Time.realtimeSinceStartup + initialDelay;
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
                if (targets[i] == null || targets[i].State != TargetState.Idle) continue;
                if (avoidImmediateRepeat && i == _lastIndex && targets.Length > 1) continue;
                candidates.Add(i);
            }

            if (candidates.Count == 0) return;

            var pick = candidates[_random.Next(candidates.Count)];
            _lastIndex = pick;

            targets[pick].Activate(responseWindow);
            Activations++;

            if (_logger != null)
                _logger.Log(LogEvent.SecondaryTargetActivated, detail: "terc=" + pick);

            if (TargetActivated != null) TargetActivated(targets[pick]);
        }

        private void ScheduleNext()
        {
            var span = maxInterval - minInterval;
            var offset = span > 0f ? (float)_random.NextDouble() * span : 0f;
            _nextActivation = Time.realtimeSinceStartup + minInterval + offset;
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
