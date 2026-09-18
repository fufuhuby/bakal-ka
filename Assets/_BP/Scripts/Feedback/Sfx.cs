using UnityEngine;

namespace BP.Feedback
{
    /// <summary>
    /// Vyrábí zvuky procedurálně, přímo do paměti.
    ///
    /// PROČ NE STAŽENÉ SAMPLY: do bakalářské práce se ozvučení musí dát
    /// popsat. Sampl se popsat nedá jinak než odkazem na soubor a licenci;
    /// tón vyrobený tady je popsaný čtyřmi čísly (frekvence, cílová
    /// frekvence, délka, hlasitost), takže je celý podnět v textu práce
    /// reprodukovatelný a nikdo ho nemusí shánět.
    ///
    /// TVAR OBÁLKY je důležitější než tvar vlny. Náběh je krátký (3 ms),
    /// aby zvuk padl přesně na okamžik události — pomalý náběh by přidal
    /// zpoždění do reakčního času u terčů. Doznívání je exponenciální,
    /// protože lineární konec je slyšet jako lupnutí.
    /// </summary>
    public static class Sfx
    {
        private const int Rate = 44100;

        /// <summary>Náběh v sekundách. Kratší už lupe, delší rozmazává okamžik.</summary>
        private const float Attack = 0.003f;

        /// <summary>
        /// Jeden tón s volitelným skluzem frekvence.
        /// </summary>
        /// <param name="name">Jméno klipu — vidí se v profileru.</param>
        /// <param name="from">Počáteční frekvence v Hz.</param>
        /// <param name="to">Koncová frekvence v Hz. Stejná jako from = bez skluzu.</param>
        /// <param name="seconds">Délka.</param>
        /// <param name="gain">Špičková hlasitost 0..1.</param>
        /// <param name="harsh">
        /// Přimíchat lichou harmonickou. Používá se u chybových zvuků: čistý
        /// sinus zní neutrálně a chyba se pak přeslechne.
        /// </param>
        public static AudioClip Tone(string name, float from, float to,
            float seconds, float gain, bool harsh = false)
        {
            var count = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var data = new float[count];

            var faze = 0f;

            for (var i = 0; i < count; i++)
            {
                var t = (float)i / count;

                // Skluz se počítá po vzorcích a fáze se integruje — skok
                // ve frekvenci spočítaný přímo z času by lupal.
                var f = Mathf.Lerp(from, to, t);
                faze += 2f * Mathf.PI * f / Rate;

                var v = Mathf.Sin(faze);
                if (harsh) v = v * 0.75f + Mathf.Sin(faze * 3f) * 0.25f;

                data[i] = v * Obalka(i, count, seconds) * gain;
            }

            return FromData(name, data);
        }

        /// <summary>
        /// Několik tónů za sebou. Používá se na potvrzení („zaskočilo",
        /// „hotovo") — vzestupná řada se čte jako úspěch bez učení.
        /// </summary>
        public static AudioClip Sequence(string name, float[] frequencies,
            float stepSeconds, float gain)
        {
            var perStep = Mathf.Max(1, Mathf.RoundToInt(stepSeconds * Rate));
            var data = new float[perStep * frequencies.Length];

            for (var k = 0; k < frequencies.Length; k++)
            {
                var faze = 0f;
                for (var i = 0; i < perStep; i++)
                {
                    faze += 2f * Mathf.PI * frequencies[k] / Rate;
                    data[k * perStep + i] =
                        Mathf.Sin(faze) * Obalka(i, perStep, stepSeconds) * gain;
                }
            }

            return FromData(name, data);
        }

        /// <summary>
        /// Krátký šumový klik. Neutrální potvrzení dotyku — tón by se
        /// při každém klepnutí do menu po chvíli stal otravným.
        /// </summary>
        public static AudioClip Click(string name, float seconds, float gain, int seed)
        {
            var count = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var data = new float[count];
            var rnd = new System.Random(seed);

            // Dolní propust z bílého šumu — čistý šum zní jako porucha.
            var minule = 0f;
            for (var i = 0; i < count; i++)
            {
                var bily = (float)(rnd.NextDouble() * 2.0 - 1.0);
                minule = Mathf.Lerp(minule, bily, 0.35f);
                data[i] = minule * Obalka(i, count, seconds) * gain;
            }

            return FromData(name, data);
        }

        /// <summary>Náběh, plocha, exponenciální doznívání.</summary>
        private static float Obalka(int i, int count, float seconds)
        {
            var t = (float)i / count * seconds;

            var nabeh = Attack <= 0f ? 1f : Mathf.Clamp01(t / Attack);
            var zbytek = 1f - (float)i / count;

            return nabeh * zbytek * zbytek;
        }

        private static AudioClip FromData(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
