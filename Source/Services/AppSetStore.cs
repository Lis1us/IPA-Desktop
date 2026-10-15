using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
namespace IpaDesktop
{
    public sealed class AppSetStore
    {
        readonly string file, folder;
        public AppSetStore(string root, string downloads)
        {
            file = Path.Combine(root, "AppSets.json");
            folder = Path.GetFullPath(downloads);
        }
        static void Validate(AppSet set)
        {
            if (set == null || String.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 64 || set.Name.Any(Char.IsControl)) throw new IOException("Название набора: от 1 до 64 символов.");
            if (set.Apps != null)
            {
                if (set.Apps.Length == 0 || set.Apps.Length > 500) throw new IOException("В наборе должно быть от 1 до 500 приложений.");
                foreach (var app in set.Apps)
                    if (app == null || !ValidId(app.ID) || String.IsNullOrWhiteSpace(app.Name) || app.Name.Length > 320 || app.Name.Any(Char.IsControl)) throw new IOException("Набор содержит некорректный App Store ID или название.");
                if (set.Apps.Select(x => x.ID).Distinct().Count() != set.Apps.Length) throw new IOException("App Store ID в наборе повторяются.");
            }
            else
            {
                if (set.Files == null || set.Files.Length == 0 || set.Files.Length > 500) throw new IOException("Пустой набор.");
                foreach (var name in set.Files)
                    if (String.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(Char.IsControl) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !String.Equals(Path.GetExtension(name), ".ipa", StringComparison.OrdinalIgnoreCase)) throw new IOException("Набор содержит некорректное имя IPA.");
            }
        }
        public List<AppSet> Load()
        {
            if (!File.Exists(file)) return new List<AppSet>();
            if (new FileInfo(file).Length > 1000000) throw new IOException("Файл наборов слишком велик.");
            var sets = JsonCodec.Deserialize<List<AppSet>>(File.ReadAllText(file, Encoding.UTF8));
            if (sets == null || sets.Count > 100) throw new IOException("Некорректный файл наборов.");
            foreach (var set in sets) Validate(set);
            if (sets.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new IOException("Имена наборов повторяются.");
            return sets.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        void Write(List<AppSet> sets)
        {
            if (sets.Count > 100) throw new IOException("Можно сохранить не более 100 наборов.");
            var text = JsonCodec.Serialize(sets);
            if (Encoding.UTF8.GetByteCount(text) > 1000000) throw new IOException("Файл наборов слишком велик.");
            File.WriteAllText(file + ".tmp", text, Encoding.UTF8);
            if (File.Exists(file)) File.Replace(file + ".tmp", file, null);
            else File.Move(file + ".tmp", file);
        }
        public static bool ValidId(string id)
        {
            ulong value;
            return !String.IsNullOrEmpty(id) && id.Length <= 20 && id.All(c => c >= '0' && c <= '9') && UInt64.TryParse(id, out value) && value > 0;
        }
        public void SaveApps(string name, IEnumerable<AppRow> apps)
        {
            var set = new AppSet { Name = (name ?? "").Trim(), Apps = apps.GroupBy(x => x.ID).Select(g => new AppRow { ID = g.Key, Name = g.First().Name }).ToArray() };
            Validate(set);
            var sets = Load();
            sets.RemoveAll(x => String.Equals(x.Name, set.Name, StringComparison.OrdinalIgnoreCase));
            sets.Add(set);
            Write(sets);
        }
        public void AddApps(string name, IEnumerable<AppRow> apps)
        {
            name = (name ?? "").Trim();
            var existing = Load().FirstOrDefault(x => String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var combined = existing == null ? new List<AppRow>() : ResolveApps(existing).ToList();
            combined.AddRange(apps);
            SaveApps(name, combined);
        }
        public void Save(string name, IEnumerable<LocalRow> rows)
        {
            SaveApps(name, rows.Select(row => FromFile(Path.GetFileName(row.Path), row.Path)).ToArray());
        }
        AppRow FromFile(string name, string path)
        {
            path = Path.GetFullPath(path);
            if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("IPA отсутствует в папке приложения: " + name + ". Создайте набор из результатов поиска.");
            var details = IpaMetadata.ReadDetails(path);
            if (!ValidId(details.AppStoreID)) throw new IOException("В IPA нет App Store ID: " + name + ". Создайте набор из результатов поиска.");
            return new AppRow { ID = details.AppStoreID, Name = details.AppName ?? ("Приложение " + details.AppStoreID) };
        }
        public AppRow[] ResolveApps(AppSet set)
        {
            Validate(set);
            if (set.Apps != null) return set.Apps.Select(x => new AppRow { ID = x.ID, Name = x.Name }).ToArray();
            // Convert only from IPA metadata, never infer IDs from filenames or reuse their account.
            return set.Files.Select(name => FromFile(name, Path.Combine(folder, name))).GroupBy(x => x.ID).Select(g => g.First()).ToArray();
        }
        public void Delete(string name)
        {
            var sets = Load();
            sets.RemoveAll(x => String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            Write(sets);
        }
    }
}
