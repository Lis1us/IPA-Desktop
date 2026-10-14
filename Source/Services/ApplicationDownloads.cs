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
    public sealed class ApplicationDownloads
    {
        readonly IAppToolClient tools;
        readonly string folder;
        public ApplicationDownloads(IAppToolClient tools, string folder)
        {
            this.tools = tools;
            this.folder = folder;
        }
        public async Task DownloadAsync(DownloadItem item, CancellationToken ct)
        {
            var arguments = new List<string>
            {
                "download", "-i", item.ID, "--purchase", "-o", folder
            };
            if (item.VersionID != null) arguments.AddRange(new[]
            {
                "--external-version-id", item.VersionID
            });
            ToolResponse.Ensure(await tools.RunAsync(arguments.ToArray(), ct, true));
        }
    }
}
