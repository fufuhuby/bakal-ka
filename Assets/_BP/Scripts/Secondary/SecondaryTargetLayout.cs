using System.Collections;
using UnityEngine;

namespace BP.Secondary
{
    /// <summary>
    /// Rozmístí periferní terče na kužel kolem směru pohledu na primární úlohu.
    ///
    /// PROČ NE RUČNĚ ROZHÁZENÉ POZICE: excentricita terče (úhel od místa, kam
    /// se participant dívá) přímo určuje, jak snadné je ho zaregistrovat
    /// periferním viděním. Když má každý terč jinou excentricitu, míchá se
    /// do reakčního času proměnná, kterou jsme nezamýšleli měřit — a průměr
    /// přes terče pak závisí na tom, který terč generátor zrovna vylosoval.
    /// Na kuželu mají všechny terče excentricitu STEJNOU a liší se jen směrem,
    /// takže je pořadí aktivací vůči reakčnímu času neutrální.
    ///
    /// DRUHÝ DŮVOD: ruční rozmístění snadno skončí terčem za zády. Terč, který
    /// není vidět, se nedá minout z nedostatku kapacity — mine se vždy, a hit
    /// rate tím dostane konstantní posun, který nemá nic společného se zátěží.
    /// Kužel s excentricitou pod 90° tuhle možnost vylučuje konstrukčně.
    /// </summary>
    public class SecondaryTargetLayout : MonoBehaviour
    {
        [Header("Vztažné body")]
        [Tooltip("Hlava participanta. Prázdné = Main Camera.")]
        [SerializeField] private Transform head;

        [Tooltip("Místo primární úlohy — stavěcí kotva. Kolem směru hlava→sem " +
                 "se prstenec rozloží.")]
        [SerializeField] private Transform primaryFocus;

        [Header("Geometrie")]
        [Tooltip("Úhel terče od primární úlohy ve stupních. Menší = terče blíž " +
                 "u stavby (snazší), větší = dál v periferii (těžší). " +
                 "NUTNÉ ODPILOTOVAT: cíl je ~90 % zásahů v single-task bloku.")]
        [Range(10f, 70f)]
        [SerializeField] private float eccentricity = 32f;

        [Tooltip("Vzdálenost terče od hlavy v metrech. Musí být v dosahu paže.")]
        [SerializeField] private float radius = 0.55f;

        [Tooltip("Pootočení prstence ve stupních. Posunuje terče tak, aby žádný " +
                 "neležel přesně dole v klíně ani přesně nad stavbou.")]
        [SerializeField] private float ringPhase = 25.7f;

        [Tooltip("Natočit terče čelem k hlavě. Terč viděný z boku má menší " +
                 "průmět a byl by tím pádem hůř postřehnutelný než ostatní.")]
        [SerializeField] private bool faceHead = true;

        [Header("Kontrola")]
        [Tooltip("Rozmístit hned po startu. Při měření až po přecentrování " +
                 "pracoviště, kdy už participant stojí, jak má.")]
        [SerializeField] private bool applyOnStart = true;

        private void Start()
        {
            if (applyOnStart && Application.isPlaying) StartCoroutine(ApplyNextFrame());
        }

        /// <summary>
        /// Rozmístit až o snímek později. Přecentrování pracoviště probíhá
        /// taky ve Start() a pořadí Start() metod není definované — kdyby se
        /// terče rozložily dřív, přecentrování by je odtáhlo i s pracovištěm
        /// a excentricita by seděla na starou pozici hlavy.
        /// </summary>
        private IEnumerator ApplyNextFrame()
        {
            yield return null;
            Apply();
        }

        /// <summary>
        /// Rozmístí přímé potomky (Target_0…N) na kužel. Volá se po
        /// přecentrování pracoviště, aby excentricita seděla na skutečnou
        /// výšku očí participanta, ne na tu, se kterou se scéna stavěla.
        /// </summary>
        [ContextMenu("Rozmístit terče")]
        public void Apply()
        {
            var h = ResolveHead();
            if (h == null)
            {
                Debug.LogWarning("[SecondaryTargetLayout] Není hlava — rozmístění přeskočeno.", this);
                return;
            }

            if (primaryFocus == null)
            {
                Debug.LogWarning("[SecondaryTargetLayout] Není primaryFocus — rozmístění přeskočeno.", this);
                return;
            }

            var count = transform.childCount;
            if (count == 0) return;

            var origin = h.position;

            var axis = primaryFocus.position - origin;
            if (axis.sqrMagnitude < 0.0001f) return;
            axis.Normalize();

            // Kolmá základna kužele. Vodorovná složka se bere od svislice,
            // aby se prstenec nepřevracel podle náklonu hlavy.
            var right = Vector3.Cross(Vector3.up, axis);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            var up = Vector3.Cross(axis, right);

            var ecc = eccentricity * Mathf.Deg2Rad;
            var cosE = Mathf.Cos(ecc);
            var sinE = Mathf.Sin(ecc);

            for (var i = 0; i < count; i++)
            {
                var child = transform.GetChild(i);

                var ring = (ringPhase + i * 360f / count) * Mathf.Deg2Rad;
                var side = right * Mathf.Cos(ring) + up * Mathf.Sin(ring);
                var dir = axis * cosE + side * sinE;

                child.position = origin + dir * radius;

                if (faceHead) child.rotation = Quaternion.LookRotation(origin - child.position, Vector3.up);

                NormalizeBacking(child);
            }
        }

        /// <summary>
        /// Srovná podložku terče do roviny kolmé na pohled. Bez toho by si
        /// podložka nesla natočení z místa, kde terč stál předtím.
        /// </summary>
        private static void NormalizeBacking(Transform target)
        {
            var backing = target.Find("Backing");
            if (backing == null) return;

            // Válec má osu v Y; otočením o 90° kolem X se postaví čelem
            // ke směru pohledu. Terč míří +Z k hlavě, takže podložka jde
            // na záporné Z — jinak by kuličku zepředu zakryla.
            backing.localPosition = new Vector3(0f, 0f, -0.022f);
            backing.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private Transform ResolveHead()
        {
            if (head != null) return head;
            return Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>
        /// Kontrola rozmístění: úhel od primární úlohy, úhel od směru pohledu
        /// a vzdálenost od hlavy pro každý terč.
        /// </summary>
        public string Describe()
        {
            var h = ResolveHead();
            if (h == null || primaryFocus == null) return "chybí hlava nebo primaryFocus";

            var axis = (primaryFocus.position - h.position).normalized;
            var sb = new System.Text.StringBuilder();

            for (var i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                var d = c.position - h.position;

                sb.Append(c.name)
                  .Append(": od ulohy ").Append(Vector3.Angle(axis, d).ToString("F0")).Append("st")
                  .Append(", od pohledu ").Append(Vector3.Angle(h.forward, d).ToString("F0")).Append("st")
                  .Append(", dosah ").Append((d.magnitude * 100f).ToString("F0")).Append("cm")
                  .AppendLine();
            }
            return sb.ToString();
        }
    }
}
