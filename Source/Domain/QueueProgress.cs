using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
namespace IpaDesktop
{
    /// <summary>Потокобезопасное состояние прогресса; использует только метрики, реально полученные от утилиты.</summary>
    public sealed class TransferUpdate
    {
        public double Percent { get; set; }
        public string Metrics { get; set; }
        public static TransferUpdate Parse(string line, bool installing)
        {
            if (line == null) return null;
            var match = Regex.Match(line, @"^PROGRESS:(\d{1,3})\s*$");
            if (!match.Success && installing) match = Regex.Match(line, @"\((\d{1,3})%\)\s*$");
            if (match.Success)
            {
                int percent;
                if (!Int32.TryParse(match.Groups[1].Value, out percent) || percent > 100) return null;
                return new TransferUpdate { Percent = percent };
            }
            match = Regex.Match(line, @"^Downloading:\s*(\d{1,3})%.*\((\d+(?:\.\d+)?\s*(?:MB|GB)/\d+(?:\.\d+)?\s*(?:MB|GB),\s*\d+(?:\.\d+)?\s*MB/s)\)");
            if (!match.Success) return null;
            var value = Double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return value <= 100 ? new TransferUpdate { Percent = value, Metrics = match.Groups[2].Value } : null;
        }
    }
    /// <summary>Потокобезопасное состояние прогресса; использует только метрики, реально полученные от утилиты.</summary>
    public sealed class QueueProgress
    {
        readonly object gate = new object();
        readonly Stopwatch elapsed = new Stopwatch();
        string kind, name, metrics, summary;
        int index, count;
        double? percent;
        public void Reset() { lock (gate) { kind = null; summary = null; elapsed.Reset(); } }
        public void Begin(string operation, int position, int total, string app)
        {
            lock (gate) { kind = operation; index = position; count = total; name = app; metrics = null; summary = null; percent = null; elapsed.Restart(); }
        }
        /// <summary>Разбирает реальный вывод CLI под блокировкой; неподдерживаемые метрики не выдумываются.</summary>
        public bool Feed(string line)
        {
            lock (gate)
            {
                if (kind == null || summary != null) return false;
                var update = TransferUpdate.Parse(line, kind == "Установка");
                if (update == null) return false;
                percent = update.Percent; metrics = update.Metrics;
                return true;
            }
        }
        public void Complete(int success, int failures)
        {
            lock (gate) { summary = "Готово: " + success + " · ошибок: " + failures + " · всего: " + count; percent = 100; index = count; elapsed.Stop(); }
        }
        /// <summary>Фиксирует отмену или ошибку и прекращает приём поздних сообщений прогресса.</summary>
        public void Stop(string reason)
        {
            lock (gate)
            {
                if (kind == null || summary != null) return;
                elapsed.Stop(); summary = reason + " · " + index + "/" + count + ": " + name;
            }
        }
        public bool Active { get { lock (gate) return kind != null; } }
        public double Value { get { lock (gate) return count > 0 ? 100.0 * (index - 1 + (percent ?? 0) / 100.0) / count : 0; } }
        public bool Indeterminate { get { lock (gate) return !percent.HasValue; } }
        public string Text
        {
            get
            {
                lock (gate)
                {
                    if (kind == null) return "";
                    if (summary != null) return summary;
                    return kind + " " + index + "/" + count + ": " + name + " · осталось после текущего: " + Math.Max(0, count - index) + " · " + (percent.HasValue ? percent.Value.ToString("0") + "%" : "выполняется") + " · " + elapsed.Elapsed.ToString(@"hh\:mm\:ss") + (metrics == null ? (kind == "Скачивание" ? " · объём и скорость недоступны" : "") : " · " + metrics);
                }
            }
        }
    }
}
