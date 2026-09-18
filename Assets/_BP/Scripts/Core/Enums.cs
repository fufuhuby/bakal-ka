namespace BP.Core
{
    /// <summary>
    /// Tvary dostupné v menu i v hlasové gramatice.
    /// Pořadí odpovídá pořadí ve sloupci menu (skica b4).
    /// </summary>
    public enum ShapeType
    {
        Torus = 0,
        Cube = 1,
        Pyramid = 2,
        Octahedron = 3,
        Sphere = 4,
        Cone = 5,
        Cylinder = 6
    }

    /// <summary>
    /// Barvy dostupné v menu i v hlasové gramatice.
    /// Pořadí odpovídá pořadí ve sloupci menu (skica b4).
    /// </summary>
    public enum PaletteColor
    {
        Blue = 0,
        Green = 1,
        Yellow = 2,
        Red = 3,
        Purple = 4,
        Orange = 5,
        Magenta = 6
    }

    /// <summary>
    /// Velikost tvaru — třetí vlastnost objektu vedle tvaru a barvy.
    /// Pořadí odpovídá pořadí ve sloupci menu.
    ///
    /// PROČ TŘETÍ VLASTNOST: hlas vysloví libovolný počet vlastností jednou
    /// větou za skoro konstantní cenu, GUI platí za každou vlastnost jedno
    /// kliknutí plus vizuální hledání. Počet vlastností je proto druhý faktor
    /// návrhu — testuje, jestli nevýhoda GUI s počtem vlastností roste.
    /// </summary>
    public enum ShapeSize
    {
        S = 0,
        M = 1,
        L = 2
    }

    /// <summary>
    /// Převody pro <see cref="ShapeSize"/>. Násobky drží jedno místo,
    /// aby se velikost v šabloně, v menu a u vytvořeného objektu nemohla
    /// rozejít — rozdíl mezi předlohou a objektem by se v datech projevil
    /// jako chyba participanta.
    /// </summary>
    public static class ShapeSizes
    {
        /// <summary>Násobek základní velikosti prefabu (tvary jsou 6 cm).</summary>
        public static float Scale(ShapeSize size)
        {
            switch (size)
            {
                case ShapeSize.S: return 0.80f;   // 4,8 cm
                case ShapeSize.L: return 1.25f;   // 7,5 cm
                default:          return 1.00f;   // 6,0 cm (M)
            }
        }

        /// <summary>Označení do plánku a do menu.</summary>
        public static string Label(ShapeSize size)
        {
            switch (size)
            {
                case ShapeSize.S: return "S";
                case ShapeSize.L: return "L";
                default:          return "M";
            }
        }

        /// <summary>Velikost, která se použije, když blok velikosti neřeší.</summary>
        public const ShapeSize Default = ShapeSize.M;
    }

    /// <summary>
    /// Experimentální podmínka = nezávislá proměnná.
    /// </summary>
    public enum InteractionCondition
    {
        Menu = 0,
        Voice = 1
    }

    /// <summary>
    /// Úroveň zátěže. SingleTask = baseline bez sekundární úlohy,
    /// slouží k výpočtu dual-task cost.
    /// </summary>
    public enum LoadCondition
    {
        SingleTask = 0,
        DualTask = 1
    }

    /// <summary>
    /// Kolik vlastností musí participant u objektu určit — druhý faktor návrhu.
    ///
    /// TwoAttributes = tvar + barva (menu 7 × 7).
    /// ThreeAttributes = tvar + barva + velikost (menu 7 × 7 × 3).
    ///
    /// Faktor se vyvažuje stejně jako podmínka: každý participant projde
    /// oběma úrovněmi v obou podmínkách, jinak by rozdíl mezi počtem
    /// vlastností splynul s efektem učení.
    /// </summary>
    public enum AttributeCount
    {
        TwoAttributes = 0,
        ThreeAttributes = 1
    }

    /// <summary>
    /// Části menu, které jde samostatně povolit nebo zamknout.
    ///
    /// Slouzi jen tutorialu, ktery vede participanta po krocich. V merenych
    /// blocich je vzdy All: kdyby se poradi voleb vynucovalo i tam, merila
    /// by se jina uloha, nez jakou ma hlasova podminka porazit.
    /// </summary>
    [System.Flags]
    public enum MenuPart
    {
        None = 0,
        Colors = 1,
        Shapes = 2,
        Sizes = 4,
        Create = 8,
        StepBack = 16,
        All = Colors | Shapes | Sizes | Create | StepBack
    }
}
