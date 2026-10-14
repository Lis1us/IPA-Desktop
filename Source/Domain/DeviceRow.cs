namespace IpaDesktop
{
    public class DeviceRow
    {
        public string ID
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public override string ToString()
        {
            return Name;
        }
    }
}
