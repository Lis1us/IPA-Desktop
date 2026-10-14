using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace IpaDesktop {
 public class Result { public int Code; public string Output; }
 public class State { public string Current; public string Previous; public string Version; public string PreviousVersion; public bool AutoUpdate = true; public bool CheckGuiUpdates = true; }
 public class Release { public string tag_name; public string html_url; public string body; public Asset[] assets; }
 public class Asset { public string name; public string browser_download_url; public string digest; public long size; }
 public class AppRow { public string Name {get;set;} public string ID {get;set;} }
 public class DownloadItem {
  public string Name {get;set;} public string ID {get;set;} public string VersionID {get;set;}
  public string Label {get {return Name+(VersionID==null?" · последняя":" · версия "+VersionID);}}
 }
 public class DownloadSelection {
  readonly List<DownloadItem> items=new List<DownloadItem>();
  public int Count {get {return items.Count;}}
  public void Add(AppRow app,string version) {
   if(app==null||String.IsNullOrEmpty(app.ID)||!app.ID.All(Char.IsDigit))throw new ArgumentException("Некорректный App Store ID.");
   if(version!=null&&(version.Length==0||!version.All(Char.IsDigit)))throw new ArgumentException("Некорректная версия.");
   var existing=items.FirstOrDefault(x=>x.ID==app.ID);
   if(existing==null)items.Add(new DownloadItem{Name=app.Name,ID=app.ID,VersionID=version});
   else {existing.Name=app.Name;existing.VersionID=version;}
  }
  public List<DownloadItem> Snapshot() {return items.Select(x=>new DownloadItem{Name=x.Name,ID=x.ID,VersionID=x.VersionID}).ToList();}
  public void Remove(string id) {items.RemoveAll(x=>x.ID==id);}
  public void Clear() {items.Clear();}
 }
 public class DeviceRow {
  public string ID {get;set;} public string Name {get;set;}
  public override string ToString() {return Name;}
 }
 public class SearchSession {
  int revision;string query="";
  public int Change(string value) {query=(value??"").Trim();return ++revision;}
  public int Revision {get {return revision;}}
  public bool IsCurrent(int request) {return request==revision&&query.Length>0;}
 }
 public class LocalRow { public string AppleAccount {get;set;} public string Name {get;set;} public string Size {get;set;} public string Path {get;set;} }
 public class InstallationSelection {
  readonly List<LocalRow> items=new List<LocalRow>();
  static LocalRow Copy(LocalRow row) {return new LocalRow{Name=row.Name,Path=row.Path,Size=row.Size,AppleAccount=row.AppleAccount};}
  public int Count {get {return items.Count;}}
  public void Add(LocalRow row) {
   if(row==null||String.IsNullOrWhiteSpace(row.Path))throw new ArgumentException("Файл не выбран.");
   if(!items.Any(x=>String.Equals(x.Path,row.Path,StringComparison.OrdinalIgnoreCase)))items.Add(Copy(row));
  }
  public List<LocalRow> Snapshot() {return items.Select(Copy).ToList();}
  public void Remove(string path) {items.RemoveAll(x=>String.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase));}
  public void Clear() {items.Clear();}
  public void Reconcile(IEnumerable<LocalRow> files) {
   var available=files.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);
   for(int i=items.Count-1;i>=0;i--) {
    LocalRow row;if(!available.TryGetValue(items[i].Path,out row))items.RemoveAt(i);else items[i]=Copy(row);
   }
  }
 }
 public static class JsonCodec {
  static JavaScriptSerializer Serializer {get {return new JavaScriptSerializer {MaxJsonLength=16000000};}}
  public static string Serialize(object value) {return Serializer.Serialize(value);}
  public static T Deserialize<T>(string value) {return Serializer.Deserialize<T>(value);}
  public static object DeserializeObject(string value) {return Serializer.DeserializeObject(value);}
 }
 public class BufferedJournal {
  readonly Queue<string> pending=new Queue<string>(); readonly object gate=new object(); int skipped;
  public void Add(string text) {
   if(text==null)return;
   string line=DateTime.Now.ToString("HH:mm:ss")+"  "+(text.Length>2000?text.Substring(0,2000)+" …":text)+Environment.NewLine;
   lock(gate) {if(pending.Count>=500){pending.Dequeue();skipped++;}pending.Enqueue(line);}
  }
  public string Drain() {
   lock(gate) {
    var result=new StringBuilder();
    if(skipped>0){result.AppendLine("Слишком частый вывод: пропущено строк "+skipped+".");skipped=0;}
    while(pending.Count>0&&result.Length<6000)result.Append(pending.Dequeue());
    return result.ToString();
   }
  }
 }
 public class Engine {
  public readonly string Root, Downloads;
  public State Settings;
  public Action<string> Log = delegate {};
  public Action<double> Progress = delegate {};
  public Process Active;
  static readonly HttpClient Http = CreateHttp();
  static HttpClient CreateHttp() {
   ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
   var h = new HttpClient(); h.Timeout = TimeSpan.FromMinutes(15);
   h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/0.1.10"); return h;
  }
  public Engine(string root) {
   Root = root; Downloads = Path.Combine(Root,"Apps"); Directory.CreateDirectory(Downloads);
   var f = Path.Combine(root,"state.json");
   Settings = File.Exists(f) ? JsonCodec.Deserialize<State>(File.ReadAllText(f)) : new State();
   if(Settings == null) throw new IOException("Не удалось прочитать настройки.");
  }
  public void Save() {
   var f=Path.Combine(Root,"state.json"); var t=f+".tmp";
   File.WriteAllText(t,JsonCodec.Serialize(Settings),Encoding.UTF8);
   if(File.Exists(f)) File.Replace(t,f,null); else File.Move(t,f);
  }
  public string CurrentRoot {
   get {
    if(String.IsNullOrEmpty(Settings.Current)) throw new Exception("Сначала установите движок: кнопка «Проверить обновления».");
    var p=Path.GetFullPath(Path.Combine(Root,"Versions",Settings.Current));
    var prefix=Path.GetFullPath(Path.Combine(Root,"Versions"))+Path.DirectorySeparatorChar;
    if(!p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new Exception("Некорректный путь движка.");
    return p;
   }
  }
  public string Binary(string pattern) {
   var files=Directory.GetFiles(CurrentRoot,pattern,SearchOption.AllDirectories).Where(x=>x.Replace('\\','/').Contains("/windows_amd64/")).ToArray();
   if(files.Length!=1) throw new Exception("Не найдена однозначная Windows-утилита: "+pattern);
   return files[0];
  }
  // Windows CommandLineToArgvW-compatible quoting; never invoke cmd.exe.
  public static string Quote(string s) {
   var b=new StringBuilder("\""); int n=0;
   foreach(char c in s) { if(c=='\\') {n++;continue;} if(c=='\"') {b.Append('\\',n*2+1);b.Append(c);} else {b.Append('\\',n);b.Append(c);} n=0; }
   b.Append('\\',n*2); return b.Append('"').ToString();
  }
  public Task<Result> Run(string exe,string[] args,CancellationToken token,bool visible,bool publish) {
   return Task.Run(delegate {
    token.ThrowIfCancellationRequested();
    var si=new ProcessStartInfo(exe,String.Join(" ",args.Select(Quote))) {WorkingDirectory=Downloads,UseShellExecute=false,CreateNoWindow=!visible};
    si.EnvironmentVariables["PATH"]=Path.GetDirectoryName(exe)+";"+Environment.GetEnvironmentVariable("PATH");
    if(!visible){si.RedirectStandardInput=true;si.RedirectStandardOutput=true;si.RedirectStandardError=true;si.StandardOutputEncoding=Encoding.UTF8;si.StandardErrorEncoding=Encoding.UTF8;}
    using(var p=new Process {StartInfo=si}) {
     var output=new StringBuilder();object gate=new object();
     bool stdoutDone=visible,stderrDone=visible;long lastOutput=Stopwatch.GetTimestamp();
     DataReceivedEventHandler stdout=delegate(object sender,DataReceivedEventArgs e) {
      lock(gate) {
       if(e.Data==null){stdoutDone=true;return;}
       lastOutput=Stopwatch.GetTimestamp();int count=Math.Min(e.Data.Length,15000000-output.Length);
       if(count>0){output.Append(e.Data,0,count);if(output.Length<15000000)output.AppendLine();}
      }
      if(publish)Log(e.Data);
     };
     DataReceivedEventHandler stderr=delegate(object sender,DataReceivedEventArgs e) {
      if(e.Data==null){lock(gate){stderrDone=true;}return;}stdout(sender,e);
     };
     if(!visible){p.OutputDataReceived+=stdout;p.ErrorDataReceived+=stderr;}
     var elapsed=Stopwatch.StartNew();long nextNotice=30;
     try {
      p.Start();Active=p;
      if(!visible){p.StandardInput.Close();p.BeginOutputReadLine();p.BeginErrorReadLine();}
      while(!p.WaitForExit(200)) {
       if(token.IsCancellationRequested) {
        try {p.Kill();}catch(InvalidOperationException){}
        if(!p.WaitForExit(5000))throw new IOException("Не удалось остановить процесс. Завершите его через Диспетчер задач.");
        token.ThrowIfCancellationRequested();
       }
       if(publish&&elapsed.Elapsed.TotalSeconds>=nextNotice) {
        long last;lock(gate){last=lastOutput;}
        Log("Процесс работает "+(int)elapsed.Elapsed.TotalSeconds+" с; без нового вывода "+(int)((Stopwatch.GetTimestamp()-last)/(double)Stopwatch.Frequency)+" с. При необходимости нажмите «Отмена».");
        nextNotice+=30;
       }
      }
      token.ThrowIfCancellationRequested();
      // A child may keep a redirected pipe open after its parent exits.
      // Never use an unbounded WaitForExit() to drain those pipes.
      var drain=Stopwatch.StartNew();
      while(drain.ElapsedMilliseconds<2000) {
       lock(gate){if(stdoutDone&&stderrDone)break;}
       token.ThrowIfCancellationRequested();Thread.Sleep(20);
      }
      lock(gate) {
       if(!stdoutDone||!stderrDone)Log("Процесс завершился, но поток вывода не закрылся. Ожидание потока прекращено.");
       return new Result{Code=p.ExitCode,Output=output.ToString()};
      }
     } finally {Active=null;}
    }
   },token);
  }
  public Task<Result> Tool(string[] args,CancellationToken ct,bool publish) {
   return Task.Run(async delegate {return await Run(Binary("ipatool*.exe"),args.Concat(new[]{"--format","json"}).ToArray(),ct,false,publish);},ct);
  }
  public static void Ensure(Result r) {if(r.Code!=0) throw new Exception("Код ошибки "+r.Code+"\n"+(r.Output.Length>8000?r.Output.Substring(r.Output.Length-8000):r.Output));}
  public static Dictionary<string,object> Object(string output) {
   // Structured output is sometimes preceded by a human-readable diagnostic.
   foreach(var line in output.Split('\n').Reverse()) {try {var d=JsonCodec.DeserializeObject(line.Trim()) as Dictionary<string,object>;if(d!=null)return d;}catch(ArgumentException){}}
   try {return JsonCodec.DeserializeObject(output.Trim()) as Dictionary<string,object> ?? new Dictionary<string,object>();}
   catch(Exception) {throw new Exception("Формат ответа движка изменился. См. журнал или выполните откат.");}
  }
  public static object Get(Dictionary<string,object> d,string key) {return d.Where(k=>String.Equals(k.Key,key,StringComparison.OrdinalIgnoreCase)).Select(k=>k.Value).FirstOrDefault();}
  public static List<string> VersionIds(string text) {
   var values=Get(Object(text),"externalVersionIdentifiers") as object[];
   if(values==null)throw new Exception("Формат списка версий не поддерживается.");
   return values.Select(Convert.ToString).Where(x=>x.Length>0&&x.All(Char.IsDigit)).ToList();
  }
  public static List<AppRow> ParseApps(string text) {
   var d=Object(text); var values=Get(d,"apps") as object[];
   if(values==null) throw new Exception("В ответе отсутствует список apps. Проверьте совместимость движка.");
   return values.OfType<Dictionary<string,object>>().Select(x=>new AppRow {Name=Convert.ToString(Get(x,"name")),ID=Convert.ToString(Get(x,"id"))}).Where(x=>x.ID.All(Char.IsDigit)&&x.ID.Length>0).ToList();
  }
  bool catalogChecked;
  public static List<AppRow> ParseCatalog(string text) {
   var rows=new List<AppRow>();
   foreach(var line in text.Split('\n')) {
    var m=System.Text.RegularExpressions.Regex.Match(line.TrimEnd('\r'),@"^(.*):\s*(\d+)(?:[ \t]+(\d{1,2}\.\d{1,2}\.\d{4}))?\s*$");
    if(!m.Success)m=System.Text.RegularExpressions.Regex.Match(line,@"^(.*?):\s*(\d+)");
    if(m.Success)rows.Add(new AppRow {Name=m.Groups[1].Value.Trim(),ID=m.Groups[2].Value});
   }
   return rows.GroupBy(x=>x.ID).Select(x=>x.First()).ToList();
  }
  public static List<AppRow> MatchCatalog(string query,List<AppRow> custom,List<AppRow> catalog) {
   var ids=new HashSet<string>(custom.Select(x=>x.ID));
   return custom.Concat(catalog.Where(x=>!ids.Contains(x.ID))).Where(x=>x.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).GroupBy(x=>x.ID).Select(x=>x.First()).ToList();
  }
  public static List<AppRow> MergeApps(IEnumerable<AppRow> local,IEnumerable<AppRow> online) {
   return local.Concat(online).GroupBy(x=>x.ID).Select(x=>x.First()).ToList();
  }
  public async Task<List<AppRow>> CatalogSearch(string query,CancellationToken ct) {
   var folder=Path.Combine(Root,"Files");Directory.CreateDirectory(folder);
   var cache=Path.Combine(folder,"AppsList.txt");
   if(!File.Exists(cache)) {
    var bundled=Directory.GetFiles(CurrentRoot,"AppsList.txt",SearchOption.AllDirectories).FirstOrDefault();
    if(bundled!=null)File.Copy(bundled,cache);
   }
   if(!catalogChecked) {
    try {
     using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
      timeout.CancelAfter(TimeSpan.FromSeconds(5));
      using(var response=await Http.GetAsync("https://raw.githubusercontent.com/kda2495/IPA_Downloader/refs/heads/main/Files/AppsList.txt",timeout.Token)) {
       response.EnsureSuccessStatusCode();var content=await response.Content.ReadAsStringAsync();
       if(content.Length>2000000||ParseCatalog(content).Count==0)throw new IOException("Неожиданный формат каталога.");
       File.WriteAllText(cache+".tmp",content,Encoding.UTF8);
       if(File.Exists(cache))File.Replace(cache+".tmp",cache,null);else File.Move(cache+".tmp",cache);
       Log("Каталог приложений обновлён из исходного репозитория.");
      }
     }
    } catch(OperationCanceledException) {ct.ThrowIfCancellationRequested();Log("Каталог: таймаут обновления, используется сохранённая копия.");}
      catch(Exception e) {Log("Каталог: используется сохранённая копия. "+e.Message);}
    catalogChecked=true;
   }
   var customPath=Path.Combine(folder,"AppsListCustom.txt");
   var custom=File.Exists(customPath)?ParseCatalog(File.ReadAllText(customPath,Encoding.UTF8)):new List<AppRow>();
   var catalog=File.Exists(cache)?ParseCatalog(File.ReadAllText(cache,Encoding.UTF8)):new List<AppRow>();
   return MatchCatalog(query,custom,catalog);
  }
  public async Task<Release> Check(CancellationToken ct) {
   using(var response=await Http.GetAsync("https://api.github.com/repos/kda2495/IPA_Downloader/releases/latest",ct)) {
    response.EnsureSuccessStatusCode(); return JsonCodec.Deserialize<Release>(await response.Content.ReadAsStringAsync());
   }
  }
  public static void Extract(string zip,string dest) {
   Directory.CreateDirectory(dest); var prefix=Path.GetFullPath(dest)+Path.DirectorySeparatorChar;
   using(var z=ZipFile.OpenRead(zip)) {
    if(z.Entries.Count>30000 || z.Entries.Sum(x=>x.Length)>2000000000L) throw new IOException("Архив превышает допустимый размер.");
    foreach(var e in z.Entries) {
     var p=Path.GetFullPath(Path.Combine(dest,e.FullName.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar)));
     if(!p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("Небезопасный путь в ZIP.");
     if(e.FullName.EndsWith("/")||e.FullName.EndsWith("\\")) {Directory.CreateDirectory(p);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(p));e.ExtractToFile(p,false);
    }
   }
  }
  public async Task Install(Release rel,CancellationToken ct) {
   var candidates=(rel.assets??new Asset[0]).Where(a=>a.name.StartsWith("IPA_Downloader",StringComparison.OrdinalIgnoreCase)&&a.name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)).ToArray();
   if(candidates.Length!=1) throw new Exception("Структура релиза изменилась: ожидается один ZIP IPA_Downloader.");
   var asset=candidates[0];
   if(!asset.browser_download_url.StartsWith("https://github.com/kda2495/IPA_Downloader/releases/download/",StringComparison.Ordinal)) throw new Exception("Неожиданный источник архива.");
   if(asset.size<=0||asset.size>1000000000) throw new Exception("Некорректный размер архива.");
   var name=Guid.NewGuid().ToString("N"); var staging=Path.Combine(Root,"Versions",name); var zip=Path.Combine(Root,name+".zip");
   bool committed=false;
   try {
    Log("Загрузка релиза "+rel.tag_name);
    using(var response=await Http.GetAsync(asset.browser_download_url,HttpCompletionOption.ResponseHeadersRead,ct)) {
     response.EnsureSuccessStatusCode();
     using(var input=await response.Content.ReadAsStreamAsync()) using(var output=File.Create(zip)) {
      var buf=new byte[131072];int n;long total=0;
      while((n=await input.ReadAsync(buf,0,buf.Length,ct))>0) {total+=n;if(total>asset.size)throw new IOException("Размер архива не совпадает.");await output.WriteAsync(buf,0,n,ct);Progress(100.0*total/asset.size);}
      if(total!=asset.size)throw new IOException("Архив загружен не полностью.");
     }
    }
    if(!String.IsNullOrEmpty(asset.digest)) {
     if(!asset.digest.StartsWith("sha256:")) throw new IOException("Неизвестный формат контрольной суммы.");
     using(var sha=SHA256.Create())using(var f=File.OpenRead(zip)) {
      var digest=BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();
      if(!String.Equals("sha256:"+digest,asset.digest,StringComparison.OrdinalIgnoreCase))throw new IOException("Контрольная сумма не совпадает.");
     }
    } else Log("GitHub не предоставил SHA-256; проверены HTTPS-источник и размер архива.");
    ct.ThrowIfCancellationRequested();Log("Распаковка и проверка команд…");
    await Task.Run(delegate {Extract(zip,staging);},ct);
    var bins=Directory.GetFiles(staging,"ipatool*.exe",SearchOption.AllDirectories).Where(x=>x.Replace('\\','/').Contains("/windows_amd64/")).ToArray();
    if(bins.Length!=1)throw new IOException("В архиве нет ожидаемого Windows-движка.");
    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
     timeout.CancelAfter(TimeSpan.FromSeconds(20));
     var test=await Run(bins[0],new string[0],timeout.Token,false,false);Ensure(test);
     foreach(var command in new[]{"auth","search","download","list-versions","get-version-metadata","--format","--external-version-id"})
      if(!test.Output.Contains(command))throw new IOException("Релиз несовместим с адаптером: нет "+command);
    }
    var previous=Settings; Settings=new State {Current=name,Version=rel.tag_name,Previous=previous.Current,PreviousVersion=previous.Version,AutoUpdate=previous.AutoUpdate};
    try {Save();committed=true;} catch {Settings=previous;throw;}
    Log("Установлен движок "+rel.tag_name+". Предыдущая версия сохранена.");
   } finally {if(File.Exists(zip))File.Delete(zip);if(!committed&&Directory.Exists(staging))Directory.Delete(staging,true);}
  }
  public void Rollback() {
   if(String.IsNullOrEmpty(Settings.Previous)||!Directory.Exists(Path.Combine(Root,"Versions",Settings.Previous)))throw new Exception("Предыдущей версии пока нет.");
   var old=Settings;Settings=new State {Current=old.Previous,Version=old.PreviousVersion,Previous=old.Current,PreviousVersion=old.Version,AutoUpdate=false};
   try {Save();}catch {Settings=old;throw;}
   Log("Откат выполнен. Автообновление отключено, чтобы не вернуть проблемный релиз.");
  }
 }
}
