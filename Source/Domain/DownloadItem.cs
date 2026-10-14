namespace IpaDesktop
{
    public class DownloadItem
    {
        public string Name
        {
            get;
            set;
        }
        public string ID
        {
            get;
            set;
        }
        public string VersionID
        {
            get;
            set;
        }
        public string Label
        {
            get
            {
                return Name + (VersionID == null ? " · последняя" : " · версия " + VersionID);
            }
        }
    }
}
