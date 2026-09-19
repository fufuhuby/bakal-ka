namespace BP.Core
{
    /// <summary>
    /// České názvy tvarů a barev pro všechno, co je vidět na scéně.
    ///
    /// JEDNO MÍSTO SCHVÁLNĚ. Tatáž slova ukazuje okno s hlasovými příkazy,
    /// tatáž se objeví v hlášce o špatném objektu a tatáž jsou kmeny, na které
    /// slyší rozpoznávání řeči. Kdyby si každé místo drželo vlastní seznam,
    /// stačilo by jedno opomenutí a participant by četl v nápovědě jiný název,
    /// než jaký po něm chce hláška — a hledal by chybu u sebe.
    ///
    /// Tvary jsou v prvním pádě a malými písmeny. Kde je potřeba verzálka,
    /// převede se to na místě; obráceně se z verzálek první pád nevyrobí.
    /// </summary>
    public static class Nazvy
    {
        public static string Tvar(ShapeType t)
        {
            switch (t)
            {
                // KRYCHLE, ne kostka. Kostka je taky hrací kostka a kostka
                // cukru; krychle je jednoznačně těleso. Parser bere obojí
                // (kmeny „krychl" i „kostk"), takže kdo řekne „kostka“,
                // dostane totéž — mění se jen to, co je napsané.
                case ShapeType.Cube: return "krychle";
                case ShapeType.Sphere: return "koule";
                case ShapeType.Cylinder: return "válec";
                case ShapeType.Cone: return "kužel";
                case ShapeType.Pyramid: return "jehlan";
                case ShapeType.Octahedron: return "osmistěn";
                default: return "torus";
            }
        }

        public static string Barva(PaletteColor c)
        {
            switch (c)
            {
                case PaletteColor.Blue: return "modrá";
                case PaletteColor.Green: return "zelená";
                case PaletteColor.Yellow: return "žlutá";
                case PaletteColor.Red: return "červená";
                case PaletteColor.Purple: return "fialová";
                case PaletteColor.Orange: return "oranžová";
                default: return "růžová";
            }
        }

        /// <summary>
        /// Je název tvaru mužského rodu?
        ///
        /// KVŮLI SHODĚ PŘÍVLASTKU. Barvy se jinde vypisují samostatně, a tam
        /// na rodu nezáleží. Jakmile se ale barva s tvarem spojí do věty,
        /// musí se shodnout: „červená kostka", ale „červený válec". Pět ze
        /// sedmi tvarů je mužských, takže bez tohohle by hláška v naprosté
        /// většině případů mluvila špatně česky.
        /// </summary>
        public static bool JeMuzsky(ShapeType t)
            => t != ShapeType.Cube && t != ShapeType.Sphere;   // kostka a koule jsou ženské

        /// <summary>Barva ve tvaru, který se shodne s daným tvarem.</summary>
        public static string Barva(PaletteColor c, ShapeType t)
        {
            if (!JeMuzsky(t)) return Barva(c);

            switch (c)
            {
                case PaletteColor.Blue: return "modrý";
                case PaletteColor.Green: return "zelený";
                case PaletteColor.Yellow: return "žlutý";
                case PaletteColor.Red: return "červený";
                case PaletteColor.Purple: return "fialový";
                case PaletteColor.Orange: return "oranžový";
                default: return "růžový";
            }
        }

        public static string Velikost(ShapeSize s)
        {
            switch (s)
            {
                case ShapeSize.S: return "malý";
                case ShapeSize.M: return "střední";
                default: return "velký";
            }
        }
    }
}
