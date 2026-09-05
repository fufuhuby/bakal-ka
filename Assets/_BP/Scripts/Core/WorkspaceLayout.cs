using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Drží celé pracoviště (stůl, šablonu, staging point, menu i periferní
    /// terče) jako jeden celek a umí ho přecentrovat před aktuální pozici hlavy.
    ///
    /// PROČ TO NENÍ JEN „POSUNOUT O KOUSEK": v MR je participant fyzicky tam,
    /// kde stojí v reálné místnosti, a virtuální stůl je na pevné pozici ve
    /// scéně. Každý participant si stoupne jinak a bude jinak vysoký. Bez
    /// přecentrování by jeden dosahoval pohodlně a druhý se natahoval — a
    /// délka dosahu se propisuje přímo do completion time, tedy do měřené
    /// veličiny.
    ///
    /// Výška se ZÁMĚRNĚ nemění: stůl má odpovídat reálné výšce pracovní desky,
    /// ne výšce očí.
    /// </summary>
    public class WorkspaceLayout : MonoBehaviour
    {
        [Tooltip("Hlava participanta. Obvykle Main Camera z XR Origin.")]
        [SerializeField] private Transform head;

        [Tooltip("Jak daleko před hlavou má být přední hrana pracoviště (metry). " +
                 "0.10 = deska začíná 10 cm před participantem.")]
        [SerializeField] private float forwardDistance = 0.10f;

        [Tooltip("Srovnat pracoviště podle směru pohledu. Vypnuto = zachová se " +
                 "původní orientace a mění se jen pozice.")]
        [SerializeField] private bool matchHeadYaw = true;

        [Tooltip("Přecentrovat automaticky po startu. Pro měření vypnout — " +
                 "operátor to udělá až když participant stojí, jak má.")]
        [SerializeField] private bool recenterOnStart = true;

        private Vector3 _originalPosition;
        private Quaternion _originalRotation;

        private void Awake()
        {
            _originalPosition = transform.position;
            _originalRotation = transform.rotation;

            if (head == null && Camera.main != null) head = Camera.main.transform;
        }

        private void Start()
        {
            if (recenterOnStart) RecenterToHead();
        }

        /// <summary>
        /// Přesune pracoviště tak, aby jeho počátek ležel forwardDistance
        /// před hlavou, na stejné výšce jako doteď.
        /// </summary>
        [ContextMenu("Přecentrovat na hlavu")]
        public void RecenterToHead()
        {
            if (head == null)
            {
                Debug.LogWarning("[WorkspaceLayout] Není nastavená hlava — přecentrování přeskočeno.", this);
                return;
            }

            // Vodorovný směr pohledu. Když se participant dívá dolů na stůl,
            // nesmí to pracoviště naklopit ani zvednout.
            var forward = head.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            var headGround = new Vector3(head.position.x, transform.position.y, head.position.z);

            transform.position = headGround + forward * forwardDistance;

            if (matchHeadYaw)
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>Vrátí pracoviště na pozici, kde bylo při startu scény.</summary>
        [ContextMenu("Vrátit na výchozí pozici")]
        public void ResetToOriginal()
        {
            transform.position = _originalPosition;
            transform.rotation = _originalRotation;
        }

        /// <summary>Vzdálenosti od hlavy k důležitým bodům — pro kontrolu dosahu.</summary>
        public string DescribeReach(params Transform[] points)
        {
            if (head == null) return "hlava není nastavená";

            var sb = new System.Text.StringBuilder();
            foreach (var p in points)
            {
                if (p == null) continue;
                sb.Append(p.name)
                  .Append('=')
                  .Append((Vector3.Distance(head.position, p.position) * 100f).ToString("F0"))
                  .Append(" cm  ");
            }
            return sb.ToString();
        }
    }
}
