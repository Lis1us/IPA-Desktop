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
        public async Task<List<DeviceRow>> FindAsync(CancellationToken ct)
        {
            Result result;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    result = await runner.Run(locator.Find("idevice_id*.exe"), new[]
                    {
                        "-l"
                    }, timeout.Token, false, false);
                } catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new IOException("Поиск iPhone не ответил за 15 секунд. Проверьте USB и драйвер Apple.");
                }
            }
            ToolResponse.Ensure(result);
            var ids = result.Output.Split(new[]
            {
                '\r', '\n'
            }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[0-9A-Fa-f-]{20,}$")).Distinct().ToList();
            var devices = new List<DeviceRow>();
            foreach (var id in ids)
            {
                string name = "iPhone · " + id.Substring(Math.Max(0, id.Length - 6));
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var info = await runner.Run(locator.Find("ideviceinfo*.exe"), new[]
                        {
                            "-u", id, "-k", "DeviceName"
                        }, timeout.Token, false, false);
                        ToolResponse.Ensure(info);
                        string value = info.Output.Trim();
                        if (value.StartsWith("DeviceName:", StringComparison.Ordinal)) value = value.Substring(11).Trim();
                        if (value.Length > 0 && value.Length <= 200 && !value.Any(Char.IsControl)) name = value;
                        else Log("Имя iPhone не получено. Используется короткий идентификатор.");
                    }
                } catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    Log("Время ожидания имени iPhone истекло. Разблокируйте устройство и подтвердите доверие.");
                } catch (Exception e)
                {
                    Log("Не удалось прочитать имя iPhone: " + e.Message + ". Разблокируйте устройство и подтвердите доверие.");
                }
                devices.Add(new DeviceRow
                {
                    ID = id, Name = name
                });
            }
            foreach (var group in devices.GroupBy(x => x.Name).Where(g => g.Count() > 1)) foreach (var device in group) device.Name += " · " + device.ID.Substring(Math.Max(0, device.ID.Length - 6));
            return devices;
        }
        /// <summary>Устанавливает указанный IPA на выбранный UDID с поддержкой отмены.</summary>
        public async Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct)
        {
            if (!File.Exists(file.Path)) throw new IOException("IPA-файл больше не существует.");
            ToolResponse.Ensure(await runner.Run(locator.Find("ideviceinstaller*.exe"), new[]
            {
                "--udid", device.ID, "install", file.Path
            }, ct, false, true));
        }
    }
}
