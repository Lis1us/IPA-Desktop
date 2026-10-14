$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'IPA-Desktop.exe'))|Out-Null
Add-Type -ReferencedAssemblies @((Join-Path $PSScriptRoot 'IPA-Desktop.exe'),'System.dll','System.Core.dll') -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IpaDesktop;
public sealed class RecordingProcessRunner : IProcessRunner
{
    readonly object gate = new object();
    public readonly List<string[]> Calls = new List<string[]>();
    public Action<string> Log { get; set; }
    public Process Active { get { return null; } }
    public RecordingProcessRunner() { Log = delegate { }; }
    public Task<Result> Run(string exe, string[] args, CancellationToken ct, bool visible, bool publish)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Calls.Add((string[])args.Clone()); }
        string output = "{\"success\":true}";
        if (Path.GetFileName(exe) == "idevice_id.exe")
            output = "00008030-0000111ABC000DEF\n00008030-0000111ABC000ABC\n";
        else if (Path.GetFileName(exe) == "ideviceinfo.exe")
            output = "Test iPhone";
        else if (args.Length >= 2 && args[0] == "auth" && args[1] == "info")
            output = "{\"success\":true,\"email\":\"test@example.com\"}";
        return Task.FromResult(new Result { Code = 0, Output = output });
    }
}
'@
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-oop-'+[Guid]::NewGuid().ToString('N'))
$window=$null
try {
 $runner=New-Object RecordingProcessRunner
 $engine=New-Object IpaDesktop.Engine($temp,$runner)
 $engine.Settings.Current='test'
 $engine.Settings.CheckGuiUpdates=$false;$engine.Settings.AutoUpdate=$false
 $bins=Join-Path $temp 'Versions/test/windows_amd64'
 New-Item -ItemType Directory -Path $bins -Force|Out-Null
 foreach($name in @('ipatool.exe','idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')){[IO.File]::WriteAllText((Join-Path $bins $name),'fixture')}
 $item=New-Object IpaDesktop.DownloadItem;$item.ID='123';$item.Name='Example';$item.VersionID='456'
 $engine.ApplicationDownloads.DownloadAsync($item,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 $call=$runner.Calls[$runner.Calls.Count-1]
 if(($call -join ' ') -ne ('download -i 123 --purchase -o '+$engine.Downloads+' --external-version-id 456 --format json')){throw 'Download service changed CLI arguments'}
 $devices=$engine.Devices.FindAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
 if($devices.Count -ne 2 -or $devices[0].Name -eq $devices[1].Name -or !$devices[0].Name.StartsWith('Test iPhone')){throw 'Device service lost names or duplicate-name disambiguation'}
 foreach($name in @('MAX.ipa','Telegram.ipa')){[IO.File]::WriteAllText((Join-Path $engine.Downloads $name),'fixture')}
 $files=$engine.LocalFiles.ListAsync().GetAwaiter().GetResult()
 if($files.Count -ne 2){throw 'Local repository did not list IPA files'}
 $engine.Devices.InstallAsync($devices[0],$files[0],[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 $call=$runner.Calls[$runner.Calls.Count-1]
 if($call[0] -ne '--udid' -or $call[1] -ne $devices[0].ID -or $call[3] -ne $files[0].Path){throw 'Installation service used label instead of UDID/path'}
 $engine.Save()
 [IO.Directory]::CreateDirectory((Join-Path $temp 'Versions/old'))|Out-Null
 $engine.Settings.Previous='old';$engine.Settings.PreviousVersion='0';$engine.Rollback()
 $restored=New-Object IpaDesktop.Engine($temp)
 if($restored.Settings.CheckGuiUpdates -or $restored.Settings.AutoUpdate){throw 'Settings/rollback lost user update preferences'}
 $engine.Settings.Current='test';$engine.Save()
 Write-Host 'PASS: injectable process runner; download arguments; device names/UDIDs; local repository; update preferences'
 $desktop=New-Object IpaDesktop.Desktop($engine,$false)
 $window=$desktop.Window;$window.Show()
 function Pump {
  $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 }
 function WaitIdle {
  $clock=[Diagnostics.Stopwatch]::StartNew()
  do {Pump;[Threading.Thread]::Sleep(30)}while($window.FindName('Cancel').IsEnabled -and $clock.Elapsed.TotalSeconds -lt 10)
  if($window.FindName('Cancel').IsEnabled){throw 'Controller operation did not finish'}
  Pump
 }
 function Click($name) {$window.FindName($name).RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))}
 function FindTabs($node) {
  if($node -is [Windows.Controls.TabControl]){return $node}
  foreach($child in [Windows.LogicalTreeHelper]::GetChildren($node)){
   if($child -is [Windows.DependencyObject]){$found=FindTabs $child;if($found){return $found}}
  }
 }
 function FindRemoveButton($node) {
  if($node -is [Windows.Controls.Button] -and $node.Tag){return $node}
  for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++){
   $found=FindRemoveButton ([Windows.Media.VisualTreeHelper]::GetChild($node,$i));if($found){return $found}
  }
 }
 function ClickRow($grid) {
  $grid.ScrollIntoView($grid.Items[0]);Pump
  $row=$grid.ItemContainerGenerator.ContainerFromIndex(0)
  if(!$row){throw 'Controller row was not generated'}
  foreach($event in @([Windows.UIElement]::PreviewMouseLeftButtonDownEvent,[Windows.UIElement]::PreviewMouseLeftButtonUpEvent)){
   $mouse=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
   $mouse.RoutedEvent=$event;$mouse.Source=$row;$grid.RaiseEvent($mouse)
  }
  Pump
 }
 Pump
 $tabs=FindTabs $window
 $window.FindName('Query').Text='123';Click 'Search';WaitIdle
 if($window.FindName('Apps').Items.Count -ne 1){throw 'Direct-ID search changed'}
 ClickRow ($window.FindName('Apps'))
 if($window.FindName('ChosenApps').Items.Count -ne 1){throw 'Search click selection failed'}
 $window.FindName('Query').Text='';Pump
 if($window.FindName('Apps').Items.Count -ne 0 -or $window.FindName('ChosenApps').Items.Count -ne 1){throw 'Search clear discarded selection or kept suggestions'}
 $remove=FindRemoveButton ($window.FindName('ChosenApps'))
 $remove.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$remove)));Pump
 if($window.FindName('ChosenApps').Items.Count -ne 0){throw 'Search remove button failed'}
 $tabs.SelectedIndex=1;Pump
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while($window.FindName('Files').Items.Count -eq 0 -and $clock.Elapsed.TotalSeconds -lt 5){Pump;[Threading.Thread]::Sleep(30)}
 ClickRow ($window.FindName('Files'))
 if($window.FindName('ChosenFiles').Items.Count -ne 1){throw 'IPA click selection failed'}
 $remove=FindRemoveButton ($window.FindName('ChosenFiles'))
 $remove.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$remove)));Pump
 if($window.FindName('ChosenFiles').Items.Count -ne 0){throw 'IPA remove button failed'}
 if($window.FindName('Devices').Background.Color.ToString() -ne '#FF16704C'){throw 'Initial device-search color changed'}
 Click 'Devices';WaitIdle
 if($window.FindName('DeviceList').Items.Count -ne 2 -or !$window.FindName('DeviceList').SelectedItem.Name.StartsWith('Test iPhone')){throw 'Named device discovery failed'}
 Click 'AccountInfo';WaitIdle
 if($window.FindName('Login').IsEnabled -or !$window.FindName('Logout').IsEnabled){throw 'Signed-in button state failed'}
 Write-Host 'PASS: real controller composition; search/clear/selection/removal; IPA selection/removal; device button/names; account state'
}finally{
 if($window){$window.Close()}
 if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}
}
