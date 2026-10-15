using System;

namespace IpaDesktop
{
    /// <summary>Снимок конкретного iPhone; ECID сохраняет адресацию после перехода в Recovery.</summary>
    public sealed class FirmwareDevice
    {
        public string Udid { get; set; }
        public string Ecid { get; set; }
        public string Name { get; set; }
        public string ProductType { get; set; }
        public string HardwareModel { get; set; }
        public string Version { get; set; }
        public override string ToString() { return Name + " · " + ProductType + " · iOS " + Version; }
    }
}
