param([switch]$Live)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-engine-'+[Guid]::NewGuid().ToString('N'))
$ct=[Threading.CancellationToken]::None
try {
 $oldBins=Join-Path $temp 'Versions/legacy/MainApp/windows_amd64'
 [IO.Directory]::CreateDirectory($oldBins)|Out-Null
 foreach($name in @('ipatool-old.exe','idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe','libimobiledevice-1.0.dll','libplist-2.0.dll','dependency.dll','Launcher.ps1')){[IO.File]::WriteAllText((Join-Path $oldBins $name),'fixture')}
 $support=Join-Path $temp 'Versions/devices/windows_amd64'
 [IpaDesktop.EngineUpdater]::CopyDeviceTools((Join-Path $temp 'Versions/legacy'),$support)
 if(Test-Path (Join-Path $support 'ipatool-old.exe')){throw 'Device import retained bundled ipatool'}
 if(Test-Path (Join-Path $support 'Launcher.ps1')){throw 'Device import retained scripts'}
 if(!(Test-Path (Join-Path $support 'dependency.dll'))){throw 'Device import lost DLL dependencies'}
 if(![IpaDesktop.EngineUpdater]::HasDeviceTools((Join-Path $temp 'Versions/devices'))){throw 'Device import is incomplete'}
 $engine=New-Object IpaDesktop.Engine($temp)
 $engine.Settings.Current='direct';$engine.Settings.Source='Sorvigolova/ipatool';$engine.Settings.Version='1.1.8'
 $engine.Settings.DeviceCurrent='devices';$engine.Settings.DeviceVersion='4.2.2'
 $engine.Settings.Previous='legacy';$engine.Settings.PreviousVersion='4.2.2'
 $engine.Settings.AutoUpdate=$false;$engine.Settings.CheckGuiUpdates=$false
 $direct=Join-Path $temp 'Versions/direct/windows_amd64/nested'
 [IO.Directory]::CreateDirectory($direct)|Out-Null
 [IO.File]::WriteAllText((Join-Path $direct 'ipatool.exe'),'fixture')
 if($engine.Binary('ipatool*.exe') -ne (Join-Path $direct 'ipatool.exe')){throw 'ipatool did not use direct release'}
 if($engine.Binary('ideviceinstaller.exe') -ne (Join-Path $support 'ideviceinstaller.exe')){throw 'Device tool used engine folder'}
 $engine.Save();$engine.Rollback()
 if($engine.Settings.Source -or $engine.Settings.DeviceCurrent -ne 'devices'){throw 'Legacy rollback lost source or device tools'}
 if($engine.Binary('ipatool*.exe') -ne (Join-Path $oldBins 'ipatool-old.exe')){throw 'Legacy rollback did not restore bundled engine'}
 $engine.Rollback()
 $restored=New-Object IpaDesktop.Engine($temp)
 if($restored.Settings.Source -ne 'Sorvigolova/ipatool' -or $restored.Settings.DeviceCurrent -ne 'devices' -or $restored.Settings.AutoUpdate -or $restored.Settings.CheckGuiUpdates){throw 'Rollback/serialization lost independent state'}
 $asset=New-Object IpaDesktop.Asset
 $asset.name='windows_amd64_ipatool-cpp.zip';$asset.size=1
 $asset.browser_download_url='https://github.com/Sorvigolova/ipatool/releases/download/fixture/windows_amd64_ipatool-cpp.zip'
 $release=New-Object IpaDesktop.Release;$release.assets=@($asset)
 [IpaDesktop.EngineUpdater]::SelectAsset($release)|Out-Null
 $release.assets=@($asset,$asset);$rejected=$false
 try{[IpaDesktop.EngineUpdater]::SelectAsset($release)|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Ambiguous Windows archive accepted'}
 $release.assets=@($asset);$asset.browser_download_url='https://github.com/kda2495/IPA_Downloader/releases/download/fixture/test.zip';$rejected=$false
 try{[IpaDesktop.EngineUpdater]::SelectAsset($release)|Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Bundled source accepted as direct engine'}
 Write-Host 'PASS: selective device import, independent tool lookup, legacy/direct rollback, persisted settings, release selection'
 if($Live){
  $liveRoot=Join-Path $temp 'live'
  $liveEngine=New-Object IpaDesktop.Engine($liveRoot)
  $latest=$liveEngine.Check($ct).GetAwaiter().GetResult()
  $liveEngine.Install($latest,$ct).GetAwaiter().GetResult()
  if($liveEngine.Settings.Source -ne 'Sorvigolova/ipatool' -or !$liveEngine.Settings.DeviceCurrent){throw 'Fresh live install did not commit independent sources'}
  $liveEngine.Binary('ipatool*.exe')|Out-Null
  foreach($pattern in @('idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')){$liveEngine.Binary($pattern)|Out-Null}
  if(@(Get-ChildItem (Join-Path $liveRoot ('Versions/'+$liveEngine.Settings.DeviceCurrent)) -Filter 'ipatool*.exe' -Recurse).Count){throw 'Fresh install retained bundled engine'}
  # Same-version legacy migration must still select direct upstream and reuse device tools.
  $prior=$liveEngine.Settings.Current
  $devices=$liveEngine.Settings.DeviceCurrent
  $liveEngine.Settings.Source=$null;$liveEngine.Save()
  $liveEngine.Install($latest,$ct).GetAwaiter().GetResult()
  if($liveEngine.Settings.Current -eq $prior -or $liveEngine.Settings.Previous -ne $prior -or $liveEngine.Settings.DeviceCurrent -ne $devices){throw 'Live migration did not preserve device tools/previous engine'}
  $liveEngine.Rollback()
  if($liveEngine.Settings.Current -ne $prior -or $liveEngine.Settings.DeviceCurrent -ne $devices){throw 'Live rollback failed'}
  Write-Host ('PASS: actual upstream Windows ZIP '+$latest.tag_name+'; CLI startup/commands; fresh device import; migration; rollback')
 }
} finally {if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
