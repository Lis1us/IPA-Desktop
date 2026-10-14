$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'IPA-Desktop.exe'))|Out-Null
$reader=[IO.File]::OpenRead((Join-Path $PSScriptRoot 'MainWindow.xaml'))
try {$window=[Windows.Markup.XamlReader]::Load($reader)}finally{$reader.Dispose()}
try {
 $icon=New-Object Windows.Media.Imaging.BitmapImage
 $icon.BeginInit();$icon.UriSource=New-Object Uri((Join-Path $PSScriptRoot 'App.ico'));$icon.CacheOption='OnLoad';$icon.EndInit()
 $window.Icon=$icon;$window.FindName('BrandIcon').Source=$icon
 $window.FindName('VersionList').Items.Add('Latest version')|Out-Null
 $window.FindName('VersionList').SelectedIndex=0
 $rows=@()
 foreach($name in @('MAX','Telegram Messenger','WhatsApp Messenger','YouTube')){$row=New-Object IpaDesktop.AppRow;$row.Name=$name;$row.ID='6739530834';$rows+=$row}
 $window.FindName('Apps').ItemsSource=$rows
 $window.FindName('Apps').SelectedIndex=0
 $window.Show()
 [IpaDesktop.WindowTheme]::Apply($window)
 $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 # Force each tab template to load and check minimum-window layout.
 $tabs=$window.FindName('Apps').Parent.Parent.Parent
 # Find the TabControl through the logical tree.
 function FindTabs($node) {
  if($node -is [Windows.Controls.TabControl]){return $node}
  foreach($child in [Windows.LogicalTreeHelper]::GetChildren($node)) {
   if($child -is [Windows.DependencyObject]){$result=FindTabs $child;if($result){return $result}}
  }
 }
 $tabs=FindTabs $window
 foreach($index in 0..3){$tabs.SelectedIndex=$index;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()}
 $tabs.SelectedIndex=0
 $window.Width=940;$window.Height=650;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 Write-Host ('Minimum layout: '+$window.FindName('Apps').ActualWidth+' x '+$window.FindName('Apps').ActualHeight)
 $minimumHeight=$window.FindName('Apps').ActualHeight
 $window.FindName('VersionList').IsDropDownOpen=$true;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $window.FindName('VersionList').IsDropDownOpen=$false
 $window.Width=1160;$window.Height=800;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 $visual=$window.Content
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$visual.ActualWidth,[int]$visual.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual)
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=New-Object IO.MemoryStream
 $encoder.Save($stream)
 Write-Host ('IPA_PREVIEW:'+ [Convert]::ToBase64String($stream.ToArray()))
 $stream.Dispose()
 if($minimumHeight -lt 80){throw 'Application table collapsed at minimum size'}
 Write-Host 'PASS: all tab templates, dark combo popup, minimum window layout, icon and DWM initialization'
}finally{$window.Close()}
