using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BP.Core;
using UnityEngine;

namespace BP.Logging
{
    /// <summary>
    /// Typy událostí, které se logují. Jeden řádek CSV = jedna událost.
    /// Formát "dlouhý" (event log) je záměrný — agregace na completion time,
    /// error rate atd. se dělá až v analýze, takže se nic neztratí.
    /// </summary>
    public enum LogEvent
    {
        BlockStart,
        BlockEnd,
        TemplateShown,
        ObjectRequested,
        ObjectSpawned,
        ObjectPlaced,
        ObjectRemoved,
        WrongObjectCreated,
        UndoUsed,
        VoiceRejected,
        VoiceMisrecognized,
        StepCompleted,
        SecondaryTargetActivated,
        SecondaryTargetHit,
        SecondaryTargetMissed,
        Note
    }

    /// <summary>
    /// CSV logger pro jeden blok. Píše průběžně a flushuje po každém řádku —
    /// když aplikace na Questu spadne nebo se vybije, data z proběhlé části
    /// bloku zůstanou na disku.
    ///
    /// Soubory jdou do Application.persistentDataPath, na Questu tedy
    /// /sdcard/Android/data/&lt;bundleId&gt;/files/ — odtud se stahují přes adb.
    /// </summary>
    public class TrialLogger : IDisposable
    {
        private const string Delimiter = ";";

        private StreamWriter _writer;
        private float _blockStartTime;
        private bool _disposed;

        public string FilePath { get; private set; }
        public bool IsOpen => _writer != null;

        /// <summary>Sekundy od začátku bloku — hlavní časová osa pro analýzu.</summary>
        public float BlockTime => Time.realtimeSinceStartup - _blockStartTime;

        public void OpenBlock(
            string participantId,
            InteractionCondition condition,
            LoadCondition load,
            string templateId,
            int blockIndex)
        {
            Close();

            var dir = Path.Combine(Application.persistentDataPath, "BP_Data");
            Directory.CreateDirectory(dir);

            // Timestamp v názvu = žádné přepsání dat, i kdyby se blok opakoval.
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"{participantId}_B{blockIndex}_{condition}_{load}_{templateId}_{stamp}.csv";

            FilePath = Path.Combine(dir, fileName);
            _writer = new StreamWriter(FilePath, false, Encoding.UTF8);

            // Metadata bloku jako komentářové řádky — analýza je přeskočí,
            // ale soubor je díky nim sebe-popisný.
            _writer.WriteLine($"# participant{Delimiter}{participantId}");
            _writer.WriteLine($"# condition{Delimiter}{condition}");
            _writer.WriteLine($"# load{Delimiter}{load}");
            _writer.WriteLine($"# template{Delimiter}{templateId}");
            _writer.WriteLine($"# block{Delimiter}{blockIndex}");
            _writer.WriteLine($"# started{Delimiter}{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            _writer.WriteLine(string.Join(Delimiter,
                "t", "event", "step", "shape", "color",
                "pos_err_m", "rot_err_deg", "rt_s", "detail"));

            _blockStartTime = Time.realtimeSinceStartup;
            _writer.Flush();

            Log(LogEvent.BlockStart);
        }

        /// <summary>
        /// Zapíše jednu událost. Nepovinné parametry se nechávají prázdné,
        /// ať se v CSV nemíchají nuly s "chybí hodnota".
        /// </summary>
        public void Log(
            LogEvent evt,
            int? step = null,
            ShapeType? shape = null,
            PaletteColor? color = null,
            float? positionError = null,
            float? rotationError = null,
            float? reactionTime = null,
            string detail = null)
        {
            if (_writer == null)
            {
                Debug.LogWarning($"[TrialLogger] Logování bez otevřeného bloku: {evt}");
                return;
            }

            var row = string.Join(Delimiter,
                F(BlockTime),
                evt.ToString(),
                step?.ToString(CultureInfo.InvariantCulture) ?? "",
                shape?.ToString() ?? "",
                color?.ToString() ?? "",
                F(positionError),
                F(rotationError),
                F(reactionTime),
                Sanitize(detail));

            _writer.WriteLine(row);
            _writer.Flush();
        }

        public void CloseBlock(string summary = null)
        {
            if (_writer == null) return;

            Log(LogEvent.BlockEnd, detail: summary);
            Close();
        }

        /// <summary>Seznam už zapsaných souborů — pro kontrolu, že se data ukládají.</summary>
        public static List<string> ListDataFiles()
        {
            var dir = Path.Combine(Application.persistentDataPath, "BP_Data");
            return Directory.Exists(dir)
                ? new List<string>(Directory.GetFiles(dir, "*.csv"))
                : new List<string>();
        }

        // Invariantní kultura je zásadní: na české lokalizaci by se float
        // zapsal s desetinnou čárkou a rozbil by CSV se středníkem.
        private static string F(float? v)
            => v.HasValue ? v.Value.ToString("F4", CultureInfo.InvariantCulture) : "";

        private static string Sanitize(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace(Delimiter, ",").Replace("\n", " ").Replace("\r", "");

        private void Close()
        {
            if (_writer == null) return;

            _writer.Flush();
            _writer.Dispose();
            _writer = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }
}
