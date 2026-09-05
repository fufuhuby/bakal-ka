using TMPro;
using UnityEngine;

namespace BP.Core
{
    /// <summary>
    /// Velká hláška v pozadí pracoviště — HIT, MISSED, BLOCK COMPLETE.
    ///
    /// Doplňuje malý řádek na panelu menu, ne nahrazuje ho: panel drží
    /// podrobnosti (co přesně chybí, reakční čas), banner nese to, co má být
    /// vidět periferním viděním, aniž by se participant musel podívat na menu.
    /// U sekundární úlohy to má smysl obzvlášť — ta se odehrává v periferii,
    /// takže i zpětná vazba k ní musí být čitelná bez otočení hlavy.
    /// </summary>
    public class StatusBanner : MonoBehaviour
    {
        [SerializeField] private TextMeshPro text;

        [Tooltip("Jak dlouho hláška zůstane (sekundy).")]
        [SerializeField] private float duration = 1.6f;

        [Tooltip("Doba zhasínání na konci (sekundy).")]
        [SerializeField] private float fadeOut = 0.5f;

        [Header("Barvy")]
        [SerializeField] private Color positiveColor = new Color(0.35f, 0.95f, 0.45f, 1f);
        [SerializeField] private Color negativeColor = new Color(1f, 0.45f, 0.35f, 1f);
        [SerializeField] private Color neutralColor = new Color(0.90f, 0.92f, 0.96f, 1f);

        private float _hideAt;
        private Color _baseColor;

        private void Awake()
        {
            if (text == null) text = GetComponentInChildren<TextMeshPro>();
            Hide();
        }

        private void Update()
        {
            if (text == null || !text.enabled) return;

            var remaining = _hideAt - Time.time;
            if (remaining <= 0f)
            {
                Hide();
                return;
            }

            // Zhasínání jen na konci — hláška musí být nejdřív plně čitelná.
            var alpha = fadeOut > 0.01f ? Mathf.Clamp01(remaining / fadeOut) : 1f;
            var c = _baseColor;
            c.a = alpha;
            text.color = c;
        }

        public void ShowPositive(string message) => Show(message, positiveColor);
        public void ShowNegative(string message) => Show(message, negativeColor);
        public void ShowNeutral(string message) => Show(message, neutralColor);

        public void Show(string message, Color color)
        {
            if (text == null) return;

            text.text = message;
            _baseColor = color;
            text.color = color;
            text.enabled = true;
            _hideAt = Time.time + duration;
        }

        public void Hide()
        {
            if (text != null) text.enabled = false;
        }
    }
}
