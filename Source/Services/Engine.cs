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
        public AppSetStore AppSets { get; private set; }
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
            AppSets = new AppSetStore(root, Downloads);
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
