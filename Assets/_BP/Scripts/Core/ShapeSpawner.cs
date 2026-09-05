using System;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Vytváří objekty na jednom pevném místě (staging point).
    ///
    /// PROČ PEVNÉ MÍSTO: kdyby se objekt objevoval různě daleko od cílové
    /// pozice, měnila by se délka manuálního přenesení mezi kroky i mezi
    /// podmínkami. Rozdíl v completion time by pak nešel přičíst modalitě.
    /// Konstantní staging point drží manuální část úlohy identickou.
    /// </summary>
    public class ShapeSpawner : MonoBehaviour
    {
        [SerializeField] private ShapeLibrary library;

        [Tooltip("Pevné místo, kde se objekt zjeví. Stejné pro obě podmínky.")]
        [SerializeField] private Transform stagingPoint;

        [Tooltip("Rodič pro vytvořené objekty. Drží scénu přehlednou.")]
        [SerializeField] private Transform container;

        /// <summary>Vytvořen nový objekt.</summary>
        public event Action<ShapeInstance> Spawned;

        public Transform StagingPoint => stagingPoint;

        private void Awake()
        {
            if (stagingPoint == null)
            {
                Debug.LogError("[ShapeSpawner] Není nastaven stagingPoint.", this);
                return;
            }

            if (container == null)
            {
                var go = new GameObject("SpawnedShapes");
                go.transform.SetParent(transform, false);
                container = go.transform;
            }

            string problem;
            if (!library.Validate(out problem))
                Debug.LogError($"[ShapeSpawner] ShapeLibrary není kompletní: {problem}", this);
        }

        /// <summary>
        /// Vytvoří objekt daného tvaru a barvy na staging pointu.
        /// stepIndex je krok šablony, ke kterému se objekt hlásí (-1 = mimo šablonu).
        /// </summary>
        public ShapeInstance Spawn(ShapeType shape, PaletteColor color, int stepIndex)
        {
            var prefab = library.GetPrefab(shape);
            if (prefab == null) return null;

            var go = Instantiate(prefab, stagingPoint.position, stagingPoint.rotation, container);
            go.name = $"{color}_{shape}";

            var instance = go.GetComponent<ShapeInstance>();
            if (instance == null)
            {
                Debug.LogError($"[ShapeSpawner] Prefab {shape} nemá ShapeInstance.", this);
                return null;
            }

            instance.Initialize(shape, color, stepIndex, library.GetMaterial(color), Time.realtimeSinceStartup);

            if (Spawned != null) Spawned(instance);
            return instance;
        }

        /// <summary>Odstraní objekt ze scény (STEP BACK / "step back").</summary>
        public void Remove(ShapeInstance instance)
        {
            if (instance == null) return;
            Destroy(instance.gameObject);
        }

        /// <summary>
        /// Vyčistí všechny vytvořené objekty. Volá TrialManager mezi bloky —
        /// bez toho by do dalšího bloku zůstala struktura z předchozího.
        /// </summary>
        public void RemoveAll()
        {
            if (container == null) return;

            for (var i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }
}
