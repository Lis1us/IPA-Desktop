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
 Write-Host 'PASS: settings, JSON, rollback, preserved downloads, WPF XAML' -ForegroundColor Green
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
