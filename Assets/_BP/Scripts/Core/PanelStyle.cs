using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BP.Core
{
    /// <summary>
    /// Vzhled oken v prostoru — tmavá karta se zaoblenými rohy, jako mají
    /// systémová okna Questu.
    ///
    /// Postup převzatý z projektu Mini_AI_Project (ZCU-KPV), odkud pochází
    /// i hlasové ovládání.
    ///
    /// ROHY SE GENERUJÍ ZA BĚHU, ne z textury. Projekt tím zůstává bez
    /// obrázkových assetů: nic se nemusí importovat, nic se nemůže ztratit
    /// při přesunu mezi počítači a v buildu se nedá zapomenout. Sprite je
    /// devítidílný, takže jedno malé řezané pole obslouží okno libovolné
    /// velikosti a rohy se nikdy nerozmažou.
    /// </summary>
    public static class PanelStyle
    {
        /// <summary>Pozadí okna.</summary>
        public static readonly Color Window = new Color(0.114f, 0.125f, 0.145f, 0.985f);

        /// <summary>Světlejší karta uvnitř okna — odděluje sekce bez čar.</summary>
        public static readonly Color Card = new Color(0.180f, 0.196f, 0.224f, 1f);

        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = new Color(0.63f, 0.66f, 0.71f, 1f);

        /// <summary>Žlutá na nadpis okna — drží se zbytkem aplikace.</summary>
        public static readonly Color Title = new Color(0.98f, 0.86f, 0.42f, 1f);

        /// <summary>Hlavní akce (CREATE, POKRAČOVAT). Zelená se ozve zvukem.</summary>
        public static readonly Color Positive = new Color(0.16f, 0.55f, 0.24f, 1f);

        /// <summary>Vedlejší akce (STEP BACK, ZPĚT DO MENU).</summary>
        public static readonly Color Neutral = new Color(0.24f, 0.26f, 0.30f, 1f);

        /// <summary>Vyžádání plánku — modrá, ať se nesplete s hlavní akcí.</summary>
        public static readonly Color Info = new Color(0.20f, 0.36f, 0.62f, 1f);

        /// <summary>Hláška o odmítnutém požadavku.</summary>
        public static readonly Color Warn = new Color(1f, 0.72f, 0.2f, 1f);

        /// <summary>Světlá podložka pod ikonu tvaru, stejná v menu i v okně.</summary>
        public static readonly Color Plate = new Color(0.92f, 0.92f, 0.94f, 1f);

        // Poloměry v jednotkách canvasu. Stupnice je záměrně hrubá — tři
        // hodnoty stačí a drží celou aplikaci v jednom rytmu. Velké okno má
        // kulatější rohy než dlaždice uvnitř, jinak by se zaoblení opticky
        // ztratilo.
        public const float RadiusWindow = 44f;
        public const float RadiusPanel = 22f;
        public const float RadiusButton = 14f;
        public const float RadiusTile = 10f;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        /// <summary>
        /// Bílý zaoblený obdélník jako devítidílný sprite. Barvu určuje tint
        /// Image, takže jeden sprite stačí na všechna okna.
        /// </summary>
        public static Sprite Rounded(int radius)
        {
            if (Cache.TryGetValue(radius, out var hotovy) && hotovy != null) return hotovy;

            // +8 px středový pruh, aby měl devítidílný sprite co roztahovat.
            var size = radius * 2 + 8;
            var tex = Textura(size, radius);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.hideFlags = HideFlags.HideAndDontSave;

            Cache[radius] = sprite;
            return sprite;
        }

        /// <summary>
        /// Zaoblený čtverec o hraně přesně jedné jednotky, pro SpriteRenderer
        /// v prostoru (odznak velikosti u plánku).
        ///
        /// NENÍ TO devítidílný sprite jako u oken. Odznak je vždycky čtverec,
        /// takže se nic neroztahuje a poloměr může být dán podílem strany —
        /// zaoblení pak vypadá stejně jako u dlaždic v inventáři. Jednotka na
        /// hranu je záměr: rozměr destičky určuje měřítko objektu a ten se
        /// počítá z velikosti písma.
        /// </summary>
        public static Sprite RoundedSquare()
        {
            if (_ctverec != null) return _ctverec;

            const int size = 128;
            const int radius = (int)(size * 0.20f);   // stejný podíl jako dlaždice menu

            var tex = Textura(size, radius);

            // pixelsPerUnit = strana textury, takže sprite měří jednu jednotku.
            _ctverec = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _ctverec.hideFlags = HideFlags.HideAndDontSave;
            return _ctverec;
        }

        private static Sprite _ctverec;

        /// <summary>Bílý zaoblený čtverec s vyhlazenou hranou.</summary>
        private static Texture2D Textura(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var px = new Color32[size * size];
            var half = size / 2f;

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // Vzdálenost od zaobleného obdélníku; poslední půlpixel se
                // použije na vyhlazení hrany, jinak jsou rohy zubaté.
                var dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                var dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp01(radius - dist + 0.5f);

                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Udělá z Image zaoblenou desku. Poloměr je v jednotkách canvasu,
        /// ne v pixelech textury — sprite se podle toho přepočítá, takže
        /// rohy vypadají stejně u malého i velkého okna.
        /// </summary>
        public static void ApplyRounded(Image img, float displayRadius, Color barva)
        {
            img.color = barva;
            img.raycastTarget = false;
            Tvarovat(img, displayRadius);
        }

        /// <summary>
        /// Nasadí tvar, barvy se nedotkne. Odděleno proto, že po načtení scény
        /// se obnovuje jen tvar — barva už mezitím může být jiná, než s jakou
        /// se deska stavěla (zelené CREATE, zhasnutá dlaždice).
        /// </summary>
        internal static void Tvarovat(Image img, float displayRadius)
        {
            const int spriteRadius = 48;

            img.sprite = Rounded(spriteRadius);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = spriteRadius / Mathf.Max(1f, displayRadius);

            // Poloměr si drží komponenta na objektu, jinak by se zaoblení
            // ztratilo při uložení scény — viz RoundedImage.
            var pamet = img.GetComponent<RoundedImage>();
            if (pamet == null) pamet = img.gameObject.AddComponent<RoundedImage>();
            pamet.Zapamatovat(displayRadius);
        }

        /// <summary>
        /// Totéž pro plochu, na kterou se klepe.
        ///
        /// ApplyRounded vypíná raycastTarget, protože drtivá většina desek je
        /// jen pozadí a chytat za ně paprsek by zakrývalo tlačítka pod nimi.
        /// U dlaždice v inventáři je to ale naopak celý smysl objektu, takže
        /// se sem přidávat nesmí — jinak menu tiše přestane reagovat.
        /// </summary>
        public static void ApplyRoundedButton(Image img, float displayRadius, Color barva)
        {
            img.color = barva;
            img.raycastTarget = true;
            Tvarovat(img, displayRadius);
        }
    }
}
