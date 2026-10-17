using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace IpaDesktop
{
    /// <summary>Обнаруживает iPhone и устанавливает IPA по реальному UDID, а не по подписи устройства.</summary>
    public sealed class DeviceService
    {
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        public Action<string> Log
        {
            get;
            set;
        }
        public DeviceService(IProcessRunner runner, ToolLocator locator)
        {
            this.runner = runner;
            this.locator = locator;
            Log = delegate
            {
            };
        }
        /// <summary>Получает UDID подключённых устройств и добавляет понятные имена для выбора в интерфейсе.</summary>
        public Task<List<DeviceRow>> FindAsync(CancellationToken ct) { return FindAsync(false, ct); }
        public async Task<List<DeviceRow>> FindAsync(bool includeWifi, CancellationToken ct)
        {
            var devices = new List<DeviceRow>(); bool usbFailed = false;
            try { devices = await FindTransport(false, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { if (!includeWifi) throw; usbFailed = true; Log("Поиск USB: " + e.Message); }
            if (includeWifi)
            {
                try { devices.AddRange(await FindTransport(true, ct)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { if (usbFailed) throw new IOException("Не удалось проверить USB и Wi-Fi. Проверьте службу Apple и соединение.\n" + e.Message, e); Log("Поиск Wi-Fi: " + e.Message + ". USB-устройства сохранены. Проверьте сетевую синхронизацию в Apple Devices/iTunes."); }
            }
            foreach (var group in devices.GroupBy(x => x.Name + "|" + x.Connection).Where(g => g.Count() > 1))
                foreach (var device in group) device.Name += " · " + device.ID.Substring(Math.Max(0, device.ID.Length - 6));
            return devices;
        }
        async Task<List<DeviceRow>> FindTransport(bool network, CancellationToken ct)
        {
            var result = await Probe(locator.Find("idevice_id*.exe"), new[] { network ? "-n" : "-l" }, ct);
            var ids = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[0-9A-Fa-f-]{20,}$")).Distinct().ToList();
            var devices = new List<DeviceRow>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                var device = new DeviceRow { ID = id, Network = network, Name = "iPhone · " + id.Substring(Math.Max(0, id.Length - 6)) };
                try
                {
                    var info = await Probe(locator.Find("ideviceinfo*.exe"), Arguments(device, "-u", id, "-k", "DeviceName"), ct);
                    string value = info.Output.Trim();
                    if (value.StartsWith("DeviceName:", StringComparison.Ordinal)) value = value.Substring(11).Trim();
                    if (value.Length > 0 && value.Length <= 200 && !value.Any(Char.IsControl)) device.Name = value;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Log("Имя iPhone (" + device.Connection + ") не получено: " + e.Message); }
                devices.Add(device);
            }
            return devices;
        }
        /// <summary>Один транспорт используется во всех командах; не допускает неявного перехода с Wi-Fi на USB.</summary>
        public static string[] Arguments(DeviceRow device, params string[] args)
        {
            if (device == null || !System.Text.RegularExpressions.Regex.IsMatch(device.ID ?? "", "^[0-9A-Fa-f-]{20,}$")) throw new IOException("Некорректный идентификатор iPhone.");
            return device.Network ? new[] { "--network" }.Concat(args).ToArray() : args;
        }
        async Task<Result> Probe(string exe, string[] args, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try { var result = await runner.Run(exe, args, timeout.Token, false, false); ToolResponse.Ensure(result); return result; }
                catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new IOException("iPhone не ответил за 15 секунд. Разблокируйте телефон; проверьте USB либо Wi-Fi и доверие компьютеру.");
                }
            }
        }
        public async Task<Dictionary<string, string>> ReadDeviceAsync(DeviceRow device, CancellationToken ct)
        {
            var result = await Probe(locator.Find("ideviceinfo*.exe"), Arguments(device, "-u", device.ID), ct);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in result.Output.Split('\n'))
            {
                int split = line.IndexOf(':');
                if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            string actual;
            if (values.TryGetValue("UniqueDeviceID", out actual) && !String.Equals(actual, device.ID, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ответил другой iPhone. Повторите поиск устройств.");
            if (device.Network && (!values.TryGetValue("ProductType", out actual) || !actual.StartsWith("iPhone", StringComparison.Ordinal)))
                throw new IOException("Не удалось подтвердить связь с выбранным iPhone по Wi-Fi. Разблокируйте телефон и проверьте сетевую синхронизацию.");
            return values;
        }
        /// <summary>По запросу читает модель и iOS одним вызовом ideviceinfo с ограниченным ожиданием.</summary>
        public async Task<string> DetailsAsync(DeviceRow device, CancellationToken ct)
        {
            var values = await ReadDeviceAsync(device, ct);
            string model, version;
            if (!values.TryGetValue("ProductType", out model)) model = "не удалось прочитать";
            if (!values.TryGetValue("ProductVersion", out version)) version = "не удалось прочитать";
            return "Имя: " + device.Name + Environment.NewLine + "Подключение: " + device.Connection + Environment.NewLine +
                "Модель (ProductType): " + model + Environment.NewLine + "iOS: " + version + Environment.NewLine + "UDID: " + device.ID;
        }
        /// <summary>Устанавливает указанный IPA на выбранный UDID с поддержкой отмены.</summary>
        public async Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct)
        {
            if (!File.Exists(file.Path)) throw new IOException("IPA-файл больше не существует.");
            try
            {
                // Проверяем тот файл, который действительно отправляем, и выбранный транспорт непосредственно перед установкой.
                using (var package = await InstallationPackage.CreateAsync(file.Path, ct))
                {
                    var details = await Task.Run(() => IpaMetadata.ReadDetails(Path.Combine(package.WorkingDirectory, package.RelativePath)), ct);
                    var info = await ReadDeviceAsync(device, ct);
                    string ios; info.TryGetValue("ProductVersion", out ios);
                    string reason = AppCompatibility.BlockReason(details, ios);
                    if (reason != null) throw new IOException(reason);
                    if (String.IsNullOrEmpty(details.MinimumOS) || details.Platforms == null || details.Platforms.Length == 0 || String.IsNullOrEmpty(ios))
                        Log("Совместимость " + file.DisplayName + ": часть метаданных недоступна; окончательную проверку выполнит iPhone.");
                    ct.ThrowIfCancellationRequested();
                    ToolResponse.Ensure(await runner.Run(locator.Find("ideviceinstaller*.exe"),
                        Arguments(device, "--udid", device.ID, "install", package.RelativePath), ct, false, true, package.WorkingDirectory));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (!device.Network) throw;
                throw new IOException("Установка по Wi-Fi не завершена. Проверьте связь и состояние приложения на iPhone. Разблокируйте телефон, проверьте общую сеть и сетевую синхронизацию; при необходимости выберите USB и повторите только ошибки.\n" + e.Message, e);
            }
        }
    }
}
