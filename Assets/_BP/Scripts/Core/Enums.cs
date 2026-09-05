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
}
