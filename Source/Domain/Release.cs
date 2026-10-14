namespace IpaDesktop
{
    public class Release
    {
        public string tag_name
        {
            get;
            set;
        }
        public string html_url
        {
            get;
            set;
        }
        public string body
        {
            get;
            set;
        }
        public Asset[] assets
        {
            get;
            set;
        }
    }
}
