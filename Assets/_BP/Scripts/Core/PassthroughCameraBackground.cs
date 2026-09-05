using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace BP.Core
{
    /// <summary>
    /// Nastaví pozadí kamery podle toho, jestli passthrough opravdu běží.
    ///
    /// Passthrough vyžaduje pozadí Solid Color s alfou 0 — obraz reálné
    /// místnosti se skládá ZA scénu. Jenže na zařízení, kde passthrough
    /// není (Quest Link, editor bez headsetu), by průhledné pozadí znamenalo,
    /// že se nevykreslí nic a scéna vypadá jako černá obrazovka. To se špatně
    /// odlišuje od „aplikace nenaběhla".
    ///
    /// Komponenta proto pozadí zprůhlední jen tehdy, když je kamerový
    /// subsystém skutečně dostupný; jinak nechá neprůhledné.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PassthroughCameraBackground : MonoBehaviour
    {
        [Tooltip("Pozadí, když passthrough NENÍ k dispozici (Link, editor).")]
        [SerializeField] private Color fallbackColor = new Color(0.16f, 0.17f, 0.20f, 1f);

        [Tooltip("Jak dlouho po startu se čeká, než se subsystém přihlásí (sekundy).")]
        [SerializeField] private float detectionWindow = 3f;

        private Camera _camera;
        private float _deadline;
        private bool _resolved;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;

            // Než se rozhodne, drží se neprůhledné pozadí — kdyby passthrough
            // nedorazil, uvidí se aspoň scéna, ne černo.
            _camera.backgroundColor = fallbackColor;
            _deadline = Time.time + detectionWindow;
        }

        private void Update()
        {
            if (_resolved) return;

            if (IsPassthroughRunning())
            {
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _resolved = true;
                Debug.Log("[BP] Passthrough běží — pozadí kamery průhledné.");
                return;
            }

            if (Time.time < _deadline) return;

            _resolved = true;
            Debug.Log("[BP] Passthrough není k dispozici — pozadí kamery neprůhledné. " +
                      "Na Questu přes Link je to očekávané; passthrough jde ověřit jen buildem.");
        }

        private static bool IsPassthroughRunning()
        {
            var subsystems = new List<XRCameraSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);

            foreach (var s in subsystems)
                if (s != null && s.running) return true;

            return false;
        }
    }
}
