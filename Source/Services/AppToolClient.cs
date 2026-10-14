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
    public sealed class AppToolClient : IAppToolClient
    {
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        public AppToolClient(IProcessRunner runner, ToolLocator locator)
        {
            this.runner = runner;
            this.locator = locator;
        }
        public Task<Result> RunAsync(string[] args, CancellationToken ct, bool publish)
        {
            return Task.Run(async delegate
            {
                return await runner.Run(locator.Find("ipatool*.exe"), args.Concat(new[]
                {
                    "--format", "json"
                }).ToArray(), ct, false, publish);
            }, ct);
        }
    }
}
