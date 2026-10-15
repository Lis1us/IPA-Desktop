$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
Copy-Item (Join-Path $projectRoot 'IPA-Desktop.exe') (Join-Path $projectRoot 'IPA-Phone-test.dll') -Force
Add-Type -ReferencedAssemblies @((Join-Path $projectRoot 'IPA-Phone-test.dll'),'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll',[Windows.Window].Assembly.Location,[Windows.RoutedEventArgs].Assembly.Location,[Windows.Threading.Dispatcher].Assembly.Location) -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IpaDesktop;
public sealed class PhoneRunner : IProcessRunner {
 public Action<string> Log {get;set;}public Process Active{get{return null;}}
 public bool Restricted,Empty,Slow,MissingCharge; public int Charge=75;public readonly List<string[]> Calls=new List<string[]>();
 public const string Id="00008100-0000000000000001";
 public async Task<Result> Run(string exe,string[] args,CancellationToken ct,bool visible,bool publish,string workingDirectory=null){
  ct.ThrowIfCancellationRequested();Calls.Add(args);
  if(publish)throw new Exception("Diagnostic commands must not publish private raw output");
  foreach(string arg in args)if(new[]{"restart","shutdown","sleep","install","restore"}.Contains(arg))throw new Exception("Write command issued");
  if(Slow)await Task.Delay(30000,ct);
  string name=Path.GetFileName(exe),text="";
  if(name=="idevice_id.exe")text=Empty?"":Id;
  else if(name=="ideviceinfo.exe"){
   if(args.Length==2)text="UniqueDeviceID: "+Id+"\nDeviceName: TestOwnerPhone\nProductType: iPhone16,2\nModelNumber: MY123\nHardwareModel: D84AP\nProductVersion: 18.0\nBuildVersion: 22A123\nSerialNumber: SERIAL_PRIVATE_123\nInternationalMobileEquipmentIdentity: 123456789123456\nActivationState: Activated";
   else if(args[3]=="com.apple.mobile.battery")text=(MissingCharge?"":"BatteryCurrentCapacity: "+Charge+"\n")+"BatteryIsCharging: false\nExternalConnected: true";
   else text="TotalDiskCapacity: 137438953472\nTotalDataCapacity: 107374182400\nTotalDataAvailable: 53687091200";
  }else{
   if(Restricted)return new Result{Code=1,Output="restricted diagnostics"};
   text="<plist><dict><key>IORegistry</key><dict><key>CycleCount</key><integer>120</integer><key>DesignCapacity</key><integer>3200</integer><key>NominalChargeCapacity</key><integer>2810</integer><key>MaximumCapacityPercent</key><integer>88</integer></dict></dict></plist>";
  }
  return new Result{Code=0,Output=text};
 }
}
public static class PhoneContract{
 public static PhoneDiagnosticsService Service;public static PhoneRunner Runner;public static string Temp;
 static string Value(PhoneDiagnostic report,string name){return report.Values.Single(x=>x.Name==name).Value;}
 static void Check(bool condition,string error){if(!condition)throw new Exception(error);}
 public static void Run(string temp){
  Temp=temp;string folder=Path.Combine(temp,"tools","windows_amd64");Directory.CreateDirectory(folder);
  foreach(var tool in new[]{"idevice_id","ideviceinfo","idevicediagnostics"})File.WriteAllText(Path.Combine(folder,tool+".exe"),"fixture");
  Runner=new PhoneRunner();Service=new PhoneDiagnosticsService(Runner,Path.Combine(temp,"tools"));
  var devices=Service.FindAsync(CancellationToken.None).GetAwaiter().GetResult();
  Check(devices.Count==1&&devices[0].Name.Contains("iPhone16,2"),"Device model/name missing");
  var report=Service.ReadAsync(devices[0],CancellationToken.None,s=>{}).GetAwaiter().GetResult();
  Check(Value(report,"Циклы зарядки")=="120"&&Value(report,"Проектная ёмкость").Contains("3200"),"Battery fields lost");
  Check(Value(report,"Соотношение ёмкостей (оценка)").StartsWith("87"),"Capacity ratio incorrect");
  Check(Value(report,"Свободно в разделе данных").StartsWith("50"),"64-bit disk space incorrect");
  string masked=report.Export(true),full=report.Export(false);
  foreach(string secret in new[]{PhoneRunner.Id,"SERIAL_PRIVATE_123","123456789123456","TestOwnerPhone"}){
   Check(!masked.Contains(secret),"Private field leaked: "+secret);Check(full.Contains(secret),"Full export lost identifier");
  }
  Runner.Restricted=true;Runner.MissingCharge=true;
  report=Service.ReadAsync(devices[0],CancellationToken.None,s=>{}).GetAwaiter().GetResult();
  Check(Value(report,"Циклы зарядки")=="Недоступно"&&Value(report,"Заряд аккумулятора")=="Недоступно"&&report.Warnings.Count>0,"Unavailable battery was treated as zero or failure of entire report");
  Runner.Restricted=false;Runner.MissingCharge=false;Runner.Charge=0;
  report=Service.ReadAsync(devices[0],CancellationToken.None,s=>{}).GetAwaiter().GetResult();
  Check(Value(report,"Заряд аккумулятора").StartsWith("0"),"Real zero charge lost");
  Runner.Empty=true;Check(Service.FindAsync(CancellationToken.None).GetAwaiter().GetResult().Count==0,"Empty device list is an error");Runner.Empty=false;
  var ambiguous=PhoneDiagnosticsService.BatteryValues("<plist><dict><key>A</key><dict><key>CycleCount</key><integer>1</integer></dict><key>B</key><dict><key>CycleCount</key><integer>2</integer></dict></dict></plist>");
  Check(!ambiguous.ContainsKey("CycleCount"),"Ambiguous measurement selected");
  Runner.Slow=true;
  using(var cts=new CancellationTokenSource()){
   cts.CancelAfter(20);bool canceled=false;
   try{Service.ReadAsync(devices[0],cts.Token,s=>{}).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}
   Check(canceled,"Read cancellation lost");
  }
  Runner.Slow=false;
 }
 public static void Attach(Engine engine){typeof(Engine).GetProperty("PhoneDiagnostics").SetValue(engine,Service,null);}
 static T Child<T>(DependencyObject root)where T:DependencyObject{
  if(root is T)return(T)root;
  foreach(var child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject){var found=Child<T>((DependencyObject)child);if(found!=null)return found;}return null;
 }
 public static void QueueDialogTest(Window owner){
  owner.Dispatcher.BeginInvoke(new Action(delegate{
   var modal=owner.OwnedWindows.Cast<Window>().Single(x=>x.Title=="Диагностика iPhone");
   var toolbar=Child<WrapPanel>(modal);
   var find=(Button)toolbar.Children[0];var combo=(ComboBox)toolbar.Children[1];var read=(Button)toolbar.Children[2];
   find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(combo.Items.Count==1&&read.IsEnabled,"Find controller did not expose the phone");
   read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   var grid=Child<DataGrid>(modal);
   Check(grid.Items.Count>15,"Controller did not display report");
   Check(grid.Items.Cast<PhoneDiagnosticValue>().Single(x=>x.Name=="Серийный номер").Value=="[СКРЫТО]","Default privacy lost in UI");
   modal.Close();
  }));
 }
}
'@
$temp=Join-Path ([IO.Path]::GetTempPath()) ('IPA-Phone-'+[guid]::NewGuid().ToString('N'))
try{
 [PhoneContract]::Run($temp)
 $engine=New-Object IpaDesktop.Engine((Join-Path $temp 'state'))
 [PhoneContract]::Attach($engine)
 $desktop=New-Object IpaDesktop.Desktop($engine,$false,$projectRoot)
 $window=$desktop.Window
 try{
  $window.Show()
  [PhoneContract]::QueueDialogTest($window)
  $window.FindName('OpenPhoneDiagnostics').RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
  if($window.OwnedWindows.Count -ne 0){throw 'Diagnostics dialog did not close'}
  $report=[PhoneContract]::Service.ReadAsync((New-Object IpaDesktop.DeviceRow -Property @{ID=[PhoneRunner]::Id;Name='Fixture'}),[Threading.CancellationToken]::None,[Action[string]]{param($text)}).GetAwaiter().GetResult()
  $preview=New-Object IpaDesktop.PhoneDiagnosticWindow($window)
  $preview.Show($report);$preview.Window.Show();$preview.Window.UpdateLayout()
  $target=$preview.Window.Content
  if($preview.Values.ActualHeight -lt 100){throw 'Diagnostic table collapsed'}
  $bmp=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$target.ActualWidth,[int]$target.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($target);$png=New-Object Windows.Media.Imaging.PngBitmapEncoder
  $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp));$stream=[IO.File]::Create((Join-Path $projectRoot 'Phone-Diagnostics-Preview.png'))
  try{$png.Save($stream)}finally{$stream.Dispose()}
  $preview.Window.Close()
 }finally{$window.Close()}
 Write-Host 'PASS: read-only phone diagnostics, partial battery access, true zero vs missing, capacity ratio, 64-bit storage, empty discovery, ambiguous fields, cancellation, privacy/full export, actual modal controller and WPF layout'
}finally{if(Test-Path $temp){Remove-Item $temp -Recurse -Force}}
