using BP.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Input
{
    /// <summary>
    /// Okno s hlasovými příkazy. V hlasové podmínce stojí na místě, kde je
    /// v klasické inventář.
    ///
    /// PROČ TAM VŮBEC JE: v menu jsou všechny volby vidět — participant je
    /// nemusí znát, jen je najde. U hlasu by bez nápovědy musel slovník držet
    /// v paměti, a rozdíl mezi podmínkami by pak zčásti měřil zapamatování
    /// slovíček, ne cenu modality. Okno tu paměťovou nerovnost srovnává.
    ///
    /// NENÍ TO ALE TOTÉŽ CO MENU. Z inventáře se objekt vytvoří klepnutím;
    /// tohle okno se jen čte a nic se z něj ovládat nedá. Rozdíl mezi
    /// podmínkami tedy zůstává v tom, ČÍM se objekt vyžádá.
    ///
    /// STEJNÉ IKONY A BARVY JAKO INVENTÁŘ, schválně. Kdyby okno ukazovalo
    /// tvary jinak než menu, musel by si participant v hlasové podmínce
    /// spojit slovo s jiným obrázkem než v klasické — a do rozdílu mezi
    /// podmínkami by se přimíchalo, jak snadno se to spojení udělá.
    ///
    /// NEZAVÍRÁ SE ŘEČÍ. Povel na schování okna zabíral místo ve slovníku,
    /// který si má participant pamatovat, a přitom řešil jen to, že okno
    /// překáží. Od toho je teď lišta pod oknem: okno se odtáhne stranou
    /// rukou, stejně jako inventář v klasické podmínce.
    /// </summary>
    public class VoiceHelpPanel : MonoBehaviour
    {
        [Header("Zdroj tvarů a barev")]
        [Tooltip("Tatáž knihovna, ze které bere ikony inventář.")]
        [SerializeField] private ShapeLibrary library;

        [Header("Vzhled")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float width = 660f;
        [SerializeField] private float canvasScale = 0.00052f;

        [Tooltip("Natočit okno k participantovi. Bere polohu hlavy při stavbě.")]
        [SerializeField] private bool faceHead = true;

        [Header("Úchytová lišta")]
        [Tooltip("Materiál lišty. Tentýž, jaký má lišta pod inventářem.")]
        [SerializeField] private Material gripMaterial;

        [SerializeField] private float gripWidth = 0.060f;
        [SerializeField] private float gripHeight = 0.010f;
        [SerializeField] private float gripDepth = 0.009f;

        [Tooltip("Mezera mezi spodní hranou okna a lištou.")]
        [SerializeField] private float gripGap = 0.010f;

        private const string CanvasName = "VoiceHelpCanvas";
        private const string GripName = "GripBar";

        private const float Pad = 28f;
        private const float TitleH = 32f;
        private const float LeadH = 30f;
        private const float SectionGap = 20f;
        private const float SectionTitleH = 24f;

        /// <summary>Dlaždice tvaru: ikona nahoře, název pod ní.</summary>
        private const float TileW = 84f;
        private const float TileH = 92f;
        private const float TileGap = 8f;

        /// <summary>Řádek barvy: kolečko a název vedle sebe.</summary>
        private const float SwatchW = 150f;
        private const float SwatchH = 34f;

        private TextMeshProUGUI _lead;
        private RectTransform _canvas;

        /// <summary>Je okno právě vidět?</summary>
        public bool IsVisible { get; private set; }

        private bool _usesSizes;
        private bool _planOnDemand;
        private Transform _grip;

        private void Awake()
        {
            // Okno se staví VŽDY znovu, i když už ve scéně je. Skládá se
            // z desítek dlaždic podle knihovny tvarů; kdyby se jen navázalo
            // na to, co zbylo ve scéně, projevila by se změna knihovny až
            // po ručním přestavění.
            Build();
            SetVisible(false);
        }

        /// <summary>
        /// Připraví obsah podle bloku. Slovník se liší: blok bez velikostí
        /// je nemá nabízet, jinak by je participant zkoušel říkat a zbytečně
        /// by se mu odmítaly.
        /// </summary>
        public void Configure(bool usesSizes, bool planOnDemand)
        {
            _usesSizes = usesSizes;
            _planOnDemand = planOnDemand;
            Build();
        }

        public void SetVisible(bool value)
        {
            IsVisible = value;

            // Pracuje se s ULOŽENÝM odkazem, ne s hledáním podle jména.
            // Build() starý canvas ruší přes Destroy, které v play módu
            // odkládá zánik na konec snímku — Find by ještě našel ten mrtvý
            // a schoval ho místo nového. Okno pak zůstalo viset přes
            // inventář v klasické podmínce.
            if (_canvas != null) _canvas.gameObject.SetActive(value);

            // Lišta mizí s oknem. Zůstat viset sama by znamenalo nabízet
            // úchyt k něčemu, co není vidět.
            if (_grip != null) _grip.gameObject.SetActive(value);

            var box = GetComponent<BoxCollider>();
            if (box != null) box.enabled = value;

            // Zámek má poslední slovo: odemyká se mezi bloky, a kdyby o skrytém
            // okně nevěděl, lištu by zase rozsvítil.
            var zamek = GetComponent<MenuDragHandle>();
            if (zamek != null) zamek.SetAvailable(value);
        }

        // ---- Stavba ----

        [ContextMenu("Postavit okno")]
        public void Build()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (c.name != CanvasName && c.name != GripName) continue;

                // Přejmenovat dřív, než se zruší: v play módu zánik nastane
                // až na konci snímku a do té doby by se na staré jméno dalo
                // omylem sáhnout.
                c.name += "_ruseny";
                c.SetActive(false);

                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }

            var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            _canvas = (RectTransform)go.transform;
            _canvas.localScale = Vector3.one * canvasScale;
            _canvas.localPosition = Vector3.zero;
            _canvas.localRotation = Quaternion.identity;

            var pozadi = Novy("Pozadi", _canvas, Vector2.zero, Vector2.one);
            PanelStyle.ApplyRounded(pozadi.AddComponent<Image>(), 44f, PanelStyle.Window);

            var vnitrek = width - Pad * 2f;
            var y = 0f;   // posun odshora, roste dolů

            // ---- Hlavička ----
            var nadpis = Text(Novy("Nadpis", _canvas, Vector2.zero, new Vector2(vnitrek, TitleH)),
                26f, TextAlignmentOptions.Left, PanelStyle.Title);
            nadpis.text = "HLASOVÉ PŘÍKAZY";
            nadpis.fontStyle = FontStyles.Bold;
            Posadit(nadpis.rectTransform, ref y, TitleH);

            _lead = Text(Novy("Uvod", _canvas, Vector2.zero, new Vector2(vnitrek, LeadH)),
                19f, TextAlignmentOptions.Left, PanelStyle.TextSecondary);
            _lead.text = _usesSizes
                ? "Řekni <b>velikost, barvu a tvar</b>, třeba „velká červená kostka“."
                : "Řekni <b>barvu a tvar</b>, třeba „červená kostka“.";
            Posadit(_lead.rectTransform, ref y, LeadH);

            y += SectionGap;

            // ---- Tvary ----
            y = Sekce("TVARY", y, vnitrek);
            y = Tvary(y, vnitrek);

            y += SectionGap;

            // ---- Barvy ----
            y = Sekce("BARVY", y, vnitrek);
            y = Barvy(y, vnitrek);

            // ---- Velikosti ----
            if (_usesSizes)
            {
                y += SectionGap;
                y = Sekce("VELIKOSTI", y, vnitrek);
                y = Velikosti(y, vnitrek);
            }

            // ---- Povely ----
            y += SectionGap;
            y = Sekce("DALŠÍ POVELY", y, vnitrek);
            y = Povely(y, vnitrek);

            var celkovaVyska = Dokoncit(y);
            PostavitListu(celkovaVyska);

            transform.localRotation = Quaternion.identity;

            if (faceHead && Camera.main != null)
            {
                var smer = transform.position - Camera.main.transform.position;
                if (smer.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(smer, Vector3.up);
            }
        }

        /// <summary>
        /// Skládání jde SHORA DOLŮ v kladných číslech a teprve nakonec se
        /// všechno posune podle skutečné výšky. Kdyby se počítalo od středu
        /// canvasu rovnou, musela by se výška okna znát dopředu — a ta závisí
        /// na tom, kolik sekcí blok má.
        /// </summary>
        private void Posadit(RectTransform rt, ref float y, float vyska)
        {
            rt.anchoredPosition = new Vector2(0f, -(y + vyska * 0.5f));
            y += vyska;
        }

        private float Sekce(string nazev, float y, float vnitrek)
        {
            var t = Text(Novy("Sekce_" + nazev, _canvas, Vector2.zero,
                new Vector2(vnitrek, SectionTitleH)), 17f,
                TextAlignmentOptions.Left, PanelStyle.TextSecondary);

            t.text = nazev;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 6f;

            t.rectTransform.anchoredPosition = new Vector2(0f, -(y + SectionTitleH * 0.5f));
            return y + SectionTitleH + 4f;
        }

        private float Tvary(float y, float vnitrek)
        {
            var tvary = (ShapeType[])System.Enum.GetValues(typeof(ShapeType));

            // Sedm dlaždic do dvou řad: čtyři a tři. Jedna řada by okno
            // roztáhla do šířky přes půl zorného pole.
            var naRadku = Mathf.CeilToInt(tvary.Length / 2f);
            var radku = Mathf.CeilToInt(tvary.Length / (float)naRadku);

            for (var i = 0; i < tvary.Length; i++)
            {
                var r = i / naRadku;
                var s = i % naRadku;

                // Zarovnání DOLEVA, ne na střed. Nadpisy sekcí začínají
                // u levého okraje; vystředěné řady pod nimi působí rozhozeně
                // a oko nemá svislici, po které by sjelo dolů.
                var x = -vnitrek * 0.5f + TileW * 0.5f + s * (TileW + TileGap);
                var stred = -(y + r * (TileH + TileGap) + TileH * 0.5f);

                Dlazdice(tvary[i], new Vector2(x, stred));
            }

            return y + radku * TileH + (radku - 1) * TileGap;
        }

        private void Dlazdice(ShapeType tvar, Vector2 pos)
        {
            var karta = Novy("Tvar_" + tvar, _canvas, pos, new Vector2(TileW, TileH));
            PanelStyle.ApplyRounded(karta.AddComponent<Image>(), 16f, PanelStyle.Card);

            var rt = (RectTransform)karta.transform;

            // SVĚTLÁ PODLOŽKA POD IKONU, stejná jako dlaždice tvaru v klasickém
            // menu. Ikony jsou šedé plastiky se stínovanou odvrácenou stranou;
            // na tmavé kartě ta strana splyne s pozadím a válec i kužel přijdou
            // o půl siluety. Obě podmínky musí ukazovat týž obrázek stejně
            // čitelně, jinak by se rozdíl mezi nimi dal svést na to, že v jedné
            // z nich se tvar hůř pozná.
            var podklad = Novy("Podklad", rt, new Vector2(0f, 14f), new Vector2(52f, 52f));
            PanelStyle.ApplyRounded(podklad.AddComponent<Image>(), 10f,
                new Color(0.92f, 0.92f, 0.94f, 1f));

            var ikona = Novy("Ikona", (RectTransform)podklad.transform, Vector2.zero,
                new Vector2(44f, 44f));
            var img = ikona.AddComponent<Image>();
            img.sprite = library != null ? library.GetIcon(tvar) : null;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;

            var popis = Text(Novy("Nazev", rt, new Vector2(0f, -28f), new Vector2(TileW - 6f, 24f)),
                16f, TextAlignmentOptions.Center, PanelStyle.TextPrimary);
            popis.text = Nazvy.Tvar(tvar);
        }

        private float Barvy(float y, float vnitrek)
        {
            var barvy = (PaletteColor[])System.Enum.GetValues(typeof(PaletteColor));

            const int naRadku = 4;
            var radku = Mathf.CeilToInt(barvy.Length / (float)naRadku);

            for (var i = 0; i < barvy.Length; i++)
            {
                var r = i / naRadku;
                var s = i % naRadku;

                var x = -vnitrek * 0.5f + SwatchW * 0.5f + s * SwatchW;
                var stred = -(y + r * (SwatchH + TileGap) + SwatchH * 0.5f);

                Vzorek(barvy[i], new Vector2(x, stred));
            }

            return y + radku * SwatchH + (radku - 1) * TileGap;
        }

        private void Vzorek(PaletteColor barva, Vector2 pos)
        {
            var radek = Novy("Barva_" + barva, _canvas, pos, new Vector2(SwatchW, SwatchH));
            var rt = (RectTransform)radek.transform;

            var puntik = Novy("Puntik", rt, new Vector2(-SwatchW * 0.5f + 16f, 0f),
                new Vector2(22f, 22f));
            PanelStyle.ApplyRounded(puntik.AddComponent<Image>(), 11f,
                library != null ? library.GetDisplayColor(barva) : Color.white);

            var popis = Text(Novy("Nazev", rt, new Vector2(10f, 0f),
                new Vector2(SwatchW - 40f, SwatchH)), 17f,
                TextAlignmentOptions.Left, PanelStyle.TextPrimary);
            popis.text = Nazvy.Barva(barva);
        }

        /// <summary>
        /// Velikosti se ukazují STEJNOU STUPNICÍ JAKO V MENU, ne jen slovem.
        ///
        /// Samotné „malý / střední / velký“ je bez měřítka nejednoznačné —
        /// participant neví, o kolik menší je malý. Stupnice ukáže všechny tři
        /// najednou a zvýrazní tu, o kterou jde, takže se slovo naváže na
        /// obrázek, který je ve stejné podobě i v klasickém inventáři.
        /// </summary>
        private float Velikosti(float y, float vnitrek)
        {
            var velikosti = (ShapeSize[])System.Enum.GetValues(typeof(ShapeSize));
            var sirka = 150f;
            const float vyska = 52f;

            for (var i = 0; i < velikosti.Length; i++)
            {
                var x = -vnitrek * 0.5f + sirka * 0.5f + i * sirka;
                var karta = Novy("Velikost_" + velikosti[i], _canvas,
                    new Vector2(x, -(y + vyska * 0.5f)), new Vector2(sirka - 8f, vyska));
                PanelStyle.ApplyRounded(karta.AddComponent<Image>(), 12f, PanelStyle.Card);

                var rt = (RectTransform)karta.transform;

                // SVĚTLÁ PODLOŽKA jako pod ikonami tvarů. Stupnice je kresba
                // tmavou na světlém; na tmavé kartě by zmizela.
                var podklad = Novy("Podklad", rt, new Vector2(-sirka * 0.5f + 32f, 0f),
                    new Vector2(38f, 38f));
                PanelStyle.ApplyRounded(podklad.AddComponent<Image>(), 9f,
                    new Color(0.92f, 0.92f, 0.94f, 1f));

                var stupnice = library != null ? library.GetSizeIcon(velikosti[i]) : null;
                if (stupnice != null)
                {
                    var ikona = Novy("Ikona", (RectTransform)podklad.transform, Vector2.zero,
                        new Vector2(30f, 30f));
                    var img = ikona.AddComponent<Image>();
                    img.sprite = stupnice;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.color = PanelStyle.Window;
                }

                var t = Text(Novy("Nazev", rt, new Vector2(18f, 0f),
                    new Vector2(sirka - 76f, vyska)), 17f,
                    TextAlignmentOptions.Left, PanelStyle.TextPrimary);
                t.text = Nazvy.Velikost(velikosti[i]);
            }

            return y + vyska;
        }

        private float Povely(float y, float vnitrek)
        {
            var radky = new System.Collections.Generic.List<string>
            {
                "<b>„zpět“</b>   vrátí objekt, který ještě není položený"
            };

            if (_planOnDemand) radky.Add("<b>„ukaž plán“</b>   na chvíli odkryje předlohu");

            const float radekH = 28f;
            var vyska = radky.Count * radekH + 16f;

            var karta = Novy("Povely", _canvas, new Vector2(0f, -(y + vyska * 0.5f)),
                new Vector2(vnitrek, vyska));
            PanelStyle.ApplyRounded(karta.AddComponent<Image>(), 16f, PanelStyle.Card);

            for (var i = 0; i < radky.Count; i++)
            {
                var t = Text(Novy("Radek" + i, (RectTransform)karta.transform,
                    new Vector2(0f, vyska * 0.5f - 8f - radekH * (i + 0.5f)),
                    new Vector2(vnitrek - 32f, radekH)), 17f,
                    TextAlignmentOptions.Left, PanelStyle.TextPrimary);
                t.text = radky[i];
            }

            return y + vyska;
        }

        /// <summary>
        /// Nastaví výšku okna a posune obsah tak, aby seděl na střed canvasu.
        /// Vrací výslednou výšku v pixelech canvasu.
        /// </summary>
        private float Dokoncit(float obsah)
        {
            var celkem = obsah + Pad * 2f;

            _canvas.sizeDelta = new Vector2(width, celkem);

            var pozadi = _canvas.Find("Pozadi") as RectTransform;
            if (pozadi != null)
            {
                pozadi.sizeDelta = new Vector2(width, celkem);
                pozadi.anchoredPosition = Vector2.zero;
            }

            // Vše ostatní bylo zatím umístěné v souřadnicích „od horního
            // okraje obsahu". Teď stačí celou tu soustavu posunout.
            var posun = celkem * 0.5f - Pad;

            foreach (RectTransform rt in _canvas)
            {
                if (rt.name == "Pozadi") continue;
                rt.anchoredPosition += new Vector2(0f, posun);
            }

            return celkem;
        }

        /// <summary>
        /// Lišta pod oknem a collider, za který se okno chytá.
        ///
        /// POČÍTÁ SE AŽ TADY, protože výška okna závisí na tom, kolik sekcí
        /// blok má — s velikostmi je okno o řádek vyšší než bez nich. Kdyby
        /// lišta seděla na pevné souřadnici, v jednom bloku by se překrývala
        /// s obsahem a v druhém by plavala kus pod oknem.
        /// </summary>
        private void PostavitListu(float vyskaCanvasu)
        {
            var spodek = -vyskaCanvasu * 0.5f * canvasScale;
            var stred = spodek - gripGap - gripHeight * 0.5f;

            var go = new GameObject(GripName, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, stred, 0f);
            go.layer = gameObject.layer;

            var bar = go.AddComponent<GripBar>();
            bar.Nastavit(gripWidth, gripHeight, gripDepth);

            if (gripMaterial != null) go.GetComponent<MeshRenderer>().sharedMaterial = gripMaterial;

            _grip = go.transform;

            var zamek = GetComponent<MenuDragHandle>();
            if (zamek != null) zamek.SetHandleVisual(go);

            // ÚCHOPOVÝ BOD NA LIŠTU. Bez něj bere XRI počátek objektu, a ten
            // je uprostřed okna — okno pak při chycení poskočí tak, aby měl
            // participant v ruce jeho střed, a ne lištu, za kterou sáhl.
            // Klasické menu to má nastavené ze scény; okno si lištu staví
            // za běhu, takže se to musí navázat tady.
            var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null) grab.attachTransform = go.transform;

            // Collider je o kousek větší než lišta. Chytat se má i tehdy, když
            // participant mine o pár milimetrů — jinak by se okno posouvalo
            // hůř než inventář a rozdíl by šel na vrub podmínce.
            var box = GetComponent<BoxCollider>();
            if (box == null) return;

            box.center = new Vector3(0f, stred, 0f);
            box.size = new Vector3(gripWidth + 0.008f, gripHeight + 0.012f, gripDepth + 0.014f);
        }

        // ---- Pomocné ----

        private TextMeshProUGUI Text(GameObject go, float velikost,
            TextAlignmentOptions zarovnani, Color barva)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();

            // Font jako první: sazba i obrys zapisují do materiálu fontu.
            if (font != null) tmp.font = font;
            tmp.fontSize = velikost;
            tmp.alignment = zarovnani;
            tmp.color = barva;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        private static GameObject Novy(string jmeno, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(jmeno, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return go;
        }
    }
}
