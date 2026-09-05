using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Výsledek vyhodnocení jednoho umístění.
    /// </summary>
    public struct PlacementResult
    {
        /// <summary>Je to správný tvar a barva pro daný krok?</summary>
        public bool IsCorrectObject;

        /// <summary>Je objekt v toleranci cílové pozice (a rotace, pokud se hodnotí)?</summary>
        public bool IsInTolerance;

        /// <summary>
        /// Vzdálenost od cílové pozice PŘED snapem, v metrech.
        /// Toto je měřená přesnost umístění — po snapu je chyba vždy nula,
        /// takže se musí zaznamenat právě tady, jinak se metrika ztratí.
        /// </summary>
        public float PositionError;

        /// <summary>Odchylka od cílové rotace před snapem, ve stupních.</summary>
        public float RotationError;

        /// <summary>Umístění je platné = správný objekt ve toleranci.</summary>
        public bool IsAccepted => IsCorrectObject && IsInTolerance;
    }

    /// <summary>
    /// Vyhodnocuje, zda participant umístil objekt správně, a provádí soft snap.
    ///
    /// SOFT SNAP: bez něj by se úloha nikdy spolehlivě nedokončila (dokonalé
    /// zarovnání ve vzduchu je nereálné) a část participantů by uvázla.
    /// Se snapem se úloha vždy dokončí, ale přesnost se měřit nepřestane —
    /// zaznamenává se chyba PŘED přichycením.
    /// </summary>
    public class PlacementValidator : MonoBehaviour
    {
        [SerializeField] private TemplateVisualizer templateVisualizer;

        [Tooltip("Přichytit objekt na přesnou cílovou pozici, když je v toleranci.")]
        [SerializeField] private bool snapOnAccept = true;

        [Tooltip("Vyhodnocovat i rotaci. U rotačně symetrických tvarů nemá smysl.")]
        [SerializeField] private bool evaluateRotation;

        public TemplateVisualizer Visualizer => templateVisualizer;

        /// <summary>
        /// Vyhodnotí umístění objektu vůči danému kroku šablony.
        /// Snap se provede jen když je výsledek přijat.
        /// </summary>
        public PlacementResult Evaluate(ShapeInstance instance, int stepIndex)
        {
            var result = new PlacementResult();

            var template = templateVisualizer.Template;
            if (template == null || stepIndex < 0 || stepIndex >= template.StepCount)
                return result;

            var step = template.GetStep(stepIndex);
            result.IsCorrectObject = instance.Matches(step.shape, step.color);

            var targetPos = templateVisualizer.GetTargetPosition(stepIndex);
            var targetRot = templateVisualizer.GetTargetRotation(stepIndex);

            result.PositionError = Vector3.Distance(instance.transform.position, targetPos);
            result.RotationError = Quaternion.Angle(instance.transform.rotation, targetRot);

            var posOk = result.PositionError <= template.positionTolerance;
            var rotOk = !evaluateRotation
                        || template.rotationTolerance <= 0f
                        || result.RotationError <= template.rotationTolerance;

            result.IsInTolerance = posOk && rotOk;

            if (result.IsAccepted && snapOnAccept)
            {
                instance.transform.position = targetPos;
                instance.transform.rotation = targetRot;
                instance.MarkConfirmed();
            }

            return result;
        }

        /// <summary>
        /// Je objekt aktuálně dost blízko cíle, aby se přichytil?
        /// Slouží pro nápovědu během držení, nemodifikuje stav.
        /// </summary>
        public bool IsWithinSnapRange(ShapeInstance instance, int stepIndex)
        {
            var template = templateVisualizer.Template;
            if (template == null || stepIndex < 0 || stepIndex >= template.StepCount) return false;

            var step = template.GetStep(stepIndex);
            if (!instance.Matches(step.shape, step.color)) return false;

            var d = Vector3.Distance(instance.transform.position, templateVisualizer.GetTargetPosition(stepIndex));
            return d <= template.positionTolerance;
        }
    }
}
