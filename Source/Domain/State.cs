namespace IpaDesktop
{
    public class State
    {
        public State()
        {
            AutoUpdate = true;
            CheckGuiUpdates = true;
        }
        public string Source { get; set; }
        public string PreviousSource { get; set; }
        public string DeviceCurrent { get; set; }
        public string DeviceVersion { get; set; }
        public string Current
        {
            get;
            set;
        }
        public string Previous
        {
            get;
            set;
        }
        public string Version
        {
            get;
            set;
        }
        public string PreviousVersion
        {
            get;
            set;
        }
        public bool AutoUpdate
        {
            get;
            set;
        }
        public bool CheckGuiUpdates
        {
            get;
            set;
        }
    }
}
