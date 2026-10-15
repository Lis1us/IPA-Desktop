$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'IPA-Desktop.exe') -Destination (Join-Path $projectRoot 'IPA-Desktop-test.dll') -Force
Add-Type -ReferencedAssemblies @((Join-Path $projectRoot 'IPA-Desktop-test.dll'),'System.dll','System.Core.dll') -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IpaDesktop;
public sealed class InstalledAppleSupportProbe : IAppleSupportProbe
{
    public AppleSupportSnapshot Read()
    {
        return new AppleSupportSnapshot
        {
            ServiceFound = true,
            ServiceStatusKnown = true,
            ServiceRunning = true,
            ServiceScanCompleted = true,
            RegistryScanCompleted = true
        };
    }
}
public sealed class RecordingProcessRunner : IProcessRunner
{
    readonly object gate = new object();
    public readonly List<string[]> Calls = new List<string[]>();
    public string LastPackage;
    public string LastWorkingDirectory;
    public bool FailInstall;
    public CancellationTokenSource CancelInstall;
    public Action<string> Log { get; set; }
    public Process Active { get { return null; } }
    public RecordingProcessRunner() { Log = delegate { }; }
    public Task<Result> Run(string exe, string[] args, CancellationToken ct, bool visible, bool publish, string workingDirectory = null)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Calls.Add((string[])args.Clone()); }
        if (args.Length == 4 && args[2] == "install")
        {
            LastWorkingDirectory = workingDirectory;
            if (String.IsNullOrEmpty(workingDirectory) || Path.IsPathRooted(args[3])) throw new Exception("Expected relative IPA path with explicit working directory");
            foreach (char c in args[3]) if (c > 127) throw new Exception("Non-ASCII install argument");
            LastPackage = Path.Combine(workingDirectory, args[3]);
            if (!File.Exists(LastPackage) || File.ReadAllText(LastPackage) != "fixture") throw new Exception("Staged IPA content is unavailable");
            if (CancelInstall != null) { CancelInstall.Cancel(); ct.ThrowIfCancellationRequested(); }
            if (FailInstall) return Task.FromResult(new Result { Code = 1, Output = "fixture install failure" });
        }
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
$originalContext=[Threading.SynchronizationContext]::Current
try {
 $runner=New-Object RecordingProcessRunner
 $supportProbe=New-Object InstalledAppleSupportProbe
 $engine=New-Object IpaDesktop.Engine($temp,$runner,$supportProbe)
 $engine.Settings.Current='test'
 $engine.Settings.CheckGuiUpdates=$false;$engine.Settings.AutoUpdate=$false;$engine.Settings.AutoUpdateDeviceTools=$false
 $bins=Join-Path $temp 'Versions/test/windows_amd64'
 New-Item -ItemType Directory -Path $bins -Force|Out-Null
 foreach($name in @('ipatool.exe','idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')){[IO.File]::WriteAllText((Join-Path $bins $name),'fixture')}
 $item=New-Object IpaDesktop.DownloadItem;$item.ID='123';$item.Name='Example';$item.VersionID='456'
 $engine.ApplicationDownloads.DownloadAsync($item,[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
 $call=$runner.Calls[$runner.Calls.Count-1]
 if(($call[0..4] -join ' ') -ne 'download -i 123 --purchase -o' -or ($call[6..9] -join ' ') -ne '--external-version-id 456 --format json' -or [IO.Path]::GetDirectoryName($call[5]) -ne $engine.Downloads -or [IO.Path]::GetExtension($call[5]) -ne '.ipa'){throw 'Download service changed CLI arguments'}
 $firstDestination=$call[5]
 $engine.ApplicationDownloads.DownloadAsync($item,[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
 if($runner.Calls[$runner.Calls.Count-1][5] -eq $firstDestination){throw 'Separate account downloads can overwrite the same IPA'}
 $devices=$engine.Devices.FindAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
 if($devices.Count -ne 2 -or $devices[0].Name -eq $devices[1].Name -or !$devices[0].Name.StartsWith('Test iPhone')){throw 'Device service lost names or duplicate-name disambiguation'}
 foreach($name in @('MAX.ipa','Telegram.ipa')){[IO.File]::WriteAllText((Join-Path $engine.Downloads $name),'fixture')}
 $files=$engine.LocalFiles.ListAsync().GetAwaiter().GetResult()
 if($files.Count -ne 2){throw 'Local repository did not list IPA files'}
 $engine.Devices.InstallAsync($devices[0],$files[0],[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
 $call=$runner.Calls[$runner.Calls.Count-1]
 if($call[0] -ne '--udid' -or $call[1] -ne $devices[0].ID -or [IO.File]::Exists($runner.LastPackage) -or [IO.Directory]::Exists([IO.Path]::GetDirectoryName($runner.LastPackage))){throw 'Installation lost UDID or retained temporary package'}
 # Exercise Unicode file names and parent folders, independent of 8.3 aliases.
 $unicodeDir=Join-Path $temp 'Пользователь Кириллица'
 [IO.Directory]::CreateDirectory($unicodeDir)|Out-Null
 $unicodeFile=Join-Path $unicodeDir 'АЗС Лукойл Макс.ipa'
 [IO.File]::WriteAllText($unicodeFile,'fixture')
 $unicodeRow=New-Object IpaDesktop.LocalRow;$unicodeRow.Path=$unicodeFile
 foreach($mode in @('success','failure','cancel')) {
  $runner.FailInstall=($mode -eq 'failure');$cancel=New-Object Threading.CancellationTokenSource
  $runner.CancelInstall=$null;if($mode -eq 'cancel'){$runner.CancelInstall=$cancel}
  $rejected=$false
  try {try{$engine.Devices.InstallAsync($devices[0],$unicodeRow,$cancel.Token).GetAwaiter().GetResult()|Out-Null}catch{$rejected=$true}}
  finally {$runner.CancelInstall=$null;$cancel.Dispose()}
  if(($mode -eq 'success') -eq $rejected){throw ('Unexpected install outcome: '+$mode)}
  if([IO.File]::ReadAllText($unicodeFile) -ne 'fixture' -or @(Get-ChildItem -LiteralPath $unicodeDir -Directory -Filter '.install-*').Count -ne 0){throw ('Installation damaged source or leaked temporary files: '+$mode)}
 }
 $runner.FailInstall=$false
 # Run a real child process from a Unicode working directory.
 $probe=Join-Path $temp 'install-path-probe.exe'
 Add-Type -OutputAssembly $probe -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
public static class InstallPathProbe {
 public static int Main(string[] args) {
  if(args.Length!=1 || Path.IsPathRooted(args[0]))return 2;
  foreach(char c in args[0])if(c>127)return 3;
  return File.ReadAllText(args[0])=="fixture" ? 0 : 4;
 }
}
'@
 $package=[IpaDesktop.InstallationPackage]::CreateAsync($unicodeFile,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 try {
  $realRunner=New-Object IpaDesktop.ProcessRunner($temp)
  $probeResult=$realRunner.Run($probe,[string[]]@($package.RelativePath),[Threading.CancellationToken]::None,$false,$false,$package.WorkingDirectory).GetAwaiter().GetResult()
  if($probeResult.Code -ne 0){throw 'Real Windows child process could not read Unicode-directory IPA through ASCII relative path'}
 }finally{$package.Dispose()}
 Write-Host 'PASS: real child process Unicode working directory, Cyrillic IPA, ASCII arguments, success/failure/cancel cleanup and original preservation'
 $engine.Save()
 [IO.Directory]::CreateDirectory((Join-Path $temp 'Versions/old'))|Out-Null
 $engine.Settings.Previous='old';$engine.Settings.PreviousVersion='0';$engine.Rollback()
 $restored=New-Object IpaDesktop.Engine($temp)
 if($restored.Settings.CheckGuiUpdates -or $restored.Settings.AutoUpdate -or $restored.Settings.AutoUpdateDeviceTools){throw 'Settings/rollback lost user update preferences'}
 $engine.Settings.Current='test';$engine.Save()
 $snapshot=New-Object IpaDesktop.AppleSupportSnapshot
 $snapshot.ServiceScanCompleted=$true;$snapshot.RegistryScanCompleted=$true
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::NotDetected){throw 'Absent components were reported installed'}
 $snapshot.InstallationDetected=$true
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::ServiceMissing){throw 'Registered components without a service were reported ready'}
 $snapshot.ServiceFound=$true;$snapshot.ServiceStatusKnown=$true
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Stopped){throw 'Stopped service was reported ready'}
 $snapshot.ServiceRunning=$true;$snapshot.InstallationDetected=$false
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Running){throw 'Store-style service without standalone installation record was missed'}
 $snapshot.ServiceStatusKnown=$false
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Unknown){throw 'Unreadable service status was reported stopped'}
 $snapshot.ServiceFound=$false;$snapshot.RegistryScanCompleted=$false
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Unknown){throw 'Registry access failure was reported absent'}
 $probe=New-Object IpaDesktop.WindowsAppleSupportProbe
 $actual=$probe.Read()
 if(!$actual){throw 'Read-only Windows probe did not return a report'}
 $report=[IpaDesktop.AppleSupportService]::Evaluate($actual)
 Write-Host ('PASS: read-only Windows probe; actual status: '+$report.Status)
 Write-Host 'PASS: absent, stopped, missing service, running without MSI record and unknown inspection states'
 Write-Host 'PASS: injectable process runner; download arguments; device names/UDIDs; local repository; update preferences'
 [Threading.SynchronizationContext]::SetSynchronizationContext((New-Object Windows.Threading.DispatcherSynchronizationContext([Windows.Threading.Dispatcher]::CurrentDispatcher)))
 $hostFolder=Join-Path $temp 'host'
 New-Item -ItemType Directory -Path $hostFolder|Out-Null
 Copy-Item -LiteralPath (Join-Path $projectRoot 'MainWindow.xaml') -Destination $hostFolder
 $desktop=New-Object IpaDesktop.Desktop($engine,$false,$hostFolder)
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
 Click 'CheckAppleSupport';WaitIdle
 if(!$window.FindName('AppleSupportStatus').Text.Contains('Apple') -or $window.FindName('AppleSupportStatus').Foreground.Color.ToString() -ne '#FF81D4AD'){throw 'Apple support check did not render the running state'}
 Click 'Devices';WaitIdle
 if($window.FindName('DeviceList').Items.Count -ne 2 -or !$window.FindName('DeviceList').SelectedItem.Name.StartsWith('Test iPhone')){throw 'Named device discovery failed'}
 Click 'AccountInfo';WaitIdle
 if($window.FindName('Login').IsEnabled -or !$window.FindName('Logout').IsEnabled){throw 'Signed-in button state failed'}
 # Retry uses failure snapshots, preserving a different version already selected for the same app.
 $tabs.SelectedIndex=0;Pump
 $window.FindName('Query').Text='333';Click 'Search';WaitIdle
 $searchController=$desktop.GetType().GetField('search',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($desktop)
 $searchController.GetType().GetField('versionApp',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($searchController,'333')
 $window.FindName('VersionList').Items.Add('777')|Out-Null;$window.FindName('VersionList').SelectedIndex=1
 Click 'AddChosen';Pump
 $failures=$searchController.GetType().GetField('failedDownloads',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($searchController)
 $retryRow=New-Object IpaDesktop.AppRow;$retryRow.ID='333';$retryRow.Name='Retry fixture';$failures.Add($retryRow,'456')
 $desktop.GetType().GetMethod('ApplyActions',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($desktop,@())|Out-Null
 if(!$window.FindName('RetryDownloads').IsEnabled){throw 'Retry button did not reflect stored errors'}
 $callsBefore=$runner.Calls.Count;Click 'RetryDownloads';WaitIdle
 $downloads=New-Object 'Collections.Generic.List[object]'
 for($i=$callsBefore;$i -lt $runner.Calls.Count;$i++){if($runner.Calls[$i][0] -eq 'download'){$downloads.Add($runner.Calls[$i])}}
 if($downloads.Count -ne 1 -or ($downloads[0] -join ' ') -notmatch '--external-version-id 456'){throw 'Retry did not use the failed version snapshot'}
 if($window.FindName('ChosenApps').Items.Count -ne 1 -or $window.FindName('ChosenApps').Items[0].VersionID -ne '777'){throw 'Retry discarded a separately selected version'}
 if($window.FindName('RetryDownloads').IsEnabled -or !$window.FindName('OperationDetail').Text.Contains('1')){throw 'Retry result/progress was not rendered'}
 # Save and restore a named set through the actual UI controls.
 $tabs.SelectedIndex=1;Pump
 $window.FindName('Files').SelectedItems.Clear()
 foreach($local in $window.FindName('Files').Items){$window.FindName('Files').SelectedItems.Add($local)}
 Click 'AddInstall';Pump
 $expected=$window.FindName('ChosenFiles').Items.Count
 $tabs.SelectedIndex=4;Pump
 $window.FindName('SetName').Text='Base fixture';Click 'SaveSet';Pump
 if($window.FindName('AppSets').Items.Count -ne 1){throw 'Set save was not connected'}
 Click 'ClearInstall';Click 'ClearChosen';Pump
 Click 'UseSet';WaitIdle
 if($window.FindName('ChosenApps').Items.Count -ne 1 -or $window.FindName('ChosenApps').Items[0].ID -ne '333' -or $window.FindName('ChosenApps').Items[0].VersionID -ne $null -or $tabs.SelectedIndex -ne 0){throw 'Set did not add account-independent IDs with latest version to download queue'}
 if($window.FindName('ChosenFiles').Items.Count -ne 0){throw 'ID set reused IPA from another account'}
 if(!$window.FindName('StorageSummary').Text.Contains('2') -or $window.FindName('StorageFiles').Items.Count -ne 2){throw 'Storage view was not populated'}
 if(!$window.FindName('ExportDiagnostics')){throw 'Diagnostic export control is missing'}
 if($window.FindName('SaveFileSet')){throw 'Removed IPA set action is still present'}
 # Inline set creation and additive updates through the editable WPF template.
 Click 'ClearChosen';$tabs.SelectedIndex=0;Pump
 $window.FindName('Query').Text='444';Click 'Search';WaitIdle;Click 'AddChosen';Pump
 $picker=$window.FindName('SearchSet');$picker.ApplyTemplate()|Out-Null
 $editor=$picker.Template.FindName('PART_EditableTextBox',$picker)
 if(!$editor -or $editor.Visibility -ne 'Visible'){throw 'Set picker has no visible editable input'}
 $editor.Text='Inline fixture';Pump
 if($picker.Text -ne 'Inline fixture'){throw 'Typing a new set name did not update the picker'}
 Click 'AddToSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'Inline fixture')
 if($saved.Count -ne 1 -or $saved[0].Apps.Count -ne 1 -or $saved[0].Apps[0].ID -ne '444'){throw 'Inline creation failed'}
 if($window.FindName('AppSets').Items.Count -ne 2){throw 'Inline set did not refresh the library tab'}
 $window.FindName('Query').Text='555';Click 'Search';WaitIdle;Click 'AddChosen';Pump
 $picker.SelectedItem='Inline fixture';Click 'AddToSet';Pump;Click 'AddToSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'Inline fixture')
 if($saved[0].Apps.Count -ne 2){throw 'Add to set replaced entries or duplicated IDs'}
 Click 'Download';WaitIdle
 if($window.FindName('ChosenApps').Items.Count -ne 0 -or !$window.FindName('AddToSet').IsEnabled -or !$window.FindName('SaveSet').IsEnabled){throw 'Set saving became unavailable after successful download'}
 $tabs.SelectedIndex=4;Pump;$window.FindName('SetName').Text='After download';Click 'SaveSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'After download')
 if($saved.Count -ne 1 -or $saved[0].Apps.Count -ne 2){throw 'Legacy save button lost the completed download selection'}
 $tabs.SelectedIndex=0;Pump;$picker.Text='Inline after download';Click 'AddToSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'Inline after download')
 if($saved.Count -ne 1 -or $saved[0].Apps.Count -ne 2){throw 'Inline save lost the completed download selection'}
 Click 'ClearChosen';Pump
 if($window.FindName('AddToSet').IsEnabled -or $window.FindName('SaveSet').IsEnabled){throw 'Explicit clear retained stale set selection'}
 # Multiple sets can be checked together; overlapping IDs preserve manually selected versions.
 $a=New-Object IpaDesktop.AppRow;$a.ID='444';$a.Name='Four'
 $b=New-Object IpaDesktop.AppRow;$b.ID='555';$b.Name='Five'
 $c=New-Object IpaDesktop.AppRow;$c.ID='666';$c.Name='Six'
 $engine.AppSets.SaveApps('Overlap A',[IpaDesktop.AppRow[]]@($a,$b))
 $engine.AppSets.SaveApps('Overlap B',[IpaDesktop.AppRow[]]@($b,$c))
 $desktop.GetType().GetMethod('RefreshAppSets',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($desktop,@())|Out-Null
 $window.FindName('Query').Text='555';Click 'Search';WaitIdle
 $searchController.GetType().GetField('versionApp',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($searchController,'555')
 $window.FindName('VersionList').Items.Add('888')|Out-Null;$window.FindName('VersionList').SelectedIndex=1
 Click 'AddChosen';Pump
 $list=$window.FindName('AppSets');$list.SelectedItems.Clear()
 foreach($set in $list.Items){if($set.Name -like 'Overlap *'){$list.SelectedItems.Add($set)|Out-Null}}
 $tabs.SelectedIndex=4;Pump
 if($list.SelectedItems.Count -ne 2 -or !$window.FindName('SetPreview').Text.Contains('3')){throw 'Multiple-set selection/preview failed'}
 Click 'UseSet';WaitIdle
 $queue=@($window.FindName('ChosenApps').Items)
 if($queue.Count -ne 3 -or ($queue|Where-Object ID -eq '555').VersionID -ne '888'){throw 'Multiple sets duplicated IDs or reset selected version'}
 $tabs.SelectedIndex=4;Pump;Click 'UseSet';WaitIdle
 if($window.FindName('ChosenApps').Items.Count -ne 3){throw 'Adding multiple sets twice duplicated the queue'}
 $reopened=New-Object IpaDesktop.Engine($temp)
 if(@($reopened.AppSets.Load()|Where-Object Name -like 'Overlap *').Count -ne 2){throw 'Multiple sets did not survive reopening'}
 $tabs.SelectedIndex=4;Pump;Click 'DeleteSet';Pump
 if(@($engine.AppSets.Load()|Where-Object Name -like 'Overlap *').Count -ne 0 -or $window.FindName('StorageFiles').Items.Count -ne 2){throw 'Batch set deletion lost files or retained selected sets'}
 Write-Host 'PASS: simultaneous sets, overlapping IDs, version preservation, repeated additions, persistence and independent deletion'
 Write-Host 'PASS: editable inline set picker, additive deduplication, cross-tab refresh, both save buttons after completed download and explicit clear'
 Write-Host 'PASS: retry snapshot/version isolation, queue progress, saved set UI and storage UI'
 Write-Host 'PASS: real controller composition; search/clear/selection/removal; IPA selection/removal; device button/names; account state'
}finally{
 if($window){$window.Close()}
 [Threading.SynchronizationContext]::SetSynchronizationContext($originalContext)
 if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}
}
