using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Изолированный комплект прошивки. Обычные обновления DeviceTools его не заменяют.</summary>
    public sealed class FirmwareService
    {
        readonly IProcessRunner runner;
        readonly string directory;
        public FirmwareService(IProcessRunner runner, string directory) { this.runner = runner; this.directory = directory; }
        string Tool(string name)
        {
            string path = Path.Combine(directory, "windows_amd64", name + ".exe");
            if (!File.Exists(path)) throw new IOException("Комплект прошивки отсутствует. Распакуйте тестовую сборку целиком, включая папку FirmwareTools.");
            return path;
        }
        async Task<Result> Inspect(string name, string[] args, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    var result = await runner.Run(Tool(name), args, timeout.Token, false, false);
                    ToolResponse.Ensure(result); return result;
                }
                catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new IOException("iPhone или инструмент не ответил за 20 секунд.");
                }
            }
        }
        /// <summary>Первая экспериментальная версия начинает работу только с доверенным iPhone в обычном режиме.</summary>
        public async Task<List<FirmwareDevice>> FindAsync(CancellationToken ct)
        {
            var help = await Inspect("idevicerestore", new[] { "--help" }, ct);
            if (!help.Output.Contains("--ipa-desktop-update-only")) throw new IOException("Установлен idevicerestore без нашей защиты от стирания. Запуск запрещён.");
            var list = await Inspect("idevice_id", new[] { "-l" }, ct);
            var devices = new List<FirmwareDevice>();
            foreach (var id in list.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Distinct())
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-fA-F0-9-]{20,}$")) continue;
                devices.Add(await ReadDevice(id, ct));
            }
            return devices;
        }
        async Task<FirmwareDevice> ReadDevice(string id, CancellationToken ct)
        {
            var info = await Inspect("ideviceinfo", new[] { "-u", id }, ct);
            var values = new Dictionary<string, string>();
            foreach (var line in info.Output.Split('\n'))
            {
                int i = line.IndexOf(':');
                if (i > 0) values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            string[] required = { "UniqueChipID", "DeviceName", "ProductType", "HardwareModel", "ProductVersion" };
            if (required.Any(x => !values.ContainsKey(x))) throw new IOException("Не получены идентификатор, модель или версия iPhone. Разблокируйте его и подтвердите доверие.");
            ulong ecid;
            if (!UInt64.TryParse(values["UniqueChipID"], out ecid) || ecid == 0) throw new IOException("Некорректный ECID iPhone.");
            return new FirmwareDevice { Udid = id, Ecid = ecid.ToString(System.Globalization.CultureInfo.InvariantCulture), Name = values["DeviceName"], ProductType = values["ProductType"], HardwareModel = values["HardwareModel"], Version = values["ProductVersion"] };
        }
        /// <summary>Проверяет устройство заново; блокирует изменение файла и использует ASCII-ссылку для нативного CLI.</summary>
        public async Task RestoreAsync(FirmwareDevice chosen, string path, FirmwareManifest expected, bool erase, CancellationToken ct, Action beginCritical, Action endCritical)
        {
            var current = await ReadDevice(chosen.Udid, ct);
            if (current.Ecid != chosen.Ecid || current.ProductType != chosen.ProductType || current.HardwareModel != chosen.HardwareModel || current.Version != chosen.Version)
                throw new IOException("Состояние iPhone изменилось. Найдите устройство и подтвердите операцию заново.");
            var help = await Inspect("idevicerestore", new[] { "--help" }, ct);
            if (!help.Output.Contains("--ipa-desktop-update-only")) throw new IOException("Нет обязательной защиты от стирания.");
            using (var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Task.Run(delegate {
                    var actual = FirmwareManifest.Read(path);
                    actual.Validate(current, erase);
                    if (actual.Version != expected.Version || actual.Build != expected.Build) throw new IOException("Выбранная IPSW изменилась. Выполните проверку и подтверждение заново.");
                }, ct);
                using (var package = await InstallationPackage.CreateAsync(path, ct, ".ipsw"))
                using (var locked = new FileStream(Path.Combine(package.WorkingDirectory, package.RelativePath), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // Проверяем именно подготовленный файл; исходный и временный файлы нельзя заменить до завершения.
                    await Task.Run(() => FirmwareManifest.Read(locked.Name).Validate(current, erase), ct);
                    ct.ThrowIfCancellationRequested();
                    var args = new[] { "--ecid", current.Ecid, "--no-input", "--plain-progress", "--logfile", "NONE", "--cache-path", Path.Combine(Path.GetDirectoryName(package.RelativePath), "cache"), erase ? "--erase" : "--ipa-desktop-update-only", package.RelativePath };
                    beginCritical();
                    try
                    {
                        // Принудительное убийство процесса во время прошивки запрещено; отмена доступна до этой точки.
                        ToolResponse.Ensure(await runner.Run(Tool("idevicerestore"), args, CancellationToken.None, false, true, package.WorkingDirectory));
                    }
                    finally { endCritical(); }
                }
            }
        }
    }
}
