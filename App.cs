using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace IpaDesktop {
 public static class RowClickSelection {
  public static void Attach(DataGrid grid,Action<object> add) {
   object pressed=null;Point origin=new Point();
   grid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new System.Windows.Input.MouseButtonEventHandler(delegate(object sender,System.Windows.Input.MouseButtonEventArgs e) {
    var row=ItemsControl.ContainerFromElement(grid,e.OriginalSource as DependencyObject) as DataGridRow;
    pressed=row==null?null:row.Item;origin=e.GetPosition(grid);
   }),true);
   grid.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new System.Windows.Input.MouseButtonEventHandler(delegate(object sender,System.Windows.Input.MouseButtonEventArgs e) {
    var row=ItemsControl.ContainerFromElement(grid,e.OriginalSource as DependencyObject) as DataGridRow;
    var distance=e.GetPosition(grid)-origin;
    if(row!=null&&Object.ReferenceEquals(pressed,row.Item)&&Math.Abs(distance.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(distance.Y)<SystemParameters.MinimumVerticalDragDistance)add(row.Item);
    pressed=null;
   }),true);
  }
 }
 public static class SelectionRemove {
  public static void Attach(ItemsControl list,Action<string> remove) {
   list.AddHandler(Button.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e) {
    // WPF can retarget Source to an item container; OriginalSource identifies the button.
    var current=e.OriginalSource as DependencyObject;
    while(current!=null&&!(current is Button)) {
     current=current is System.Windows.Media.Visual||current is System.Windows.Media.Media3D.Visual3D?
      System.Windows.Media.VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
    }
    var button=current as Button;
    if(button==null||button.Tag==null)return;
    remove(Convert.ToString(button.Tag));e.Handled=true;
   }),true);
  }
 }
 public static class JournalView {
  public static void Attach(TextBox journal,BufferedJournal buffer) {
   journal.IsVisibleChanged+=delegate {
    if(!journal.IsVisible)return;
    journal.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate {
     Flush(journal,buffer,true);journal.ScrollToEnd();
    }));
   };
  }
  public static void Flush(TextBox journal,BufferedJournal buffer,bool all) {
   var text=new StringBuilder();string chunk;
   do {chunk=buffer.Drain();text.Append(chunk);}while(all&&chunk.Length>0);
   if(text.Length==0)return;
   if(text.Length>120000)text.Remove(0,text.Length-120000);
   if(journal.Text.Length>120000)journal.Text=journal.Text.Substring(journal.Text.Length-80000);
   journal.AppendText(text.ToString());if(journal.IsVisible)journal.ScrollToEnd();
  }
 }
 public class Desktop {
  Window w;Engine engine;CancellationTokenSource cancellation;bool busy;bool setting;bool signedIn;
  readonly InstallationSelection installChosen=new InstallationSelection();
  readonly DownloadSelection chosen=new DownloadSelection();
  long lastProgress;
  readonly SearchSession searchSession=new SearchSession();
  string versionApp; Release guiRelease; bool exitingForUpdate;
  T C<T>(string name) where T:class {return w.FindName(name) as T;}
  void On(string name,Action action) {C<Button>(name).Click+=delegate {try {action();}catch(Exception e){Error(e);}};}
  readonly BufferedJournal journalBuffer=new BufferedJournal();
  System.Windows.Threading.DispatcherTimer journalTimer;
  void Log(string text) {journalBuffer.Add(text);}
  void FlushJournal() {JournalView.Flush(C<TextBox>("Journal"),journalBuffer,false);}
  void Error(Exception e) {Log(e.Message);C<TextBlock>("Status").Text="Ошибка — подробности в журнале";MessageBox.Show(w,e.Message,"IPA Desktop",MessageBoxButton.OK,MessageBoxImage.Warning);}
  void StateView() {C<TextBlock>("EngineVersion").Text="Движок: "+(engine.Settings.Version??"не установлен");setting=true;C<CheckBox>("AutoUpdate").IsChecked=engine.Settings.AutoUpdate;setting=false;}
  void ApplyActions() {
   C<Button>("InstallGuiUpdate").IsEnabled=!busy&&guiRelease!=null;
   C<Button>("AddChosen").IsEnabled=!busy&&C<DataGrid>("Apps").SelectedItems.Count>0;
   C<Button>("ClearChosen").IsEnabled=!busy&&chosen.Count>0;
   C<Button>("Download").IsEnabled=!busy&&chosen.Count>0;
   C<ItemsControl>("ChosenApps").IsEnabled=!busy;
   C<ItemsControl>("ChosenFiles").IsEnabled=!busy;
   C<Button>("AddInstall").IsEnabled=!busy&&C<DataGrid>("Files").SelectedItems.Count>0;
   C<Button>("ClearInstall").IsEnabled=!busy&&installChosen.Count>0;
   C<Button>("InstallFiles").IsEnabled=!busy&&installChosen.Count>0&&C<ComboBox>("DeviceList").SelectedItem!=null;
   C<Button>("Devices").Background=new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(C<ComboBox>("DeviceList").Items.Count==0?"#16704C":"#243247"));
   C<Button>("Login").IsEnabled=!busy&&!signedIn;
   C<Button>("Logout").IsEnabled=!busy&&signedIn;
   C<Button>("VersionInfo").IsEnabled=!busy&&C<DataGrid>("Apps").SelectedItems.Count==1;
  }
  async void Work(string title,Func<CancellationToken,Task> action) {
   if(busy)return;busy=true;cancellation=new CancellationTokenSource();
   string[] buttons={"Login","AccountInfo","Logout","Search","Versions","VersionInfo","Download","RefreshFiles","ImportFile","DeleteFiles","Devices","InstallFiles","CheckUpdate","Rollback","CheckGuiUpdate","InstallGuiUpdate","AddChosen","ClearChosen","AddInstall","ClearInstall"};
   foreach(var name in buttons)C<Button>(name).IsEnabled=false;
   C<ItemsControl>("ChosenFiles").IsEnabled=false;
   C<ItemsControl>("ChosenApps").IsEnabled=false;
   C<CheckBox>("CheckGuiUpdates").IsEnabled=false;C<CheckBox>("AutoUpdate").IsEnabled=false;C<Button>("Cancel").IsEnabled=true;C<ProgressBar>("Progress").IsIndeterminate=true;C<TextBlock>("Status").Text=title;Log(title);
   try {await action(cancellation.Token);C<TextBlock>("Status").Text="Готово";}
   catch(OperationCanceledException){Log("Операция отменена.");C<TextBlock>("Status").Text="Отменено";}
   catch(Exception e){Error(e);}
   finally {busy=false;cancellation.Dispose();cancellation=null;foreach(var name in buttons)C<Button>(name).IsEnabled=true;C<CheckBox>("CheckGuiUpdates").IsEnabled=true;C<CheckBox>("AutoUpdate").IsEnabled=true;C<Button>("Cancel").IsEnabled=false;C<ProgressBar>("Progress").IsIndeterminate=false;C<ProgressBar>("Progress").Value=0;StateView();RefreshFiles();ApplyActions();if(exitingForUpdate)w.Close();}
  }
  void RefreshChosen() {
   C<ItemsControl>("ChosenApps").ItemsSource=chosen.Snapshot();
   C<TextBlock>("ChosenCount").Text=chosen.Count==0?"Добавьте приложения из результатов поиска":"Выбрано: "+chosen.Count+" · сохраняются при новом поиске";
   C<Button>("Download").Content="Скачать ("+chosen.Count+")";
   ApplyActions();
  }
  void RefreshInstallChosen() {
   C<ItemsControl>("ChosenFiles").ItemsSource=installChosen.Snapshot();
   C<TextBlock>("InstallCount").Text=installChosen.Count==0?"ЛКМ по файлу — добавить к установке":"К установке: "+installChosen.Count;
   C<Button>("InstallFiles").Content="Установить ("+installChosen.Count+")";
   ApplyActions();
  }
  List<AppRow> SelectedApps() {
   var rows=C<DataGrid>("Apps").SelectedItems.Cast<AppRow>().ToList();
   if(rows.Count==0)throw new Exception("Выберите приложение в таблице.");return rows;
  }
  int filesGeneration;
  async void RefreshFiles() {
   int generation=++filesGeneration;
   var selected=new HashSet<string>(C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);
   try {
    var rows=await Task.Run(delegate {
     return Directory.GetFiles(engine.Downloads,"*.ipa").Select(x=>new FileInfo(x)).OrderByDescending(x=>x.LastWriteTime).Select(x=>new LocalRow{Name=x.Name,Size=(x.Length/1048576.0).ToString("N1")+" МБ",Path=x.FullName,AppleAccount=IpaMetadata.ReadAccount(x.FullName)}).ToList();
    });
    if(generation!=filesGeneration)return;
    C<DataGrid>("Files").ItemsSource=rows;
    foreach(var row in rows)if(selected.Contains(row.Path))C<DataGrid>("Files").SelectedItems.Add(row);
    installChosen.Reconcile(rows);RefreshInstallChosen();
   } catch(Exception e) {if(generation==filesGeneration)Log("Не удалось обновить список IPA: "+e.Message);}
  }
  async Task Account(CancellationToken ct) {
   var r=await engine.Tool(new[]{"auth","info"},ct,false);
   signedIn=false;
   C<TextBlock>("AccountLabel").Text="Вход не выполнен";
   Engine.Ensure(r);
   var data=Engine.Object(r.Output);var email=Convert.ToString(Engine.Get(data,"email"));
   if(String.IsNullOrWhiteSpace(email)||!Object.Equals(Engine.Get(data,"success"),true))throw new Exception("Не удалось подтвердить сохранённый вход в аккаунт.");
   signedIn=true;C<TextBlock>("AccountLabel").Text=email;Log("Аккаунт проверен.");
  }
  async Task Update(CancellationToken ct,bool startup) {
   var release=await engine.Check(ct);
   if(String.Equals(release.tag_name,engine.Settings.Version,StringComparison.Ordinal)) {Log("Установлен актуальный релиз "+release.tag_name);return;}
   if(!startup && MessageBox.Show(w,"Установить официальный релиз "+release.tag_name+"?\nТекущая версия будет сохранена для отката.","Обновление движка",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   await engine.Install(release,ct);
   if(startup)Log("Автоматическое обновление завершено.");
  }
  async Task CheckGui(CancellationToken ct) {
   guiRelease=await GuiUpdater.Check(ct);
   string text=guiRelease==null?"Установлена актуальная версия "+GuiUpdater.CurrentVersion:"Доступна версия "+guiRelease.tag_name;
   C<TextBlock>("GuiVersionStatus").Text=text;
   C<Button>("InstallGuiUpdate").Content=guiRelease==null?"Установить обновление":"Обновить до "+guiRelease.tag_name;
   C<TextBlock>("UpdateNotice").Text=guiRelease==null?"":text+" — вкладка «Обновления»";
   C<TextBlock>("UpdateNotice").Visibility=guiRelease==null?Visibility.Collapsed:Visibility.Visible;
   Log(text);
  }
  public Desktop() {
   using(var stream=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MainWindow.xaml")))w=(Window)XamlReader.Load(stream);
   journalTimer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background,w.Dispatcher){Interval=TimeSpan.FromMilliseconds(150)};
   JournalView.Attach(C<TextBox>("Journal"),journalBuffer);
   journalTimer.Tick+=delegate {FlushJournal();};journalTimer.Start();
   w.Closed+=delegate {journalTimer.Stop();};
   w.SourceInitialized+=delegate {WindowTheme.Apply(w);};
   w.Activated+=delegate {WindowTheme.Apply(w);};
   using(var icon=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"App.ico"))) {
    var decoder=new System.Windows.Media.Imaging.IconBitmapDecoder(icon,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
    w.Icon=decoder.Frames[0];C<Image>("BrandIcon").Source=decoder.Frames[decoder.Frames.Count-1];
   }
   engine=new Engine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IPA-Desktop"));engine.Log=Log;
   engine.Progress=delegate(double value) {long now=Stopwatch.GetTimestamp();if(value<100&&(now-lastProgress)/(double)Stopwatch.Frequency<0.15)return;lastProgress=now;w.Dispatcher.BeginInvoke(new Action(delegate {C<ProgressBar>("Progress").IsIndeterminate=false;C<ProgressBar>("Progress").Value=value;}));};
   StateView();RefreshFiles();C<ComboBox>("VersionList").Items.Add("Последняя версия");C<ComboBox>("VersionList").SelectedIndex=0;
   C<CheckBox>("CheckGuiUpdates").IsChecked=engine.Settings.CheckGuiUpdates;
   C<CheckBox>("CheckGuiUpdates").Click+=delegate {engine.Settings.CheckGuiUpdates=C<CheckBox>("CheckGuiUpdates").IsChecked==true;try {engine.Save();}catch(Exception e){Error(e);}};
   On("CheckGuiUpdate",delegate {Work("Проверка версии IPA Desktop…",CheckGui);});
   On("InstallGuiUpdate",delegate {
    if(guiRelease==null)return;
    if(MessageBox.Show(w,"Скачать и установить IPA Desktop "+guiRelease.tag_name+"? Программа закроется и запустится снова. Скачанные IPA и настройки сохранятся.","Обновление программы",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
    Work("Скачивание обновления IPA Desktop…",async delegate(CancellationToken ct) {
     string stage=await GuiUpdater.Prepare(guiRelease,ct,delegate(double value){w.Dispatcher.BeginInvoke(new Action(delegate {C<ProgressBar>("Progress").IsIndeterminate=false;C<ProgressBar>("Progress").Value=value;}));});
     ct.ThrowIfCancellationRequested();GuiUpdater.Launch(stage);exitingForUpdate=true;
    });
   });
   On("OpenGuiReleases",delegate {Process.Start(new ProcessStartInfo("https://github.com/Lis1us/IPA-Desktop/releases"){UseShellExecute=true});});
   On("Cancel",delegate {if(cancellation!=null)cancellation.Cancel();});
   On("OpenFolder",delegate {Process.Start(new ProcessStartInfo(engine.Downloads){UseShellExecute=true});});
   On("OpenRelease",delegate {Process.Start(new ProcessStartInfo("https://github.com/kda2495/IPA_Downloader/releases"){UseShellExecute=true});});
   On("CheckUpdate",delegate {Work("Проверка обновлений…",ct=>Update(ct,false));});
   On("Rollback",delegate {if(MessageBox.Show(w,"Вернуть предыдущий движок и отключить автообновление?","Откат",MessageBoxButton.YesNo)==MessageBoxResult.Yes){engine.Rollback();StateView();}});
   C<CheckBox>("AutoUpdate").Checked+=delegate {if(!setting){engine.Settings.AutoUpdate=true;try{engine.Save();}catch(Exception e){Error(e);}}};
   C<CheckBox>("AutoUpdate").Unchecked+=delegate {if(!setting){engine.Settings.AutoUpdate=false;try{engine.Save();}catch(Exception e){Error(e);}}};
   On("Login",delegate {Work("Вход: завершите авторизацию в открывшемся окне",async delegate(CancellationToken ct) {var r=await engine.Run(engine.Binary("ipatool*.exe"),new[]{"auth","login"},ct,true,false);Engine.Ensure(r);await Account(ct);});});
   On("AccountInfo",delegate {Work("Проверка аккаунта…",Account);});
   On("Logout",delegate {if(MessageBox.Show(w,"Выйти из текущей сессии ipatool? Это затронет и исходный скрипт.","Выход",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Work("Выход…",async delegate(CancellationToken ct){Engine.Ensure(await engine.Tool(new[]{"auth","revoke"},ct,false));signedIn=false;C<TextBlock>("AccountLabel").Text="Вход не выполнен";});});
   C<TextBox>("Query").TextChanged+=delegate {
    searchSession.Change(C<TextBox>("Query").Text);
    if(String.IsNullOrWhiteSpace(C<TextBox>("Query").Text)) {
     C<DataGrid>("Apps").ItemsSource=null;ApplyActions();
    }
   };
   On("Search",delegate {
    string query=C<TextBox>("Query").Text.Trim();if(query.Length==0)return;
    int request=searchSession.Revision;
    Work("Поиск приложений…",async delegate(CancellationToken ct) {
     if(query.All(Char.IsDigit)) {
      if(searchSession.IsCurrent(request))C<DataGrid>("Apps").ItemsSource=new[]{new AppRow{Name="Приложение по ID "+query,ID=query}};
     } else {
      var local=await engine.CatalogSearch(query,ct);
      if(!searchSession.IsCurrent(request))return;
      C<DataGrid>("Apps").ItemsSource=local;Log("Найдено в каталогах: "+local.Count);
      try {
       var r=await engine.Tool(new[]{"search",query,"--limit","10"},ct,false);Engine.Ensure(r);
       if(!searchSession.IsCurrent(request))return;
       C<DataGrid>("Apps").ItemsSource=Engine.MergeApps(local,Engine.ParseApps(r.Output));
      }catch(OperationCanceledException){throw;}catch(Exception e) {
       if(!searchSession.IsCurrent(request))return;
       if(local.Count==0)throw;Log("App Store: "+e.Message+". Показаны результаты каталога.");
      }
     }
     if(searchSession.IsCurrent(request))C<DataGrid>("Apps").SelectedIndex=0;
    });
   });
   C<TextBox>("Query").KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter&&C<Button>("Search").IsEnabled)C<Button>("Search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
   C<DataGrid>("Apps").SelectionChanged+=delegate {versionApp=null;C<ComboBox>("VersionList").Items.Clear();C<ComboBox>("VersionList").Items.Add("Последняя версия");C<ComboBox>("VersionList").SelectedIndex=0;ApplyActions();};
   On("Versions",delegate {var selected=SelectedApps();if(selected.Count!=1)throw new Exception("Для списка версий выберите одно приложение.");string id=selected[0].ID;Work("Получение доступных версий…",async delegate(CancellationToken ct){var r=await engine.Tool(new[]{"list-versions","-i",id},ct,false);Engine.Ensure(r);var values=Engine.Get(Engine.Object(r.Output),"externalVersionIdentifiers") as object[];if(values==null)throw new Exception("Список версий не получен: формат ответа не поддерживается.");if(SelectedApps().Count!=1||SelectedApps()[0].ID!=id)throw new Exception("Выбор приложения изменился. Повторите запрос версий.");var combo=C<ComboBox>("VersionList");combo.Items.Clear();combo.Items.Add("Последняя версия");foreach(var v in values.Reverse()){var s=Convert.ToString(v);if(s.Length>0&&s.All(Char.IsDigit))combo.Items.Add(s);}combo.SelectedIndex=0;versionApp=id;Log("Версий: "+values.Length+". В списке отображаются идентификаторы Apple.");});});
   On("VersionInfo",delegate {
    var selected=SelectedApps();if(selected.Count!=1)throw new Exception("Выберите одно приложение.");
    var row=selected[0];var combo=C<ComboBox>("VersionList");
    string version=combo.SelectedIndex>0?Convert.ToString(combo.SelectedItem):null;
    if(version!=null&&row.ID!=versionApp)throw new Exception("Обновите список версий выбранного приложения.");
    Work("Получение сведений о версии…",async delegate(CancellationToken ct) {
     if(version==null) {
      var list=await engine.Tool(new[]{"list-versions","-i",row.ID},ct,false);Engine.Ensure(list);
      var ids=Engine.VersionIds(list.Output);
      if(ids.Count==0)throw new Exception("Движок не вернул доступных версий приложения.");
      version=ids[ids.Count-1];Log("Последняя доступная версия: ID "+version);
     }
     var r=await engine.Tool(new[]{"get-version-metadata","-i",row.ID,"--external-version-id",version},ct,false);Engine.Ensure(r);
     var data=Engine.Object(r.Output);
     var number=Convert.ToString(Engine.Get(data,"displayVersion"));var date=Convert.ToString(Engine.Get(data,"releaseDate"));
     MessageBox.Show(w,row.Name+Environment.NewLine+Environment.NewLine+"Версия: "+(String.IsNullOrWhiteSpace(number)?"не указана":number)+Environment.NewLine+"Дата выпуска: "+(String.IsNullOrWhiteSpace(date)?"не указана":date)+Environment.NewLine+"ID версии: "+version+Environment.NewLine+"App Store ID: "+row.ID,"Сведения о версии",MessageBoxButton.OK,MessageBoxImage.Information);
    });
   });
   RowClickSelection.Attach(C<DataGrid>("Apps"),delegate(object item) {
    if(busy)return;var row=item as AppRow;if(row==null||chosen.Snapshot().Any(x=>x.ID==row.ID))return;
    var combo=C<ComboBox>("VersionList");
    chosen.Add(row,combo.SelectedIndex>0&&versionApp==row.ID?Convert.ToString(combo.SelectedItem):null);RefreshChosen();
   });
   RowClickSelection.Attach(C<DataGrid>("Files"),delegate(object item) {
    if(busy)return;var row=item as LocalRow;if(row==null)return;installChosen.Add(row);RefreshInstallChosen();
   });
   C<DataGrid>("Files").SelectionChanged+=delegate {ApplyActions();};
   C<ComboBox>("DeviceList").SelectionChanged+=delegate {ApplyActions();};
   On("AddInstall",delegate {foreach(var row in C<DataGrid>("Files").SelectedItems.Cast<LocalRow>())installChosen.Add(row);RefreshInstallChosen();});
   On("ClearInstall",delegate {installChosen.Clear();RefreshInstallChosen();});
   SelectionRemove.Attach(C<ItemsControl>("ChosenFiles"),delegate(string path) {
    if(busy)return;installChosen.Remove(path);RefreshInstallChosen();
   });
   On("AddChosen",delegate {
    var rows=SelectedApps();var combo=C<ComboBox>("VersionList");
    string version=combo.SelectedIndex>0?Convert.ToString(combo.SelectedItem):null;
    if(version!=null&&(rows.Count!=1||rows[0].ID!=versionApp))throw new Exception("Для конкретной версии выберите одно приложение и обновите список версий.");
    foreach(var row in rows)chosen.Add(row,version);
    RefreshChosen();
   });
   On("ClearChosen",delegate {chosen.Clear();RefreshChosen();});
   SelectionRemove.Attach(C<ItemsControl>("ChosenApps"),delegate(string id) {
    if(busy)return;chosen.Remove(id);RefreshChosen();
   });
   On("Download",delegate {
    var rows=chosen.Snapshot();if(rows.Count==0)throw new Exception("Добавьте приложения в выбранное.");
    Work("Скачивание выбранных IPA…",async delegate(CancellationToken ct) {
     var failed=new List<string>();int index=0;
     foreach(var row in rows) {
      ct.ThrowIfCancellationRequested();index++;
      C<TextBlock>("Status").Text="Скачивание "+index+" из "+rows.Count+": "+row.Name;
      Log("В очереди: "+row.Label);
      try {
       var args=new List<string>{"download","-i",row.ID,"--purchase","-o",engine.Downloads};
       if(row.VersionID!=null)args.AddRange(new[]{"--external-version-id",row.VersionID});
       var result=await engine.Tool(args.ToArray(),ct,true);Engine.Ensure(result);
       chosen.Remove(row.ID);RefreshChosen();
      }catch(OperationCanceledException){throw;}catch(Exception e){failed.Add(row.Name);Log("Не удалось скачать "+row.Name+": "+e.Message);}
      RefreshFiles();
     }
     if(failed.Count>0)throw new Exception("Не удалось скачать: "+String.Join(", ",failed)+". Эти приложения остались в выбранном.");
    });
   });
   On("RefreshFiles",RefreshFiles);
   On("DeleteFiles",delegate {
    if(busy)return;
    var rows=C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().ToList();
    if(rows.Count==0)throw new Exception("Выберите IPA-файлы для удаления. Несколько файлов: Ctrl / Shift.");
    var summary=String.Join(Environment.NewLine,rows.Take(8).Select(x=>x.Name));
    if(rows.Count>8)summary+=Environment.NewLine+"… и ещё "+(rows.Count-8);
    if(MessageBox.Show(w,"Переместить в корзину выбранные IPA ("+rows.Count+")?"+Environment.NewLine+Environment.NewLine+summary+Environment.NewLine+Environment.NewLine+"Приложения на iPhone останутся установленными.","Удаление IPA",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
    Work("Перемещение IPA в корзину…",async delegate(CancellationToken ct) {
     var failures=new List<string>();int removed=0;
     await Task.Run(delegate {
      foreach(var row in rows) {
       ct.ThrowIfCancellationRequested();
       try {
        var path=Path.GetFullPath(row.Path);
        if(!String.Equals(Path.GetDirectoryName(path),Path.GetFullPath(engine.Downloads),StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetExtension(path),".ipa",StringComparison.OrdinalIgnoreCase))throw new IOException("Файл не относится к папке IPA.");
        if(!File.Exists(path)){Log("Файл уже отсутствует: "+row.Name);continue;}
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Удаление ссылок не поддерживается.");
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        removed++;Log("Перемещено в корзину: "+row.Name);
       } catch(OperationCanceledException){throw;}
         catch(Exception e){failures.Add(row.Name+": "+e.Message);}
      }
     },ct);
     Log("Удалено из папки IPA: "+removed);
     if(failures.Count>0)throw new IOException("Не удалось удалить:"+Environment.NewLine+String.Join(Environment.NewLine,failures));
    });
   });

   On("ImportFile",delegate {var dialog=new OpenFileDialog{Filter="IPA files (*.ipa)|*.ipa",Multiselect=true};if(dialog.ShowDialog(w)!=true)return;var paths=dialog.FileNames;Work("Добавление IPA…",async delegate(CancellationToken ct){await Task.Run(delegate {foreach(var p in paths){ct.ThrowIfCancellationRequested();var target=Path.Combine(engine.Downloads,Path.GetFileName(p));if(String.Equals(Path.GetFullPath(p),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))continue;if(File.Exists(target))throw new IOException("Файл уже существует: "+Path.GetFileName(p));File.Copy(p,target);}},ct);});});
   On("Devices",delegate {
    Work("Поиск iPhone…",async delegate(CancellationToken ct) {
     var combo=C<ComboBox>("DeviceList");combo.ItemsSource=null;ApplyActions();
     using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
      timeout.CancelAfter(TimeSpan.FromSeconds(15));
      Result r;
      try {r=await engine.Run(engine.Binary("idevice_id*.exe"),new[]{"-l"},timeout.Token,false,false);}
      catch(OperationCanceledException){ct.ThrowIfCancellationRequested();throw new IOException("Поиск iPhone не ответил за 15 секунд. Проверьте USB и драйвер Apple.");}
      Engine.Ensure(r);
      var ids=r.Output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>System.Text.RegularExpressions.Regex.IsMatch(x,"^[0-9A-Fa-f-]{20,}$")).Distinct().ToList();
      var devices=new List<DeviceRow>();
      foreach(var id in ids) {
       string name="iPhone · "+id.Substring(Math.Max(0,id.Length-6));
       try {
        using(var names=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
         names.CancelAfter(TimeSpan.FromSeconds(10));
         var info=await engine.Run(engine.Binary("ideviceinfo*.exe"),new[]{"-u",id,"-k","DeviceName"},names.Token,false,false);
         Engine.Ensure(info);
         string value=info.Output.Trim();
         if(value.StartsWith("DeviceName:",StringComparison.Ordinal))value=value.Substring(11).Trim();
         if(value.Length>0&&value.Length<=200&&!value.Any(Char.IsControl)){name=value;}
         else Log("Имя iPhone не получено. Используется короткий идентификатор.");
        }
       }catch(OperationCanceledException){ct.ThrowIfCancellationRequested();Log("Время ожидания имени iPhone истекло. Разблокируйте устройство и подтвердите доверие.");}
        catch(Exception e){Log("Не удалось прочитать имя iPhone: "+e.Message+". Разблокируйте устройство и подтвердите доверие.");}
       devices.Add(new DeviceRow{ID=id,Name=name});
      }
      foreach(var group in devices.GroupBy(x=>x.Name).Where(g=>g.Count()>1))foreach(var device in group)device.Name+=" · "+device.ID.Substring(Math.Max(0,device.ID.Length-6));
      combo.ItemsSource=devices;if(devices.Count>0){combo.SelectedIndex=0;Log("Найдено: "+String.Join(", ",devices.Select(x=>x.Name)));}
      else Log("iPhone не обнаружен. Проверьте USB, доверие и драйвер Apple.");
      ApplyActions();
     }
    });
   });
   On("InstallFiles",delegate {
    var device=C<ComboBox>("DeviceList").SelectedItem as DeviceRow;string udid=device==null?null:device.ID;var rows=installChosen.Snapshot();
    if(String.IsNullOrEmpty(udid)||rows.Count==0)throw new Exception("Выберите устройство и добавьте IPA к установке.");
    string summary=String.Join(Environment.NewLine,rows.Take(8).Select(x=>x.Name));
    if(rows.Count>8)summary+=Environment.NewLine+"… и ещё "+(rows.Count-8);
    if(MessageBox.Show(w,"Установить IPA ("+rows.Count+") на устройство "+device.Name+"?"+Environment.NewLine+Environment.NewLine+summary,"Установка",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
    Work("Установка IPA…",async delegate(CancellationToken ct) {
     var failed=new List<string>();int index=0;
     foreach(var row in rows) {
      ct.ThrowIfCancellationRequested();index++;C<TextBlock>("Status").Text="Установка "+index+" из "+rows.Count+": "+row.Name;Log("Установка "+row.Name);
      try {
       if(!File.Exists(row.Path))throw new IOException("IPA-файл больше не существует.");
       Engine.Ensure(await engine.Run(engine.Binary("ideviceinstaller*.exe"),new[]{"--udid",udid,"install",row.Path},ct,false,true));
       installChosen.Remove(row.Path);RefreshInstallChosen();
      }catch(OperationCanceledException){throw;}catch(Exception e){failed.Add(row.Name);Log("Ошибка установки "+row.Name+": "+e.Message);}
     }
     if(failed.Count>0)throw new Exception("Не удалось установить: "+String.Join(", ",failed)+". Доступные IPA остались в выбранном для повтора.");
    });
   });
   w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(w,"Сначала отмените текущую операцию и дождитесь её завершения.","Выполняется операция");}};
   ApplyActions();
   w.Loaded+=delegate {
    Log("IPA Desktop 0.1.10. Файлы: "+engine.Downloads);
    Work("Проверка движка и сохранённого входа…",async delegate(CancellationToken ct) {
     if(engine.Settings.CheckGuiUpdates) {
      try {await CheckGui(ct);}catch(OperationCanceledException){if(ct.IsCancellationRequested)throw;Log("Проверка IPA Desktop: время ожидания истекло.");}catch(Exception e){Log("Проверка IPA Desktop: "+e.Message);}
     }
     if(engine.Settings.AutoUpdate) {
      try {await Update(ct,true);}catch(OperationCanceledException){throw;}catch(Exception e){Log("Не удалось обновить движок: "+e.Message);}
     }
     if(!String.IsNullOrEmpty(engine.Settings.Current)) {
      try {await Account(ct);}catch(OperationCanceledException){throw;}catch(Exception e){Log("Проверка входа: "+e.Message);}
     }
    });
   };
  }
  [STAThread] public static void Main() {
   bool created;using(var mutex=new Mutex(true,"Local\\IPA-Desktop-0.1",out created)) {
    if(!created){MessageBox.Show("IPA Desktop уже запущен.");return;}
    try {var app=new Application();var desktop=new Desktop();app.Run(desktop.w);}catch(Exception e){MessageBox.Show(e.ToString(),"Ошибка запуска IPA Desktop");}
   }
  }
 }
}
