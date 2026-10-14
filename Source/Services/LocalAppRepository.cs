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
