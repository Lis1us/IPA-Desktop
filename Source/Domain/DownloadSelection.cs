using System;
using System.Collections.Generic;
using System.Linq;

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
