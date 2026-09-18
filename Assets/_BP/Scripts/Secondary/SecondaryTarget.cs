using System;
using UnityEngine;

namespace BP.Secondary
{
    public enum TargetState
    {
        /// <summary>Klidový režim — průhledný, ale viditelný (skica b3).</summary>
        Idle = 0,

        /// <summary>Aktivní — červený, běží okno odezvy.</summary>
        Active = 1,

        /// <summary>Krátké zelené potvrzení po dotyku.</summary>
        Confirmed = 2
    }

    /// <summary>
    /// Jeden periferní terč sekundární úlohy (skica b3).
    ///
    /// Stavový automat: Idle → Active (zčervená) → dotyk → Confirmed (zeleně)
    /// → Idle. Když okno vyprší bez dotyku, jde se automaticky zpět do Idle
    /// a započítá se miss.
    ///
    /// DOTYK SE MĚŘÍ VLASTNÍ VZDÁLENOSTÍ, ne přes XRI interakci. Důvod:
    /// XRI má u poke několik skrytých podmínek (registrace colliderů,
    /// požadavek na XRPokeFilter), na kterých detekce tiše nefunguje — a
    /// u měřicí aparatury je tichý výpadek nejhorší možná porucha. Takhle
    /// je „dotyk" definován jedním číslem, které se dá v práci uvést:
    /// hrot ovladače nebo prst do touchRadius od středu terče.
    ///
    /// ZÁMĚRNĚ SE NEPOUŽÍVÁ MÍŘENÍ NA DÁLKU. Kdyby stačilo na terč ukázat,
    /// participant by na něj „dosáhl" bez pohybu ruky a úloha by přestala
    /// měřit motorickou dostupnost — zbyla by z ní jen reakce na barvu.
    /// </summary>
    public class SecondaryTarget : MonoBehaviour
    {
        [Header("Vizuál")]
        [SerializeField] private MeshRenderer targetRenderer;

        [Tooltip("Matný disk za terčem. Zaručuje kontrast proti libovolnému " +
                 "pozadí reálné místnosti v passthroughu.")]
        [SerializeField] private MeshRenderer backingRenderer;

        [Header("Dotyk")]
        [Tooltip("Do jaké vzdálenosti od středu terče se to počítá jako dotyk (metry). " +
                 "Vizuál má průměr 3,8 cm, takže 5 cm je mírně velkorysé — " +
                 "hodnotu je nutné odpilotovat.")]
        [SerializeField] private float touchRadius = 0.05f;

        [Header("Barvy stavů")]
        [SerializeField] private Color idleColor = new Color(0.55f, 0.56f, 0.58f, 0.45f);
        [SerializeField] private Color activeColor = new Color(0.90f, 0.13f, 0.10f, 1f);
        [SerializeField] private Color confirmedColor = new Color(0.15f, 0.80f, 0.25f, 1f);

        [Tooltip("Jak dlouho svítí zelené potvrzení (sekundy).")]
        [SerializeField] private float confirmationDuration = 0.35f;

        /// <summary>Terč byl zasažen. Parametry: terč, reakční čas v sekundách.</summary>
        public event Action<SecondaryTarget, float> Hit;

        /// <summary>Okno vypršelo bez dotyku.</summary>
        public event Action<SecondaryTarget> Missed;

        public TargetState State { get; private set; } = TargetState.Idle;

        /// <summary>Index terče v poli — jde do logu, aby se dala dohledat pozice.</summary>
        public int Index { get; set; }

        /// <summary>Vzdálenost, na kterou se dotyk počítá.</summary>
        public float TouchRadius => touchRadius;

        private Transform[] _touchers = Array.Empty<Transform>();
        private Material _material;
        private float _activationTime;
        private float _windowEnd;
        private float _confirmedUntil;
        private float _stateSince;

        private void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<MeshRenderer>();

            // Instance materiálu: každý terč mění barvu samostatně.
            if (targetRenderer != null)
            {
                _material = new Material(targetRenderer.sharedMaterial);
                targetRenderer.sharedMaterial = _material;
            }

            ApplyState(TargetState.Idle);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        /// <summary>
        /// Nastaví body, kterými se dá terč dotknout — hroty ovladačů,
        /// případně prsty. Volá SecondaryTaskManager.
        /// </summary>
        public void SetTouchers(Transform[] touchers)
        {
            _touchers = touchers ?? Array.Empty<Transform>();
        }

        private void Update()
        {
            switch (State)
            {
                case TargetState.Active:
                    if (CheckTouch()) return;

                    if (Time.realtimeSinceStartup >= _windowEnd)
                    {
                        ApplyState(TargetState.Idle);
                        if (Missed != null) Missed(this);
                    }
                    break;

                case TargetState.Confirmed:
                    if (Time.realtimeSinceStartup >= _confirmedUntil)
                        ApplyState(TargetState.Idle);
                    break;
            }
        }

        /// <summary>Aktivuje terč na dané okno odezvy.</summary>
        public void Activate(float responseWindow)
        {
            if (State != TargetState.Idle) return;

            _activationTime = Time.realtimeSinceStartup;
            _windowEnd = _activationTime + responseWindow;
            ApplyState(TargetState.Active);
        }

        /// <summary>Vynucený návrat do klidu — mezi bloky.</summary>
        public void ResetToIdle() => ApplyState(TargetState.Idle);

        /// <summary>Nejbližší dotykový bod a jeho vzdálenost. Pro ladění dosahu.</summary>
        public float GetNearestToucherDistance()
        {
            var best = float.MaxValue;
            foreach (var t in _touchers)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var d = Vector3.Distance(t.position, transform.position);
                if (d < best) best = d;
            }
            return best;
        }

        private bool CheckTouch()
        {
            foreach (var t in _touchers)
            {
                // Neaktivní modalita (odložený ovladač, vypnuté ruce) má
                // zastaralou pozici — takové body se musí přeskočit,
                // jinak by terč šel „trefit" nehybnou rukou.
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                if (Vector3.Distance(t.position, transform.position) > touchRadius) continue;

                var reactionTime = Time.realtimeSinceStartup - _activationTime;

                _confirmedUntil = Time.realtimeSinceStartup + confirmationDuration;
                ApplyState(TargetState.Confirmed);

                if (Hit != null) Hit(this, reactionTime);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Jak dlouho je terč v současném stavu. Slouží hlídači v manažeru:
        /// stav s časovým limitem drží vlastní Update terče, a ten se
        /// nevykonává, když je objekt skrytý — terč tak může v rozsvíceném
        /// stavu uvíznout a zablokovat celou úlohu.
        /// </summary>
        public float TimeInState => Time.realtimeSinceStartup - _stateSince;

        private void ApplyState(TargetState state)
        {
            State = state;
            _stateSince = Time.realtimeSinceStartup;

            if (_material == null) return;

            switch (state)
            {
                case TargetState.Idle: SetColor(idleColor); break;
                case TargetState.Active: SetColor(activeColor); break;
                case TargetState.Confirmed: SetColor(confirmedColor); break;
            }
        }

        private void SetColor(Color c)
        {
            _material.SetColor("_BaseColor", c);

            // Emission drží aktivní terč čitelný i proti světlému pozadí.
            if (State == TargetState.Idle)
            {
                _material.SetColor("_EmissionColor", Color.black);
            }
            else
            {
                _material.EnableKeyword("_EMISSION");
                _material.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * 0.6f);
            }
        }
    }
}
