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
        public string CurrentRoot
        {
            get
            {
                if (String.IsNullOrEmpty(Settings.Current)) throw new Exception("Сначала установите движок: кнопка «Проверить обновления».");
                var p = Path.GetFullPath(Path.Combine(Root, "Versions", Settings.Current));
                var prefix = Path.GetFullPath(Path.Combine(Root, "Versions")) + Path.DirectorySeparatorChar;
                if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Некорректный путь движка.");
                return p;
            }
        }
        public string Find(string pattern)
        {
            var files = Directory.GetFiles(CurrentRoot, pattern, SearchOption.AllDirectories).Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).ToArray();
            if (files.Length != 1) throw new Exception("Не найдена однозначная Windows-утилита: " + pattern);
            return files[0];
        }
    }
}
