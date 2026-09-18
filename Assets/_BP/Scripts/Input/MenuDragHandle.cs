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

        /// <summary>
        /// Je okno, ke kterému úchyt patří, vůbec na scéně?
        ///
        /// PROČ TO NESTAČÍ ŘEŠIT ZÁMKEM: okno s příkazy se mezi bloky schová,
        /// ale jeho kořen zůstává aktivní, protože si na něm visí skript, který
        /// okno staví. Odemčení mezi bloky pak lištu zase rozsvítilo a ta
        /// zůstala viset v prostoru sama, bez okna, i v klasickém bloku.
        /// </summary>
        private bool available = true;

        private XRGrabInteractable _grab;

        public bool IsLocked => locked;

        private void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            ApplyLock();
        }

        private void ZajistitGrab()
        {
            if (_grab == null) _grab = GetComponent<XRGrabInteractable>();
        }

        public void SetLocked(bool value)
        {
            locked = value;
            ApplyLock();
        }

        /// <summary>Přihlásí nebo odhlásí celý úchyt podle toho, je-li okno vidět.</summary>
        public void SetAvailable(bool value)
        {
            available = value;
            ApplyLock();
        }

        /// <summary>
        /// Přihlásí lištu, která se staví až za běhu. Okno s příkazy si ji
        /// vyrábí podle své výšky, takže ji do inspektoru zadat nejde.
        /// </summary>
        public void SetHandleVisual(GameObject visual)
        {
            handleVisual = visual;
            ApplyLock();
        }

        private void ApplyLock()
        {
            // Pořadí Awake mezi komponentami není dané: okno si lištu staví
            // ve svém Awake a může se ozvat dřív, než tenhle skript nabere
            // odkaz na grab. Proto se dohledává i tady.
            ZajistitGrab();

            var pouzitelny = available && !locked;

            if (_grab != null) _grab.enabled = pouzitelny;
            if (handleVisual != null) handleVisual.SetActive(pouzitelny);
        }
    }
}
