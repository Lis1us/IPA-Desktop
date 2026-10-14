# Run in Windows PowerShell 5.1, STA. No Apple login or network access.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'IPA-Desktop.exe')) | Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-test-'+[Guid]::NewGuid().ToString('N'))
try {
 $e=[IpaDesktop.Engine]::new($temp)
 $e.Save()
 $e2=[IpaDesktop.Engine]::new($temp)
 if(!$e2.Settings.AutoUpdate){throw 'Settings round trip failed'}
 $rows=[IpaDesktop.Engine]::ParseApps('{"apps":[{"id":123,"name":"Test"}],"success":true}')
 if($rows.Count -ne 1 -or $rows[0].ID -ne '123'){throw 'Search parser failed'}
 [IO.Directory]::CreateDirectory((Join-Path $temp 'Versions/old'))|Out-Null
 [IO.Directory]::CreateDirectory((Join-Path $temp 'Versions/new'))|Out-Null
 [IO.File]::WriteAllText((Join-Path $e.Downloads 'keep.ipa'),'fixture')
 $e.Settings.Current='new';$e.Settings.Version='2'
 $e.Settings.Previous='old';$e.Settings.PreviousVersion='1';$e.Save();$e.Rollback()
 $e3=[IpaDesktop.Engine]::new($temp)
 if($e3.Settings.Version -ne '1' -or $e3.Settings.AutoUpdate){throw 'Rollback failed'}
 if(!(Test-Path (Join-Path $e.Downloads 'keep.ipa'))){throw 'Downloads lost'}
 $reader=[IO.File]::OpenRead((Join-Path $PSScriptRoot 'MainWindow.xaml'))
 try {$window=[Windows.Markup.XamlReader]::Load($reader);$window.Close()}finally{$reader.Dispose()}
 $selection=New-Object IpaDesktop.DownloadSelection
 $first=New-Object IpaDesktop.AppRow;$first.Name='MAX';$first.ID='6739530834'
 $second=New-Object IpaDesktop.AppRow;$second.Name='Telegram';$second.ID='686449807'
 $selection.Add($first,[NullString]::Value);$selection.Add($second,'123456')
 if($selection.Count -ne 2){throw 'Selection did not retain apps from separate searches'}
 $first.Name='MAX alias';$selection.Add($first,'999')
 if($selection.Count -ne 2){throw 'Duplicate App Store ID added'}
 $snapshot=$selection.Snapshot()
 if($snapshot[0].VersionID -ne '999' -or $snapshot[1].VersionID -ne '123456'){throw 'Per-app versions lost'}
 $snapshot[0].Name='mutated'
 if($selection.Snapshot()[0].Name -eq 'mutated'){throw 'Download snapshot is not isolated'}
 $selection.Remove($first.ID)
 if($selection.Count -ne 1 -or $selection.Snapshot()[0].ID -ne $second.ID){throw 'Completed download removed other pending apps'}
 $selection.Clear()
 if($selection.Count -ne 0){throw 'Clear failed'}
 Write-Host 'PASS: selection across searches, ID deduplication, individual versions, immutable snapshots and removal'
 $install=New-Object IpaDesktop.InstallationSelection
 $ipaA=New-Object IpaDesktop.LocalRow;$ipaA.Name='A.ipa';$ipaA.Path='C:\Apps\A.ipa';$ipaA.AppleAccount='a@example.com'
 $ipaB=New-Object IpaDesktop.LocalRow;$ipaB.Name='B.ipa';$ipaB.Path='C:\Apps\B.ipa';$ipaB.AppleAccount='b@example.com'
 $install.Add($ipaA);$install.Add($ipaB);$install.Add($ipaA)
 if($install.Count -ne 2){throw 'Duplicate installation entry'}
 $snapshot=$install.Snapshot();$snapshot[0].AppleAccount='changed'
 if($install.Snapshot()[0].AppleAccount -ne 'a@example.com'){throw 'Installation snapshot is shared'}
 $ipaA.AppleAccount='updated@example.com'
 $install.Reconcile([IpaDesktop.LocalRow[]]@($ipaA,$ipaB))
 if($install.Count -ne 2 -or $install.Snapshot()[0].AppleAccount -ne $ipaA.AppleAccount){throw 'Refresh lost selection or metadata'}
 $install.Reconcile([IpaDesktop.LocalRow[]]@($ipaB))
 if($install.Count -ne 1 -or $install.Snapshot()[0].Name -ne 'B.ipa'){throw 'Removed file still queued'}
 $install.Remove('c:\apps\b.ipa')
 if($install.Count -ne 0){throw 'Path removal must ignore case on Windows'}
 Write-Host 'PASS: install queue deduplication, snapshots, refresh, missing files and removal'
 $buffer=New-Object IpaDesktop.BufferedJournal
 for($i=0;$i -lt 20000;$i++){$buffer.Add(('x'*3000))}
 $total=0;$chunks=0
 do {$chunk=$buffer.Drain();$total+=$chunk.Length;$chunks++;if($chunk.Length -gt 8200){throw 'Unbounded UI batch'}}while($chunk.Length -gt 0)
 if($total -gt 1100000 -or $chunks -gt 200){throw 'Log buffer grew without limit'}
 Write-Host 'PASS: log flood has bounded memory and UI batches'
 $powershell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
 $script='for($i=0;$i -lt 2000;$i++){[Console]::Out.WriteLine("OUT:"+$i);[Console]::Error.WriteLine("ERR:"+$i)}'
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
 $timer=[Diagnostics.Stopwatch]::StartNew()
 $task=$e.Run($powershell,[string[]]@('-NoProfile','-EncodedCommand',$encoded),[Threading.CancellationToken]::None,$false,$false)
 if($timer.Elapsed.TotalSeconds -gt 1){throw 'Process launch blocked its caller'}
 $result=$task.GetAwaiter().GetResult()
 if($result.Code -ne 0 -or !$result.Output.Contains('OUT:1999') -or !$result.Output.Contains('ERR:1999')){throw 'Concurrent stdout/stderr output lost'}
 Write-Host 'PASS: nonblocking launch and simultaneous stdout/stderr flood'
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('Start-Sleep -Seconds 60'))
 $cancel=New-Object Threading.CancellationTokenSource
 try {
  $cancel.CancelAfter(400)
  $timer.Restart();$cancelled=$false
  try {$e.Run($powershell,[string[]]@('-NoProfile','-EncodedCommand',$encoded),$cancel.Token,$false,$false).GetAwaiter().GetResult()|Out-Null}catch{$cancelled=$cancel.IsCancellationRequested}
  if(!$cancelled -or $timer.Elapsed.TotalSeconds -gt 8){throw 'Silent process cancellation did not complete promptly'}
 }finally{$cancel.Dispose()}
 Write-Host 'PASS: cancellation of a silent process'
 $search=New-Object IpaDesktop.SearchSession
 $request=$search.Change('MAX')
 if(!$search.IsCurrent($request)){throw 'Current query rejected'}
 [void]$search.Change('')
 if($search.IsCurrent($request)){throw 'Cleared query accepted a late search reply'}
 $request=$search.Change('Telegram');[void]$search.Change('   ')
 if($search.IsCurrent($request)){throw 'Whitespace query accepted a late search reply'}
 $request=$search.Change('MAX');[void]$search.Change('Telegram')
 if($search.IsCurrent($request)){throw 'Changed query accepted obsolete results'}
 $device=New-Object IpaDesktop.DeviceRow;$device.Name='My iPhone';$device.ID='00008030-0000111ABC000DEF'
 if($device.ToString() -ne 'My iPhone' -or $device.ID -eq $device.ToString()){throw 'Device name replaced its installation ID'}
 Write-Host 'PASS: cleared and changed searches reject late replies; device labels retain separate UDIDs'
 Write-Host 'PASS: settings, JSON, rollback, preserved downloads, WPF XAML' -ForegroundColor Green
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
