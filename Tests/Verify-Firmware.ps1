$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
Copy-Item (Join-Path $projectRoot 'IPA-Desktop.exe') (Join-Path $projectRoot 'IPA-Firmware-test.dll') -Force
Add-Type -ReferencedAssemblies @((Join-Path $projectRoot 'IPA-Firmware-test.dll'),'System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using IpaDesktop;
public sealed class FirmwareRunner : IProcessRunner {
 public Action<string> Log {get;set;}
 public Process Active {get{return null;}}
 public bool Guard=true, Changed=false, Fail=false, Ran=false, CanCancel=true, Locked=false;
 public string[] Arguments; public string Prepared;
 public Task<Result> Run(string exe,string[] args,CancellationToken ct,bool visible,bool publish,string workingDirectory=null) {
  ct.ThrowIfCancellationRequested();
  string output="";
  if(Path.GetFileName(exe)=="idevice_id.exe") output="00008100-0000000000000001";
  else if(Path.GetFileName(exe)=="ideviceinfo.exe") output="UniqueChipID: "+(Changed?"456":"123")+"\nDeviceName: Test Phone\nProductType: iPhone16,2\nHardwareModel: D84AP\nProductVersion: 18.0";
  else if(args[0]=="--help") output=Guard?"--ipa-desktop-update-only":"upstream help";
  else {
   Ran=true;CanCancel=ct.CanBeCanceled;Arguments=args;
   Prepared=Path.Combine(workingDirectory,args[args.Length-1]);
   try {using(var f=new FileStream(Prepared,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){} }
   catch(IOException){Locked=true;}
   if(Fail)return Task.FromResult(new Result{Code=1,Output="TSS rejected fixture"});
  }
  return Task.FromResult(new Result{Code=0,Output=output});
 }
}
public static class FirmwareContract {
 public static void Archive(string path,string version,string products,string hardware,bool upgrade,bool erase) {
  using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)) {
   var entry=zip.CreateEntry("BuildManifest.plist");
   using(var w=new StreamWriter(entry.Open())) {
    w.Write("<plist><dict><key>ProductVersion</key><string>"+version+"</string><key>ProductBuildVersion</key><string>FixtureBuild</string><key>SupportedProductTypes</key><array><string>"+products+"</string></array><key>BuildIdentities</key><array>");
    foreach(var mode in new[]{"Update","Erase"}) if((mode=="Update"&&upgrade)||(mode=="Erase"&&erase))
     w.Write("<dict><key>Info</key><dict><key>DeviceClass</key><string>"+hardware+"</string><key>RestoreBehavior</key><string>"+mode+"</string><key>Variant</key><string>Customer "+(mode=="Update"?"Upgrade":"Erase")+" Install (IPSW)</string></dict></dict>");
    w.Write("</array></dict></plist>");
   }
  }
 }
 static void Reject(Action action,string label) {
  try{action();}catch(Exception){return;}throw new Exception("Safety gate accepted: "+label);
 }
 public static void Run(string temp) {
  Directory.CreateDirectory(Path.Combine(temp,"tools","windows_amd64"));
  foreach(var name in new[]{"idevice_id","ideviceinfo","idevicerestore"})
   File.WriteAllText(Path.Combine(temp,"tools","windows_amd64",name+".exe"),"fixture");
  string valid=Path.Combine(temp,"Тест прошивки.ipsw");
  Archive(valid,"18.1","iPhone16,2","d84ap",true,true);
  var runner=new FirmwareRunner();var service=new FirmwareService(runner,Path.Combine(temp,"tools"));
  var device=service.FindAsync(CancellationToken.None).GetAwaiter().GetResult()[0];
  var expected=FirmwareManifest.Read(valid);
  expected.Validate(device,false);expected.Validate(device,true);
  var other=new FirmwareDevice{ProductType="iPhone15,2",HardwareModel="d84ap",Version="18.0"};
  Reject(()=>expected.Validate(other,false),"wrong product");
  other.ProductType=device.ProductType;other.HardwareModel="other";
  Reject(()=>expected.Validate(other,true),"wrong board");
  other.HardwareModel=device.HardwareModel;other.Version="19.0";
  Reject(()=>expected.Validate(other,false),"downgrade");
  string eraseOnly=Path.Combine(temp,"erase.ipsw");Archive(eraseOnly,"18.1","iPhone16,2","d84ap",false,true);
  Reject(()=>FirmwareManifest.Read(eraseOnly).Validate(device,false),"erase fallback");
  string noManifest=Path.Combine(temp,"invalid.ipsw");
  using(var zip=ZipFile.Open(noManifest,ZipArchiveMode.Create)){zip.CreateEntry("Other.plist");}
  Reject(()=>FirmwareManifest.Read(noManifest),"missing manifest");
  runner.Guard=false;Reject(()=>service.FindAsync(CancellationToken.None).GetAwaiter().GetResult(),"unpatched tool");
  runner.Guard=true;runner.Changed=true;
  Reject(()=>service.RestoreAsync(device,valid,expected,false,CancellationToken.None,()=>{},()=>{}).GetAwaiter().GetResult(),"changed ECID");
  if(runner.Ran)throw new Exception("Restore started for a different phone");
  runner.Changed=false;
  using(var cts=new CancellationTokenSource()){
   cts.Cancel();Reject(()=>service.RestoreAsync(device,valid,expected,false,cts.Token,()=>{},()=>{}).GetAwaiter().GetResult(),"cancellation");
  }
  int begin=0,end=0;
  service.RestoreAsync(device,valid,expected,false,CancellationToken.None,()=>begin++,()=>end++).GetAwaiter().GetResult();
  if(!runner.Ran||runner.CanCancel||!runner.Locked||begin!=1||end!=1||Array.IndexOf(runner.Arguments,"--ipa-desktop-update-only")<0||Array.IndexOf(runner.Arguments,"--erase")>=0)
   throw new Exception("Unsafe update command or lifecycle");
  if(File.Exists(runner.Prepared)||!File.Exists(valid))throw new Exception("Staging cleanup changed original IPSW");
  runner.Fail=true;begin=end=0;
  Reject(()=>service.RestoreAsync(device,valid,expected,true,CancellationToken.None,()=>begin++,()=>end++).GetAwaiter().GetResult(),"TSS failure");
  if(begin!=1||end!=1||Array.IndexOf(runner.Arguments,"--erase")<0||File.Exists(runner.Prepared))throw new Exception("Erase/error lifecycle failed");
  var progress=new QueueProgress();progress.Begin("Прошивка",1,1,"Phone");
  if(!progress.Feed("progress: 4 0.500000")||progress.Value!=50||!progress.Text.Contains("этап 4"))throw new Exception("Stage progress was not parsed");
 }
}
'@
$temp=Join-Path ([IO.Path]::GetTempPath()) ('IPA-Firmware-'+[guid]::NewGuid().ToString('N'))
try{
 [FirmwareContract]::Run($temp)
 $engine=New-Object IpaDesktop.Engine((Join-Path $temp 'state'))
 $desktop=New-Object IpaDesktop.Desktop($engine,$false,$projectRoot)
 $window=$desktop.Window
 try{
  $window.Show();$window.FindName('FirmwareTab').IsSelected=$true;$window.UpdateLayout()
  $columns=$window.FindName('FirmwareReleases').Columns
  if($columns[2].ActualWidth -lt 100 -or $columns[5].ActualWidth -lt 100){throw 'Firmware signing or local presence column is collapsed'}
  if(!$window.FindName('FirmwareSignedOnly').IsChecked -or !$window.FindName('FirmwareLoadModels').IsEnabled){throw 'Catalog is unavailable or unsigned firmware shown by default'}
  if($window.FindName('FirmwareStart').IsEnabled){throw 'Restore must not be enabled before validation and confirmation'}
  if(!$window.FindName('FirmwareChoose').IsEnabled -or !$window.FindName('FirmwareUpdate').IsChecked){throw 'Firmware tab unavailable or erase selected by default'}
  $method=[IpaDesktop.Desktop].GetMethod('SetFirmwareCritical',[Reflection.BindingFlags]'Instance,NonPublic')
  $method.Invoke($desktop,@($true))|Out-Null
  if($window.FindName('Cancel').IsEnabled){throw 'Cancel enabled during firmware critical phase'}
  $method.Invoke($desktop,@($false))|Out-Null
  $target=$window.Content
  $bmp=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$target.ActualWidth,[int]$target.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($target);$png=New-Object Windows.Media.Imaging.PngBitmapEncoder
  $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $file=Join-Path $projectRoot 'Firmware-Preview.png';$stream=[IO.File]::Create($file)
  try{$png.Save($stream)}finally{$stream.Dispose()}
 }finally{$window.Close()}
 Write-Host 'PASS: firmware compatibility, wrong board/product, downgrade, erase fallback, guard, changed ECID, cancellation, locked staging, no forced kill, failure cleanup and WPF controls'
}finally{if(Test-Path $temp){Remove-Item $temp -Recurse -Force}}
