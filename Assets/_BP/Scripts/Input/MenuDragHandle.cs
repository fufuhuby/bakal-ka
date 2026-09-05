using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BP.Input
{
    /// <summary>
    /// Zámek posouvání menu.
    ///
    /// Grab je záměrně na KOŘENOVÉM objektu menu, ne na samostatném úchytu:
    /// úchyt jako potomek menu, který zároveň menu posouvá, je zpětná smyčka
    /// (posun menu posune i úchyt). Collider tvoří jen lišta pod panelem,
    /// takže uchopení nekoliduje s klikáním na dlaždice.
    ///
    /// ZÁMEK JE METODOLOGICKY PODSTATNÝ: pozice menu musí být během měřeného
    /// bloku pevná. Kdyby si ji participant posouval v průběhu, lišila by se
    /// vzdálenost ruky k menu a rozdíly v completion time by částečně
    /// vznikaly z geometrie, ne z modality ovládání.
    ///
    /// Zamýšlené použití: participant si menu narovná PŘED blokem (kalibrace),
    /// pak ho TrialManager zamkne.
    /// </summary>
    public class MenuDragHandle : MonoBehaviour
    {
        [Tooltip("Zamčeno = menu se nedá posunout. Během měření musí být true.")]
        [SerializeField] private bool locked;

        [Tooltip("Vizuál úchytové lišty; při zamčení se skryje, aby nemátl.")]
        [SerializeField] private GameObject handleVisual;

        private XRGrabInteractable _grab;

        public bool IsLocked => locked;

        private void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            ApplyLock();
        }

        public void SetLocked(bool value)
        {
            locked = value;
            ApplyLock();
        }

        private void ApplyLock()
        {
            if (_grab != null) _grab.enabled = !locked;
            if (handleVisual != null) handleVisual.SetActive(!locked);
        }
    }
}
