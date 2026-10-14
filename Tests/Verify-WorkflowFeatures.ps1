$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-features-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
function Assert($condition,$message){if(!$condition){throw $message}}
function NewIpa($folder,$name,$bundle,$version,$account,$binaryInfo=$null){
 $path=Join-Path $folder $name
 $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
 try{
  $metadata='<?xml version="1.0"?><plist version="1.0"><dict><key>appleId</key><string>'+ $account +'</string><key>itemName</key><string>Example</string><key>softwareVersionBundleId</key><string>'+ $bundle +'</string><key>bundleVersion</key><string>0.1</string></dict></plist>'
  $info='<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleIdentifier</key><string>'+ $bundle +'</string><key>CFBundleShortVersionString</key><string>'+ $version +'</string><key>CFBundleVersion</key><string>10</string></dict></plist>'
  foreach($pair in @(@('iTunesMetadata.plist',[Text.Encoding]::UTF8.GetBytes($metadata)),@('Payload/Example.app/Info.plist',$(if($binaryInfo){[Convert]::FromBase64String($binaryInfo)}else{[Text.Encoding]::UTF8.GetBytes($info)})))){
   $entry=$zip.CreateEntry($pair[0]);$stream=$entry.Open()
   try{$bytes=[byte[]]$pair[1];$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
  }
 }finally{$zip.Dispose()}
 return $path
}
try{
 $engine=New-Object IpaDesktop.Engine($temp)
 $old=NewIpa $engine.Downloads 'old.ipa' 'com.example.app' '1.9' 'first@example.com'
 $latest=NewIpa $engine.Downloads 'latest.ipa' 'com.example.app' '1.10' 'first@example.com'
 $other=NewIpa $engine.Downloads 'other-account.ipa' 'com.example.app' '1.0' 'second@example.com'
 $unknown=NewIpa $engine.Downloads 'unknown.ipa' 'com.example.app' '0.5' ''
 $binary=NewIpa $engine.Downloads 'binary.ipa' 'com.example.binary' '0.1' 'first@example.com' 'YnBsaXN0MDDTAQIDBAUGXxASQ0ZCdW5kbGVJZGVudGlmaWVyXxAaQ0ZCdW5kbGVTaG9ydFZlcnNpb25TdHJpbmdfEA9DRkJ1bmRsZVZlcnNpb25fEBJjb20uZXhhbXBsZS5iaW5hcnlTMi4wUjEwCA8kQVNobAAAAAAAAAEBAAAAAAAAAAcAAAAAAAAAAAAAAAAAAABv'
 $rows=$engine.LocalFiles.ListAsync().GetAwaiter().GetResult()
 Assert ($rows.Count -eq 5) 'Storage lost IPA files'
 Assert (($rows|Where-Object Path -eq $old).IsOld) 'Numeric old-version detection failed'
 Assert (!($rows|Where-Object Path -eq $latest).IsOld) 'Newest version was selected for deletion'
 Assert (!($rows|Where-Object Path -eq $other).IsOld) 'Another Apple account was grouped with old versions'
 Assert (!($rows|Where-Object Path -eq $unknown).IsOld) 'Unknown account was selected for cleanup'
 Assert (($rows|Where-Object Path -eq $binary).Version -eq '2.0') 'Binary Info.plist version was not read'
 Assert (($rows|Where-Object Path -eq $latest).Version -eq '1.10') 'Info.plist did not override metadata fallback'
 Assert (($rows|Measure-Object Bytes -Sum).Sum -gt 0) 'Storage size is missing'
 $engine.AppSets.Save('Base',[IpaDesktop.LocalRow[]]@($rows|Where-Object { $_.Path -eq $old -or $_.Path -eq $latest }))
 $sets=$engine.AppSets.Load();Assert ($sets.Count -eq 1 -and $sets[0].Files.Count -eq 2) 'Set did not persist selected IPA names'
 $missing=$null;$resolved=$engine.AppSets.Resolve($sets[0],$rows,[ref]$missing)
 Assert ($resolved.Count -eq 2 -and $missing.Count -eq 0) 'Set did not resolve exact IPA files'
 [IO.File]::Delete($old)
 $now=$engine.LocalFiles.ListAsync().GetAwaiter().GetResult();$resolved=$engine.AppSets.Resolve($sets[0],$now,[ref]$missing)
 Assert ($resolved.Count -eq 1 -and $missing.Count -eq 1 -and $missing[0] -eq 'old.ipa') 'Missing set IPA was silently replaced'
 $bad=New-Object IpaDesktop.AppSet;$bad.Name='Bad';$bad.Files=@('..\outside.ipa');$rejected=$false
 try{$engine.AppSets.Resolve($bad,$rows,[ref]$missing)|Out-Null}catch{$rejected=$true}
 Assert $rejected 'Set path traversal was accepted'
 $outside=New-Object IpaDesktop.LocalRow;$outside.Path=Join-Path $temp 'outside.ipa';[IO.File]::WriteAllText($outside.Path,'fixture')
 $rejected=$false;try{$engine.AppSets.Save('Outside',[IpaDesktop.LocalRow[]]@($outside))}catch{$rejected=$true}
 Assert $rejected 'Set accepted an IPA outside the downloads folder'
 $engine.AppSets.Delete('Base');Assert ($engine.AppSets.Load().Count -eq 0 -and [IO.File]::Exists($latest)) 'Deleting a set removed IPA files'
 Write-Host 'PASS: persisted IPA sets, missing files, path bounds, XML/binary metadata, numeric versions, account isolation and storage size'
 $progress=New-Object IpaDesktop.QueueProgress
 $progress.Begin('Скачивание',2,3,'Example')
 Assert ($progress.Feed('PROGRESS:50')) 'Machine-readable progress was ignored'
 Assert ([Math]::Abs($progress.Value-50) -lt 0.01) 'Queue percentage is incorrect'
 Assert ($progress.Text.Contains('2/3') -and $progress.Text.Contains('Example')) 'Progress lost queue/app identity'
 Assert (!$progress.Feed('PROGRESS:101')) 'Invalid percentage accepted'
 Assert ($progress.Text.Contains('недоступны')) 'Unreported speed/bytes were invented'
 Assert ($progress.Feed('Downloading: 50% |bar| (10 MB/20 MB, 2.0 MB/s)')) 'Reported transfer metrics were ignored'
 Assert ($progress.Text.Contains('2.0 MB/s')) 'Reported transfer speed was lost'
 $progress.Begin('Установка',1,2,'Example')
 Assert ($progress.Feed('Install: InstallingApplication (30%)')) 'Installer percentage was ignored'
 $progress.Complete(1,1);Assert ($progress.Value -eq 100 -and $progress.Text.Contains('ошибок: 1')) 'Queue result summary is incorrect'
 $progress.Begin('Скачивание',1,2,'Canceled');$progress.Feed('PROGRESS:50')|Out-Null;$progress.Stop('Отменено')
 Assert ($progress.Text.Contains('Отменено') -and !$progress.Feed('PROGRESS:80')) 'Canceled progress continued updating'
 $progress.Reset();Assert (!$progress.Active) 'New operation retained old progress'
 Write-Host 'PASS: actual progress formats, honest missing metrics, global queue progress and result summary'
 $raw=@'
App 6739530834 version 1.10
first@example.com
C:\Users\PrivateUser\Apps\MAX.ipa
Authorization: Bearer header-secret
Cookie: cookie-secret
{"password":"json-pass","passwordToken":"json-token","authCode":"123456","dsid":9876543210}
--password "argument pass" --auth-code 654321 --keychain-passphrase 'phrase-secret'
refresh_token=refresh-secret
<key>passwordToken</key><string>plist-secret</string>
00008030-0000111ABC000DEF
Private iPhone
'@
 $report=[IpaDesktop.DiagnosticReport]::Create($raw,'1.1.8','device-tools-v1.0.0',[string[]]@('Private iPhone'))
 foreach($secret in @('first@example.com','PrivateUser','header-secret','cookie-secret','json-pass','json-token','123456','9876543210','argument pass','654321','phrase-secret','refresh-secret','plist-secret','00008030-0000111ABC000DEF','Private iPhone')){Assert (!$report.Contains($secret)) ('Diagnostic report leaked '+$secret)}
 Assert ($report.Contains('6739530834') -and $report.Contains('version 1.10') -and $report.Contains('1.1.8')) 'Diagnostic redaction discarded useful application/version data'
 Write-Host 'PASS: diagnostic emails, profiles, headers, JSON secrets, CLI credentials, plist tokens and device identifiers are scrubbed'
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
