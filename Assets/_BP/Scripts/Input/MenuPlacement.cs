using UnityEngine;

namespace BP.Input
{
    /// <summary>
    /// Určuje, kde je menu ukotvené.
    ///
    /// POZOR — tohle není jen otázka pohodlí. Když menu drží ruka, je jedna
    /// ruka po celý blok obsazená. Sekundární úloha (dotyk periferních terčů)
    /// se pak v menu podmínce stává fyzicky obtížnou nezávisle na pozornosti,
    /// takže rozdíl mezi podmínkami by nevznikl z kognitivní zátěže, ale
    /// z dostupnosti ruky. Skica b1 kreslí menu jako panel zakotvený
    /// v prostoru, což je pro měření čistší varianta.
    /// </summary>
    public enum MenuAnchorMode
    {
        /// <summary>Panel stojí na pevném místě v pracovním prostoru (skica b1).</summary>
        WorldFixed = 0,

        /// <summary>Panel je přichycený k ovladači nebo ruce (skica b4).</summary>
        HandFixed = 1
    }

    /// <summary>
    /// Umístí menu podle zvoleného režimu. Přepnutí režimu je jediná změna,
    /// kterou to vyžaduje — zbytek menu ani úlohy o tom neví.
    /// </summary>
    public class MenuPlacement : MonoBehaviour
    {
        [SerializeField] private MenuAnchorMode mode = MenuAnchorMode.WorldFixed;

        [Header("Zakotvení v prostoru")]
        [Tooltip("Kotva v pracovním prostoru. Panel se na ni umístí a naklopí k participantovi.")]
        [SerializeField] private Transform worldAnchor;

        [Header("Přichycení k ruce")]
        [Tooltip("Ovladač nebo ruka, ke které se panel přichytí.")]
        [SerializeField] private Transform handAnchor;

        [SerializeField] private Vector3 handOffset = new Vector3(0f, 0.045f, 0.02f);
        [SerializeField] private Vector3 handRotation = new Vector3(-55f, 0f, 0f);

        public MenuAnchorMode Mode => mode;

        private void Awake() => Apply();

        [ContextMenu("Použít umístění")]
        public void Apply()
        {
            switch (mode)
            {
                case MenuAnchorMode.WorldFixed:
                    if (worldAnchor == null)
                    {
                        Debug.LogError("[MenuPlacement] Režim WorldFixed, ale chybí worldAnchor.", this);
                        return;
                    }

                    // Lokální pozice se resetuje jen při skutečném přeparentování.
                    // Kdyby se nulovala vždy, zahodila by se pozice, kterou si
                    // participant nastavil úchytem během kalibrace.
                    if (transform.parent != worldAnchor)
                    {
                        transform.SetParent(worldAnchor, false);
                        transform.localPosition = Vector3.zero;
                        transform.localRotation = Quaternion.identity;
                    }
                    break;

                case MenuAnchorMode.HandFixed:
                    if (handAnchor == null)
                    {
                        Debug.LogError("[MenuPlacement] Režim HandFixed, ale chybí handAnchor.", this);
                        return;
                    }
                    transform.SetParent(handAnchor, false);
                    transform.localPosition = handOffset;
                    transform.localRotation = Quaternion.Euler(handRotation);
                    break;
            }
        }

        /// <summary>Přepnutí režimu za běhu — pro zkoušení, ne pro měření.</summary>
        public void SetMode(MenuAnchorMode value)
        {
            mode = value;
            Apply();
        }
    }
}
