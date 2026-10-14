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
 foreach($name in @('MAX','Telegram Messenger','WhatsApp Messenger','YouTube')){$row=New-Object IpaDesktop.AppRow;$row.Name=$name;$row.ID=(6739530834L+$rows.Count).ToString();$rows+=$row}
 $window.FindName('Apps').ItemsSource=$rows
 $window.FindName('Apps').SelectedIndex=0
 $chosen=New-Object IpaDesktop.DownloadSelection
 foreach($row in $rows){$chosen.Add($row,[NullString]::Value)}
 $window.FindName('ChosenApps').ItemsSource=$chosen.Snapshot()
 $window.FindName('ChosenCount').Text='Selected: '+$chosen.Count
 $window.FindName('Download').Content='Download ('+$chosen.Count+')'
 $localRows=@()
 foreach($name in @('MAX.ipa','Telegram.ipa')) {
  $file=New-Object IpaDesktop.LocalRow;$file.Name=$name;$file.Path='C:\Apps\'+$name;$file.AppleAccount='example@icloud.com';$file.Size='100 MB';$localRows+=$file
 }
 $window.FindName('Files').ItemsSource=$localRows
 $window.FindName('ChosenFiles').ItemsSource=$localRows
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
 # Verify the same routed left-click binding used by both application grids.
 foreach($pair in @(@('Apps',0),@('Files',1))) {
  $tabs.SelectedIndex=$pair[1]
  $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
  $grid=$window.FindName($pair[0]);$row=$grid.ItemContainerGenerator.ContainerFromIndex(0)
  if(!$row){throw 'Missing visible data row'}
  $script:clickedItem=$null
  [IpaDesktop.RowClickSelection]::Attach($grid,[Action[object]]{param($item) $script:clickedItem=$item})
  foreach($eventId in @([Windows.UIElement]::PreviewMouseLeftButtonDownEvent,[Windows.UIElement]::PreviewMouseLeftButtonUpEvent)) {
   $mouse=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
   $mouse.RoutedEvent=$eventId;$mouse.Source=$row
   $grid.RaiseEvent($mouse)
  }
  if(![object]::ReferenceEquals($script:clickedItem,$grid.Items[0])){throw 'Left click did not add the correct data row'}
 }
 Write-Host 'PASS: routed left-click selection on search and local IPA grids'
 $tabs.SelectedIndex=0
 $window.Width=940;$window.Height=720;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 Write-Host ('Minimum layout: '+$window.FindName('Apps').ActualWidth+' x '+$window.FindName('Apps').ActualHeight)
 $minimumHeight=$window.FindName('Apps').ActualHeight
 $tabs.SelectedIndex=1;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 if($window.FindName('Files').ActualHeight -lt 80){throw 'Files table collapsed at minimum size'}
 $tabs.SelectedIndex=0;$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
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
