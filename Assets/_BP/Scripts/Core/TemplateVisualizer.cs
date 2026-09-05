using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Jak se krok šablony zobrazuje. Bez tohoto rozlišení musí participant
    /// držet v hlavě, na kterém čísle je — a chyby pak vznikají ze zapomenutí,
    /// ne z vlastnosti testované modality.
    /// </summary>
    /// <summary>
    /// Kolik čísel kroků se u šablony zobrazuje.
    ///
    /// Výchozí je None: zvýraznění aktuálního kroku už samo říká, co je na
    /// řadě, a osm čísel u sebe se navzájem plete a překáží v obraze.
    /// Postup „3 / 8" je navíc vidět na panelu menu.
    /// </summary>
    public enum StepNumberDisplay
    {
        /// <summary>Žádná čísla — spoléhá se na zvýraznění.</summary>
        None = 0,

        /// <summary>Jen u kroku, který je právě na řadě.</summary>
        CurrentOnly = 1,

        /// <summary>Všechna čísla 1..N.</summary>
        All = 2
    }

    /// <summary>
    /// K čemu vizualizér slouží.
    ///
    /// Rozdělení předlohy a místa stavby je zásadní: dokud se stavělo PŘÍMO
    /// do šablony, překrývaly se reálné objekty s předlohou a výsledek byl
    /// nečitelný, ať se předloha kreslila jakkoli. Skica b1 to má oddělené —
    /// šablona v rámečku vlevo, stavba vedle ní.
    /// </summary>
    public enum TemplateDisplayMode
    {
        /// <summary>Předloha: celá cílová struktura, plné barvy. Nestaví se do ní.</summary>
        Reference = 0,

        /// <summary>Vodítko stavby: jen krok, který je právě na řadě, drátově.</summary>
        BuildGuide = 1
    }

    public enum StepVisualState
    {
        /// <summary>Ještě nepřišel na řadu.</summary>
        Pending = 0,

        /// <summary>Právě se staví — zvýrazněný.</summary>
        Current = 1,

        /// <summary>Hotovo — potlačený, aby nepřebíjel zbytek.</summary>
        Done = 2
    }

    /// <summary>
    /// Zobrazí cílovou šablonu zakotvenou v prostoru (skica b1) — participant
    /// ji vidí po celou dobu bloku, takže úkol netestuje paměť, ale interakci.
    ///
    /// DŮLEŽITÉ: objekty šablony se staví z meshů, NE z prefabů tvarů.
    /// Nemají collider, Rigidbody ani XRGrabInteractable — participant do nich
    /// nesmí sáhnout a omylem si je odnést. Šablona je čistě vizuál.
    /// </summary>
    public class TemplateVisualizer : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private AssemblyTemplate template;
        [SerializeField] private ShapeLibrary library;

        [Header("Zobrazení")]
        [Tooltip("Kam se staví objekty šablony. Když je prázdné, vytvoří se potomek 'Objects'.")]
        [SerializeField] private Transform container;

        [Tooltip("Kolik čísel kroků zobrazovat. None = spoléhá se na zvýraznění.")]
        [SerializeField] private StepNumberDisplay numberDisplay = StepNumberDisplay.None;

        [Tooltip("Jak daleko od objektu se číslo vykreslí (metry).")]
        [SerializeField] private float numberOffset = 0.05f;

        [SerializeField] private float numberSize = 0.03f;
        [SerializeField] private TMP_FontAsset numberFont;

        [Header("Režim")]
        [SerializeField] private TemplateDisplayMode displayMode = TemplateDisplayMode.Reference;

        [Tooltip("Zmenšení předlohy. 0.6 = model o 40 % menší než skutečnost, " +
                 "aby působil jako návod, ne jako druhá stavba.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float referenceScale = 0.6f;

        [Header("Materiály")]
        [Tooltip("Materiál se shaderem BP/WireUnlit — pro vodítko stavby.")]
        [SerializeField] private Material wireMaterial;

        [Header("Průhlednost podle stavu kroku")]
        [Tooltip("Alfa kroku, který ještě nepřišel na řadu.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float alpha = 0.40f;

        [Tooltip("Alfa aktuálního kroku. Musí být výrazně vyšší než Pending.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float alphaCurrent = 0.85f;

        [Tooltip("Alfa už postaveného kroku. Nízká, ať nepřebíjí zbytek.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float alphaDone = 0.12f;

        [Tooltip("Jak silně svítí aktuální krok.")]
        [SerializeField] private float currentEmission = 0.5f;

        [Tooltip("Zvětšení aktuálního kroku. 1 = bez zvětšení.")]
        [SerializeField] private float currentScale = 1.06f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        public AssemblyTemplate Template => template;

        /// <summary>Kotva šablony — vůči ní jsou všechny lokální pozice kroků.</summary>
        public Transform Anchor => transform;

        private void Start()
        {
            if (_spawned.Count == 0) Build();
        }

        /// <summary>
        /// Vymění šablonu a hned ji postaví znovu. Volá TrialManager na začátku
        /// bloku, protože každý blok má mít jinou (obtížnostně ekvivalentní)
        /// strukturu, aby se neprojevil efekt učení konkrétní šablony.
        /// </summary>
        public void SetTemplate(AssemblyTemplate value)
        {
            template = value;
            Build();
        }

        /// <summary>Světová cílová pozice daného kroku — používá ji validátor umístění.</summary>
        public Vector3 GetTargetPosition(int stepIndex)
        {
            var step = template.GetStep(stepIndex);
            return transform.TransformPoint(step.localPosition);
        }

        /// <summary>Světová cílová rotace daného kroku.</summary>
        public Quaternion GetTargetRotation(int stepIndex)
        {
            var step = template.GetStep(stepIndex);
            return transform.rotation * Quaternion.Euler(step.localEulerAngles);
        }

        /// <summary>
        /// Nastaví vzhled jednoho kroku. Volá se z pohledu úlohy při posunu
        /// kroku, aby participant vždy viděl, co má na řadě.
        /// </summary>
        public void SetStepState(int stepIndex, StepVisualState state)
        {
            if (stepIndex < 0 || stepIndex >= _spawned.Count) return;

            var go = _spawned[stepIndex];
            if (go == null) return;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) return;

            go.transform.localScale = Vector3.one;

            if (displayMode == TemplateDisplayMode.Reference)
            {
                // Předloha zůstává celá viditelná — je to návod. Hotové kroky
                // se nezhasínají, jen zmenšují, aby byl vidět postup a přitom
                // zůstal čitelný cíl.
                mr.enabled = true;
                go.transform.localScale = Vector3.one *
                    (state == StepVisualState.Current ? 1.12f : 1f);
                return;
            }

            // Vodítko stavby ukazuje POUZE krok, který je na řadě.
            mr.enabled = state == StepVisualState.Current;
            if (!mr.enabled) return;

            var baseColor = library.GetDisplayColor(template.GetStep(stepIndex).color);
            baseColor.a = alphaCurrent;
            mr.sharedMaterial.SetColor("_Color", baseColor);

            var label = go.transform.Find("Number");
            if (label == null) return;

            // V režimu CurrentOnly se zobrazí jen číslo kroku, který je na řadě.
            var visible = numberDisplay == StepNumberDisplay.All
                          || (numberDisplay == StepNumberDisplay.CurrentOnly
                              && state == StepVisualState.Current);

            label.gameObject.SetActive(visible);
            if (!visible) return;

            var tmp = label.GetComponent<TextMeshPro>();
            if (tmp == null) return;

            tmp.alpha = state == StepVisualState.Done ? 0.25f : 1f;
            tmp.fontStyle = state == StepVisualState.Current
                ? FontStyles.Bold | FontStyles.Underline
                : FontStyles.Bold;
        }

        /// <summary>
        /// Signalizuje, že držený objekt je dost blízko, aby zaskočil.
        /// Bez tohoto vodítka se participant dozví o úspěchu až po puštění
        /// a při nezdaru neví, jestli byl blízko nebo daleko.
        /// </summary>
        public void SetSnapAffordance(int stepIndex, bool ready)
        {
            if (stepIndex < 0 || stepIndex >= _spawned.Count) return;

            var go = _spawned[stepIndex];
            if (go == null) return;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) return;

            var c = library.GetDisplayColor(template.GetStep(stepIndex).color);
            c.a = ready ? 1f : alphaCurrent;

            mr.enabled = true;
            mr.sharedMaterial.SetColor("_Color", c);

            // V dosahu přichycení se drát mírně zvětší — signál "pusť teď".
            go.transform.localScale = Vector3.one * (ready ? 1.05f : 1f);
        }

        /// <summary>
        /// Přenastaví celou šablonu podle aktuálního kroku: nižší = hotové,
        /// aktuální = zvýrazněný, vyšší = čekající.
        /// </summary>
        public void ApplyProgress(int currentStep)
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                var state = i < currentStep ? StepVisualState.Done
                          : i == currentStep ? StepVisualState.Current
                          : StepVisualState.Pending;
                SetStepState(i, state);
            }
        }

        /// <summary>
        /// Zvýší se při každé stavbě. Pohledy tím poznají, že se materiály
        /// vyrobily znovu a je nutné znovu aplikovat stavy kroků — samotná
        /// reference na šablonu nestačí, protože při restartu bloku
        /// zůstává stejná.
        /// </summary>
        public int BuildVersion { get; private set; }

        [ContextMenu("Postavit šablonu")]
        public void Build()
        {
            Clear();
            BuildVersion++;

            if (template == null || library == null)
            {
                Debug.LogError("[TemplateVisualizer] Chybí template nebo library.", this);
                return;
            }

            if (container == null)
            {
                var go = new GameObject("Objects");
                go.transform.SetParent(transform, false);
                container = go.transform;
            }

            // Zmenšuje se POUZE kontejner, ne kořen vizualizéru. Cílové pozice
            // pro validátor se počítají z kořene, takže je zmenšení předlohy
            // nesmí ovlivnit.
            container.localScale = displayMode == TemplateDisplayMode.Reference
                ? Vector3.one * referenceScale
                : Vector3.one;

            for (var i = 0; i < template.StepCount; i++)
            {
                var step = template.GetStep(i);
                var obj = BuildStepVisual(step, i);
                if (obj != null) _spawned.Add(obj);
            }
        }

        [ContextMenu("Smazat šablonu")]
        public void Clear()
        {
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            _spawned.Clear();

            // Uklidí i to, co zbylo z předchozího běhu (např. po reloadu domény).
            if (container != null)
            {
                for (var i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i).gameObject;
                    if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
            }
        }

        private GameObject BuildStepVisual(TemplateStep step, int index)
        {
            Mesh mesh;
            if (displayMode == TemplateDisplayMode.Reference)
            {
                // Předloha je plná — nic se do ní nestaví, takže nemusí
                // prosvítat a může být čitelná jako model u stavebnice.
                var prefab = library.GetPrefab(step.shape);
                var mf = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
                mesh = mf != null ? mf.sharedMesh : null;
            }
            else
            {
                mesh = library.GetWireMesh(step.shape);
            }

            if (mesh == null)
            {
                Debug.LogError($"[TemplateVisualizer] Chybí mesh pro {step.shape} ({displayMode}).", this);
                return null;
            }

            var go = new GameObject($"T{index + 1}_{step.color}_{step.shape}");
            go.transform.SetParent(container, false);
            go.transform.localPosition = step.localPosition;
            go.transform.localRotation = Quaternion.Euler(step.localEulerAngles);
            go.transform.localScale = Vector3.one * (step.uniformScale > 0.001f ? step.uniformScale : 1f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            if (displayMode == TemplateDisplayMode.Reference)
            {
                // Předloha bere rovnou plný barevný materiál tvaru.
                mr.sharedMaterial = library.GetMaterial(step.color);
            }
            else
            {
                // Vlastní instance, aby šla nastavit barva kroku nezávisle.
                var c = library.GetDisplayColor(step.color);
                c.a = alpha;

                var mat = new Material(wireMaterial);
                mat.SetColor("_Color", c);
                mr.sharedMaterial = mat;
            }

            // Label se staví vždy, když se čísla vůbec používají — o tom,
            // které je vidět, rozhoduje až stav kroku v SetStepState.
            if (numberDisplay != StepNumberDisplay.None) BuildNumberLabel(go.transform, index);

            return go;
        }

        /// <summary>
        /// Umístí číslo tak, aby ho nezakryl jiný objekt šablony.
        /// Do strany, pokud je objekt na okraji; dopředu k participantovi,
        /// pokud je uprostřed a ve stejné výšce má sousedy (např. oktahedron
        /// mezi dvěma kuličkami — číslo do strany by skončilo za kuličkou).
        /// </summary>
        private void BuildNumberLabel(Transform parent, int index)
        {
            var step = template.GetStep(index);

            const float heightBand = 0.04f;
            var widestNeighbour = 0f;

            for (var j = 0; j < template.StepCount; j++)
            {
                if (j == index) continue;
                var other = template.GetStep(j);
                if (Mathf.Abs(other.localPosition.y - step.localPosition.y) > heightBand) continue;
                widestNeighbour = Mathf.Max(widestNeighbour, Mathf.Abs(other.localPosition.x));
            }

            var myAbsX = Mathf.Abs(step.localPosition.x);
            var blocked = widestNeighbour >= myAbsX - 0.001f && widestNeighbour > 0.001f;

            var labelGo = new GameObject("Number");
            labelGo.transform.SetParent(parent, false);

            if (blocked)
            {
                labelGo.transform.localPosition = new Vector3(0f, 0f, -numberOffset);
            }
            else
            {
                var side = step.localPosition.x < -0.001f ? -1f : 1f;
                var targetX = side * (myAbsX + numberOffset);
                labelGo.transform.localPosition = new Vector3(targetX - step.localPosition.x, 0f, 0f);
            }

            // Výchozí rotace = identita. Participant stojí na -Z a hledí na +Z,
            // takže forward textu (+Z) míří od něj a text se čte správně
            // i v edit modu, kde FaceCamera nemusí běžet.
            labelGo.transform.localRotation = Quaternion.identity;

            // TMP fontSize NENÍ v metrech. Při localScale 0.01 dává fontSize F
            // text vysoký přibližně F/1000 metru — proto se odvozuje z numberSize.
            const float labelScale = 0.01f;
            var fontSize = numberSize / labelScale * 10f;

            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = (index + 1).ToString();
            tmp.fontSize = fontSize;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;

            // Tmavé jádro + světlý obrys: čitelné na světlém stole i na
            // nepředvídatelném pozadí reálné místnosti v passthroughu.
            tmp.color = new Color32(20, 20, 24, 255);
            tmp.outlineWidth = 0.3f;
            tmp.outlineColor = new Color32(255, 255, 255, 235);

            if (numberFont != null) tmp.font = numberFont;

            // Rect se odvozuje od fontSize ve stejných lokálních jednotkách,
            // jinak TMP text odřízne.
            var rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(fontSize * 2f, fontSize * 1.5f);
            rt.localScale = Vector3.one * labelScale;

            labelGo.AddComponent<FaceCamera>();
        }
    }

    /// <summary>
    /// Otáčí objekt k hlavní kameře. Čísla u šablony musí být čitelná
    /// z jakéhokoli místa, odkud se participant dívá.
    /// ExecuteAlways: aby se orientace dala zkontrolovat i v edit modu.
    /// </summary>
    [ExecuteAlways]
    public class FaceCamera : MonoBehaviour
    {
        private Transform _cam;

        private void LateUpdate()
        {
            if (_cam == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _cam = cam.transform;
            }

            // TMP text je čitelný, když jeho forward míří OD pozorovatele —
            // obrácený směr text zrcadlově převrátí. Y se nuluje, aby se čísla
            // nezaklápěla při pohledu shora nebo zdola.
            var dir = transform.position - _cam.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
