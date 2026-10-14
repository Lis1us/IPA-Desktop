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
            using (var response = await Http.GetAsync("https://api.github.com/repos/kda2495/IPA_Downloader/releases/latest", ct))
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
            var candidates = (rel.assets ?? new Asset[0]).Where(a => a.name.StartsWith("IPA_Downloader", StringComparison.OrdinalIgnoreCase) && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1) throw new Exception("Структура релиза изменилась: ожидается один ZIP IPA_Downloader.");
            var asset = candidates[0];
            if (!asset.browser_download_url.StartsWith("https://github.com/kda2495/IPA_Downloader/releases/download/", StringComparison.Ordinal)) throw new Exception("Неожиданный источник архива.");
            if (asset.size <= 0 || asset.size > 1000000000) throw new Exception("Некорректный размер архива.");
            var name = Guid.NewGuid().ToString("N");
            var staging = Path.Combine(Root, "Versions", name);
            var zip = Path.Combine(Root, name + ".zip");
            bool committed = false;
            try
            {
                Log("Загрузка релиза " + rel.tag_name);
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
                ct.ThrowIfCancellationRequested();
                Log("Распаковка и проверка команд…");
                await Task.Run(delegate
                {
                    Extract(zip, staging);
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
                var previous = Settings;
                Settings = new State
                {
                    Current = name, Version = rel.tag_name, Previous = previous.Current, PreviousVersion = previous.Version, AutoUpdate = previous.AutoUpdate, CheckGuiUpdates = previous.CheckGuiUpdates
                };
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
                if (!committed && Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
        public void Rollback()
        {
            if (String.IsNullOrEmpty(Settings.Previous) || !Directory.Exists(Path.Combine(Root, "Versions", Settings.Previous))) throw new Exception("Предыдущей версии пока нет.");
            var old = Settings;
            Settings = new State
            {
                Current = old.Previous, Version = old.PreviousVersion, Previous = old.Current, PreviousVersion = old.Version, AutoUpdate = false, CheckGuiUpdates = old.CheckGuiUpdates
            };
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
