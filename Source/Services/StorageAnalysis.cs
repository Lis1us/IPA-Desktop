using System;
using System.Linq;
using System.Collections.Generic;
namespace IpaDesktop
{
    public static class StorageAnalysis
    {
        public static string Bytes(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("N2") + " ГБ";
            return (bytes / 1048576.0).ToString("N1") + " МБ";
        }
        static Version Parse(string text)
        {
            Version value;
            if (!Version.TryParse(text, out value)) return null;
            return new Version(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        }
        public static void Mark(IEnumerable<LocalRow> rows)
        {
            var list = rows.ToList();
            foreach (var row in list) { row.IsOld = false; row.StorageStatus = "Версия не определена"; }
            var known = list.Where(x => !String.IsNullOrWhiteSpace(x.BundleID) && x.AppleAccount != null && x.AppleAccount.Contains("@") && Parse(x.Version) != null);
            foreach (var group in known.GroupBy(x => x.BundleID.ToLowerInvariant() + "\n" + x.AppleAccount.ToLowerInvariant()))
            {
                var newest = group.Select(x => Parse(x.Version)).Max();
                foreach (var row in group)
                {
                    row.IsOld = Parse(row.Version).CompareTo(newest) < 0;
                    row.StorageStatus = row.IsOld ? "Старая версия" : "Самая новая в папке";
                }
            }
        }
    }
}
