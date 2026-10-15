using System;
using System.Collections.Generic;
using System.Linq;

namespace IpaDesktop
{
    /// <summary>Очередь загрузок, уникальная по App Store ID; снимки отделены от внутреннего состояния.</summary>
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
        /// <summary>Дополняет очередь набором, сохраняя уже выбранную пользователем версию приложения.</summary>
        public int AddMissing(IEnumerable<AppRow> apps)
        {
            var incoming = AppSetRules.UniqueApps(apps);
            int added = 0;
            foreach (var app in incoming)
            {
                if (items.Any(x => x.ID == app.ID)) continue;
                Add(app, null); added++;
            }
            return added;
        }
        /// <summary>Возвращает копии элементов, чтобы текущая операция не зависела от дальнейших изменений выбора.</summary>
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
