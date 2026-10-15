$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Net.Http,System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
Copy-Item (Join-Path $projectRoot 'IPA-Desktop.exe') (Join-Path $projectRoot 'IPA-Catalog-test.dll') -Force
Add-Type -ReferencedAssemblies @((Join-Path $projectRoot 'IPA-Catalog-test.dll'),'System.dll','System.Core.dll','System.Net.Http.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using IpaDesktop;
public sealed class CancelReadStream : MemoryStream {
 public Action Cancel;
 public CancelReadStream(byte[] data,Action cancel):base(data){Cancel=cancel;}
 public override Task<int> ReadAsync(byte[] b,int o,int n,CancellationToken ct){
  ct.ThrowIfCancellationRequested();int count=Read(b,o,Math.Min(n,32));Cancel();return Task.FromResult(count);
 }
}
public sealed class CatalogTransport : HttpMessageHandler {
 public byte[] Data;public bool Redirect,WrongModel;public Action Cancel;
 public int AppleCalls;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){
  ct.ThrowIfCancellationRequested();
  var r=new HttpResponseMessage(HttpStatusCode.OK);r.RequestMessage=req;
  if(req.RequestUri.Host=="api.ipsw.me"){
   string text;
   if(req.RequestUri.AbsolutePath.EndsWith("/devices"))
    text="[{\"identifier\":\"iPad15,7\",\"name\":\"iPad\"},{\"identifier\":\"iPhone16,2\",\"name\":\"iPhone 15 Pro Max\"}]";
   else text="{\"identifier\":\""+(WrongModel?"iPhone15,2":"iPhone16,2")+"\",\"firmwares\":[{\"identifier\":\"iPhone16,2\",\"version\":\"18.1\",\"buildid\":\"FixtureBuild\",\"filesize\":100,\"signed\":true,\"releasedate\":\"2025-01-01\"},{\"identifier\":\"iPhone16,2\",\"version\":\"18.0\",\"buildid\":\"OldBuild\",\"filesize\":100,\"signed\":false,\"releasedate\":\"2024-01-01\"}]}";
   r.Content=new StringContent(text);
  }else{
   AppleCalls++;
   if(Redirect){r.StatusCode=HttpStatusCode.Found;r.Headers.Location=new Uri("https://evil.example/file");r.Content=new ByteArrayContent(new byte[0]);}
   else if(Cancel!=null)r.Content=new StreamContent(new CancelReadStream(Data,Cancel));
   else r.Content=new ByteArrayContent(Data);
  }
  return Task.FromResult(r);
 }
}
public static class CatalogContract {
 static void Reject(Action action,string text){try{action();}catch(Exception){return;}throw new Exception("Accepted: "+text);}
 static string Hash(byte[] b,bool sha256){using(HashAlgorithm h=sha256?(HashAlgorithm)SHA256.Create():SHA1.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
 public static void Run(string temp){
  Directory.CreateDirectory(temp);
  string fixture=Path.Combine(temp,"fixture.ipsw");
  using(var z=ZipFile.Open(fixture,ZipArchiveMode.Create))
  using(var w=new StreamWriter(z.CreateEntry("BuildManifest.plist").Open()))
   w.Write("<plist><dict><key>ProductVersion</key><string>18.1</string><key>ProductBuildVersion</key><string>FixtureBuild</string><key>SupportedProductTypes</key><array><string>iPhone16,2</string></array><key>BuildIdentities</key><array/></dict></plist>");
  byte[] bytes=File.ReadAllBytes(fixture);
  var transport=new CatalogTransport{Data=bytes};
  using(var client=new HttpClient(transport))
  using(var catalog=new FirmwareCatalog(client)){
   var models=catalog.ModelsAsync(CancellationToken.None).GetAwaiter().GetResult();
   if(models.Count!=1||models[0].identifier!="iPhone16,2")throw new Exception("Model filtering failed");
   var rows=catalog.ReleasesAsync("iPhone16,2",CancellationToken.None).GetAwaiter().GetResult();
   if(rows.Count!=2||!rows[0].signed||rows[1].signed)throw new Exception("Catalog lost versions or signing status");
   transport.WrongModel=true;Reject(()=>catalog.ReleasesAsync("iPhone16,2",CancellationToken.None).GetAwaiter().GetResult(),"wrong catalog model");transport.WrongModel=false;
   foreach(string url in new[]{"http://updates.cdn-apple.com/file","https://updates.cdn-apple.com.evil.test/file","https://user@updates.cdn-apple.com/file","https://updates.cdn-apple.com:444/file"})
    if(FirmwareCatalog.AppleUrl(url))throw new Exception("Unsafe Apple URL accepted");
   var release=new FirmwareRelease{identifier="iPhone16,2",version="18.1",buildid="FixtureBuild",url="https://updates.cdn-apple.com/test.ipsw",filesize=bytes.Length,sha256sum=Hash(bytes,true),sha1sum=Hash(bytes,false)};
   string target=Path.Combine(temp,"Скачанная прошивка.ipsw");
   catalog.DownloadAsync(release,target,CancellationToken.None,null).GetAwaiter().GetResult();
   if(Hash(File.ReadAllBytes(target),true)!=release.sha256sum)throw new Exception("Download not committed");
   File.WriteAllText(target,"keep");
   release.sha256sum=new string('0',64);
   Reject(()=>catalog.DownloadAsync(release,target,CancellationToken.None,null).GetAwaiter().GetResult(),"bad SHA256 despite valid SHA1");
   if(File.ReadAllText(target)!="keep")throw new Exception("Bad hash replaced destination");
   release.sha256sum=Hash(bytes,true);release.filesize++;
   Reject(()=>catalog.DownloadAsync(release,target,CancellationToken.None,null).GetAwaiter().GetResult(),"wrong size");release.filesize--;
   transport.Redirect=true;
   int calls=transport.AppleCalls;
   Reject(()=>catalog.DownloadAsync(release,target,CancellationToken.None,null).GetAwaiter().GetResult(),"redirect");
   if(transport.AppleCalls!=calls+1)throw new Exception("Redirect was followed");
   transport.Redirect=false;
   using(var cts=new CancellationTokenSource()){
    transport.Cancel=()=>cts.Cancel();
    Reject(()=>catalog.DownloadAsync(release,target,cts.Token,null).GetAwaiter().GetResult(),"midstream cancellation");
    transport.Cancel=null;
   }
   if(File.ReadAllText(target)!="keep"||Directory.GetFiles(temp,"*.part").Length!=0)throw new Exception("Failure/cancellation did not preserve destination or clean partial file");
   release.identifier="iPhone15,2";
   Reject(()=>catalog.DownloadAsync(release,target,CancellationToken.None,null).GetAwaiter().GetResult(),"wrong downloaded manifest model");
   release.identifier="iPhone16,2";release.sha256sum=null;
   catalog.DownloadAsync(release,target,CancellationToken.None,null).GetAwaiter().GetResult();
   if(Hash(File.ReadAllBytes(target),false)!=release.sha1sum)throw new Exception("SHA1 fallback or verified overwrite failed");
  }
 }
}
'@
$temp=Join-Path ([IO.Path]::GetTempPath()) ('IPA-Catalog-'+[guid]::NewGuid().ToString('N'))
try{
 [CatalogContract]::Run($temp)
 Write-Host 'PASS: IPSW model/version catalog, signing flags, wrong model, HTTPS host restrictions, SHA256 preference, SHA1 fallback, size, redirect refusal, midstream cancellation, manifest validation, atomic overwrite and partial cleanup'
}finally{if(Test-Path $temp){Remove-Item $temp -Recurse -Force}}
