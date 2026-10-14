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
    public sealed class EngineUpdater
    {
        public const string SourceRepository = "Sorvigolova/ipatool";
        public const string WindowsAssetName = "windows_amd64_ipatool-cpp.zip";
        static readonly string[] DeviceFiles = { "idevice_id.exe", "ideviceinfo.exe", "ideviceinstaller.exe", "libimobiledevice-1.0.dll", "libplist-2.0.dll" };
        public static Asset SelectAsset(Release release)
        {
            var matches = (release.assets ?? new Asset[0]).Where(a => a.name == WindowsAssetName).ToArray();
            if (matches.Length != 1) throw new IOException("В релизе ipatool ожидается один Windows x64 ZIP: " + WindowsAssetName);
            ValidateAsset(matches[0], SourceRepository);
            return matches[0];
        }
        static void ValidateAsset(Asset asset, string repository)
        {
            if (asset.browser_download_url == null || !asset.browser_download_url.StartsWith("https://github.com/" + repository + "/releases/download/", StringComparison.Ordinal)) throw new IOException("Неожиданный источник архива.");
            if (asset.size <= 0 || asset.size > 1000000000) throw new IOException("Некорректный размер архива.");
        }
        public static Asset SelectDeviceAsset(Release release)
        {
            var matches = (release.assets ?? new Asset[0]).Where(a => a.name.StartsWith("IPA_Downloader", StringComparison.OrdinalIgnoreCase) && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new IOException("Не найден однозначный архив инструментов для iPhone.");
            ValidateAsset(matches[0], "kda2495/IPA_Downloader");
            return matches[0];
        }
        public static bool HasDeviceTools(string root)
        {
            if (!Directory.Exists(root)) return false;
            return DeviceFiles.All(file => Directory.GetFiles(root, file, SearchOption.AllDirectories).Count(x => x.Replace('\\', '/').Contains("/windows_amd64/")) == 1);
        }
        public static void CopyDeviceTools(string source, string destination)
        {
            var bins = Directory.GetFiles(source, "ideviceinstaller.exe", SearchOption.AllDirectories).Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).ToArray();
            if (bins.Length != 1) throw new IOException("Не найден комплект Windows-инструментов для iPhone.");
            var folder = Path.GetDirectoryName(bins[0]);
            if (DeviceFiles.Any(file => !File.Exists(Path.Combine(folder, file)))) throw new IOException("Неполный комплект инструментов для iPhone.");
            Directory.CreateDirectory(destination);
            // Keep only device executables and their shipped DLL dependencies.
            foreach (var file in DeviceFiles.Take(3).Select(x => Path.Combine(folder, x)).Concat(Directory.GetFiles(folder, "*.dll"))) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        }

        readonly SettingsStore store;
        readonly IProcessRunner runner;
        static HttpClient Http
        {
            get
            {
                return NetworkClient.Http;
            }
        }
        string Root
        {
            get
            {
                return store.Root;
            }
        }
        State Settings
        {
            get
            {
                return store.Settings;
            }
            set
            {
                store.Settings = value;
            }
        }
        void Save()
        {
            store.Save();
        }
        public Action<string> Log
        {
            get;
            set;
        }
        public Action<double> Progress
        {
            get;
            set;
        }
        public EngineUpdater(SettingsStore store, IProcessRunner runner)
        {
            this.store = store;
            this.runner = runner;
            Log = delegate
            {
            };
            Progress = delegate
            {
            };
        }
        public async Task<Release> Check(CancellationToken ct)
        {
            using (var response = await Http.GetAsync("https://api.github.com/repos/Sorvigolova/ipatool/releases/latest", ct))
            {
                response.EnsureSuccessStatusCode();
                return JsonCodec.Deserialize<Release> (await response.Content.ReadAsStringAsync());
            }
        }
        public static void Extract(string zip, string dest)
        {
            Directory.CreateDirectory(dest);
            var prefix = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
            using (var z = ZipFile.OpenRead(zip))
            {
                if (z.Entries.Count > 30000 || z.Entries.Sum(x => x.Length) > 2000000000L) throw new IOException("Архив превышает допустимый размер.");
                foreach (var e in z.Entries)
                {
                    var p = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                    if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Небезопасный путь в ZIP.");
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(p);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    e.ExtractToFile(p, false);
                }
            }
        }
        public async Task Install(Release rel, CancellationToken ct)
        {
            var asset = SelectAsset(rel);
            var name = Guid.NewGuid().ToString("N");
            var staging = Path.Combine(Root, "Versions", name);
            var zip = Path.Combine(Root, name + ".zip");
            var supportName = Settings.DeviceCurrent ?? Settings.Current;
            var locator = new ToolLocator(store);
            var needSupport = String.IsNullOrEmpty(supportName) || !HasDeviceTools(locator.VersionRoot(supportName));
            var newSupport = "support-" + name;
            var supportStage = Path.Combine(Root, "Versions", newSupport);
            var bundleStage = Path.Combine(Root, "bundle-" + name);
            var supportZip = Path.Combine(Root, "support-" + name + ".zip");
            var supportVersion = Settings.DeviceVersion ?? Settings.Version;
            bool committed = false;
            try
            {
                Log("Загрузка релиза " + rel.tag_name);
                await Download(asset, zip, ct);
                if (needSupport)
                {
                    Log("Получение только Windows-инструментов установки из IPA_Downloader…");
                    var supportRelease = await CheckDeviceTools(ct);
                    await Download(SelectDeviceAsset(supportRelease), supportZip, ct);
                    await Task.Run(delegate
                    {
                        Extract(supportZip, bundleStage);
                        CopyDeviceTools(bundleStage, Path.Combine(supportStage, "windows_amd64"));
                    }, ct);
                    await ValidateDeviceTools(supportStage, ct);
                    supportName = newSupport;
                    supportVersion = supportRelease.tag_name;
                }
                ct.ThrowIfCancellationRequested();
                Log("Распаковка и проверка команд…");
                await Task.Run(delegate
                {
                    Extract(zip, Path.Combine(staging, "windows_amd64"));
                }, ct);
                var bins = Directory.GetFiles(staging, "ipatool*.exe", SearchOption.AllDirectories).Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).ToArray();
                if (bins.Length != 1) throw new IOException("В архиве нет ожидаемого Windows-движка.");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var test = await runner.Run(bins[0], new string[0], timeout.Token, false, false);
                    ToolResponse.Ensure(test);
                    foreach (var command in new[]
                    {
                        "auth", "search", "download", "list-versions", "get-version-metadata", "--format", "--external-version-id"
                    }) if (!test.Output.Contains(command)) throw new IOException("Релиз несовместим с адаптером: нет " + command);
                }
                ct.ThrowIfCancellationRequested();
                var previous = Settings;
                Settings = previous.Copy();
                Settings.Source = SourceRepository;
                Settings.PreviousSource = previous.Source;
                Settings.DeviceCurrent = supportName;
                Settings.DeviceVersion = supportVersion;
                Settings.Current = name;
                Settings.Version = rel.tag_name;
                Settings.Previous = previous.Current;
                Settings.PreviousVersion = previous.Version;
                try
                {
                    Save();
                    committed = true;
                } catch
                {
                    Settings = previous;
                    throw;
                }
                Log("Установлен движок " + rel.tag_name + ". Предыдущая версия сохранена.");
            } finally
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (File.Exists(supportZip)) File.Delete(supportZip);
                if (Directory.Exists(bundleStage)) Directory.Delete(bundleStage, true);
                if (!committed && Directory.Exists(staging)) Directory.Delete(staging, true);
                if (!committed && Directory.Exists(supportStage)) Directory.Delete(supportStage, true);
            }
        }
        async Task Download(Asset asset, string zip, CancellationToken ct)
        {
            using (var response = await Http.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync()) using (var output = File.Create(zip))
                {
                    var buf = new byte[131072];
                    int n;
                    long total = 0;
                    while ((n = await input.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        total += n;
                        if (total > asset.size) throw new IOException("Размер архива не совпадает.");
                        await output.WriteAsync(buf, 0, n, ct);
                        Progress(100.0 * total / asset.size);
                    }
                    if (total != asset.size) throw new IOException("Архив загружен не полностью.");
                }
            }
            if (!String.IsNullOrEmpty(asset.digest))
            {
                if (!asset.digest.StartsWith("sha256:")) throw new IOException("Неизвестный формат контрольной суммы.");
                using (var sha = SHA256.Create()) using (var f = File.OpenRead(zip))
                {
                    var digest = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (!String.Equals("sha256:" + digest, asset.digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма не совпадает.");
                }
            } else Log("GitHub не предоставил SHA-256; проверены HTTPS-источник и размер архива.");
        }
        public async Task<Release> CheckDeviceTools(CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await Http.GetAsync("https://api.github.com/repos/kda2495/IPA_Downloader/releases/latest", timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    return JsonCodec.Deserialize<Release>(await response.Content.ReadAsStringAsync());
                }
            }
        }
        public bool DeviceToolsCurrent(Release release)
        {
            var current = Settings.DeviceCurrent ?? Settings.Current;
            var version = Settings.DeviceVersion ?? (Settings.Source == null ? Settings.Version : null);
            return !String.IsNullOrEmpty(current) && release.tag_name == version && HasDeviceTools(new ToolLocator(store).VersionRoot(current));
        }
        async Task ValidateDeviceTools(string root, CancellationToken ct)
        {
            if (!HasDeviceTools(root)) throw new IOException("Неполный комплект инструментов для iPhone.");
            foreach (var name in DeviceFiles.Take(3))
            {
                var file = Directory.GetFiles(root, name, SearchOption.AllDirectories).Single();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var result = await runner.Run(file, new[] { "--version" }, timeout.Token, false, false);
                    ToolResponse.Ensure(result);
                    if (String.IsNullOrWhiteSpace(result.Output)) throw new IOException("Инструмент не сообщил версию: " + name);
                }
            }
        }
        public async Task InstallDeviceTools(Release release, CancellationToken ct)
        {
            var asset = SelectDeviceAsset(release);
            var name = "support-" + Guid.NewGuid().ToString("N");
            var stage = Path.Combine(Root, "Versions", name);
            var bundle = Path.Combine(Root, "bundle-" + name);
            var zip = Path.Combine(Root, name + ".zip");
            bool committed = false;
            try
            {
                Log("Обновление инструментов для iPhone из IPA_Downloader " + release.tag_name);
                await Download(asset, zip, ct);
                await Task.Run(delegate
                {
                    Extract(zip, bundle);
                    CopyDeviceTools(bundle, Path.Combine(stage, "windows_amd64"));
                }, ct);
                await ValidateDeviceTools(stage, ct);
                ct.ThrowIfCancellationRequested();
                var old = Settings;
                Settings = old.Copy();
                Settings.PreviousDeviceCurrent = old.DeviceCurrent ?? (old.Source == null ? old.Current : null);
                Settings.PreviousDeviceVersion = old.DeviceVersion ?? (old.Source == null ? old.Version : null);
                Settings.DeviceCurrent = name;
                Settings.DeviceVersion = release.tag_name;
                try { Save(); committed = true; }
                catch { Settings = old; throw; }
                Log("Инструменты для iPhone обновлены. Предыдущий комплект сохранён.");
            }
            finally
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (Directory.Exists(bundle)) Directory.Delete(bundle, true);
                if (!committed && Directory.Exists(stage)) Directory.Delete(stage, true);
            }
        }
        public void RollbackDeviceTools()
        {
            var old = Settings;
            if (String.IsNullOrEmpty(old.PreviousDeviceCurrent) || !HasDeviceTools(new ToolLocator(store).VersionRoot(old.PreviousDeviceCurrent))) throw new IOException("Предыдущего комплекта инструментов для iPhone пока нет.");
            Settings = old.Copy();
            Settings.DeviceCurrent = old.PreviousDeviceCurrent;
            Settings.DeviceVersion = old.PreviousDeviceVersion;
            Settings.PreviousDeviceCurrent = old.DeviceCurrent ?? old.Current;
            Settings.PreviousDeviceVersion = old.DeviceVersion ?? old.Version;
            Settings.AutoUpdateDeviceTools = false;
            try { Save(); }
            catch { Settings = old; throw; }
            Log("Комплект для iPhone восстановлен. Его автообновление отключено.");
        }
        public void Rollback()
        {
            if (String.IsNullOrEmpty(Settings.Previous) || !Directory.Exists(Path.Combine(Root, "Versions", Settings.Previous))) throw new Exception("Предыдущей версии пока нет.");
            var old = Settings;
            Settings = old.Copy();
            Settings.Source = old.PreviousSource;
            Settings.PreviousSource = old.Source;
            Settings.DeviceCurrent = old.DeviceCurrent ?? old.Current;
            Settings.DeviceVersion = old.DeviceVersion ?? old.Version;
            Settings.Current = old.Previous;
            Settings.Version = old.PreviousVersion;
            Settings.Previous = old.Current;
            Settings.PreviousVersion = old.Version;
            Settings.AutoUpdate = false;
            try
            {
                Save();
            } catch
            {
                Settings = old;
                throw;
            }
            Log("Откат выполнен. Автообновление отключено, чтобы не вернуть проблемный релиз.");
        }
    }
}
