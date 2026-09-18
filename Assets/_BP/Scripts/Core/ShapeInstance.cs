using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Identita jednoho objektu vytvořeného participantem.
    /// Bez ní by validátor ani logger nevěděl, co se ve scéně nachází —
    /// nelze se spoléhat na název GameObjectu.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShapeInstance : MonoBehaviour
    {
        [SerializeField] private ShapeType shape;
        [SerializeField] private PaletteColor color;
        [SerializeField] private ShapeSize size = ShapeSize.M;

        /// <summary>Ke kterému kroku šablony objekt patří. -1 = objekt navíc / mimo šablonu.</summary>
        [SerializeField] private int stepIndex = -1;

        [SerializeField] private MeshRenderer meshRenderer;

        private UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask _originalLayers;
        private bool _layersCaptured;

        private void Awake()
        {
            var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null && !_layersCaptured)
            {
                _originalLayers = grab.interactionLayers;
                _layersCaptured = true;
            }
        }

        public ShapeType Shape => shape;
        public PaletteColor Color => color;
        public ShapeSize Size => size;
        public int StepIndex => stepIndex;

        /// <summary>Byl objekt už vyhodnocen jako správně umístěný?</summary>
        public bool IsConfirmed { get; private set; }

        /// <summary>Kdy byl objekt vytvořen (Time.realtimeSinceStartup) — pro čas na objekt.</summary>
        public float SpawnTime { get; private set; }

        /// <summary>
        /// Nastavení identity při spawnu. Volá výhradně factory, ne uživatelský kód.
        /// </summary>
        public void Initialize(ShapeType s, PaletteColor c, ShapeSize sz, int step,
            Material material, float spawnTime)
        {
            shape = s;
            color = c;
            size = sz;
            stepIndex = step;
            SpawnTime = spawnTime;
            IsConfirmed = false;

            // Velikost se aplikuje na objekt, ne na mesh — stejny prefab
            // tak slouzi vsem trem velikostem a nemusi existovat trikrat.
            transform.localScale = Vector3.one * ShapeSizes.Scale(sz);

            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null && material != null) meshRenderer.sharedMaterial = material;
        }

        public void MarkConfirmed() => IsConfirmed = true;

        /// <summary>
        /// Zafixuje objekt ve struktuře — přestane být uchopitelný.
        ///
        /// Bez tohoto by si participant mohl už zasazený objekt vzít zpátky
        /// a strukturu rozebrat. Do dat by se dostaly pohyby, které nepatří
        /// k žádnému kroku, a completion time by přestal odpovídat postupu.
        /// Odebrat objekt lze dál, ale jen přes STEP BACK — tedy zaznamenaně.
        /// </summary>
        public void SetFixed(bool value)
        {
            IsFixed = value;

            var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null)
            {
                // Komponenta se NEVYPÍNÁ. Její OnDisable umí objekt vrátit do
                // pozice před uchopením — objekt by po zasazení skočil zpátky
                // na staging point. Místo toho se vyřadí z interakčních vrstev:
                // XRI ji dál zpracovává, ale žádný interaktor ji nemůže chytit.
                grab.interactionLayers = value
                    ? new UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask()
                    : _originalLayers;
            }

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        /// <summary>
        /// Vynutí přesnou pozici a rotaci. Volá se po zafixování, aby výsledná
        /// poloha nezávisela na tom, co si XRI drží ve své cílové póze.
        /// </summary>
        public void ForcePose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
            }
        }

        /// <summary>Je objekt zafixovaný ve struktuře?</summary>
        public bool IsFixed { get; private set; }

        /// <summary>Odpovídá objekt zadanému tvaru a barvě?</summary>
        public bool Matches(ShapeType s, PaletteColor c) => shape == s && color == c;

        /// <summary>Shoda včetně velikosti — používá validátor v třívlastnostním bloku.</summary>
        public bool Matches(ShapeType s, PaletteColor c, ShapeSize sz)
            => shape == s && color == c && size == sz;

        public override string ToString()
            => $"{ShapeSizes.Label(size)} {color} {shape} (krok {stepIndex + 1})";
    }
}
