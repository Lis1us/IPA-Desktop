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
