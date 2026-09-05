using System;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Měří čas bloku a eviduje časové postihy za minuté terče.
    ///
    /// ČISTÝ ČAS A POSTIHY SE DRŽÍ ODDĚLENĚ. Completion time je hlavní
    /// závislá proměnná; kdyby se do něj postihy přičetly, nešlo by rozlišit
    /// „stavěl pomaleji" od „minul víc terčů" a metrika by ztratila význam.
    /// Participant vidí součet, do dat jde obojí zvlášť.
    /// </summary>
    public class BlockTimer : MonoBehaviour
    {
        [Tooltip("Kolik sekund se přičte za každý minutý terč. " +
                 "Pouze pro zobrazení participantovi — do čistého času nevstupuje.")]
        [SerializeField] private float penaltyPerMiss = 5f;

        /// <summary>Běží měření?</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Čistý uplynulý čas bloku v sekundách — bez postihů.</summary>
        public float RawTime { get; private set; }

        /// <summary>Součet postihů v sekundách.</summary>
        public float PenaltyTime { get; private set; }

        /// <summary>Počet postihů (= počet minutých terčů).</summary>
        public int PenaltyCount { get; private set; }

        /// <summary>Co se ukazuje participantovi.</summary>
        public float DisplayTime => RawTime + PenaltyTime;

        /// <summary>Přibyl postih. Parametr je jeho velikost v sekundách.</summary>
        public event Action<float> PenaltyAdded;

        private float _startedAt;

        public void StartBlock()
        {
            RawTime = 0f;
            PenaltyTime = 0f;
            PenaltyCount = 0;
            _startedAt = Time.realtimeSinceStartup;
            IsRunning = true;
        }

        public void StopBlock()
        {
            if (!IsRunning) return;

            RawTime = Time.realtimeSinceStartup - _startedAt;
            IsRunning = false;
        }

        /// <summary>Přičte postih za minutý terč.</summary>
        public void AddPenalty()
        {
            if (!IsRunning) return;

            PenaltyTime += penaltyPerMiss;
            PenaltyCount++;

            if (PenaltyAdded != null) PenaltyAdded(penaltyPerMiss);
        }

        private void Update()
        {
            if (!IsRunning) return;
            RawTime = Time.realtimeSinceStartup - _startedAt;
        }

        /// <summary>Formát mm:ss.d pro zobrazení.</summary>
        public static string Format(float seconds)
        {
            if (seconds < 0f) seconds = 0f;

            var m = Mathf.FloorToInt(seconds / 60f);
            var s = seconds - m * 60f;
            return string.Format("{0}:{1:00.0}", m, s);
        }

        /// <summary>Řádek do souhrnu bloku.</summary>
        public string GetSummary()
        {
            return string.Format("cistyCas={0:F2}s postihy={1}x={2:F1}s celkem={3:F2}s",
                RawTime, PenaltyCount, PenaltyTime, DisplayTime);
        }
    }
}
