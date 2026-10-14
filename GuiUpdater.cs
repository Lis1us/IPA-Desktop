using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
namespace IpaDesktop {
 public static class GuiUpdater {
  public const string CurrentVersion="0.1.9";
  public static readonly string[] PackageFiles={"IPA-Desktop.exe","IPA-Desktop.exe.config","MainWindow.xaml","App.ico","Update-App.ps1","Core.cs","App.cs","IpaMetadata.cs","WindowTheme.cs","GuiUpdater.cs","Build.cmd","Start.cmd","README.md","LICENSE.txt","Verify.ps1","Verify-IpaMetadata.ps1","Verify-Updater.ps1"};
  public static Version ParseVersion(string tag) {
   Version v;
   if(tag==null||!System.Text.RegularExpressions.Regex.IsMatch(tag,"^v?[0-9]+\\.[0-9]+\\.[0-9]+$")||!Version.TryParse(tag.TrimStart('v'),out v))throw new InvalidDataException("Некорректная версия релиза.");
   return v;
  }
  static HttpClient Client() {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var h=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/"+CurrentVersion);return h;
  }
  public static async Task<Release> Check(CancellationToken ct) {
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
    timeout.CancelAfter(TimeSpan.FromSeconds(15));
    using(var h=Client())using(var response=await h.GetAsync("https://api.github.com/repos/Lis1us/IPA-Desktop/releases/latest",timeout.Token)) {
     response.EnsureSuccessStatusCode();var r=JsonCodec.Deserialize<Release>(await response.Content.ReadAsStringAsync());
     return ParseVersion(r.tag_name)>new Version(CurrentVersion)?r:null;
    }
   }
  }
  public static Asset SelectAsset(Release release) {
   var version=ParseVersion(release.tag_name).ToString();
   var matches=(release.assets??new Asset[0]).Where(a=>a.name=="IPA-Desktop-"+version+"-Windows.zip").ToArray();
   if(matches.Length!=1)throw new InvalidDataException("В релизе нет единственного ZIP для Windows.");
   var asset=matches[0];
   string expected="https://github.com/Lis1us/IPA-Desktop/releases/download/"+release.tag_name+"/"+asset.name;
   if(asset.browser_download_url!=expected||asset.size<=0||asset.size>64*1024*1024||asset.digest==null||!System.Text.RegularExpressions.Regex.IsMatch(asset.digest,"^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("Не удалось проверить источник, размер или SHA-256 обновления.");
   return asset;
  }
  public static void Extract(string zip,string stage) {
   using(var archive=ZipFile.OpenRead(zip)) {
    long total=0;
    foreach(var name in PackageFiles) {
     var entries=archive.Entries.Where(e=>e.FullName==name).ToArray();
     if(entries.Length!=1||entries[0].Length==0||entries[0].Length>32*1024*1024)throw new InvalidDataException("Неполный пакет обновления: "+name);
     total+=entries[0].Length;if(total>128*1024*1024)throw new InvalidDataException("Пакет слишком велик.");
    }
    Directory.CreateDirectory(stage);
    foreach(var name in PackageFiles)archive.GetEntry(name).ExtractToFile(Path.Combine(stage,name));
   }
  }
  public static async Task<string> Prepare(Release release,CancellationToken ct,Action<double> progress) {
   var asset=SelectAsset(release);
   string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IPA-Desktop","GuiUpdates");
   Directory.CreateDirectory(root);
   foreach(var dir in Directory.GetDirectories(root,"update-*"))try {if(Directory.GetLastWriteTimeUtc(dir)<DateTime.UtcNow.AddDays(-7))Directory.Delete(dir,true);}catch(IOException){}catch(UnauthorizedAccessException){}
   string work=Path.Combine(root,"update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
   string zip=Path.Combine(work,"package.zip"),stage=Path.Combine(work,"stage");
   try {
    using(var h=Client())using(var response=await h.GetAsync(asset.browser_download_url,HttpCompletionOption.ResponseHeadersRead,ct)) {
     response.EnsureSuccessStatusCode();
     using(var input=await response.Content.ReadAsStreamAsync())using(var output=File.Create(zip)) {
      byte[] buffer=new byte[65536];long total=0;int n;
      while((n=await input.ReadAsync(buffer,0,buffer.Length,ct))>0) {total+=n;if(total>asset.size)throw new InvalidDataException("Размер обновления не совпал.");await output.WriteAsync(buffer,0,n,ct);progress(total*100.0/asset.size);}
      if(total!=asset.size)throw new InvalidDataException("Обновление скачалось не полностью.");
     }
    }
    using(var sha=SHA256.Create())using(var file=File.OpenRead(zip)) {
     string digest=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
     if(!digest.Equals(asset.digest.Substring(7),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA-256 обновления не совпал.");
    }
    await Task.Run(delegate {ct.ThrowIfCancellationRequested();Extract(zip,stage);},ct);
    File.Delete(zip);return stage;
   } catch {try {Directory.Delete(work,true);}catch{}throw;}
  }
  static string QuotePS(string value) {return "'"+value.Replace("'","''")+"'";}
  public static void Launch(string stage) {
   string target=AppDomain.CurrentDomain.BaseDirectory;
   string probe=Path.Combine(target,".update-probe-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);
   string helper=Path.Combine(Path.GetDirectoryName(stage),"Apply.ps1");
   File.Copy(Path.Combine(target,"Update-App.ps1"),helper);
   string command="& "+QuotePS(helper)+" -Stage "+QuotePS(stage)+" -Target "+QuotePS(target)+" -ParentId "+Process.GetCurrentProcess().Id;
   string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
   Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -EncodedCommand "+encoded){UseShellExecute=false,CreateNoWindow=true});
  }
 }
}
