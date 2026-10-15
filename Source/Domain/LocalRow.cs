namespace IpaDesktop
{
    /// <summary>Строка локального IPA: путь, размер, версия, аккаунт и признак старой версии.</summary>
    public class LocalRow
    {
        public long Bytes { get; set; }
        public string BundleID { get; set; }
        public string AppName { get; set; }
        public string Version { get; set; }
        public string Build { get; set; }
        public bool IsOld { get; set; }
        public string StorageStatus { get; set; }
        public string AppleAccount
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public string Size
        {
            get;
            set;
        }
        public string Path
        {
            get;
            set;
        }
    }
}
