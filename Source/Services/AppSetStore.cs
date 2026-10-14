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
            if (set.Files == null || set.Files.Length == 0 || set.Files.Length > 500) throw new IOException("В наборе должно быть от 1 до 500 IPA.");
            foreach (var name in set.Files)
                if (String.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(Char.IsControl) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !String.Equals(Path.GetExtension(name), ".ipa", StringComparison.OrdinalIgnoreCase)) throw new IOException("Набор содержит некорректное имя IPA.");
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
        public void Save(string name, IEnumerable<LocalRow> rows)
        {
            var names = rows.Select(row =>
            {
                var path = Path.GetFullPath(row.Path);
                if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Наборы сохраняют только существующие IPA из папки приложения.");
                return Path.GetFileName(path);
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var set = new AppSet { Name = (name ?? "").Trim(), Files = names };
            Validate(set);
            var sets = Load();
            sets.RemoveAll(x => String.Equals(x.Name, set.Name, StringComparison.OrdinalIgnoreCase));
            sets.Add(set);
            Write(sets);
        }
        public void Delete(string name)
        {
            var sets = Load();
            sets.RemoveAll(x => String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            Write(sets);
        }
        public List<LocalRow> Resolve(AppSet set, IEnumerable<LocalRow> available, out string[] missing)
        {
            Validate(set);
            var lookup = available.Where(x => String.Equals(Path.GetDirectoryName(Path.GetFullPath(x.Path)), folder, StringComparison.OrdinalIgnoreCase)).ToDictionary(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase);
            missing = set.Files.Where(x => !lookup.ContainsKey(x)).ToArray();
            return set.Files.Where(lookup.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => lookup[x]).ToList();
        }
    }
}
