using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Xml;

namespace IpaDesktop
{
    /// <summary>Читает только ограниченный BuildManifest, без распаковки многогигабайтного IPSW.</summary>
    public sealed class FirmwareManifest
    {
        public string Version { get; private set; }
        public string Build { get; private set; }
        public List<string> Products { get; private set; }
        readonly List<Tuple<string, string, string>> identities = new List<Tuple<string, string, string>>();
        const int MaxManifest = 32 * 1024 * 1024;

        static XmlNode Value(XmlNode dict, string key)
        {
            if (dict == null) return null;
            foreach (XmlNode child in dict.ChildNodes)
                if (child.Name == "key" && child.InnerText == key)
                {
                    var next = child.NextSibling;
                    while (next != null && next.NodeType != XmlNodeType.Element) next = next.NextSibling;
                    return next;
                }
            return null;
        }
        static string Text(XmlNode dict, string key)
        {
            var node = Value(dict, key);
            return node != null && node.Name == "string" ? node.InnerText : null;
        }
        public static FirmwareManifest Read(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entries = zip.Entries.Where(x => x.FullName == "BuildManifest.plist").ToList();
                if (entries.Count != 1 || entries[0].Length > MaxManifest) throw new InvalidDataException("IPSW не содержит однозначный BuildManifest допустимого размера.");
                using (var input = entries[0].Open())
                using (var bytes = new MemoryStream())
                {
                    var buffer = new byte[8192]; int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (bytes.Length + n > MaxManifest) throw new InvalidDataException("BuildManifest слишком велик.");
                        bytes.Write(buffer, 0, n);
                    }
                    bytes.Position = 0;
                    var doc = new XmlDocument { XmlResolver = null };
                    try
                    {
                        using (var reader = XmlReader.Create(bytes, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaxManifest }))
                            doc.Load(reader);
                    }
                    catch (XmlException) { throw new InvalidDataException("Не поддерживается формат BuildManifest. Прошивка не будет запущена."); }
                    var root = doc.SelectSingleNode("/plist/dict");
                    var result = new FirmwareManifest { Version = Text(root, "ProductVersion"), Build = Text(root, "ProductBuildVersion"), Products = new List<string>() };
                    var products = Value(root, "SupportedProductTypes");
                    if (products != null) foreach (XmlNode product in products.ChildNodes) if (product.Name == "string") result.Products.Add(product.InnerText);
                    var builds = Value(root, "BuildIdentities");
                    if (builds != null) foreach (XmlNode identity in builds.ChildNodes)
                    {
                        var info = Value(identity, "Info");
                        result.identities.Add(Tuple.Create(Text(info, "DeviceClass"), Text(info, "RestoreBehavior"), Text(info, "Variant")));
                    }
                    if (String.IsNullOrEmpty(result.Version) || String.IsNullOrEmpty(result.Build) || result.Products.Count == 0)
                        throw new InvalidDataException("IPSW не содержит версии, сборки или списка моделей.");
                    return result;
                }
            }
        }
        /// <summary>Запрещает несовместимую модель, неизвестный вариант и откат в режиме сохранения данных.</summary>
        public void Validate(FirmwareDevice device, bool erase)
        {
            if (device == null || !Products.Contains(device.ProductType)) throw new InvalidDataException("Эта IPSW не предназначена для выбранного iPhone.");
            string behavior = erase ? "Erase" : "Update";
            string variant = erase ? "Customer Erase Install (IPSW)" : "Customer Upgrade Install (IPSW)";
            if (!identities.Any(x => String.Equals(x.Item1, device.HardwareModel, StringComparison.OrdinalIgnoreCase) && x.Item2 == behavior && x.Item3 == variant))
                throw new InvalidDataException("IPSW не содержит подтверждённый вариант " + behavior + " для этой модели. Переход к другому режиму запрещён.");
            if (!erase)
            {
                System.Version current, target;
                if (!System.Version.TryParse(device.Version, out current) || !System.Version.TryParse(Version, out target) || target < current)
                    throw new InvalidDataException("Обновление с сохранением данных запрещено: версия неизвестна или ниже установленной.");
            }
        }
    }
}
