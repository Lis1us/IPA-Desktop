$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$zip=Join-Path $projectRoot ('IPA-Desktop-'+[IpaDesktop.GuiUpdater]::CurrentVersion+'-Windows.zip')
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
$stream=[IO.File]::Create($zip)
$archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($file in [IpaDesktop.GuiUpdater]::PackageFiles) {
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $projectRoot $file),$file,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
 }
 # Root aliases allow installed legacy versions to download and apply this package.
 # The executable relocates/removes these known copies on its first launch.
 $aliases=@{
  'App.ico'='Assets/App.ico';'Update-App.ps1'='Scripts/Update-App.ps1'
  'Core.cs'='Generated/Core.cs';'App.cs'='Generated/App.cs'
  'IpaMetadata.cs'='Generated/IpaMetadata.cs';'WindowTheme.cs'='Generated/WindowTheme.cs';'GuiUpdater.cs'='Generated/GuiUpdater.cs'
  'Verify.ps1'='Tests/Verify.ps1';'Verify-IpaMetadata.ps1'='Tests/Verify-IpaMetadata.ps1';'Verify-Updater.ps1'='Tests/Verify-Updater.ps1'
 }
 foreach($name in $aliases.Keys) {
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $projectRoot $aliases[$name]),$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
 }
}finally{$archive.Dispose();$stream.Dispose()}
Write-Host ('Package: '+$zip)
