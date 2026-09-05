using System;
using System.Collections.Generic;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Jeden krok šablony = jeden objekt, který má participant vytvořit a umístit.
    /// Pozice jsou LOKÁLNÍ vůči kotvě šablony, aby se dala šablona zakotvit kamkoli.
    /// </summary>
    [Serializable]
    public struct TemplateStep
    {
        public ShapeType shape;
        public PaletteColor color;

        [Tooltip("Lokální pozice vůči kotvě šablony (metry).")]
        public Vector3 localPosition;

        [Tooltip("Lokální rotace vůči kotvě šablony (stupně).")]
        public Vector3 localEulerAngles;

        [Tooltip("Volitelné zvětšení; 1 = základní velikost prefabu.")]
        public float uniformScale;
    }

    /// <summary>
    /// Cílová struktura, kterou participant replikuje.
    /// Kroky jsou SEŘAZENÉ — číslo kroku (1..N) se zobrazuje u šablony,
    /// takže pořadí stavby je fixní a nevnáší do dat varianci ve strategii.
    /// </summary>
    [CreateAssetMenu(menuName = "BP/Assembly Template", fileName = "Template_")]
    public class AssemblyTemplate : ScriptableObject
    {
        [Header("Identifikace")]
        [Tooltip("Krátké ID do logu, např. 'T1'. Musí být unikátní.")]
        public string templateId = "T1";

        [Tooltip("K čemu šablona slouží — trénink, nebo měřený blok.")]
        public bool isTrainingTemplate;

        [Header("Kroky")]
        [SerializeField]
        private List<TemplateStep> steps = new List<TemplateStep>();

        [Header("Tolerance dokončení")]
        [Tooltip("Do jaké vzdálenosti od cílové pozice se objekt považuje za správně umístěný (metry).")]
        public float positionTolerance = 0.03f;

        [Tooltip("Do jaké odchylky od cílové rotace se objekt považuje za správně umístěný (stupně). " +
                 "0 = rotace se nevyhodnocuje.")]
        public float rotationTolerance = 0f;

        public IReadOnlyList<TemplateStep> Steps => steps;
        public int StepCount => steps.Count;

        public TemplateStep GetStep(int index) => steps[index];

        /// <summary>
        /// Kontrola obtížnostní ekvivalence mezi šablonami — počet objektů
        /// a pestrost tvarů/barev musí být napříč měřenými bloky srovnatelné,
        /// jinak se do dat dostane rozdíl v obtížnosti místo rozdílu v podmínce.
        /// </summary>
        public TemplateComplexity GetComplexity()
        {
            var shapes = new HashSet<ShapeType>();
            var colors = new HashSet<PaletteColor>();
            foreach (var s in steps)
            {
                shapes.Add(s.shape);
                colors.Add(s.color);
            }

            return new TemplateComplexity
            {
                stepCount = steps.Count,
                distinctShapes = shapes.Count,
                distinctColors = colors.Count
            };
        }
    }

    public struct TemplateComplexity
    {
        public int stepCount;
        public int distinctShapes;
        public int distinctColors;

        public override string ToString()
            => $"kroků={stepCount}, tvarů={distinctShapes}, barev={distinctColors}";
    }
}
