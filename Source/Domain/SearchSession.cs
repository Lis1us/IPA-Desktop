namespace IpaDesktop
{
    public class SearchSession
    {
        int revision;
        string query = "";
        public int Change(string value)
        {
            query = (value ?? "").Trim();
            return++revision;
        }
        public int Revision
        {
            get
            {
                return revision;
            }
        }
        public bool IsCurrent(int request)
        {
            return request == revision && query.Length > 0;
        }
    }
}
