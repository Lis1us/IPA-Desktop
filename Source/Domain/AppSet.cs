using System;
namespace IpaDesktop
{
    public sealed class AppSet
    {
        public string Name { get; set; }
        public string[] Files { get; set; }
        public string Label { get { return Name + " · " + (Files == null ? 0 : Files.Length) + " IPA"; } }
    }
}
