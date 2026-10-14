using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace IpaDesktop
{
    public static class NetworkClient
    {
        public static readonly HttpClient Http = CreateHttp();
        static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var h = new HttpClient();
            h.Timeout = TimeSpan.FromMinutes(15);
            h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/0.1.17");
            return h;
        }
    }
}
