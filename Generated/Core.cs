// Generated from Source/. Edit the source classes, then run Sync-Sources.ps1.
using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Security;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System;

// Domain/AppleSupportReport.cs
namespace IpaDesktop
{
    public enum AppleSupportStatus
    {
        Running,
        Stopped,
        ServiceMissing,
        NotDetected,
        Unknown
    }

    public sealed class AppleSupportSnapshot
    {
        public bool InstallationDetected { get; set; }
        public bool ServiceFound { get; set; }
        public bool ServiceStatusKnown { get; set; }
        public bool ServiceRunning { get; set; }
        public bool ServiceScanCompleted { get; set; }
        public bool RegistryScanCompleted { get; set; }
        public string Details { get; set; }
    }

    public sealed class AppleSupportReport
    {
        public AppleSupportStatus Status { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string HelpUrl
        {
            get
            {
                return Status == AppleSupportStatus.Stopped
                    ? "https://support.apple.com/ru-ru/102347"
                    : "https://support.apple.com/ru-ru/118290";
            }
        }
    }
}

// Domain/AppRow.cs
namespace IpaDesktop
{
    public class AppRow
    {
        public string Name
        {
            get;
            set;
        }
        public string ID
        {
            get;
            set;
        }
    }
}

// Domain/Asset.cs
namespace IpaDesktop
{
    public class Asset
    {
        public string name
        {
            get;
            set;
        }
        public string browser_download_url
        {
            get;
            set;
        }
        public string digest
        {
            get;
            set;
        }
        public long size
        {
            get;
            set;
        }
    }
}

// Domain/DeviceRow.cs
namespace IpaDesktop
{
    public class DeviceRow
    {
        public string ID
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public override string ToString()
        {
            return Name;
        }
    }
}

// Domain/DownloadItem.cs
namespace IpaDesktop
{
    public class DownloadItem
    {
        public string Name
        {
            get;
            set;
        }
        public string ID
        {
            get;
            set;
        }
        public string VersionID
        {
            get;
            set;
        }
        public string Label
        {
            get
            {
                return Name + (VersionID == null ? " · последняя" : " · версия " + VersionID);
            }
        }
    }
}

// Domain/DownloadSelection.cs
namespace IpaDesktop
{
    public class DownloadSelection
    {
        readonly List<DownloadItem> items = new List<DownloadItem>();
        public int Count
        {
            get
            {
                return items.Count;
            }
        }
        public void Add(AppRow app, string version)
        {
            if (app == null || String.IsNullOrEmpty(app.ID) || !app.ID.All(Char.IsDigit)) throw new ArgumentException("Некорректный App Store ID.");
            if (version != null && (version.Length == 0 || !version.All(Char.IsDigit))) throw new ArgumentException("Некорректная версия.");
            var existing = items.FirstOrDefault(x => x.ID == app.ID);
            if (existing == null) items.Add(new DownloadItem
            {
                Name = app.Name, ID = app.ID, VersionID = version
            });
            else
            {
                existing.Name = app.Name;
                existing.VersionID = version;
            }
        }
        public List<DownloadItem> Snapshot()
        {
            return items.Select(x => new DownloadItem
            {
                Name = x.Name, ID = x.ID, VersionID = x.VersionID
            }).ToList();
        }
        public void Remove(string id)
        {
            items.RemoveAll(x => x.ID == id);
        }
        public void Clear()
        {
            items.Clear();
        }
    }
}

// Domain/InstallationSelection.cs
namespace IpaDesktop
{
    public class InstallationSelection
    {
        readonly List<LocalRow> items = new List<LocalRow>();
        static LocalRow Copy(LocalRow row)
        {
            return new LocalRow
            {
                Name = row.Name, Path = row.Path, Size = row.Size, AppleAccount = row.AppleAccount
            };
        }
        public int Count
        {
            get
            {
                return items.Count;
            }
        }
        public void Add(LocalRow row)
        {
            if (row == null || String.IsNullOrWhiteSpace(row.Path)) throw new ArgumentException("Файл не выбран.");
            if (!items.Any(x => String.Equals(x.Path, row.Path, StringComparison.OrdinalIgnoreCase))) items.Add(Copy(row));
        }
        public List<LocalRow> Snapshot()
        {
            return items.Select(Copy).ToList();
        }
        public void Remove(string path)
        {
            items.RemoveAll(x => String.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        public void Clear()
        {
            items.Clear();
        }
        public void Reconcile(IEnumerable<LocalRow> files)
        {
            var available = files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                LocalRow row;
                if (!available.TryGetValue(items[i].Path, out row)) items.RemoveAt(i);
                else items[i] = Copy(row);
            }
        }
    }
}

// Domain/LocalRow.cs
namespace IpaDesktop
{
    public class LocalRow
    {
        public string AppleAccount
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public string Size
        {
            get;
            set;
        }
        public string Path
        {
            get;
            set;
        }
    }
}

// Domain/Release.cs
namespace IpaDesktop
{
    public class Release
    {
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public string tag_name
        {
            get;
            set;
        }
        public string html_url
        {
            get;
            set;
        }
        public string body
        {
            get;
            set;
        }
        public Asset[] assets
        {
            get;
            set;
        }
    }
}

// Domain/Result.cs
namespace IpaDesktop
{
    public class Result
    {
        public int Code
        {
            get;
            set;
        }
        public string Output
        {
            get;
            set;
        }
    }
}

// Domain/SearchSession.cs
namespace IpaDesktop
{
    public class SearchSession
    {
        int revision;
        string query = "";
        public int Change(string value)
        {
            query = (value ?? "").Trim();
            return++revision;
        }
        public int Revision
        {
            get
            {
                return revision;
            }
        }
        public bool IsCurrent(int request)
        {
            return request == revision && query.Length > 0;
        }
    }
}

// Domain/State.cs
namespace IpaDesktop
{
    public class State
    {
        public State()
        {
            AutoUpdate = true;
            AutoUpdateDeviceTools = true;
            CheckGuiUpdates = true;
        }
        public State Copy() { return (State)MemberwiseClone(); }
        public bool AutoUpdateDeviceTools { get; set; }
        public string PreviousDeviceCurrent { get; set; }
        public string PreviousDeviceVersion { get; set; }
        public string Source { get; set; }
        public string PreviousSource { get; set; }
        public string DeviceSource { get; set; }
        public string PreviousDeviceSource { get; set; }
        public string DeviceCurrent { get; set; }
        public string DeviceVersion { get; set; }
        public string Current
        {
            get;
            set;
        }
        public string Previous
        {
            get;
            set;
        }
        public string Version
        {
            get;
            set;
        }
        public string PreviousVersion
        {
            get;
            set;
        }
        public bool AutoUpdate
        {
            get;
            set;
        }
        public bool CheckGuiUpdates
        {
            get;
            set;
        }
    }
}

// Infrastructure/BufferedJournal.cs
namespace IpaDesktop
{
    public class BufferedJournal
    {
        readonly Queue<string> pending = new Queue<string> ();
        readonly object gate = new object();
        int skipped;
        public void Add(string text)
        {
            if (text == null) return;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (text.Length > 2000 ? text.Substring(0, 2000) + " …" : text) + Environment.NewLine;
            lock (gate)
            {
                if (pending.Count >= 500)
                {
                    pending.Dequeue();
                    skipped++;
                }
                pending.Enqueue(line);
            }
        }
        public string Drain()
        {
            lock (gate)
            {
                var result = new StringBuilder();
                if (skipped > 0)
                {
                    result.AppendLine("Слишком частый вывод: пропущено строк " + skipped + ".");
                    skipped = 0;
                }
                while (pending.Count > 0 && result.Length < 6000) result.Append(pending.Dequeue());
                return result.ToString();
            }
        }
    }
}

// Infrastructure/InstallationLayout.cs
namespace IpaDesktop
{
    public static class InstallationLayout
    {
        static readonly Dictionary<string, string> LegacyFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Assets/App.ico", "App.ico" },
            { "Assets/App.svg", "App.svg" },
            { "Scripts/Update-App.ps1", "Update-App.ps1" },
            { "Scripts/Sync-Sources.ps1", "Sync-Sources.ps1" },
            { "Docs/ARCHITECTURE.md", "ARCHITECTURE.md" },
            { "Generated/Core.cs", "Core.cs" },
            { "Generated/App.cs", "App.cs" },
            { "Generated/IpaMetadata.cs", "IpaMetadata.cs" },
            { "Generated/WindowTheme.cs", "WindowTheme.cs" },
            { "Generated/GuiUpdater.cs", "GuiUpdater.cs" },
            { "Tests/Verify.ps1", "Verify.ps1" },
            { "Tests/Verify-IpaMetadata.ps1", "Verify-IpaMetadata.ps1" },
            { "Tests/Verify-Updater.ps1", "Verify-Updater.ps1" },
            { "Tests/Verify-Design.ps1", "Verify-Design.ps1" },
            { "Tests/Verify-Services.ps1", "Verify-Services.ps1" }
        };

        public static void Ensure(string installationDirectory)
        {
            string root = Path.GetFullPath(installationDirectory);
            var assembly = typeof(InstallationLayout).Assembly;
            foreach (var relative in GuiUpdater.PackageFiles.Where(path => path.Contains("/")))
            {
                using (var resource = assembly.GetManifestResourceStream("IPA.Layout." + relative.Replace('/', '.')))
                {
                    if (resource == null) throw new InvalidDataException("Отсутствует ресурс структуры программы: " + relative);
                    byte[] data;
                    using (var buffer = new MemoryStream())
                    {
                        resource.CopyTo(buffer);
                        data = buffer.ToArray();
                    }

                    string destination = Path.Combine(root, relative);
                    string legacyName;
                    string legacy = LegacyFiles.TryGetValue(relative, out legacyName) ? Path.Combine(root, legacyName) : null;
                    bool migrate = legacy != null && File.Exists(legacy) && new FileInfo(legacy).Length == data.Length && File.ReadAllBytes(legacy).SequenceEqual(data);
                    if (!File.Exists(destination) || migrate)
                    {
                        if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Файл программы является ссылкой: " + relative);
                        string folder = Path.GetDirectoryName(destination);
                        for (string parent = folder; parent != null && parent.StartsWith(root, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                        {
                            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                                throw new IOException("Папка программы содержит ссылку: " + relative);
                            if (parent.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
                        }
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(destination, data);
                    }

                    // Remove only known migration copies matching this binary's resources.
                    // User-modified files and IPA downloads are never removed here.
                    if (migrate) File.Delete(legacy);
                }
            }
        }
    }
}

// Infrastructure/IProcessRunner.cs
namespace IpaDesktop
{
    public interface IProcessRunner
    {
        Action<string> Log
        {
            get;
            set;
        }
        Process Active
        {
            get;
        }
        Task<Result> Run(string executable, string[] arguments, CancellationToken cancellation, bool visible, bool publish);
    }
}

// Infrastructure/JsonCodec.cs
namespace IpaDesktop
{
    public static class JsonCodec
    {
        static JavaScriptSerializer Serializer
        {
            get
            {
                return new JavaScriptSerializer
                {
                    MaxJsonLength = 16000000
                };
            }
        }
        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }
        public static T Deserialize<T> (string value)
        {
            return Serializer.Deserialize<T> (value);
        }
        public static object DeserializeObject(string value)
        {
            return Serializer.DeserializeObject(value);
        }
    }
}

// Infrastructure/NetworkClient.cs
namespace IpaDesktop
{
    public static class NetworkClient
    {
        public static readonly HttpClient Http = CreateHttp();
        static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var h = new HttpClient();
            h.Timeout = TimeSpan.FromMinutes(15);
            h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/0.1.16");
            return h;
        }
    }
}

// Infrastructure/ProcessRunner.cs
namespace IpaDesktop
{
    public sealed class ProcessRunner : IProcessRunner
    {
        readonly string Downloads;
        public Action<string> Log
        {
            get;
            set;
        }
        public Process Active
        {
            get;
            private set;
        }
        public ProcessRunner(string workingDirectory)
        {
            Downloads = workingDirectory;
            Log = delegate
            {
            };
        }
        // Windows CommandLineToArgvW-compatible quoting; never invoke cmd.exe.
        public static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            int n = 0;
            foreach (char c in s)
            {
                if (c == '\\')
                {
                    n++;
                    continue;
                }
                if (c == '\"')
                {
                    b.Append('\\', n * 2 + 1);
                    b.Append(c);
                } else
                {
                    b.Append('\\', n);
                    b.Append(c);
                }
                n = 0;
            }
            b.Append('\\', n * 2);
            return b.Append('"').ToString();
        }
        public Task<Result> Run(string exe, string[] args, CancellationToken token, bool visible, bool publish)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                var si = new ProcessStartInfo(exe, String.Join(" ", args.Select(Quote)))
                {
                    WorkingDirectory = Downloads, UseShellExecute = false, CreateNoWindow = !visible
                };
                si.EnvironmentVariables["PATH"] = Path.GetDirectoryName(exe) + ";" + Environment.GetEnvironmentVariable("PATH");
                if (!visible)
                {
                    si.RedirectStandardInput = true;
                    si.RedirectStandardOutput = true;
                    si.RedirectStandardError = true;
                    si.StandardOutputEncoding = Encoding.UTF8;
                    si.StandardErrorEncoding = Encoding.UTF8;
                }
                using (var p = new Process
                {
                    StartInfo = si
                })
                {
                    var output = new StringBuilder();
                    object gate = new object();
                    bool stdoutDone = visible, stderrDone = visible;
                    long lastOutput = Stopwatch.GetTimestamp();
                    DataReceivedEventHandler stdout = delegate(object sender, DataReceivedEventArgs e)
                    {
                        lock (gate)
                        {
                            if (e.Data == null)
                            {
                                stdoutDone = true;
                                return;
                            }
                            lastOutput = Stopwatch.GetTimestamp();
                            int count = Math.Min(e.Data.Length, 15000000 - output.Length);
                            if (count > 0)
                            {
                                output.Append(e.Data, 0, count);
                                if (output.Length<15000000) output.AppendLine();
                            }
                        }
                        if (publish) Log(e.Data);
                    };
                    DataReceivedEventHandler stderr = delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null)
                        {
                            lock (gate)
                            {
                                stderrDone = true;
                            }
                            return;
                        }
                        stdout(sender, e);
                    };
                    if (!visible)
                    {
                        p.OutputDataReceived += stdout;
                        p.ErrorDataReceived += stderr;
                    }
                    var elapsed = Stopwatch.StartNew();
                    long nextNotice = 30;
                    try
                    {
                        p.Start();
                        Active = p;
                        if (!visible)
                        {
                            p.StandardInput.Close();
                            p.BeginOutputReadLine();
                            p.BeginErrorReadLine();
                        }
                        while (!p.WaitForExit(200))
                        {
                            if (token.IsCancellationRequested)
                            {
                                try
                                {
                                    p.Kill();
                                } catch (InvalidOperationException)
                                {
                                }
                                if (!p.WaitForExit(5000)) throw new IOException("Не удалось остановить процесс. Завершите его через Диспетчер задач.");
                                token.ThrowIfCancellationRequested();
                            }
                            if (publish && elapsed.Elapsed.TotalSeconds>= nextNotice)
                            {
                                long last;
                                lock (gate)
                                {
                                    last = lastOutput;
                                }
                                Log("Процесс работает " + (int) elapsed.Elapsed.TotalSeconds + " с; без нового вывода " + (int)((Stopwatch.GetTimestamp() - last) / (double) Stopwatch.Frequency) + " с. При необходимости нажмите «Отмена».");
                                nextNotice += 30;
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        // A child may keep a redirected pipe open after its parent exits.
                        // Never use an unbounded WaitForExit() to drain those pipes.
                        var drain = Stopwatch.StartNew();
                        while (drain.ElapsedMilliseconds < 2000)
                        {
                            lock (gate)
                            {
                                if (stdoutDone && stderrDone) break;
                            }
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(20);
                        }
                        lock (gate)
                        {
                            if (!stdoutDone || !stderrDone) Log("Процесс завершился, но поток вывода не закрылся. Ожидание потока прекращено.");
                            return new Result
                            {
                                Code = p.ExitCode, Output = output.ToString()
                            };
                        }
                    } finally
                    {
                        Active = null;
                    }
                }
            }, token);
        }
    }
}

// Infrastructure/SettingsStore.cs
namespace IpaDesktop
{
    public sealed class SettingsStore
    {
        public string Root
        {
            get;
            private set;
        }
        public State Settings
        {
            get;
            set;
        }
        public SettingsStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "state.json");
            Settings = File.Exists(path) ? JsonCodec.Deserialize<State> (File.ReadAllText(path)) : new State();
            if (Settings == null) throw new IOException("Не удалось прочитать настройки.");
        }
        public void Save()
        {
            var f = Path.Combine(Root, "state.json");
            var t = f + ".tmp";
            File.WriteAllText(t, JsonCodec.Serialize(Settings), Encoding.UTF8);
            if (File.Exists(f)) File.Replace(t, f, null);
            else File.Move(t, f);
        }
    }
}

// Infrastructure/ToolLocator.cs
namespace IpaDesktop
{
    public sealed class ToolLocator
    {
        readonly SettingsStore store;
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
        }
        public ToolLocator(SettingsStore store)
        {
            this.store = store;
        }
        public string VersionRoot(string version)
        {
            if (String.IsNullOrEmpty(version)) throw new Exception("Сначала установите инструменты: кнопка «Проверить обновления».");
            var p = Path.GetFullPath(Path.Combine(Root, "Versions", version));
            var prefix = Path.GetFullPath(Path.Combine(Root, "Versions")) + Path.DirectorySeparatorChar;
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Некорректный путь инструментов.");
            return p;
        }
        public string CurrentRoot { get { return VersionRoot(Settings.Current); } }
        public string DeviceRoot { get { return VersionRoot(Settings.DeviceCurrent ?? Settings.Current); } }
        public string Find(string pattern)
        {
            var root = pattern.StartsWith("ipatool", StringComparison.OrdinalIgnoreCase) ? CurrentRoot : DeviceRoot;
            var files = Directory.GetFiles(root, pattern, SearchOption.AllDirectories).Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).ToArray();
            if (files.Length != 1) throw new Exception("Не найдена однозначная Windows-утилита: " + pattern);
            return files[0];
        }
    }
}

// Infrastructure/WindowsAppleSupportProbe.cs
namespace IpaDesktop
{
    public interface IAppleSupportProbe
    {
        AppleSupportSnapshot Read();
    }

    public sealed class WindowsAppleSupportProbe : IAppleSupportProbe
    {
        public AppleSupportSnapshot Read()
        {
            var result = new AppleSupportSnapshot();
            var details = new List<string>();
            ServiceController[] services = null;
            try
            {
                services = ServiceController.GetServices();
                result.ServiceScanCompleted = true;
                foreach (var service in services)
                {
                    if (!service.ServiceName.Equals("AppleMobileDeviceService", StringComparison.OrdinalIgnoreCase)
                        && !service.ServiceName.Equals("Apple Mobile Device Service", StringComparison.OrdinalIgnoreCase)
                        && !service.DisplayName.StartsWith("Apple Mobile Device", StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.ServiceFound = true;
                    details.Add("Служба: " + service.ServiceName);
                    result.ServiceRunning = service.Status == ServiceControllerStatus.Running;
                    result.ServiceStatusKnown = true;
                    details.Add("Состояние службы: " + service.Status);
                    break;
                }
            }
            catch (InvalidOperationException error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            catch (Win32Exception error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            catch (SecurityException error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            finally
            {
                if (services != null)
                    foreach (var service in services) service.Dispose();
            }

            bool registryComplete = true;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (var apple = root.OpenSubKey(@"SOFTWARE\Apple Inc.\Apple Mobile Device Support"))
                        {
                            if (apple != null)
                            {
                                result.InstallationDetected = true;
                                details.Add("Регистрация компонентов Apple: " + view);
                            }
                        }
                        using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (var keyName in uninstall.GetSubKeyNames())
                            {
                                using (var product = uninstall.OpenSubKey(keyName))
                                {
                                    if (product == null) continue;
                                    string name = Convert.ToString(product.GetValue("DisplayName"));
                                    if (!name.Equals("Apple Mobile Device Support", StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    result.InstallationDetected = true;
                                    details.Add("Установленный компонент: " + name + " " + Convert.ToString(product.GetValue("DisplayVersion")));
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
                catch (SecurityException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
                catch (System.IO.IOException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
            }
            result.RegistryScanCompleted = registryComplete;
            result.Details = String.Join(Environment.NewLine, details);
            return result;
        }
    }
}

// Services/AppCatalog.cs
namespace IpaDesktop
{
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

// Services/AppleSupportService.cs
namespace IpaDesktop
{
    public sealed class AppleSupportService
    {
        readonly IAppleSupportProbe probe;

        public AppleSupportService(IAppleSupportProbe probe)
        {
            if (probe == null) throw new ArgumentNullException("probe");
            this.probe = probe;
        }

        public Task<AppleSupportReport> CheckAsync(CancellationToken cancellation)
        {
            return Task.Run(delegate
            {
                cancellation.ThrowIfCancellationRequested();
                var snapshot = probe.Read();
                cancellation.ThrowIfCancellationRequested();
                return Evaluate(snapshot);
            }, cancellation);
        }

        public static AppleSupportReport Evaluate(AppleSupportSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            AppleSupportStatus status;
            string message;
            string guidance;
            if (snapshot.ServiceFound && snapshot.ServiceStatusKnown)
            {
                status = snapshot.ServiceRunning ? AppleSupportStatus.Running : AppleSupportStatus.Stopped;
                message = snapshot.ServiceRunning ? "Служба Apple запущена" : "Служба Apple остановлена";
                guidance = snapshot.ServiceRunning
                    ? "Служба Apple Mobile Device работает. Если iPhone не найден, проверьте USB, разблокировку и доверие."
                    : "Откройте services.msc, найдите Apple Mobile Device Service и запустите или перезапустите её. Для изменения состояния службы могут потребоваться права администратора.";
            }
            else if (snapshot.ServiceFound || !snapshot.ServiceScanCompleted || !snapshot.RegistryScanCompleted)
            {
                status = AppleSupportStatus.Unknown;
                message = "Статус компонентов Apple неизвестен";
                guidance = "Не удалось полностью проверить компоненты Apple. Подробности — ниже. Поиск iPhone можно выполнить вручную.";
            }
            else if (snapshot.InstallationDetected)
            {
                status = AppleSupportStatus.ServiceMissing;
                message = "Компоненты Apple найдены, служба отсутствует";
                guidance = "Найдена регистрация Apple Mobile Device Support, но служба отсутствует. Проверьте или восстановите установку ПО Apple, затем повторите проверку.";
            }
            else
            {
                status = AppleSupportStatus.NotDetected;
                message = "Apple Mobile Device Support не обнаружен";
                guidance = "Служба и регистрация Apple Mobile Device Support не найдены. Установите ПО для устройств с официального сайта Apple или Microsoft Store, затем повторите проверку.";
            }
            return new AppleSupportReport
            {
                Status = status,
                Message = message,
                Details = guidance + (String.IsNullOrWhiteSpace(snapshot.Details) ? "" : Environment.NewLine + snapshot.Details)
            };
        }
    }
}

// Services/ApplicationDownloads.cs
namespace IpaDesktop
{
    public sealed class ApplicationDownloads
    {
        readonly IAppToolClient tools;
        readonly string folder;
        public ApplicationDownloads(IAppToolClient tools, string folder)
        {
            this.tools = tools;
            this.folder = folder;
        }
        public async Task DownloadAsync(DownloadItem item, CancellationToken ct)
        {
            var arguments = new List<string>
            {
                "download", "-i", item.ID, "--purchase", "-o", folder
            };
            if (item.VersionID != null) arguments.AddRange(new[]
            {
                "--external-version-id", item.VersionID
            });
            ToolResponse.Ensure(await tools.RunAsync(arguments.ToArray(), ct, true));
        }
    }
}

// Services/AppToolClient.cs
namespace IpaDesktop
{
    public sealed class AppToolClient : IAppToolClient
    {
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        public AppToolClient(IProcessRunner runner, ToolLocator locator)
        {
            this.runner = runner;
            this.locator = locator;
        }
        public Task<Result> RunAsync(string[] args, CancellationToken ct, bool publish)
        {
            return Task.Run(async delegate
            {
                return await runner.Run(locator.Find("ipatool*.exe"), args.Concat(new[]
                {
                    "--format", "json"
                }).ToArray(), ct, false, publish);
            }, ct);
        }
    }
}

// Services/DeviceService.cs
namespace IpaDesktop
{
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

// Services/Engine.cs
namespace IpaDesktop
{
    public sealed class Engine
    {
        readonly SettingsStore settings;
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        readonly IAppToolClient tools;
        readonly AppCatalog catalog;
        readonly EngineUpdater updater;
        public string Root
        {
            get
            {
                return settings.Root;
            }
        }
        public string Downloads
        {
            get;
            private set;
        }
        public State Settings
        {
            get
            {
                return settings.Settings;
            }
            set
            {
                settings.Settings = value;
            }
        }
        public Process Active
        {
            get
            {
                return runner.Active;
            }
        }
        public AppleSupportService AppleSupport { get; private set; }
        public DeviceService Devices
        {
            get;
            private set;
        }
        public LocalAppRepository LocalFiles
        {
            get;
            private set;
        }
        public ApplicationDownloads ApplicationDownloads
        {
            get;
            private set;
        }
        public Action<string> Log
        {
            get
            {
                return runner.Log;
            }
            set
            {
                runner.Log = value;
                catalog.Log = value;
                updater.Log = value;
                Devices.Log = value;
            }
        }
        public Action<double> Progress
        {
            get
            {
                return updater.Progress;
            }
            set
            {
                updater.Progress = value;
            }
        }
        public Engine(string root) : this(root, null)
        {
        }
        public Engine(string root, IProcessRunner processRunner) : this(root, processRunner, new WindowsAppleSupportProbe())
        {
        }
        public Engine(string root, IProcessRunner processRunner, IAppleSupportProbe appleSupportProbe)
        {
            settings = new SettingsStore(root);
            Downloads = Path.Combine(root, "Apps");
            Directory.CreateDirectory(Downloads);
            runner = processRunner ?? new ProcessRunner(Downloads);
            locator = new ToolLocator(settings);
            tools = new AppToolClient(runner, locator);
            catalog = new AppCatalog(settings, locator);
            updater = new EngineUpdater(settings, runner);
            AppleSupport = new AppleSupportService(appleSupportProbe);
            Devices = new DeviceService(runner, locator);
            LocalFiles = new LocalAppRepository(Downloads);
            ApplicationDownloads = new ApplicationDownloads(tools, Downloads);
        }
        public void Save()
        {
            settings.Save();
        }
        public string CurrentRoot
        {
            get
            {
                return locator.CurrentRoot;
            }
        }
        public string Binary(string pattern)
        {
            return locator.Find(pattern);
        }
        public Task<Result> Run(string exe, string[] args, CancellationToken ct, bool visible, bool publish)
        {
            return runner.Run(exe, args, ct, visible, publish);
        }
        public Task<Result> Tool(string[] args, CancellationToken ct, bool publish)
        {
            return tools.RunAsync(args, ct, publish);
        }
        public Task<List<AppRow>> CatalogSearch(string query, CancellationToken ct)
        {
            return catalog.SearchAsync(query, ct);
        }
        public Task<Release> Check(CancellationToken ct)
        {
            return updater.Check(ct);
        }
        public Task Install(Release release, CancellationToken ct)
        {
            return updater.Install(release, ct);
        }
        public Task<Release> CheckDeviceTools(CancellationToken ct) { return updater.CheckDeviceTools(ct); }
        public bool DeviceToolsCurrent(Release release) { return updater.DeviceToolsCurrent(release); }
        public Task InstallDeviceTools(Release release, CancellationToken ct) { return updater.InstallDeviceTools(release, ct); }
        public void RollbackDeviceTools() { updater.RollbackDeviceTools(); }
        public void Rollback()
        {
            updater.Rollback();
        }
        // Compatibility API for portable builds and existing verification scripts.
        public static string Quote(string text)
        {
            return ProcessRunner.Quote(text);
        }
        public static void Ensure(Result result)
        {
            ToolResponse.Ensure(result);
        }
        public static Dictionary<string, object> Object(string output)
        {
            return ToolResponse.Object(output);
        }
        public static object Get(Dictionary<string, object> data, string key)
        {
            return ToolResponse.Get(data, key);
        }
        public static List<string> VersionIds(string output)
        {
            return ToolResponse.VersionIds(output);
        }
        public static List<AppRow> ParseApps(string output)
        {
            return ToolResponse.ParseApps(output);
        }
        public static List<AppRow> ParseCatalog(string text)
        {
            return ToolResponse.ParseCatalog(text);
        }
        public static List<AppRow> MatchCatalog(string query, List<AppRow> custom, List<AppRow> catalog)
        {
            return ToolResponse.MatchCatalog(query, custom, catalog);
        }
        public static List<AppRow> MergeApps(IEnumerable<AppRow> local, IEnumerable<AppRow> online)
        {
            return ToolResponse.MergeApps(local, online);
        }
        public static void Extract(string zip, string destination)
        {
            EngineUpdater.Extract(zip, destination);
        }
    }
}

// Services/EngineUpdater.cs
namespace IpaDesktop
{
    public sealed class EngineUpdater
    {
        public const string SourceRepository = "Sorvigolova/ipatool";
        public const string DeviceRepository = "Lis1us/IPA-Desktop";
        public const string DeviceTagPrefix = "device-tools-v";
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
            if (release.tag_name == null || !System.Text.RegularExpressions.Regex.IsMatch(release.tag_name, @"^device-tools-v\d+\.\d+\.\d+$")) throw new IOException("Некорректный релиз нашего комплекта для iPhone.");
            var expected = "IPA-DeviceTools-" + release.tag_name.Substring(DeviceTagPrefix.Length) + "-Windows-x64.zip";
            var matches = (release.assets ?? new Asset[0]).Where(a => a.name == expected).ToArray();
            if (matches.Length != 1) throw new IOException("Не найден однозначный Windows x64 архив нашего комплекта.");
            ValidateAsset(matches[0], DeviceRepository);
            if (!matches[0].browser_download_url.StartsWith("https://github.com/" + DeviceRepository + "/releases/download/" + release.tag_name + "/", StringComparison.Ordinal)) throw new IOException("Архив принадлежит другому релизу.");
            if (matches[0].digest == null || !System.Text.RegularExpressions.Regex.IsMatch(matches[0].digest, @"^sha256:[0-9a-fA-F]{64}$")) throw new IOException("Для комплекта требуется SHA-256 от GitHub.");
            return matches[0];
        }
        public static bool HasDeviceTools(string root)
        {
            if (!Directory.Exists(root)) return false;
            return new[] { "idevice_id.exe", "ideviceinfo.exe", "ideviceinstaller.exe", "libimobiledevice-1.0*.dll", "libplist-2.0*.dll" }.All(file => Directory.GetFiles(root, file, SearchOption.AllDirectories).Count(x => x.Replace('\\', '/').Contains("/windows_amd64/")) == 1);
        }
        public static void CheckDevicePackage(string root)
        {
            if (!HasDeviceTools(root) || !Directory.Exists(Path.Combine(root, "LICENSES")) || !File.Exists(Path.Combine(root, "sources.json"))) throw new IOException("Неполный комплект: нужны утилиты, DLL, лицензии и исходные ревизии.");
            var names = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            if (names.Length != 3 || DeviceFiles.Take(3).Any(name => !names.Contains(name))) throw new IOException("Комплект содержит неожиданные программы.");
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
            var supportZip = Path.Combine(Root, "support-" + name + ".zip");
            var supportVersion = Settings.DeviceVersion ?? Settings.Version;
            var supportSource = Settings.DeviceSource;
            bool committed = false;
            try
            {
                Log("Загрузка релиза " + rel.tag_name);
                await Download(asset, zip, ct);
                if (needSupport)
                {
                    Log("Получение нашего Windows-комплекта для iPhone…");
                    var supportRelease = await CheckDeviceTools(ct);
                    await Download(SelectDeviceAsset(supportRelease), supportZip, ct);
                    await Task.Run(delegate
                    {
                        Extract(supportZip, supportStage);
                        CheckDevicePackage(supportStage);
                    }, ct);
                    await ValidateDeviceTools(supportStage, ct);
                    supportName = newSupport;
                    supportVersion = supportRelease.tag_name;
                    supportSource = DeviceRepository;
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
                Settings.DeviceSource = supportSource;
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
                // Tool releases are separate from GUI releases and never become GUI latest.
                for (int page = 1; page <= 10; page++)
                {
                    using (var response = await Http.GetAsync("https://api.github.com/repos/" + DeviceRepository + "/releases?per_page=100&page=" + page, timeout.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        var releases = JsonCodec.Deserialize<Release[]>(await response.Content.ReadAsStringAsync());
                        var found = releases.FirstOrDefault(r => !r.draft && !r.prerelease && r.tag_name != null && r.tag_name.StartsWith(DeviceTagPrefix, StringComparison.Ordinal));
                        if (found != null) { SelectDeviceAsset(found); return found; }
                        if (releases.Length < 100) break;
                    }
                }
                throw new IOException("Наш опубликованный Windows-комплект для iPhone пока не найден.");
            }
        }
        public bool DeviceToolsCurrent(Release release)
        {
            var current = Settings.DeviceCurrent ?? Settings.Current;
            var version = Settings.DeviceVersion ?? (Settings.Source == null ? Settings.Version : null);
            return Settings.DeviceSource == DeviceRepository && !String.IsNullOrEmpty(current) && release.tag_name == version && HasDeviceTools(new ToolLocator(store).VersionRoot(current));
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
            var zip = Path.Combine(Root, name + ".zip");
            bool committed = false;
            try
            {
                Log("Обновление нашего комплекта для iPhone " + release.tag_name);
                await Download(asset, zip, ct);
                await Task.Run(delegate
                {
                    Extract(zip, stage);
                    CheckDevicePackage(stage);
                }, ct);
                await ValidateDeviceTools(stage, ct);
                ct.ThrowIfCancellationRequested();
                var old = Settings;
                Settings = old.Copy();
                Settings.PreviousDeviceCurrent = old.DeviceCurrent ?? (old.Source == null ? old.Current : null);
                Settings.PreviousDeviceVersion = old.DeviceVersion ?? (old.Source == null ? old.Version : null);
                Settings.PreviousDeviceSource = old.DeviceSource;
                Settings.DeviceCurrent = name;
                Settings.DeviceVersion = release.tag_name;
                Settings.DeviceSource = DeviceRepository;
                try { Save(); committed = true; }
                catch { Settings = old; throw; }
                Log("Инструменты для iPhone обновлены. Предыдущий комплект сохранён.");
            }
            finally
            {
                if (File.Exists(zip)) File.Delete(zip);
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
            Settings.DeviceSource = old.PreviousDeviceSource;
            Settings.PreviousDeviceSource = old.DeviceSource;
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

// Services/IAppToolClient.cs
namespace IpaDesktop
{
    public interface IAppToolClient
    {
        Task<Result> RunAsync(string[] arguments, CancellationToken cancellation, bool publish);
    }
}

// Services/LocalAppRepository.cs
namespace IpaDesktop
{
    public sealed class LocalAppRepository
    {
        readonly string folder;
        public LocalAppRepository(string folder)
        {
            this.folder = Path.GetFullPath(folder);
        }
        public Task<List<LocalRow>> ListAsync()
        {
            return Task.Run(delegate
            {
                return Directory.GetFiles(folder, "*.ipa").Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTime).Select(x => new LocalRow
                {
                    Name = x.Name, Size = (x.Length / 1048576.0).ToString("N1") + " МБ", Path = x.FullName, AppleAccount = IpaMetadata.ReadAccount(x.FullName)
                }).ToList();
            });
        }
        public Task ImportAsync(IEnumerable<string> paths, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = Path.Combine(folder, Path.GetFileName(path));
                    if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(target)) throw new IOException("Файл уже существует: " + Path.GetFileName(path));
                    File.Copy(path, target);
                }
            }, ct);
        }
        public void Delete(string file)
        {
            var path = Path.GetFullPath(file);
            if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) || !String.Equals(Path.GetExtension(path), ".ipa", StringComparison.OrdinalIgnoreCase)) throw new IOException("Файл не относится к папке IPA.");
            if (!File.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Удаление ссылок не поддерживается.");
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
    }
}

// Services/ToolResponse.cs
namespace IpaDesktop
{
    public static class ToolResponse
    {
        public static void Ensure(Result r)
        {
            if (r.Code != 0) throw new Exception("Код ошибки " + r.Code + "\n" + (r.Output.Length > 8000 ? r.Output.Substring(r.Output.Length - 8000) : r.Output));
        }
        public static Dictionary<string, object> Object(string output)
        {
            // Structured output is sometimes preceded by a human-readable diagnostic.
            foreach (var line in output.Split('\n').Reverse())
            {
                try
                {
                    var d = JsonCodec.DeserializeObject(line.Trim()) as Dictionary<string, object>;
                    if (d != null) return d;
                } catch (ArgumentException)
                {
                }
            }
            try
            {
                return JsonCodec.DeserializeObject(output.Trim()) as Dictionary<string, object> ?? new Dictionary<string, object>();
            } catch (Exception)
            {
                throw new Exception("Формат ответа движка изменился. См. журнал или выполните откат.");
            }
        }
        public static object Get(Dictionary<string, object> d, string key)
        {
            return d.Where(k => String.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase)).Select(k => k.Value).FirstOrDefault();
        }
        public static List<string> VersionIds(string text)
        {
            var values = Get(Object(text), "externalVersionIdentifiers") as object[];
            if (values == null) throw new Exception("Формат списка версий не поддерживается.");
            return values.Select(Convert.ToString).Where(x => x.Length > 0 && x.All(Char.IsDigit)).ToList();
        }
        public static List<AppRow> ParseApps(string text)
        {
            var d = Object(text);
            var values = Get(d, "apps") as object[];
            if (values == null) throw new Exception("В ответе отсутствует список apps. Проверьте совместимость движка.");
            return values.OfType<Dictionary<string, object>>().Select(x => new AppRow
            {
                Name = Convert.ToString(Get(x, "name")), ID = Convert.ToString(Get(x, "id"))
            }).Where(x => x.ID.All(Char.IsDigit) && x.ID.Length > 0).ToList();
        }
        public static List<AppRow> ParseCatalog(string text)
        {
            var rows = new List<AppRow>();
            foreach (var line in text.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.TrimEnd('\r'), @"^(.*):\s*(\d+)(?:[ \t]+(\d{1,2}\.\d{1,2}\.\d{4}))?\s*$");
                if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(line, @"^(.*?):\s*(\d+)");
                if (m.Success) rows.Add(new AppRow
                {
                    Name = m.Groups[1].Value.Trim(), ID = m.Groups[2].Value
                });
            }
            return rows.GroupBy(x => x.ID).Select(x => x.First()).ToList();
        }
        public static List<AppRow> MatchCatalog(string query, List<AppRow> custom, List<AppRow> catalog)
        {
            var ids = new HashSet<string>(custom.Select(x => x.ID));
            return custom.Concat(catalog.Where(x => !ids.Contains(x.ID))).Where(x => x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).GroupBy(x => x.ID).Select(x => x.First()).ToList();
        }
        public static List<AppRow> MergeApps(IEnumerable<AppRow> local, IEnumerable<AppRow> online)
        {
            return local.Concat(online).GroupBy(x => x.ID).Select(x => x.First()).ToList();
        }
    }
}
