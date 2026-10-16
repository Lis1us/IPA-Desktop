using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace IpaDesktop
{
    /// <summary>Каталог IPSW.me и потоковая загрузка только с HTTPS-серверов Apple, без автоматических перенаправлений.</summary>
    public sealed class FirmwareCatalog : IDisposable
    {
        readonly HttpClient http;
        readonly bool ownsClient;
        public FirmwareCatalog() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), true) { }
        public FirmwareCatalog(HttpClient client) : this(client, false) { }
        FirmwareCatalog(HttpClient client, bool owns)
        {
            http = client; ownsClient = owns;
            if (owns)
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/" + AppInfo.Version);
            }
        }
        /// <summary>Не разрешает HTTP, чужие хосты, нестандартные порты и пользовательские данные в URL.</summary>
        public static bool AppleUrl(string text)
        {
            Uri uri;
            return Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Port == 443 &&
                String.IsNullOrEmpty(uri.UserInfo) && String.IsNullOrEmpty(uri.Fragment) &&
                (uri.Host.EndsWith(".apple.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".cdn-apple.com", StringComparison.OrdinalIgnoreCase));
        }
        static bool Model(string id) { return id != null && Regex.IsMatch(id, @"^iPhone[0-9]{1,3},[0-9]{1,3}$"); }
        async Task<string> Json(string url, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var data = new MemoryStream())
                        {
                            byte[] bytes = new byte[8192]; int count;
                            while ((count = await input.ReadAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                if (data.Length + count > 16000000) throw new IOException("Ответ каталога IPSW слишком велик.");
                                data.Write(bytes, 0, count);
                            }
                            return System.Text.Encoding.UTF8.GetString(data.ToArray());
                        }
                    }
                }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); throw new IOException("Каталог IPSW не ответил за 30 секунд. Повторите поиск."); }
            }
        }
        public async Task<List<FirmwareModel>> ModelsAsync(CancellationToken ct)
        {
            var models = JsonCodec.Deserialize<List<FirmwareModel>>(await Json("https://api.ipsw.me/v4/devices?type=ipsw", ct).ConfigureAwait(false));
            if (models == null) throw new IOException("Каталог не вернул модели iPhone.");
            return models.Where(x => x != null && Model(x.identifier) && !String.IsNullOrWhiteSpace(x.name))
                .GroupBy(x => x.identifier).Select(x => x.First()).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public sealed class DeviceResponse
        {
            public string identifier { get; set; }
            public List<FirmwareRelease> firmwares { get; set; }
        }
        public async Task<List<FirmwareRelease>> ReleasesAsync(string model, CancellationToken ct)
        {
            if (!Model(model)) throw new ArgumentException("Выберите модель iPhone из каталога.");
            var result = JsonCodec.Deserialize<DeviceResponse>(await Json("https://api.ipsw.me/v4/device/" + Uri.EscapeDataString(model) + "?type=ipsw", ct).ConfigureAwait(false));
            if (result == null || result.identifier != model || result.firmwares == null) throw new IOException("Каталог вернул данные другой модели или неверный ответ.");
            return result.firmwares.Where(x => x != null && x.identifier == model && Regex.IsMatch(x.version ?? "", "^[0-9A-Za-z. -]{1,64}$") &&
                Regex.IsMatch(x.buildid ?? "", "^[A-Za-z0-9]{1,32}$") && x.filesize > 0 && x.filesize <= 100L * 1024 * 1024 * 1024)
                .GroupBy(x => x.buildid).Select(x => x.First()).OrderByDescending(x => x.releasedate, StringComparer.Ordinal).ToList();
        }
        public static bool Downloadable(FirmwareRelease release)
        {
            return release != null && AppleUrl(release.url) && release.filesize > 0 && release.filesize <= 100L * 1024 * 1024 * 1024 &&
                (Regex.IsMatch(release.sha256sum ?? "", "^[a-fA-F0-9]{64}$") || Regex.IsMatch(release.sha1sum ?? "", "^[a-fA-F0-9]{40}$"));
        }
        /// <summary>Проверка локальной копии без сети; отменяется между блоками, не блокирует интерфейс.</summary>
        public Task<bool> VerifyLocalAsync(FirmwareRelease release, string path, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                ct.ThrowIfCancellationRequested();
                if (!Downloadable(release) || !File.Exists(path)) return false;
                try
                {
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (input.Length != release.filesize) return false;
                        bool sha256 = Regex.IsMatch(release.sha256sum ?? "", "^[a-fA-F0-9]{64}$");
                        using (HashAlgorithm hash = sha256 ? (HashAlgorithm)SHA256.Create() : SHA1.Create())
                        {
                            byte[] bytes = new byte[1024 * 1024]; int count;
                            while ((count = input.Read(bytes, 0, bytes.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                hash.TransformBlock(bytes, 0, count, bytes, 0);
                            }
                            hash.TransformFinalBlock(new byte[0], 0, 0);
                            if (!String.Equals(BitConverter.ToString(hash.Hash).Replace("-", ""), sha256 ? release.sha256sum : release.sha1sum, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                        var manifest = FirmwareManifest.Read(path);
                        ct.ThrowIfCancellationRequested();
                        return manifest.Products.Contains(release.identifier) && manifest.Version == release.version && manifest.Build == release.buildid;
                    }
                }
                catch (IOException) { return false; }
                catch (System.Xml.XmlException) { return false; }
            }, ct);
        }
        /// <summary>Считает хеш в потоке; только полностью проверенный IPSW заменяет файл назначения.</summary>
        public Task<string> DownloadAsync(FirmwareRelease release, string destination, CancellationToken ct, Action<FirmwareTransfer> progress)
        {
            return Task.Run(async delegate
            {
                if (!Downloadable(release) || !Model(release.identifier)) throw new IOException("Нет безопасной HTTPS-ссылки Apple, размера или контрольной суммы для этой IPSW.");
                string target = Path.GetFullPath(destination);
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
                bool sha256 = Regex.IsMatch(release.sha256sum ?? "", "^[a-fA-F0-9]{64}$");
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        // Таймер сбрасывается после каждого прочитанного блока, а не ограничивает всю загрузку.
                        timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        using (var response = await http.GetAsync(release.url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            // Редирект не принимается: ссылка из каталога не может перенести загрузку на чужой сервер.
                            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("Сервер Apple не отдал IPSW напрямую (HTTP " + (int)response.StatusCode + "). Обновите каталог.");
                            var finalUri = response.RequestMessage == null ? null : response.RequestMessage.RequestUri;
                            if (finalUri != null && !AppleUrl(finalUri.AbsoluteUri)) throw new IOException("Перенаправление за пределы серверов Apple запрещено.");
                            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != release.filesize) throw new IOException("Размер IPSW на сервере отличается от каталога.");
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                            using (HashAlgorithm hash = sha256 ? (HashAlgorithm)SHA256.Create() : SHA1.Create())
                            {
                                byte[] bytes = new byte[1024 * 1024]; long total = 0; int count;
                                var watch = Stopwatch.StartNew(); long last = -250;
                                while ((count = await input.ReadAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                {
                                    timeout.CancelAfter(TimeSpan.FromSeconds(60));
                                    total += count;
                                    if (total > release.filesize) throw new IOException("IPSW превышает размер из каталога.");
                                    await output.WriteAsync(bytes, 0, count, timeout.Token).ConfigureAwait(false);
                                    hash.TransformBlock(bytes, 0, count, bytes, 0);
                                    if (progress != null && watch.ElapsedMilliseconds - last >= 250)
                                    {
                                        last = watch.ElapsedMilliseconds;
                                        progress(new FirmwareTransfer { Bytes = total, Total = release.filesize, BytesPerSecond = total / Math.Max(0.001, watch.Elapsed.TotalSeconds) });
                                    }
                                }
                                hash.TransformFinalBlock(new byte[0], 0, 0);
                                string actual = BitConverter.ToString(hash.Hash).Replace("-", "");
                                if (total != release.filesize || !String.Equals(actual, sha256 ? release.sha256sum : release.sha1sum, StringComparison.OrdinalIgnoreCase))
                                    throw new IOException("IPSW не прошла проверку размера или контрольной суммы. Временная копия удалена.");
                            }
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    var manifest = FirmwareManifest.Read(temporary);
                    if (!manifest.Products.Contains(release.identifier) || manifest.Version != release.version || manifest.Build != release.buildid)
                        throw new IOException("Содержимое IPSW не соответствует выбранной модели, версии или сборке.");
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(target)) File.Replace(temporary, target, null);
                    else File.Move(temporary, target);
                    return target;
                }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); throw new IOException("Сервер Apple не передавал данные более 60 секунд. Повторите загрузку."); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, ct);
        }
        public void Dispose() { if (ownsClient) http.Dispose(); }
    }
}
