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

        [Header("Neutrální značka místa")]
        [Tooltip("Rohový rámeček místo obrysu tvaru. Používá se v bloku, kde " +
                 "je plánek na vyžádání — vodítko tam nesmí prozradit, CO se " +
                 "má postavit, jinak je plánek zbytečný.")]
        [SerializeField] private Mesh neutralMesh;

        [Tooltip("Barva značky. Neutrální, aby nenapovídala barvu objektu.")]
        [SerializeField] private Color neutralColor = new Color(0.85f, 0.86f, 0.90f, 0.85f);

        [Tooltip("Velikost značky v metrech. PEVNÁ — kdyby se řídila velikostí " +
                 "kroku, prozradila by v třívlastnostním bloku velikost.")]
        [SerializeField] private float neutralSize = 0.075f;

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

        [Header("Vzhled předlohy")]
        [Tooltip("Ztlumit hotové a čekající kroky podle stavu. Vypnuto = " +
                 "předloha vypadá pořád stejně, jako dřív.")]
        [SerializeField] private bool referenceShowsProgress = true;

        [Tooltip("Měkký stín pod předlohou. Bez něj stavba visí v prázdnu " +
                 "a v passthroughu nemá vztah k místnosti.")]
        [SerializeField] private bool referenceShadow = true;

        [Tooltip("Natočit předlohu k hlavě. Stavba je plochá, takže ze strany " +
                 "se zdegeneruje do čáry.")]
        [SerializeField] private bool referenceFaceHead = true;

        [Tooltip("Náklon předlohy ve stupních. Mírný nadhled dá plochému " +
                 "modelu prostorovost a odkryje díru torusu. 0 = zepředu.")]
        [Range(-40f, 40f)]
        [SerializeField] private float referenceTilt = 16f;

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

            var meritko = StepScale(stepIndex);
            go.transform.localScale = Vector3.one * meritko;

            // POPISEK SE ŘEŠÍ JAKO PRVNÍ, jinak by u kroků, které nejsou na
            // řadě, zůstal svítit: vykreslování tvaru se pro ně ukončuje
            // předčasně a k popisku by se řízení nedostalo.
            ApplyLabelState(go, state);

            if (displayMode == TemplateDisplayMode.Reference)
            {
                // Předloha zůstává celá viditelná — je to návod. Hotové kroky
                // se nezhasínají, jen ztlumí, aby byl vidět postup a přitom
                // zůstal čitelný cíl.
                mr.enabled = true;
                go.transform.localScale = Vector3.one * meritko *
                    (state == StepVisualState.Current ? 1.12f : 1f);

                if (referenceShowsProgress) StavPredlohy(mr, stepIndex, state);
                return;
            }

            // Vodítko stavby ukazuje POUZE krok, který je na řadě.
            mr.enabled = state == StepVisualState.Current;
            if (!mr.enabled) return;

            // Neutrální značka nesmí nést barvu objektu — prozradila by ji.
            var baseColor = _neutralGuide
                ? neutralColor
                : library.GetDisplayColor(template.GetStep(stepIndex).color);

            baseColor.a = alphaCurrent;
            mr.sharedMaterial.SetColor("_Color", baseColor);
        }

        /// <summary>
        /// Ztlumí krok předlohy podle toho, jestli je hotový, na řadě, nebo čeká.
        ///
        /// PROČ TO DŘÍV NEFUNGOVALO: předloha brala rovnou SDÍLENÝ materiál
        /// barvy — tentýž, jaký mají objekty, které participant staví. Sáhnout
        /// mu na průhlednost by přebarvilo i je. Proto se tu materiál kopíruje
        /// a kopie se přepne do průhledného režimu.
        ///
        /// ZÁPIS DO HLOUBKY ZŮSTÁVÁ ZAPNUTÝ. Bez něj by bylo skrz každý tvar
        /// vidět jeho vlastní odvrácenou stěnu a předloha by se rozpadla na
        /// změť. Takhle vyjde jako duch: tvar si drží formu a prosvítá jen
        /// pozadím.
        /// </summary>
        private void StavPredlohy(MeshRenderer mr, int stepIndex, StepVisualState state)
        {
            var barva = library.GetDisplayColor(template.GetStep(stepIndex).color);

            barva.a = state == StepVisualState.Current ? alphaCurrent
                    : state == StepVisualState.Done ? alphaDone
                    : alpha;

            var mat = mr.material;   // instance, ne sdílený materiál
            Prusvitnit(mat);
            mat.SetColor("_BaseColor", barva);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", barva);

            // Krok na řadě lehce svítí, ať se najde i koutkem oka.
            if (state == StepVisualState.Current && currentEmission > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", barva * currentEmission);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
        }

        /// <summary>Přepne URP Lit materiál do průhledného režimu.</summary>
        private static void Prusvitnit(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Měřítko, které kroku náleží podle jeho velikosti.
        ///
        /// MUSÍ SE POUŽÍT VŠUDE, kde se sahá na localScale. Metody, které
        /// mění měřítko kvůli zvýraznění, ho jinak přepíšou na jedničku
        /// a všechny tvary se srovnají na základní velikost — předloha pak
        /// ukazuje jinou velikost, než jaká se má postavit.
        /// </summary>
        private float StepScale(int stepIndex)
        {
            // Neutrální značka má PEVNOU velikost pro všechny kroky. Kdyby
            // se řídila velikostí kroku, prozradila by ji — a jde o to,
            // aby vodítko nesdělovalo nic než místo.
            if (_neutralGuide) return neutralSize;

            var step = template.GetStep(stepIndex);
            var doladeni = step.uniformScale > 0.001f ? step.uniformScale : 1f;
            return ShapeSizes.Scale(step.size) * doladeni;
        }

        /// <summary>
        /// Zapne nebo zhasne označení velikosti u jednoho kroku podle jeho stavu.
        /// </summary>
        private void ApplyLabelState(GameObject go, StepVisualState state)
        {
            var label = go.transform.Find("Number");
            if (label == null) return;

            // V režimu CurrentOnly se zobrazí jen označení kroku, který je na řadě.
            var visible = numberDisplay == StepNumberDisplay.All
                          || (numberDisplay == StepNumberDisplay.CurrentOnly
                              && state == StepVisualState.Current);

            label.gameObject.SetActive(visible);
            if (!visible) return;

            var tmp = label.GetComponent<TextMeshPro>();
            if (tmp == null) return;

            tmp.alpha = state == StepVisualState.Done ? 0.25f : 1f;

            // ŽÁDNÉ PODTRŽENÍ. Dřív označovalo krok, který je na řadě, ale
            // popisek se dnes ukazuje jen u něj — a na tmavé destičce
            // vypadala čára jako vada vykreslení, ne jako zvýraznění.
            tmp.fontStyle = FontStyles.Bold;
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
            go.transform.localScale = Vector3.one * StepScale(stepIndex) * (ready ? 1.05f : 1f);
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

        /// <summary>
        /// Kreslit místo obrysu tvaru neutrální značku? Nastavuje TrialManager
        /// podle bloku. Přestavuje vizuál, takže se šablona staví znovu.
        /// </summary>
        public bool UseNeutralGuide
        {
            get { return _neutralGuide; }
            set
            {
                if (_neutralGuide == value) return;
                _neutralGuide = value;
                if (template != null) Build();
            }
        }

        private bool _neutralGuide;

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

            if (displayMode == TemplateDisplayMode.Reference)
            {
                // POŘADÍ: nejdřív natočit, pak stín. Stín je potomek
                // kontejneru, takže kdyby vznikl dřív, natočení by ho
                // překlopilo spolu se stavbou a přestal by ležet vodorovně.
                NatocitPredlohu();
                if (referenceShadow) PostavitStin();
            }
        }

        /// <summary>
        /// Měkký stín pod předlohou.
        ///
        /// NENÍ TO OZDOBA. Model bez opory visí v prázdnu a v passthroughu,
        /// kde je za ním skutečná místnost, si ho oko nemá kam posadit.
        /// Stín je nejlevnější hloubkový signál, jaký existuje, a na rozdíl
        /// od podstavy nebo mřížky nepřidává nic, co by se dalo splést
        /// s úlohou.
        ///
        /// Sprite, ne model: Sprites/Default je v URP vždycky k dispozici,
        /// kdežto vlastní shader se do buildu nemusí dostat.
        /// </summary>
        private void PostavitStin()
        {
            var obal = new Bounds();
            var prvni = true;

            foreach (var go in _spawned)
            {
                if (go == null) continue;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                if (prvni) { obal = mr.bounds; prvni = false; }
                else obal.Encapsulate(mr.bounds);
            }

            if (prvni) return;

            var stin = new GameObject("Stin");
            stin.transform.SetParent(container, false);

            var sr = stin.AddComponent<SpriteRenderer>();
            sr.sprite = MekkyKotouc();
            sr.color = new Color(0f, 0f, 0f, 0.38f);

            // Vodorovně pod nejnižší bod stavby.
            stin.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            stin.transform.position = new Vector3(obal.center.x, obal.min.y - 0.004f, obal.center.z);

            // Sprite měří jednu jednotku; stín je o kus širší než stavba.
            var sirka = Mathf.Max(obal.size.x, obal.size.z) * 1.35f / Mathf.Max(container.lossyScale.x, 0.001f);
            stin.transform.localScale = new Vector3(sirka, sirka * 0.55f, 1f);
        }

        /// <summary>
        /// Natočí předlohu k participantovi a mírně ji nakloní.
        ///
        /// OTÁČÍ SE KONTEJNER, NE KOŘEN. Cílové pozice kroků se počítají
        /// z kořene, takže kdyby se otočil ten, rozešly by se s tím, co
        /// validátor očekává.
        ///
        /// NÁKLON: všechny šablony jsou dokonale ploché (z = 0 u každého
        /// kroku), takže čelní pohled je vlastně dvojrozměrný a ze strany
        /// se stavba zdegeneruje do čáry. Mírný nadhled vrátí modelu
        /// prostorovost a zároveň odkryje díru torusu, která je zepředu
        /// neviditelná.
        /// </summary>
        private void NatocitPredlohu()
        {
            var naklon = Quaternion.Euler(referenceTilt, 0f, 0f);

            if (!referenceFaceHead || Camera.main == null)
            {
                container.localRotation = naklon;
                return;
            }

            var smer = container.position - Camera.main.transform.position;
            smer.y = 0f;

            if (smer.sqrMagnitude < 0.0001f) { container.localRotation = naklon; return; }

            container.rotation = Quaternion.LookRotation(smer, Vector3.up) * naklon;
        }

        /// <summary>Kruhová skvrna, která na krajích měkce vyhasne.</summary>
        private static Sprite MekkyKotouc()
        {
            if (_kotouc != null) return _kotouc;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var px = new Color32[size * size];
            var stred = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x - stred, y - stred).magnitude / stred;

                // Druhá mocnina dá měkký přechod bez ostrého okraje.
                var a = Mathf.Clamp01(1f - d);
                a *= a;

                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();

            _kotouc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _kotouc.hideFlags = HideFlags.HideAndDontSave;
            return _kotouc;
        }

        private static Sprite _kotouc;

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
            else if (_neutralGuide)
            {
                // Neutrální značka: sděluje KAM, ne CO. Bez toho by vodítko
                // prozradilo tvar i barvu a plánek na vyžádání by nemel smysl.
                mesh = neutralMesh;
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

            var go = new GameObject(_neutralGuide
                ? $"T{index + 1}_Znacka"
                : $"T{index + 1}_{step.color}_{step.shape}");

            go.transform.SetParent(container, false);
            go.transform.localPosition = step.localPosition;
            go.transform.localRotation = Quaternion.Euler(step.localEulerAngles);

            go.transform.localScale = Vector3.one * StepScale(index);

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

            // Popisek se staví jen u šablony, která velikosti řeší — jinde
            // by neměl co říct. O tom, které popisky jsou vidět, rozhoduje
            // až stav kroku v SetStepState.
            if (template.usesSizes && numberDisplay != StepNumberDisplay.None)
                BuildNumberLabel(go.transform, index);

            return go;
        }

        /// <summary>
        /// Umístí označení velikosti nalevo od tvaru, ke kterému patří.
        ///
        /// Odsazení se odvozuje od skutečné velikosti tvaru, ne od jeho místa
        /// v šabloně — u XL tvaru by pevné odsazení skončilo na něm. A protože
        /// je popisek potomkem tvaru, dělí se měřítkem: jinak by se s tvarem
        /// zvětšovalo i odsazení a písmo, a označení u XL by bylo dvakrát
        /// větší než u S.
        /// </summary>
        /// <summary>
        /// Tmavá destička za písmeno velikosti.
        ///
        /// Je potomkem popisku, takže se s ním natáčí k hlavě i škáluje —
        /// nic se nemusí dopočítávat zvlášť.
        ///
        /// Sprite místo modelu: Sprites/Default je v URP vždycky k dispozici,
        /// kdežto Unlit/Color se do buildu nemusí dostat a destička by
        /// v brýlích vyšla růžová.
        ///
        /// Zaoblení si bere ze stejného zdroje jako okna a dlaždice menu,
        /// aby odznak u plánku nevypadal jako z jiné aplikace.
        /// </summary>
        private void PostavitDesticku(Transform popisek, float velikostPisma)
        {
            var go = new GameObject("Plate");
            go.transform.SetParent(popisek, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PanelStyle.RoundedSquare();
            sr.color = new Color(0.04f, 0.045f, 0.06f, 0.96f);

            // Kreslit PŘED textem, jinak by ho destička překryla. Oba leží
            // skoro v jedné rovině, takže o pořadí nerozhoduje hloubka.
            sr.sortingOrder = -1;

            // ROZMĚR SE ODVOZUJE Z VELIKOSTI PÍSMA, ne z naměřených hranic
            // textu. V okamžiku stavby je krok ještě neaktivní, TMP nemá
            // vygenerovanou sazbu a textBounds vyjde nulový — destička by
            // pak vůbec nevznikla. Popisek je vždycky jedno písmeno, takže
            // pevný poměr sedí.
            //
            // POZOR NA JEDNOTKY: sprite měří jednu jednotku rodiče, kdežto
            // fontSize je v jednotkách TMP, kde glyf vysoký F zabírá zhruba
            // F/10. Bez toho dělení vyšla destička desetkrát větší než celá
            // stavba.
            // ČTVEREC, ne obdélník: odznak nese vždycky jedno písmeno a
            // čtverec se čte jako značka, kdežto obdélník jako useknutý štítek.
            var jednotka = velikostPisma * 0.1f;
            var strana = jednotka * 1.35f;
            go.transform.localScale = new Vector3(strana, strana, 1f);

            // Destička sedí přesně na transformu popisku. Text je zarovnaný
            // na střed rámečku, takže tam sedí i glyf — svislý posun tady
            // kompenzoval podtržení, které se mezitím zrušilo, a bez něj by
            // písmeno sedělo mimo střed.
            go.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        }

        /// <summary>
        /// Stupnice velikosti na destičku — tři čtverečky, zvýrazněný ten,
        /// o kterou velikost jde.
        ///
        /// PROČ NE PÍSMENO: odznak S/M/L se dá přečíst jen tak, že si ho člověk
        /// přeloží na slovo, a „M“ se plete s medium. V hlasové podmínce se za
        /// ten překlad platí časem, v klasické ne — rozdíl by se pak schoval
        /// do naměřené ceny hlasu. Stupnice ukáže velikost rovnou a je to
        /// TÝŽ obrázek jako na dlaždici v inventáři, takže si obě podmínky
        /// spojují slovo se stejnou kresbou.
        ///
        /// Sprite je čtvercový s průhledným okrajem, proto se škáluje podle
        /// vlastních hranic, ne podle počtu pixelů textury.
        /// </summary>
        private void PostavitStupnici(Transform popisek, float velikostPisma, Sprite ikona)
        {
            var go = new GameObject("Scale");
            go.transform.SetParent(popisek, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ikona;

            // Bílá na tmavé destičce, stejně jako dřív písmeno. Barva by
            // konkurovala paletě objektů, kde barva už nese význam.
            sr.color = Color.white;

            // Nad destičkou, která kreslí na -1.
            sr.sortingOrder = 0;

            var strana = velikostPisma * 0.1f * 1.35f * 0.86f;
            var vlastni = Mathf.Max(ikona.bounds.size.x, 0.0001f);
            go.transform.localScale = Vector3.one * (strana / vlastni);

            // Na transformu popisku, tedy PŘED destičkou: ta sedí na +Z, což
            // je směr OD participanta (popisek se k němu natáčí přes FaceCamera).
            go.transform.localPosition = Vector3.zero;
        }

        /// <summary>
        /// Na kterou stranu tvaru patří odznak: -1 vlevo, +1 vpravo.
        ///
        /// PROČ TO NENÍ NATVRDO DOLEVA: u posledního kroku je vlevo celá
        /// postavená věž a odznak by v ní zmizel. Přesně to se stalo
        /// u pravého ramene.
        ///
        /// PŘEKÁŽEJÍ JEN UŽ POSTAVENÉ KROKY, tedy ty s nižším pořadím.
        /// Kroky, které teprve přijdou, ve scéně nejsou — brát je jako
        /// překážku by odznak uhýbal před něčím, co ještě neexistuje.
        ///
        /// Výška se bere přibližně z poloviny hrany; u placatého torusu
        /// vyjde větší, než je, což jen zpřísní rozhodování.
        /// </summary>
        private float StranaOdznaku(int index)
        {
            var step = template.GetStep(index);
            var mojeY = step.localPosition.y;
            var mojePolovina = 0.030f * StepScale(index);

            // STŘED destičky na každé straně, ne její kraj — jinak by test
            // vycházel o půl destičky vedle a strana by se vybírala špatně.
            // Šířka destičky je ve světě stálá, protože se popisek dělí
            // měřítkem svého kroku.
            const float polovinaOdznaku = 0.030f;
            var odsazeni = mojePolovina + numberOffset;

            var vlevo = step.localPosition.x - odsazeni;
            var vpravo = step.localPosition.x + odsazeni;

            // Tolerance proti přeskakování kvůli ničemu. Dva tvary nad sebou
            // se svými obálkami dotýkají o zlomek milimetru a bez tolerance
            // by odznak kvůli takovému doteku uhnul na druhou stranu —
            // střídavé strany se čtou hůř než pár milimetrů překryvu.
            const float tolerance = 0.010f;

            var vlevoVolno = true;
            var vpravoVolno = true;

            for (var j = 0; j < index; j++)
            {
                var jiny = template.GetStep(j);
                var jehoMeritko = jiny.uniformScale > 0.001f ? jiny.uniformScale : 1f;
                var jehoPolovina = 0.030f * ShapeSizes.Scale(jiny.size) * jehoMeritko;

                if (Mathf.Abs(jiny.localPosition.y - mojeY)
                    >= mojePolovina + jehoPolovina - tolerance) continue;

                var dosah = jehoPolovina + polovinaOdznaku - tolerance;

                if (Mathf.Abs(jiny.localPosition.x - vlevo) < dosah) vlevoVolno = false;
                if (Mathf.Abs(jiny.localPosition.x - vpravo) < dosah) vpravoVolno = false;
            }

            // Vlevo je výchozí strana; doprava se jde, jen když je vlevo
            // obsazeno. Když je obsazeno obojí, zůstává vlevo — aspoň to
            // drží stejnou stranu jako u ostatních kroků.
            return !vlevoVolno && vpravoVolno ? 1f : -1f;
        }

        private void BuildNumberLabel(Transform parent, int index)
        {
            var step = template.GetStep(index);
            var meritko = StepScale(index);
            var stupnice = library != null ? library.GetSizeIcon(step.size) : null;

            var labelGo = new GameObject("Number");
            labelGo.transform.SetParent(parent, false);

            // Odznak sedí hned vedle SVÉHO tvaru, ne ve společném sloupci
            // u kraje stavby.
            //
            // Sloupec by měl smysl, kdyby byla označení vidět všechna naráz —
            // pak by se u různě širokých tvarů klikatila. Průvodce stavbou
            // ale ukazuje vždycky jen krok, který je na řadě, takže společný
            // sloupec odznak jen zbytečně odsouval pryč od tvaru, ke
            // kterému patří.
            //
            // STRANA SE VYBÍRÁ PODLE VOLNÉHO MÍSTA, ne natvrdo doleva.
            // U pravého ramene by odznak nalevo padl doprostřed komínu a
            // zmizel by v něm. Vlevo se dává, kdykoli je tam volno; jinak
            // doprava.
            //
            // Odsazení se dělí měřítkem kroku: popisek je jeho potomek, takže
            // by se jinak u velkého tvaru roztáhlo spolu s ním.
            labelGo.transform.localPosition = new Vector3(
                StranaOdznaku(index) * (0.030f + numberOffset / Mathf.Max(meritko, 0.01f)),
                0f, 0f);

            // Výchozí rotace = identita. Participant stojí na -Z a hledí na +Z,
            // takže forward textu (+Z) míří od něj a text se čte správně
            // i v edit modu, kde FaceCamera nemusí běžet.
            labelGo.transform.localRotation = Quaternion.identity;

            // TMP fontSize NENÍ v metrech. Při localScale 0.01 dává fontSize F
            // text vysoký přibližně F/1000 metru — proto se odvozuje z numberSize.
            const float labelScale = 0.01f;
            var fontSize = numberSize / labelScale * 10f;

            var tmp = labelGo.AddComponent<TextMeshPro>();

            // FONT SE PRIRAZUJE JAKO PRVNI. Vlastnosti obrysu zapisuji do
            // materialu fontu, a dokud font neni prirazeny, material
            // neexistuje — nastaveni outlineWidth pak spadne na
            // NullReferenceException uvnitr TMP. Chyba byla dlouho neviditelna,
            // protoze se cisla kroku vubec nezobrazovala.
            tmp.font = numberFont != null ? numberFont : TMP_Settings.defaultFontAsset;

            // Popisek nese POUZE velikost. Cislo kroku se neukazuje —
            // poradi stavby je dane a vypisovat ho k tvarum jen zahlcuje
            // predlohu. Bez oznaceni velikosti by ji ale participant musel
            // odhadovat z pomeru k sousednim tvarum, a chyba by vznikala
            // z necitelnosti predlohy, ne z modality ovladani.
            // SVĚTLÉ PÍSMO NA TMAVÉM ODZNAKU, ne holý text. V passthroughu je
            // pozadím skutečná místnost — jakákoli barva písma se na něčem
            // ztratí. Odznak si nese vlastní podklad, takže čitelnost
            // nezávisí na tom, co má participant za stavbou.
            //
            // PODKLAD JE VLASTNÍ DESTIČKA, ne značka <mark> v TMP. Mark se
            // nedrží zadané barvy ani šířky — vyšel z něj šedý podklad místo
            // černého, písmeno mimo střed a pod destičkou světlý proužek.
            // Vlastní sprite má přesně tu velikost a barvu, která se mu zadá.
            //
            // PÍSMENO JE ZÁLOHA, ne první volba — viz PostavitStupnici.
            tmp.text = stupnice != null ? string.Empty : ShapeSizes.Label(step.size);
            tmp.fontSize = fontSize;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            // ZAROVNANI NA STRED, ne doleva. Rámeček TMP je široký (násobek
            // fontSize) a při zarovnání doleva začínají glyfy u jeho LEVÉ
            // hrany — tedy o půl šířky rámečku vlevo od transformu. Při
            // měřítku 0,01 to dělalo skoro metr: transform seděl napravo od
            // stavby, ale nápis se kreslil někde vedle cedule. Na střed
            // zarovnané glyfy sedí na transformu bez ohledu na šířku rámečku.
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            // Bílá na tmavé destičce. Barevné písmo by si konkurovalo
            // s paletou objektů — sedm barev už význam nese a osmá by
            // mátla, protože označení velikosti žádnou barvu neoznačuje.
            tmp.color = new Color32(255, 255, 255, 255);

            // Obrys jen kdyz material existuje. Bez fontu by to shodilo
            // spusteni bloku, a to se projevi jako "START nic nedela".
            if (tmp.fontSharedMaterial != null)
            {
                // Jen náznak. Silný obrys by bílé písmeno na tmavé destičce
                // zbytečně ztenčil — kontrast obstarává destička.
                tmp.outlineWidth = 0.08f;
                tmp.outlineColor = new Color32(0, 0, 0, 255);
            }
            else
            {
                Debug.LogWarning("[TemplateVisualizer] Popisek kroku nemá font — " +
                                 "obrys přeskočen. Přiřaď numberFont.", this);
            }

            // Rect se odvozuje od fontSize ve stejných lokálních jednotkách,
            // jinak TMP text odřízne. Odznak s mezerami je širší než samotné
            // písmeno, proto trojnásobek místo dvojnásobku.
            var rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(fontSize * 3f, fontSize * 1.6f);

            // Deleno meritkem tvaru, aby byla vsechna oznaceni stejne velka.
            rt.localScale = Vector3.one * labelScale / Mathf.Max(meritko, 0.01f);

            PostavitDesticku(labelGo.transform, fontSize);
            if (stupnice != null) PostavitStupnici(labelGo.transform, fontSize, stupnice);

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
