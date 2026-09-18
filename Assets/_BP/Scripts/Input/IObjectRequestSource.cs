using System;
using BP.Core;

namespace BP.Input
{
    /// <summary>
    /// Jeden požadavek na vytvoření objektu.
    /// </summary>
    public readonly struct ObjectRequest
    {
        public readonly ShapeType Shape;
        public readonly PaletteColor Color;
        public readonly ShapeSize Size;

        /// <summary>Time.realtimeSinceStartup v momentě potvrzení požadavku.</summary>
        public readonly float TimeStamp;

        public ObjectRequest(ShapeType shape, PaletteColor color, ShapeSize size, float timeStamp)
        {
            Shape = shape;
            Color = color;
            Size = size;
            TimeStamp = timeStamp;
        }

        public override string ToString() => $"{ShapeSizes.Label(Size)} {Color} {Shape}";
    }

    /// <summary>
    /// ZDE JE JÁDRO VALIDITY EXPERIMENTU.
    ///
    /// Menu i hlasové ovládání implementují toto rozhraní a emitují naprosto
    /// stejné eventy. Zbytek aplikace (spawn, umisťování, validace, logování)
    /// nezná rozdíl mezi podmínkami. Tím je zaručeno, že se podmínky liší
    /// POUZE ve způsobu vytvoření objektu — což je jediná nezávislá proměnná.
    /// </summary>
    public interface IObjectRequestSource
    {
        /// <summary>Participant potvrdil, že chce daný objekt vytvořit.</summary>
        event Action<ObjectRequest> ObjectRequested;

        /// <summary>Participant chce smazat poslední umístěný objekt (STEP BACK / "step back").</summary>
        event Action UndoRequested;

        /// <summary>Která podmínka tento zdroj reprezentuje — jde do logu.</summary>
        InteractionCondition Condition { get; }

        /// <summary>
        /// Zapnutí/vypnutí vstupu. Mezi bloky a během instrukcí musí být vstup
        /// vypnutý, aby se do dat nedostaly náhodné akce.
        /// </summary>
        void SetInputEnabled(bool enabled);
    }
}
