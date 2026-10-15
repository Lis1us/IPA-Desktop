namespace IpaDesktop
{
    /// <summary>Обнаруженный iPhone: подпись для интерфейса и реальный UDID для команд установки.</summary>
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
