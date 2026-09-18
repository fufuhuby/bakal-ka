using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BP.Core;

namespace BP.Input
{
    /// <summary>Co participant vyslovil.</summary>
    public enum VoiceIntent
    {
        /// <summary>Nic srozumitelného.</summary>
        None = 0,

        /// <summary>Vytvořit objekt — rozpoznaná barva, tvar a případně velikost.</summary>
        Create = 1,

        /// <summary>Vzít zpět poslední objekt. Obdoba tlačítka STEP BACK.</summary>
        Undo = 2,

        /// <summary>Ukázat plánek. Ve třetím bloku jediná cesta, jak se na něj podívat.</summary>

        Reveal = 3
    }

    /// <summary>Výsledek rozboru jedné promluvy.</summary>
    public readonly struct VoiceCommand
    {
        public readonly VoiceIntent Intent;
        public readonly ShapeType Shape;
        public readonly PaletteColor Color;
        public readonly ShapeSize Size;

        /// <summary>Byla velikost opravdu vyslovena, nebo se doplnila výchozí?</summary>
        public readonly bool HasSize;

        /// <summary>Co chybělo, když se příkaz nepovedlo složit — jde do logu.</summary>
        public readonly string Problem;

        public VoiceCommand(VoiceIntent intent, ShapeType shape, PaletteColor color,
            ShapeSize size, bool hasSize, string problem)
        {
            Intent = intent;
            Shape = shape;
            Color = color;
            Size = size;
            HasSize = hasSize;
            Problem = problem;
        }

        public static VoiceCommand Nic(string problem)
            => new VoiceCommand(VoiceIntent.None, default, default, default, false, problem);
    }

    /// <summary>
    /// Rozebere českou promluvu na příkaz.
    ///
    /// PROČ SE NEHLEDAJÍ CELÁ SLOVA, ALE KMENY: čeština skloňuje, a participant
    /// řekne „červená kostka" stejně přirozeně jako „červenou kostku". Vypisovat
    /// všechny tvary by znamenalo tabulku o stovkách položek, ve které se dřív
    /// nebo později na nějaký tvar zapomene — a ta chyba by se projevila jako
    /// „hlas nefunguje", tedy jako vlastnost podmínky, kterou experiment měří.
    /// Kmen „červen" pokryje červená, červený, červenou i červené naráz.
    ///
    /// DIAKRITIKA SE ODSTRAŇUJE. Přepis ji občas vynechá nebo splete a rozdíl
    /// mezi „kužel" a „kuzel" nemá žádný význam pro to, co participant chtěl.
    ///
    /// SLOVESO SE NEVYŽADUJE. Příkaz je „červená kostka", ne „vytvoř červenou
    /// kostku" — kratší promluva se rychleji vysloví i rozpozná, a protože
    /// v tomhle úkolu nejde dělat s objektem nic jiného než ho vytvořit,
    /// není co plést.
    /// </summary>
    public static class CzechCommandParser
    {
        // ---- Slovník ----
        //
        // Kmeny jsou bez diakritiky a bez koncovky. Pořadí uvnitř skupiny
        // nerozhoduje; mezi skupinami ano — delší kmen musí být dřív, aby
        // kratší nesebral shodu (viz „kuzel" vs „kus").

        private static readonly (string kmen, PaletteColor barva)[] Barvy =
        {
            ("modr", PaletteColor.Blue),
            ("zelen", PaletteColor.Green),
            ("zlut", PaletteColor.Yellow),
            ("cerven", PaletteColor.Red),
            ("fialov", PaletteColor.Purple),
            ("oranzov", PaletteColor.Orange),
            ("ruzov", PaletteColor.Magenta),
            ("purpurov", PaletteColor.Magenta),
        };

        private static readonly (string kmen, ShapeType tvar)[] Tvary =
        {
            ("valec", ShapeType.Cylinder),
            ("valc", ShapeType.Cylinder),
            ("kuzel", ShapeType.Cone),
            ("kuzl", ShapeType.Cone),
            ("osmisten", ShapeType.Octahedron),
            ("osmisteny", ShapeType.Octahedron),
            ("oktaedr", ShapeType.Octahedron),
            ("jehlan", ShapeType.Pyramid),
            ("pyramid", ShapeType.Pyramid),
            ("kostk", ShapeType.Cube),
            ("krychl", ShapeType.Cube),
            ("torus", ShapeType.Torus),
            ("donut", ShapeType.Torus),
            ("koul", ShapeType.Sphere),
            ("kul", ShapeType.Sphere),
        };

        private static readonly (string kmen, ShapeSize velikost)[] Velikosti =
        {
            ("mal", ShapeSize.S),
            ("stredn", ShapeSize.M),
            ("velk", ShapeSize.L),
        };

        private static readonly string[] Zpet =
        {
            "zpet", "vrat", "smaz", "zrus", "odstran",
        };

        /// <summary>Slova, která znamenají plánek sama o sobě.</summary>
        private static readonly string[] Planek = { "plan", "predloh" };

        /// <summary>
        /// Sloveso „ukaž" samotné.
        ///
        /// PLATÍ JEN VE VĚTĚ BEZ BARVY A BEZ TVARU. „Ukaž" se dá připojit
        /// skoro k čemukoli, takže kdyby stačilo samo, spadlo by do žádosti
        /// o plánek i „ukaž mi červenou kostku". Když ale ve větě není ani
        /// barva, ani tvar, nemá to sloveso co jiného znamenat než plánek.
        /// </summary>
        private static readonly string[] Ukaz = { "ukaz", "zobraz" };

        /// <summary>
        /// Slovník do promptu pro přepis. Whisper s ním dělá míň překlepů
        /// v názvech, které v běžné češtině nejsou časté.
        /// </summary>
        public const string Slovnik =
            "modrá, zelená, žlutá, červená, fialová, oranžová, růžová, "
            + "válec, kužel, osmistěn, jehlan, kostka, torus, koule, "
            + "malý, střední, velký, zpět, ukaž plán";

        /// <summary>
        /// Rozebere přepis. Velikost je nepovinná — blok, který ji neřeší,
        /// ji v příkazu nečeká a parser vrátí HasSize = false.
        /// </summary>
        public static VoiceCommand Parse(string prepis)
        {
            if (string.IsNullOrWhiteSpace(prepis)) return VoiceCommand.Nic("prazdny prepis");

            var text = Normalizovat(prepis);

            // Barva a tvar se hledají PŘED rozhodnutím o plánku: podle nich
            // se pozná, jestli „ukaž" míří na předlohu, nebo je to jen
            // sloveso u objektu.
            var maBarvu = NajdiBarvu(text, out var barva);
            var maTvar = NajdiTvar(text, out var tvar);
            var maVelikost = NajdiVelikost(text, out var velikost);

            // Plánek jako první. Nenese barvu ani tvar, takže by jinak spadl
            // do větve „chybí barva".
            if (Obsahuje(text, Planek)
                || (Obsahuje(text, Ukaz) && !maBarvu && !maTvar))
                return new VoiceCommand(VoiceIntent.Reveal, default, default, default, false, null);

            if (Obsahuje(text, Zpet)) return new VoiceCommand(
                VoiceIntent.Undo, default, default, default, false, null);

            if (!maBarvu && !maTvar) return VoiceCommand.Nic("nerozpoznano");
            if (!maBarvu) return VoiceCommand.Nic("chybi barva");
            if (!maTvar) return VoiceCommand.Nic("chybi tvar");

            return new VoiceCommand(VoiceIntent.Create, tvar, barva,
                maVelikost ? velikost : ShapeSizes.Default, maVelikost, null);
        }

        // ---- Hledání ----

        private static bool NajdiBarvu(string text, out PaletteColor barva)
        {
            foreach (var (kmen, hodnota) in Barvy)
            {
                if (text.IndexOf(kmen, StringComparison.Ordinal) < 0) continue;
                barva = hodnota;
                return true;
            }

            barva = default;
            return false;
        }

        private static bool NajdiTvar(string text, out ShapeType tvar)
        {
            foreach (var (kmen, hodnota) in Tvary)
            {
                if (text.IndexOf(kmen, StringComparison.Ordinal) < 0) continue;
                tvar = hodnota;
                return true;
            }

            tvar = default;
            return false;
        }

        private static bool NajdiVelikost(string text, out ShapeSize velikost)
        {
            foreach (var (kmen, hodnota) in Velikosti)
            {
                if (text.IndexOf(kmen, StringComparison.Ordinal) < 0) continue;
                velikost = hodnota;
                return true;
            }

            velikost = ShapeSizes.Default;
            return false;
        }

        private static bool Obsahuje(string text, IReadOnlyList<string> kmeny)
        {
            for (var i = 0; i < kmeny.Count; i++)
                if (text.IndexOf(kmeny[i], StringComparison.Ordinal) >= 0) return true;

            return false;
        }

        /// <summary>Malá písmena bez diakritiky — na tom se hledají kmeny.</summary>
        public static string Normalizovat(string s)
        {
            var rozlozene = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(rozlozene.Length);

            foreach (var c in rozlozene)
            {
                // Diakritická znaménka jsou po rozkladu samostatné znaky
                // kategorie NonSpacingMark — stačí je vynechat.
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
