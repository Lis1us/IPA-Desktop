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
 Write-Host 'PASS: settings, JSON, rollback, preserved downloads, WPF XAML' -ForegroundColor Green
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
