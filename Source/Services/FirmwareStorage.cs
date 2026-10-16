using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Отдельное хранилище IPSW: устойчивые имена по модели и сборке, без выбора папки при загрузке.</summary>
    public sealed class FirmwareStorage
    {
        public string Root { get; private set; }
        public FirmwareStorage(string root) { Root = Path.GetFullPath(root); }
        public string FilePath(FirmwareRelease release)
        {
            if (release == null || !Regex.IsMatch(release.identifier ?? "", @"^iPhone[0-9]{1,3},[0-9]{1,3}$") ||
                !Regex.IsMatch(release.buildid ?? "", "^[A-Za-z0-9]{1,32}$"))
                throw new ArgumentException("Неверная модель или сборка IPSW.");
            // Версия не входит в путь: модель + build однозначно определяют файл, независимо от подписи и URL.
            return Path.Combine(Root, release.identifier + "_" + release.buildid + "_Restore.ipsw");
        }
        public bool Present(FirmwareRelease release)
        {
            var info = new FileInfo(FilePath(release));
            return info.Exists && info.Length == release.filesize;
        }
        public void Refresh(FirmwareRelease release)
        {
            try { release.LocalStatus = Present(release) ? "Есть на диске" : File.Exists(FilePath(release)) ? "Неполная копия" : "Не скачана"; }
            catch (IOException) { release.LocalStatus = "Нет доступа"; }
            catch (UnauthorizedAccessException) { release.LocalStatus = "Нет доступа"; }
        }
        /// <summary>Существующий файл проверяется в фоне целиком; сеть нужна только для отсутствующей или неверной копии.</summary>
        public async Task<string> AcquireAsync(FirmwareCatalog catalog, FirmwareRelease release, CancellationToken ct, Action<FirmwareTransfer> progress)
        {
            Directory.CreateDirectory(Root);
            string target = FilePath(release);
            if (await catalog.VerifyLocalAsync(release, target, ct).ConfigureAwait(false)) return target;
            return await catalog.DownloadAsync(release, target, ct, progress).ConfigureAwait(false);
        }
    }
}
