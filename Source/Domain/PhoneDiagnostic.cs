using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace IpaDesktop
{
    /// <summary>Одно измерение; отсутствие показания никогда не заменяется нулём.</summary>
    public sealed class PhoneDiagnosticValue
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Source { get; set; }
        public bool Sensitive { get; set; }
    }
    /// <summary>Снимок проверки с временем получения, неполными показаниями и экспортом с маскированием.</summary>
    public sealed class PhoneDiagnostic
    {
        public DateTime Created { get; set; }
        public string Udid { get; set; }
        public List<PhoneDiagnosticValue> Values { get; private set; }
        public List<string> Warnings { get; private set; }
        public PhoneDiagnostic()
        {
            Created = DateTime.Now;
            Values = new List<PhoneDiagnosticValue>();
            Warnings = new List<string>();
        }
        public void Add(string name, string value, string source, bool sensitive = false)
        {
            Values.Add(new PhoneDiagnosticValue { Name = name, Value = String.IsNullOrWhiteSpace(value) ? "Недоступно" : value, Source = source, Sensitive = sensitive });
        }
        public List<PhoneDiagnosticValue> Display(bool hideIdentifiers)
        {
            return Values.Select(x => new PhoneDiagnosticValue { Name = x.Name, Source = x.Source,
                Value = hideIdentifiers && x.Sensitive && x.Value != "Недоступно" ? "[СКРЫТО]" : x.Value }).ToList();
        }
        public string Export(bool hideIdentifiers)
        {
            var text = new StringBuilder("IPA Desktop — диагностика iPhone\r\n");
            text.AppendLine("Дата: " + Created.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            foreach (var item in Display(hideIdentifiers)) text.AppendLine(item.Name + ": " + item.Value + " · " + item.Source);
            if (Warnings.Count > 0) { text.AppendLine(); text.AppendLine("Ограничения проверки:"); foreach (var warning in Warnings) text.AppendLine("• " + warning); }
            text.AppendLine();
            text.AppendLine("Показания предоставлены устройством. Расчёт ёмкости может отличаться от состояния АКБ в настройках iOS.");
            if (!hideIdentifiers) return text.ToString();
            return DiagnosticReport.Redact(text.ToString(), new[] { Udid });
        }
    }
}
