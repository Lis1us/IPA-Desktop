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
    /// <summary>Ищет по кэшируемому каталогу IPA_Downloader; при сетевой ошибке использует локальную копию.</summary>
    public sealed class AppCatalog
    {
        readonly SettingsStore store;
        readonly ToolLocator locator;
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
        bool catalogChecked;
        public Action<string> Log
        {
            get;
            set;
        }
        public AppCatalog(SettingsStore store, ToolLocator locator)
        {
            this.store = store;
            this.locator = locator;
            Log = delegate
            {
            };
        }
        /// <summary>Ищет по каталогам асинхронно и учитывает токен отмены.</summary>
        public async Task<List<AppRow>> SearchAsync(string query, CancellationToken ct)
        {
            var folder = Path.Combine(Root, "Files");
            Directory.CreateDirectory(folder);
            var cache = Path.Combine(folder, "AppsList.txt");
            if (!File.Exists(cache) && !String.IsNullOrEmpty(store.Settings.Current) && Directory.Exists(locator.CurrentRoot))
            {
                var bundled = Directory.GetFiles(locator.CurrentRoot, "AppsList.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (bundled != null) File.Copy(bundled, cache);
            }
            if (!catalogChecked)
            {
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        using (var response = await Http.GetAsync("https://raw.githubusercontent.com/kda2495/IPA_Downloader/refs/heads/main/Files/AppsList.txt", timeout.Token))
                        {
                            response.EnsureSuccessStatusCode();
                            var content = await response.Content.ReadAsStringAsync();
                            if (content.Length > 2000000 || ToolResponse.ParseCatalog(content).Count == 0) throw new IOException("Неожиданный формат каталога.");
                            File.WriteAllText(cache + ".tmp", content, Encoding.UTF8);
                            if (File.Exists(cache)) File.Replace(cache + ".tmp", cache, null);
                            else File.Move(cache + ".tmp", cache);
                            Log("Каталог приложений обновлён из исходного репозитория.");
                        }
                    }
                } catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    Log("Каталог: таймаут обновления, используется сохранённая копия.");
                } catch (Exception e)
                {
                    Log("Каталог: используется сохранённая копия. " + e.Message);
                }
                catalogChecked = true;
            }
            var customPath = Path.Combine(folder, "AppsListCustom.txt");
            var custom = File.Exists(customPath) ? ToolResponse.ParseCatalog(File.ReadAllText(customPath, Encoding.UTF8)) : new List<AppRow>();
            var catalog = File.Exists(cache) ? ToolResponse.ParseCatalog(File.ReadAllText(cache, Encoding.UTF8)) : new List<AppRow>();
            return ToolResponse.MatchCatalog(query, custom, catalog);
        }
    }
}
