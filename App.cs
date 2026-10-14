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
 public class Desktop {
  Window w;Engine engine;CancellationTokenSource cancellation;bool busy;bool setting;bool signedIn;
  string versionApp; Release guiRelease; bool exitingForUpdate;
  T C<T>(string name) where T:class {return w.FindName(name) as T;}
  void On(string name,Action action) {C<Button>(name).Click+=delegate {try {action();}catch(Exception e){Error(e);}};}
  void Log(string text) {w.Dispatcher.BeginInvoke(new Action(delegate {var j=C<TextBox>("Journal");if(j.Text.Length>180000)j.Text=j.Text.Substring(j.Text.Length-120000);j.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);j.ScrollToEnd();}));}
  void Error(Exception e) {Log(e.Message);C<TextBlock>("Status").Text="Ошибка — подробности в журнале";MessageBox.Show(w,e.Message,"IPA Desktop",MessageBoxButton.OK,MessageBoxImage.Warning);}
  void StateView() {C<TextBlock>("EngineVersion").Text="Движок: "+(engine.Settings.Version??"не установлен");setting=true;C<CheckBox>("AutoUpdate").IsChecked=engine.Settings.AutoUpdate;setting=false;}
  void ApplyActions() {
   C<Button>("InstallGuiUpdate").IsEnabled=!busy&&guiRelease!=null;
   C<Button>("Login").IsEnabled=!busy&&!signedIn;
   C<Button>("Logout").IsEnabled=!busy&&signedIn;
   C<Button>("VersionInfo").IsEnabled=!busy&&C<DataGrid>("Apps").SelectedItems.Count==1;
  }
  async void Work(string title,Func<CancellationToken,Task> action) {
   if(busy)return;busy=true;cancellation=new CancellationTokenSource();
   string[] buttons={"Login","AccountInfo","Logout","Search","Versions","VersionInfo","Download","RefreshFiles","ImportFile","DeleteFiles","Devices","InstallFiles","CheckUpdate","Rollback","CheckGuiUpdate","InstallGuiUpdate"};
   foreach(var name in buttons)C<Button>(name).IsEnabled=false;
   C<CheckBox>("CheckGuiUpdates").IsEnabled=false;C<CheckBox>("AutoUpdate").IsEnabled=false;C<Button>("Cancel").IsEnabled=true;C<ProgressBar>("Progress").IsIndeterminate=true;C<TextBlock>("Status").Text=title;Log(title);
   try {await action(cancellation.Token);C<TextBlock>("Status").Text="Готово";}
   catch(OperationCanceledException){Log("Операция отменена.");C<TextBlock>("Status").Text="Отменено";}
   catch(Exception e){Error(e);}
   finally {busy=false;cancellation.Dispose();cancellation=null;foreach(var name in buttons)C<Button>(name).IsEnabled=true;C<CheckBox>("CheckGuiUpdates").IsEnabled=true;C<CheckBox>("AutoUpdate").IsEnabled=true;C<Button>("Cancel").IsEnabled=false;C<ProgressBar>("Progress").IsIndeterminate=false;C<ProgressBar>("Progress").Value=0;StateView();RefreshFiles();ApplyActions();if(exitingForUpdate)w.Close();}
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
   Log(text);
  }
  public Desktop() {
   using(var stream=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MainWindow.xaml")))w=(Window)XamlReader.Load(stream);
   w.SourceInitialized+=delegate {WindowTheme.Apply(w);};
   w.Activated+=delegate {WindowTheme.Apply(w);};
   using(var icon=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"App.ico"))) {
    var decoder=new System.Windows.Media.Imaging.IconBitmapDecoder(icon,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
    w.Icon=decoder.Frames[0];C<Image>("BrandIcon").Source=decoder.Frames[decoder.Frames.Count-1];
   }
   engine=new Engine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IPA-Desktop"));engine.Log=Log;
   engine.Progress=delegate(double value) {w.Dispatcher.BeginInvoke(new Action(delegate {C<ProgressBar>("Progress").IsIndeterminate=false;C<ProgressBar>("Progress").Value=value;}));};
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
   On("Search",delegate {string query=C<TextBox>("Query").Text.Trim();if(query.Length==0)throw new Exception("Введите название или App Store ID.");Work("Поиск приложений…",async delegate(CancellationToken ct){if(query.All(Char.IsDigit)){C<DataGrid>("Apps").ItemsSource=new[]{new AppRow{Name="Приложение по ID "+query,ID=query}};}else{var local=await engine.CatalogSearch(query,ct);C<DataGrid>("Apps").ItemsSource=local;Log("Найдено в каталогах: "+local.Count);try {var r=await engine.Tool(new[]{"search",query,"--limit","10"},ct,false);Engine.Ensure(r);var online=Engine.ParseApps(r.Output);C<DataGrid>("Apps").ItemsSource=Engine.MergeApps(local,online);Log("Получено из App Store: "+online.Count);}catch(OperationCanceledException){throw;}catch(Exception e){if(local.Count==0)throw;Log("App Store: "+e.Message+". Показаны результаты каталога.");}}C<DataGrid>("Apps").SelectedIndex=0;});});
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
   On("Download",delegate {var rows=SelectedApps();var combo=C<ComboBox>("VersionList");string v=combo.SelectedIndex>0?Convert.ToString(combo.SelectedItem):null;if(v!=null&&(rows.Count!=1||rows[0].ID!=versionApp))throw new Exception("Выбранная версия относится к другому приложению.");Work("Скачивание IPA…",async delegate(CancellationToken ct){var failed=new List<string>();foreach(var row in rows){ct.ThrowIfCancellationRequested();C<TextBlock>("Status").Text="Скачивание: "+row.Name;Log("В очереди: "+row.Name);var args=new List<string>{"download","-i",row.ID,"--purchase","-o",engine.Downloads};if(v!=null)args.AddRange(new[]{"--external-version-id",v});var result=await engine.Tool(args.ToArray(),ct,true);if(result.Code!=0)failed.Add(row.Name);RefreshFiles();}if(failed.Count>0)throw new Exception("Не удалось скачать: "+String.Join(", ",failed)+". Подробности в журнале.");});});
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
   On("Devices",delegate {Work("Поиск устройств…",async delegate(CancellationToken ct){var r=await engine.Run(engine.Binary("idevice_id*.exe"),new[]{"-l"},ct,false,false);Engine.Ensure(r);var devices=r.Output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(x=>System.Text.RegularExpressions.Regex.IsMatch(x,"^[0-9A-Fa-f-]{20,}$")).ToList();C<ComboBox>("DeviceList").ItemsSource=devices;if(devices.Count>0)C<ComboBox>("DeviceList").SelectedIndex=0;else Log("Устройства не обнаружены. Проверьте USB, доверие и драйвер Apple.");});});
   On("InstallFiles",delegate {string udid=Convert.ToString(C<ComboBox>("DeviceList").SelectedItem);var rows=C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().ToList();if(String.IsNullOrEmpty(udid)||rows.Count==0)throw new Exception("Выберите устройство и IPA-файлы.");if(MessageBox.Show(w,"Установить выбранные IPA ("+rows.Count+") на устройство "+udid+"?","Установка",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Work("Установка IPA…",async delegate(CancellationToken ct){foreach(var row in rows){Log("Установка "+row.Name);Engine.Ensure(await engine.Run(engine.Binary("ideviceinstaller*.exe"),new[]{"--udid",udid,"install",row.Path},ct,false,true));}});});
   w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy){e.Cancel=true;MessageBox.Show(w,"Сначала отмените текущую операцию и дождитесь её завершения.","Выполняется операция");}};
   ApplyActions();
   w.Loaded+=delegate {
    Log("IPA Desktop 0.1.6. Файлы: "+engine.Downloads);
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
