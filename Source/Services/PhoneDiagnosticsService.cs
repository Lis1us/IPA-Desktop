using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace IpaDesktop
{
    /// <summary>Только чтение сведений, батареи и диска через фиксированный набор команд. Перезапуск и ремонт не вызываются.</summary>
    public sealed class PhoneDiagnosticsService
    {
        readonly IProcessRunner runner;
        readonly string directory;
        public PhoneDiagnosticsService(IProcessRunner runner, string directory) { this.runner = runner; this.directory = directory; }
        async Task<string> Query(string tool, string[] args, CancellationToken ct)
        {
            string path = Path.Combine(directory, "windows_amd64", tool + ".exe");
            if (!File.Exists(path)) throw new IOException("Не найден комплект диагностики. Распакуйте тестовую сборку целиком с папкой FirmwareTools.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    var result = await runner.Run(path, args, timeout.Token, false, false);
                    if (result.Code != 0) throw new IOException("iPhone не предоставил данные. Проверьте разблокировку, доверие компьютеру и драйвер Apple.");
                    if ((tool != "idevice_id" && String.IsNullOrWhiteSpace(result.Output)) || (result.Output ?? "").Length > 2000000) throw new IOException("Ответ iPhone пустой или слишком большой.");
                    return result.Output ?? "";
                }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); throw new IOException("Время ожидания iPhone истекло (12 секунд)."); }
            }
        }
        public async Task<List<DeviceRow>> FindAsync(CancellationToken ct)
        {
            var output = await Query("idevice_id", new[] { "-l" }, ct);
            var list = new List<DeviceRow>();
            foreach (var id in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Distinct())
            {
                if (!Regex.IsMatch(id, "^[a-fA-F0-9-]{20,}$")) continue;
                try
                {
                    var info = Flat(await Query("ideviceinfo", new[] { "-u", id }, ct));
                    if ((Get(info, "ProductType") ?? "").StartsWith("iPhone", StringComparison.Ordinal))
                        list.Add(new DeviceRow { ID = id, Name = (Get(info, "DeviceName") ?? "iPhone") + " · " + Get(info, "ProductType") + " · " + id.Substring(id.Length - 6) });
                }
                catch (OperationCanceledException) { throw; }
                catch (IOException) { list.Add(new DeviceRow { ID = id, Name = "Устройство · " + id.Substring(id.Length - 6) + " — подтвердите доверие" }); }
            }
            return list;
        }
        static Dictionary<string, string> Flat(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n'))
            {
                int split = line.IndexOf(':');
                if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            return values;
        }
        static string Get(Dictionary<string, string> data, string key) { string value; return data.TryGetValue(key, out value) ? value : null; }
        static double? Number(string text, double min, double max)
        {
            double value;
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max ? (double?)value : null;
        }
        static string Boolean(string text) { return text == "true" || text == "1" ? "Да" : text == "false" || text == "0" ? "Нет" : null; }
        static string Bytes(string text)
        {
            var value = Number(text, 0, 8.0 * 1024 * 1024 * 1024 * 1024);
            return value.HasValue ? (value.Value / 1073741824.0).ToString("0.0", CultureInfo.CurrentCulture) + " ГиБ" : null;
        }
        static string Units(double? number, string suffix) { return number.HasValue ? number.Value.ToString("0.#", CultureInfo.CurrentCulture) + suffix : null; }
        /// <summary>Разбирает только ограниченный XML plist; противоречащие друг другу одноимённые показатели не объединяет.</summary>
        public static Dictionary<string, string> BatteryValues(string xml)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Ignore, MaxCharactersInDocument = 2000000 })) doc.Load(reader);
            if (doc.SelectSingleNode("/plist/dict") == null) throw new InvalidDataException("Неподдерживаемый формат диагностики.");
            var found = new Dictionary<string, List<string>>();
            foreach (XmlNode key in doc.SelectNodes("//dict/key"))
            {
                var next = key.NextSibling;
                while (next != null && next.NodeType != XmlNodeType.Element) next = next.NextSibling;
                if (next == null || !(next.Name == "integer" || next.Name == "real" || next.Name == "string")) continue;
                List<string> values;
                if (!found.TryGetValue(key.InnerText, out values)) { values = new List<string>(); found.Add(key.InnerText, values); }
                values.Add(next.InnerText);
            }
            return found.Where(x => x.Value.Distinct(StringComparer.Ordinal).Count() == 1).ToDictionary(x => x.Key, x => x.Value[0], StringComparer.Ordinal);
        }
        async Task<Dictionary<string, string>> Optional(PhoneDiagnostic report, string stage, string tool, string[] args, bool xml, CancellationToken ct)
        {
            try { string output = await Query(tool, args, ct); return xml ? BatteryValues(output) : Flat(output); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (!(e is IOException || e is XmlException || e is System.ComponentModel.Win32Exception)) throw;
                report.Warnings.Add(stage + ": " + e.Message.Substring(0, Math.Min(e.Message.Length, 240)));
                return new Dictionary<string, string>();
            }
        }
        public async Task<PhoneDiagnostic> ReadAsync(DeviceRow device, CancellationToken ct, Action<string> stage)
        {
            if (device == null || !Regex.IsMatch(device.ID ?? "", "^[a-fA-F0-9-]{20,}$")) throw new ArgumentException("Выберите iPhone.");
            stage("Чтение сведений iPhone…");
            var info = Flat(await Query("ideviceinfo", new[] { "-u", device.ID }, ct));
            string product = Get(info, "ProductType");
            if (Get(info, "UniqueDeviceID") != null && Get(info, "UniqueDeviceID") != device.ID) throw new IOException("Идентификатор устройства изменился. Найдите iPhone заново.");
            if (String.IsNullOrEmpty(product) || !product.StartsWith("iPhone", StringComparison.Ordinal)) throw new IOException("Выбранное устройство не подтверждено как iPhone.");
            var report = new PhoneDiagnostic { Udid = device.ID };
            report.Add("Имя iPhone", Get(info, "DeviceName"), "iPhone", true);
            report.Add("Модель (ProductType)", product, "iPhone");
            report.Add("Номер модели", Get(info, "ModelNumber"), "iPhone");
            report.Add("Аппаратная модель", Get(info, "HardwareModel"), "iPhone");
            report.Add("iOS", Get(info, "ProductVersion"), "iPhone");
            report.Add("Сборка iOS", Get(info, "BuildVersion"), "iPhone");
            report.Add("Регион модели", Get(info, "RegionInfo"), "iPhone");
            report.Add("Состояние активации", Get(info, "ActivationState"), "iPhone");
            report.Add("Серийный номер", Get(info, "SerialNumber"), "iPhone", true);
            report.Add("IMEI", Get(info, "InternationalMobileEquipmentIdentity"), "iPhone", true);
            report.Add("IMEI 2", Get(info, "InternationalMobileEquipmentIdentity2"), "iPhone", true);
            report.Add("UDID", device.ID, "iPhone", true);
            stage("Чтение заряда аккумулятора…");
            var battery = await Optional(report, "Заряд аккумулятора", "ideviceinfo", new[] { "-u", device.ID, "-q", "com.apple.mobile.battery" }, false, ct);
            report.Add("Заряд аккумулятора", Units(Number(Get(battery, "BatteryCurrentCapacity"), 0, 100), " %"), "iPhone");
            report.Add("Аккумулятор заряжается", Boolean(Get(battery, "BatteryIsCharging")), "iPhone");
            report.Add("Внешнее питание", Boolean(Get(battery, "ExternalConnected")), "iPhone");
            stage("Чтение памяти iPhone…");
            var disk = await Optional(report, "Память", "ideviceinfo", new[] { "-u", device.ID, "-q", "com.apple.disk_usage" }, false, ct);
            report.Add("Накопитель", Bytes(Get(disk, "TotalDiskCapacity")), "iPhone");
            report.Add("Раздел данных", Bytes(Get(disk, "TotalDataCapacity")), "iPhone");
            report.Add("Свободно в разделе данных", Bytes(Get(disk, "TotalDataAvailable")), "iPhone");
            stage("Чтение показателей контроллера АКБ…");
            var smart = await Optional(report, "Контроллер АКБ", "idevicediagnostics", new[] { "-u", device.ID, "ioregentry", "AppleSmartBattery" }, true, ct);
            if (Number(Get(smart, "CycleCount"), 0, 100000) == null || Number(Get(smart, "NominalChargeCapacity"), 100, 20000) == null)
            {
                stage("Дополнительный запрос показателей АКБ…");
                var gas = await Optional(report, "Диагностика АКБ", "idevicediagnostics", new[] { "-u", device.ID, "diagnostics", "GasGauge" }, true, ct);
                foreach (var pair in gas) if (!smart.ContainsKey(pair.Key)) smart[pair.Key] = pair.Value;
            }
            double? cycles = Number(Get(smart, "CycleCount"), 0, 100000);
            double? design = Number(Get(smart, "DesignCapacity"), 100, 20000);
            double? nominal = Number(Get(smart, "NominalChargeCapacity"), 100, 20000);
            double? percent = Number(Get(smart, "MaximumCapacityPercent"), 0, 100);
            report.Add("Циклы зарядки", Units(cycles, ""), "Контроллер АКБ");
            report.Add("Проектная ёмкость", Units(design, " мА·ч"), "Контроллер АКБ");
            report.Add("Оценка полной ёмкости", Units(nominal, " мА·ч"), "Контроллер АКБ");
            report.Add("Максимальная ёмкость, сообщённая устройством", Units(percent, " %"), "Контроллер АКБ");
            double? estimated = design.HasValue && nominal.HasValue ? nominal.Value / design.Value * 100 : (double?)null;
            if (estimated.HasValue && (estimated.Value < 20 || estimated.Value > 130)) estimated = null;
            report.Add("Соотношение ёмкостей (оценка)", Units(estimated, " %"), "Расчёт: полная / проектная");
            if (!cycles.HasValue || !design.HasValue || !nominal.HasValue) report.Warnings.Add("Часть показателей АКБ не предоставлена. «Недоступно» не означает неисправность или замену детали.");
            ct.ThrowIfCancellationRequested();
            report.Created = DateTime.Now;
            return report;
        }
    }
}
