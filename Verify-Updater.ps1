$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'IPA-Desktop.exe'))|Out-Null
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$temp=Join-Path ([IO.Path]::GetTempPath()) ("ipa updater's test "+[Guid]::NewGuid().ToString('N'))
try {
 New-Item -ItemType Directory -Path $temp|Out-Null
 if([IpaDesktop.GuiUpdater]::ParseVersion('v0.1.10') -le [IpaDesktop.GuiUpdater]::ParseVersion('0.1.9')){throw 'Version ordering failed'}
 $rejected=$false
 try {[IpaDesktop.GuiUpdater]::ParseVersion('v0.1.7-beta')}catch{$rejected=$true}
 if(!$rejected){throw 'Prerelease accepted'}
 $release=New-Object IpaDesktop.Release
 $release.tag_name='v0.1.7'
 $asset=New-Object IpaDesktop.Asset
 $asset.name='IPA-Desktop-0.1.7-Windows.zip'
 $asset.browser_download_url='https://github.com/Lis1us/IPA-Desktop/releases/download/v0.1.7/IPA-Desktop-0.1.7-Windows.zip'
 $asset.size=123
 $asset.digest='sha256:'+('a'*64)
 $release.assets=@($asset)
 [IpaDesktop.GuiUpdater]::SelectAsset($release)|Out-Null
 $asset.browser_download_url='https://example.com/update.zip'
 $rejected=$false;try {[IpaDesktop.GuiUpdater]::SelectAsset($release)}catch{$rejected=$true}
 if(!$rejected){throw 'Untrusted release accepted'}
 Write-Host 'PASS: versions and release source validation'
 $files=[IpaDesktop.GuiUpdater]::PackageFiles
 $stage=Join-Path $temp 'stage'
 $target=Join-Path $temp 'target'
 New-Item -ItemType Directory -Path $stage,$target|Out-Null
 foreach($file in $files) {
  [IO.File]::WriteAllText((Join-Path $stage $file),"new:$file")
  [IO.File]::WriteAllText((Join-Path $target $file),"old:$file")
 }
 [IO.File]::WriteAllText((Join-Path $target 'keep.ipa'),'keep')
 & (Join-Path $PSScriptRoot 'Update-App.ps1') -Stage $stage -Target $target -NoRestart
 foreach($file in $files){if([IO.File]::ReadAllText((Join-Path $target $file)) -ne "new:$file"){throw 'Update copy failed'}}
 if([IO.File]::ReadAllText((Join-Path $target 'keep.ipa')) -ne 'keep'){throw 'User file changed'}
 Write-Host 'PASS: update installation, spaces/apostrophes in paths, preserved user files'
 Remove-Item -LiteralPath (Join-Path $temp 'backup') -Recurse -Force
 foreach($file in $files){[IO.File]::WriteAllText((Join-Path $target $file),"old:$file")}
 # Reading is allowed for backup, but writing the second file must fail after the EXE was replaced.
 $lock=[IO.File]::Open((Join-Path $target $files[1]),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try {
  $rejected=$false
  try {& (Join-Path $PSScriptRoot 'Update-App.ps1') -Stage $stage -Target $target -NoRestart}catch{$rejected=$true}
  if(!$rejected){throw 'Locked destination was overwritten'}
 }finally{$lock.Dispose()}
 foreach($file in $files){if([IO.File]::ReadAllText((Join-Path $target $file)) -ne "old:$file"){throw "Rollback failed: $file"}}
 Write-Host 'PASS: failed installation rolls back changed files'
 $zip=Join-Path $temp 'package.zip'
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
 $unpacked=Join-Path $temp 'unpacked'
 [IpaDesktop.GuiUpdater]::Extract($zip,$unpacked)
 if(!(Test-Path -LiteralPath (Join-Path $unpacked 'App.ico'))){throw 'Extraction failed'}
 Remove-Item -LiteralPath $zip
 Remove-Item -LiteralPath (Join-Path $stage 'App.ico')
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
 $rejected=$false
 try {[IpaDesktop.GuiUpdater]::Extract($zip,(Join-Path $temp 'incomplete'))}catch{$rejected=$true}
 if(!$rejected){throw 'Incomplete package accepted'}
 Write-Host 'PASS: complete package extraction and incomplete package rejection'
}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
