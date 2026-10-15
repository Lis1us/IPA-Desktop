using System;
using System.Globalization;

namespace IpaDesktop
{
    /// <summary>Модель в каталоге IPSW.me; идентификатор используется в API и проверке IPSW.</summary>
    public sealed class FirmwareModel
    {
        public string identifier { get; set; }
        public string name { get; set; }
        public override string ToString() { return name + " · " + identifier; }
    }
    /// <summary>Метаданные каталога; signed — справочный статус, а не разрешение Apple TSS.</summary>
    public sealed class FirmwareRelease
    {
        public string identifier { get; set; }
        public string version { get; set; }
        public string buildid { get; set; }
        public string url { get; set; }
        public string sha256sum { get; set; }
        public string sha1sum { get; set; }
        public long filesize { get; set; }
        public bool signed { get; set; }
        public string releasedate { get; set; }
        public string Signing { get { return signed ? "Подписывается" : "Не подписывается"; } }
        public string Size { get { return (filesize / 1073741824.0).ToString("0.00", CultureInfo.CurrentCulture) + " ГБ"; } }
        public string Date { get { DateTime value; return DateTime.TryParse(releasedate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value) ? value.ToString("dd.MM.yyyy") : "—"; } }
    }
    public sealed class FirmwareTransfer
    {
        public long Bytes { get; set; }
        public long Total { get; set; }
        public double BytesPerSecond { get; set; }
    }
}
