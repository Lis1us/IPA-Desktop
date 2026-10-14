// Generated from Source/. Edit the source classes, then run Sync-Sources.ps1.
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows;
using System;

// Presentation/AccountController.cs
namespace IpaDesktop
{
    public sealed class AccountController : WindowController
    {
        bool signedIn;
        public AccountController(Desktop view, Engine engine) : base(view, engine)
        {
            On("Login", delegate
            {
                Work("Вход: завершите авторизацию в открывшемся окне", async delegate(CancellationToken ct)
                {
                    var r = await engine.Run(engine.Binary("ipatool*.exe"), new[]
                    {
                        "auth", "login"
                    }, ct, true, false);
                    Engine.Ensure(r);
                    await Account(ct);
                });
            });
            On("AccountInfo", delegate
            {
                Work("Проверка аккаунта…", Account);
            });
            On("Logout", delegate
            {
                if (MessageBox.Show(w, "Выйти из текущей сессии ipatool? Это затронет и исходный скрипт.", "Выход", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Work("Выход…", async delegate(CancellationToken ct)
                {
                    Engine.Ensure(await engine.Tool(new[]
                    {
                        "auth", "revoke"
                    }, ct, false));
                    signedIn = false;
                    C<TextBlock>("AccountLabel").Text = "Вход не выполнен";
                });
            });
        }
        public override void RefreshActions()
        {
            C<Button>("Login").IsEnabled = !busy && !signedIn;
            C<Button>("Logout").IsEnabled = !busy && signedIn;
        }
        public async Task Account(CancellationToken ct)
        {
            var r = await engine.Tool(new[]
            {
                "auth", "info"
            }, ct, false);
            signedIn = false;
            C<TextBlock>("AccountLabel").Text = "Вход не выполнен";
            Engine.Ensure(r);
            var data = Engine.Object(r.Output);
            var email = Convert.ToString(Engine.Get(data, "email"));
            if (String.IsNullOrWhiteSpace(email) || !Object.Equals(Engine.Get(data, "success"), true)) throw new Exception("Не удалось подтвердить сохранённый вход в аккаунт.");
            signedIn = true;
            C<TextBlock>("AccountLabel").Text = email;
            Log("Аккаунт проверен.");
        }
    }
}

// Presentation/Desktop.cs
namespace IpaDesktop
{
    public sealed class Desktop
    {
        Window w;
        Engine engine;
        CancellationTokenSource cancellation;
        bool busy;
        bool setting;
        bool exitingForUpdate;
        long lastProgress;
        AccountController account;
        SearchController search;
        FilesController files;
        UpdatesController updates;
        public Window Window
        {
            get
            {
                return w;
            }
        }
        internal bool IsBusy
        {
            get
            {
                return busy;
            }
        }
        internal bool IsUpdatingSettings
        {
            get
            {
                return setting;
            }
        }
        internal void RequestRestart()
        {
            exitingForUpdate = true;
        }
        internal void RefreshFiles()
        {
            if (files != null) files.RefreshFiles();
        }
        internal void ApplyActions()
        {
            if (account != null) account.RefreshActions();
            if (search != null) search.RefreshActions();
            if (files != null) files.RefreshActions();
            if (updates != null) updates.RefreshActions();
        }
        internal T C<T>(string name) where T : class
        {
            return w.FindName(name) as T;
        }
        internal void On(string name, Action action)
        {
            C<Button>(name).Click += delegate
            {
                try
                {
                    action();
                } catch (Exception e)
                {
                    Error(e);
                }
            };
        }
        readonly BufferedJournal journalBuffer = new BufferedJournal();
        System.Windows.Threading.DispatcherTimer journalTimer;
        readonly QueueProgress queueProgress = new QueueProgress();
        internal void BeginQueue(string kind, int index, int total, string app)
        {
            queueProgress.Begin(kind, index, total, app);
            C<TextBlock>("Status").Text = kind + " " + index + "/" + total + ": " + app;
            RenderQueue();
        }
        internal void CompleteQueue(int success, int failed) { queueProgress.Complete(success, failed); RenderQueue(); }
        void RenderQueue()
        {
            C<TextBlock>("OperationDetail").Text = queueProgress.Text;
            C<TextBlock>("OperationDetail").ToolTip = queueProgress.Text;
            if (!queueProgress.Active) return;
            C<ProgressBar>("Progress").IsIndeterminate = busy && queueProgress.Indeterminate;
            C<ProgressBar>("Progress").Value = queueProgress.Value;
        }
        internal void Log(string text)
        {
            if (queueProgress.Feed(text)) return;
            journalBuffer.Add(text);
        }
        void FlushJournal()
        {
            JournalView.Flush(C<TextBox>("Journal"), journalBuffer, false);
        }
        internal void Error(Exception e)
        {
            Log(e.Message);
            C<TextBlock>("Status").Text = "Ошибка — подробности в журнале";
            MessageBox.Show(w, e.Message, "IPA Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        internal void StateView()
        {
            C<TextBlock>("EngineVersion").Text = "Движок: " + (engine.Settings.Version ?? "не установлен");
            setting = true;
            C<CheckBox>("AutoUpdate").IsChecked = engine.Settings.AutoUpdate;
            C<CheckBox>("AutoUpdateDeviceTools").IsChecked = engine.Settings.AutoUpdateDeviceTools;
            C<TextBlock>("DeviceToolsVersion").Text = (engine.Settings.DeviceSource == EngineUpdater.DeviceRepository ? "Наш комплект: " : "Прежний комплект: ") + (engine.Settings.DeviceVersion ?? (engine.Settings.Source == null ? engine.Settings.Version : null) ?? "не установлен");
            setting = false;
        }
        internal async void Work(string title, Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true;
            queueProgress.Reset();
            C<TextBlock>("OperationDetail").Text = "";
            cancellation = new CancellationTokenSource();
            string[] buttons =
            {
                "Login", "AccountInfo", "Logout", "Search", "Versions", "VersionInfo", "Download", "RefreshFiles", "ImportFile", "DeleteFiles", "Devices", "InstallFiles", "CheckUpdate", "Rollback", "CheckGuiUpdate", "InstallGuiUpdate", "AddChosen", "ClearChosen", "AddInstall", "ClearInstall", "CheckAppleSupport", "CheckDeviceUpdate", "RollbackDevices", "RetryDownloads", "ForgetDownloadErrors", "RetryInstalls", "ForgetInstallErrors", "SaveSet", "UseSet", "DeleteSet", "SelectOld", "DeleteStorage", "RefreshStorage"
            };
            foreach (var name in buttons) C<Button>(name).IsEnabled = false;
            C<ItemsControl>("ChosenFiles").IsEnabled = false;
            C<ItemsControl>("ChosenApps").IsEnabled = false;
            C<CheckBox>("CheckGuiUpdates").IsEnabled = false;
            C<CheckBox>("AutoUpdate").IsEnabled = false;
            C<CheckBox>("AutoUpdateDeviceTools").IsEnabled = false;
            C<Button>("Cancel").IsEnabled = true;
            C<ProgressBar>("Progress").IsIndeterminate = true;
            C<TextBlock>("Status").Text = title;
            Log(title);
            try
            {
                await action(cancellation.Token);
                C<TextBlock>("Status").Text = "Готово";
            } catch (OperationCanceledException)
            {
                Log("Операция отменена.");
                C<TextBlock>("Status").Text = "Отменено";
            } catch (Exception e)
            {
                Error(e);
            } finally
            {
                busy = false;
                cancellation.Dispose();
                cancellation = null;
                foreach (var name in buttons) C<Button>(name).IsEnabled = true;
                C<CheckBox>("CheckGuiUpdates").IsEnabled = true;
                C<CheckBox>("AutoUpdate").IsEnabled = true;
                C<CheckBox>("AutoUpdateDeviceTools").IsEnabled = true;
                C<Button>("Cancel").IsEnabled = false;
                C<ProgressBar>("Progress").IsIndeterminate = false;
                C<ProgressBar>("Progress").Value = 0;
                RenderQueue();
                StateView();
                RefreshFiles();
                ApplyActions();
                if (exitingForUpdate) w.Close();
            }
        }
        public Desktop() : this(null, true)
        {
        }
        public Desktop(Engine backend, bool checkAtStartup) : this(backend, checkAtStartup, AppDomain.CurrentDomain.BaseDirectory)
        {
        }
        public Desktop(Engine backend, bool checkAtStartup, string assetDirectory)
        {
            InstallationLayout.Ensure(assetDirectory);
            using (var stream = File.OpenRead(Path.Combine(assetDirectory, "MainWindow.xaml"))) w = (Window) XamlReader.Load(stream);
            journalTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, w.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            JournalView.Attach(C<TextBox>("Journal"), journalBuffer);
            journalTimer.Tick += delegate
            {
                FlushJournal();
                if (busy) RenderQueue();
            };
            journalTimer.Start();
            w.Closed += delegate
            {
                journalTimer.Stop();
            };
            w.SourceInitialized += delegate
            {
                WindowTheme.Apply(w);
            };
            w.Activated += delegate
            {
                WindowTheme.Apply(w);
            };
            using (var icon = File.OpenRead(Path.Combine(assetDirectory, "Assets", "App.ico")))
            {
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(icon, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                w.Icon = decoder.Frames[0];
                C<Image>("BrandIcon").Source = decoder.Frames[decoder.Frames.Count - 1];
            }
            engine = backend ?? new Engine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPA-Desktop"));
            engine.Log = Log;
            engine.Progress = delegate(double value)
            {
                long now = Stopwatch.GetTimestamp();
                if (value < 100 && (now - lastProgress) / (double) Stopwatch.Frequency < 0.15) return;
                lastProgress = now;
                w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    C < ProgressBar >("Progress").IsIndeterminate = false;
                    C < ProgressBar >("Progress").Value = value;
                }));
            };
            account = new AccountController(this, engine);
            search = new SearchController(this, engine);
            files = new FilesController(this, engine);
            updates = new UpdatesController(this, engine);
            StateView();
            RefreshFiles();
            On("Cancel", delegate
            {
                if (cancellation != null) cancellation.Cancel();
            });
            On("ExportDiagnostics", delegate
            {
                JournalView.Flush(C<TextBox>("Journal"), journalBuffer, true);
                var privateValues = new List<string> { Environment.UserName };
                var email = C<TextBlock>("AccountLabel").Text;
                if (email.Contains("@")) privateValues.Add(email);
                foreach (var device in C<ComboBox>("DeviceList").Items.Cast<DeviceRow>()) { privateValues.Add(device.ID); privateValues.Add(device.Name); }
                var report = DiagnosticReport.Create(C<TextBox>("Journal").Text, engine.Settings.Version ?? "not installed", engine.Settings.DeviceVersion ?? "not installed", privateValues);
                var dialog = new SaveFileDialog { Filter = "Текстовый отчёт (*.txt)|*.txt", FileName = "IPA-Desktop-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
                if (dialog.ShowDialog(w) != true) return;
                File.WriteAllText(dialog.FileName, report, Encoding.UTF8);
                Log("Отчёт диагностики сохранён.");
            });
            On("OpenFolder", delegate
            {
                Process.Start(new ProcessStartInfo(engine.Downloads)
                {
                    UseShellExecute = true
                });
            });
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (busy)
                {
                    e.Cancel = true;
                    MessageBox.Show(w, "Сначала отмените текущую операцию и дождитесь её завершения.", "Выполняется операция");
                }
            };
            ApplyActions();
            w.Loaded += delegate
            {
                Log("IPA Desktop 0.1.17. Файлы: " + engine.Downloads);
                if (!checkAtStartup) return;
                Work("Проверка движка и сохранённого входа…", async delegate(CancellationToken ct)
                {
                    await files.CheckSupport(ct);
                    if (engine.Settings.CheckGuiUpdates)
                    {
                        try
                        {
                            await updates.CheckGui(ct);
                        } catch (OperationCanceledException)
                        {
                            if (ct.IsCancellationRequested) throw;
                            Log("Проверка IPA Desktop: время ожидания истекло.");
                        } catch (Exception e)
                        {
                            Log("Проверка IPA Desktop: " + e.Message);
                        }
                    }
                    if (engine.Settings.AutoUpdate)
                    {
                        try
                        {
                            await updates.Update(ct, true);
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            Log("Не удалось обновить движок: " + e.Message);
                        }
                    }
                    if (engine.Settings.AutoUpdateDeviceTools)
                    {
                        try { await updates.UpdateDevices(ct, true); }
                        catch (OperationCanceledException)
                        {
                            if (ct.IsCancellationRequested) throw;
                            Log("Проверка инструментов для iPhone: время ожидания истекло.");
                        }
                        catch (Exception e) { Log("Не удалось обновить инструменты для iPhone: " + e.Message); }
                    }
                    if (!String.IsNullOrEmpty(engine.Settings.Current))
                    {
                        try
                        {
                            await account.Account(ct);
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            Log("Проверка входа: " + e.Message);
                        }
                    }
                });
            };
        }
        [STAThread] public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\IPA-Desktop-0.1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("IPA Desktop уже запущен.");
                    return;
                }
                try
                {
                    var app = new Application();
                    var desktop = new Desktop();
                    app.Run(desktop.w);
                } catch (Exception e)
                {
                    MessageBox.Show(e.ToString(), "Ошибка запуска IPA Desktop");
                }
            }
        }
    }
}

// Presentation/FilesController.cs
namespace IpaDesktop
{
    public sealed class FilesController : WindowController
    {
        AppleSupportReport appleSupport;
        readonly InstallationSelection installChosen = new InstallationSelection();
        readonly InstallationSelection failedInstalls = new InstallationSelection();
        List<LocalRow> localRows = new List<LocalRow>();
        public FilesController(Desktop view, Engine engine) : base(view, engine)
        {
            RowClickSelection.Attach(C<DataGrid>("Files"), delegate(object item)
            {
                if (busy) return;
                var row = item as LocalRow;
                if (row == null) return;
                installChosen.Add(row);
                RefreshInstallChosen();
            });
            C<DataGrid>("Files").SelectionChanged += delegate
            {
                ApplyActions();
            };
            C<ComboBox>("DeviceList").SelectionChanged += delegate
            {
                ApplyActions();
            };
            On("AddInstall", delegate
            {
                foreach (var row in C<DataGrid>("Files").SelectedItems.Cast<LocalRow>()) installChosen.Add(row);
                RefreshInstallChosen();
            });
            On("ClearInstall", delegate
            {
                installChosen.Clear();
                RefreshInstallChosen();
            });
            SelectionRemove.Attach(C<ItemsControl>("ChosenFiles"), delegate(string path)
            {
                if (busy) return;
                installChosen.Remove(path);
                RefreshInstallChosen();
            });
            On("RefreshFiles", RefreshFiles);
            On("DeleteFiles", delegate { DeleteRows(C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().ToList()); });
            On("DeleteStorage", delegate { DeleteRows(C<DataGrid>("StorageFiles").SelectedItems.Cast<LocalRow>().ToList()); });
            On("RefreshStorage", RefreshFiles);
            On("SelectOld", delegate
            {
                var grid = C<DataGrid>("StorageFiles"); grid.SelectedItems.Clear();
                foreach (var row in localRows.Where(x => x.IsOld)) grid.SelectedItems.Add(row);
                ApplyActions();
            });
            C<DataGrid>("StorageFiles").SelectionChanged += delegate { ApplyActions(); };
            C<ComboBox>("AppSets").SelectionChanged += delegate { ApplyActions(); };
            On("SaveSet", delegate
            {
                engine.AppSets.Save(C<TextBox>("SetName").Text, installChosen.Snapshot());
                LoadSets();
                C<TextBlock>("SetStatus").Text = "Набор сохранён. IPA-файлы не копируются.";
            });
            On("DeleteSet", delegate
            {
                var set = C<ComboBox>("AppSets").SelectedItem as AppSet;
                if (set == null) return;
                engine.AppSets.Delete(set.Name); LoadSets();
                C<TextBlock>("SetStatus").Text = "Набор удалён. IPA-файлы сохранены.";
            });
            On("UseSet", delegate
            {
                var set = C<ComboBox>("AppSets").SelectedItem as AppSet;
                if (set == null) return;
                Work("Добавление набора…", async delegate(CancellationToken ct)
                {
                    var available = await engine.LocalFiles.ListAsync();
                    ct.ThrowIfCancellationRequested();
                    string[] missing;
                    var rows = engine.AppSets.Resolve(set, available, out missing);
                    foreach (var row in rows) installChosen.Add(row);
                    RefreshInstallChosen();
                    C<TextBlock>("SetStatus").Text = "Добавлено IPA: " + rows.Count + (missing.Length > 0 ? " · отсутствуют: " + String.Join(", ", missing) : "");
                    Log("Набор «" + set.Name + "»: добавлено " + rows.Count + ", отсутствует " + missing.Length);
                    if (missing.Length > 0) Log("Отсутствующие IPA: " + String.Join(", ", missing));
                    if (rows.Count > 0) C<TabItem>("FilesTab").IsSelected = true;
                });
            });
            LoadSets();
            On("ImportFile", delegate
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "IPA files (*.ipa)|*.ipa", Multiselect = true
                };
                if (dialog.ShowDialog(w) != true) return;
                var paths = dialog.FileNames;
                Work("Добавление IPA…", ct => engine.LocalFiles.ImportAsync(paths, ct));
            });
            On("CheckAppleSupport", delegate
            {
                Work("Проверка компонентов Apple…", CheckSupport);
            });
            On("OpenAppleSupportHelp", delegate
            {
                string url = appleSupport == null ? "https://support.apple.com/ru-ru/118290" : appleSupport.HelpUrl;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            });
            On("Devices", delegate
            {
                Work("Поиск iPhone…", async delegate(CancellationToken ct)
                {
                    await CheckSupport(ct);
                    var combo = C<ComboBox>("DeviceList");
                    combo.ItemsSource = null;
                    ApplyActions();
                    var devices = await engine.Devices.FindAsync(ct);
                    combo.ItemsSource = devices;
                    if (devices.Count > 0)
                    {
                        combo.SelectedIndex = 0;
                        Log("Найдено: " + String.Join(", ", devices.Select(x => x.Name)));
                    } else Log("iPhone не обнаружен. Проверьте USB, доверие и драйвер Apple.");
                    ApplyActions();
                });
            });
            On("InstallFiles", delegate { StartInstall(installChosen.Snapshot()); });
            On("RetryInstalls", delegate { StartInstall(failedInstalls.Snapshot()); });
            On("ForgetInstallErrors", delegate { failedInstalls.Clear(); ApplyActions(); });
        }
        void LoadSets()
        {
            var combo = C<ComboBox>("AppSets");
            var previous = combo.SelectedItem as AppSet;
            try
            {
                var sets = engine.AppSets.Load(); combo.ItemsSource = sets;
                combo.SelectedItem = sets.FirstOrDefault(x => previous != null && x.Name == previous.Name) ?? sets.FirstOrDefault();
            }
            catch (Exception e) { C<TextBlock>("SetStatus").Text = e.Message; Log("Наборы: " + e.Message); }
        }
        void DeleteRows(List<LocalRow> rows)
        {
            if (busy || rows.Count == 0) return;
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(x => x.Name));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (MessageBox.Show(w, "Переместить в корзину выбранные IPA (" + rows.Count + ")?" + Environment.NewLine + Environment.NewLine + summary + Environment.NewLine + Environment.NewLine + "Приложения на iPhone останутся установленными.", "Удаление IPA", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Work("Перемещение IPA в корзину…", async delegate(CancellationToken ct)
            {
                var failures = new List<string>(); int removed = 0;
                await Task.Run(delegate
                {
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        try { engine.LocalFiles.Delete(row.Path); removed++; Log("Перемещено в корзину: " + row.Name); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e) { failures.Add(row.Name + ": " + e.Message); }
                    }
                }, ct);
                Log("Удалено из папки IPA: " + removed);
                if (failures.Count > 0) throw new IOException("Не удалось удалить:" + Environment.NewLine + String.Join(Environment.NewLine, failures));
            });
        }
        void StartInstall(List<LocalRow> rows)
        {
            var device = C<ComboBox>("DeviceList").SelectedItem as DeviceRow;
            if (device == null || rows.Count == 0) throw new Exception("Выберите устройство и IPA для установки.");
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(x => x.Name));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (MessageBox.Show(w, "Установить IPA (" + rows.Count + ") на " + device.Name + "?" + Environment.NewLine + Environment.NewLine + summary, "Установка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Work("Установка IPA…", async delegate(CancellationToken ct)
            {
                await CheckSupport(ct);
                int index = 0, success = 0;
                var failures = new List<string>();
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    view.BeginQueue("Установка", ++index, rows.Count, row.Name);
                    Log("Установка " + row.Name);
                    try
                    {
                        await engine.Devices.InstallAsync(device, row, ct);
                        installChosen.Remove(row.Path); failedInstalls.Remove(row.Path); success++;
                        RefreshInstallChosen();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        failedInstalls.Add(row); failures.Add(row.Name);
                        Log("Ошибка установки " + row.Name + ": " + e.Message);
                    }
                }
                view.CompleteQueue(success, failures.Count);
                if (failures.Count > 0) throw new Exception("Не удалось установить: " + String.Join(", ", failures) + ". Кнопка «Повторить ошибки» повторит только эти IPA.");
            });
        }
        public async Task CheckSupport(CancellationToken cancellation)
        {
            appleSupport = await engine.AppleSupport.CheckAsync(cancellation);
            var label = C<TextBlock>("AppleSupportStatus");
            label.Text = appleSupport.Message;
            label.ToolTip = appleSupport.Details;
            string color = appleSupport.Status == AppleSupportStatus.Running ? "#81D4AD" :
                appleSupport.Status == AppleSupportStatus.NotDetected ? "#F2A0A0" : "#E8C382";
            label.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            Log(appleSupport.Message + ". " + appleSupport.Details);
        }

        public override void RefreshActions()
        {
            C<Button>("RetryInstalls").IsEnabled = !busy && failedInstalls.Count > 0 && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("RetryInstalls").Content = "Повторить ошибки (" + failedInstalls.Count + ")";
            C<Button>("RetryInstalls").ToolTip = String.Join(Environment.NewLine, failedInstalls.Snapshot().Select(x => x.Name));
            C<Button>("ForgetInstallErrors").IsEnabled = !busy && failedInstalls.Count > 0;
            C<Button>("SaveSet").IsEnabled = !busy && installChosen.Count > 0;
            C<Button>("UseSet").IsEnabled = !busy && C<ComboBox>("AppSets").SelectedItem != null;
            C<Button>("DeleteSet").IsEnabled = !busy && C<ComboBox>("AppSets").SelectedItem != null;
            C<Button>("SelectOld").IsEnabled = !busy && localRows.Any(x => x.IsOld);
            C<Button>("DeleteStorage").IsEnabled = !busy && C<DataGrid>("StorageFiles").SelectedItems.Count > 0;
            C<ItemsControl>("ChosenFiles").IsEnabled = !busy;
            C<Button>("AddInstall").IsEnabled = !busy && C<DataGrid>("Files").SelectedItems.Count > 0;
            C<Button>("ClearInstall").IsEnabled = !busy && installChosen.Count > 0;
            C<Button>("InstallFiles").IsEnabled = !busy && installChosen.Count > 0 && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("Devices").Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color) System.Windows.Media.ColorConverter.ConvertFromString(C<ComboBox>("DeviceList").Items.Count == 0 ? "#16704C" : "#243247"));
        }
        void RefreshInstallChosen()
        {
            C<ItemsControl>("ChosenFiles").ItemsSource = installChosen.Snapshot();
            C<TextBlock>("InstallCount").Text = installChosen.Count == 0 ? "ЛКМ по файлу — добавить к установке" : "К установке: " + installChosen.Count;
            C<Button>("InstallFiles").Content = "Установить (" + installChosen.Count + ")";
            ApplyActions();
        }
        int filesGeneration;
        public async void RefreshFiles()
        {
            int generation = ++filesGeneration;
            var storageSelected = new HashSet<string>(C<DataGrid>("StorageFiles").SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await engine.LocalFiles.ListAsync();
                if (generation != filesGeneration) return;
                localRows = rows;
                C<DataGrid>("Files").ItemsSource = rows;
                C<DataGrid>("StorageFiles").ItemsSource = rows;
                foreach (var row in rows) if (storageSelected.Contains(row.Path)) C<DataGrid>("StorageFiles").SelectedItems.Add(row);
                C<TextBlock>("StorageSummary").Text = "IPA: " + rows.Count + " · занято: " + StorageAnalysis.Bytes(rows.Sum(x => x.Bytes)) + " · старых версий: " + rows.Count(x => x.IsOld) + " (" + StorageAnalysis.Bytes(rows.Where(x => x.IsOld).Sum(x => x.Bytes)) + ")";

                foreach (var row in rows) if (selected.Contains(row.Path)) C<DataGrid>("Files").SelectedItems.Add(row);
                installChosen.Reconcile(rows);
                RefreshInstallChosen();
            } catch (Exception e)
            {
                if (generation == filesGeneration) Log("Не удалось обновить список IPA: " + e.Message);
            }
        }
    }
}

// Presentation/JournalView.cs
namespace IpaDesktop
{
    public static class JournalView
    {
        public static void Attach(TextBox journal, BufferedJournal buffer)
        {
            journal.IsVisibleChanged += delegate
            {
                if (!journal.IsVisible) return;
                journal.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate
                {
                    Flush(journal, buffer, true);
                    journal.ScrollToEnd();
                }));
            };
        }
        public static void Flush(TextBox journal, BufferedJournal buffer, bool all)
        {
            var text = new StringBuilder();
            string chunk;
            do
            {
                chunk = buffer.Drain();
                text.Append(chunk);
            }
            while (all && chunk.Length > 0);
            if (text.Length == 0) return;
            if (text.Length > 120000) text.Remove(0, text.Length - 120000);
            if (journal.Text.Length > 120000) journal.Text = journal.Text.Substring(journal.Text.Length - 80000);
            journal.AppendText(text.ToString());
            if (journal.IsVisible) journal.ScrollToEnd();
        }
    }
}

// Presentation/RowClickSelection.cs
namespace IpaDesktop
{
    public static class RowClickSelection
    {
        public static void Attach(DataGrid grid, Action<object> add)
        {
            object pressed = null;
            Point origin = new Point();
            grid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler(delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
                pressed = row == null ? null : row.Item;
                origin = e.GetPosition(grid);
            }), true);
            grid.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler(delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
                var distance = e.GetPosition(grid) - origin;
                if (row != null && Object.ReferenceEquals(pressed, row.Item) && Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) add(row.Item);
                pressed = null;
            }), true);
        }
    }
}

// Presentation/SearchController.cs
namespace IpaDesktop
{
    public sealed class SearchController : WindowController
    {
        readonly DownloadSelection chosen = new DownloadSelection();
        readonly SearchSession searchSession = new SearchSession();
        readonly DownloadSelection failedDownloads = new DownloadSelection();
        string versionApp;
        public SearchController(Desktop view, Engine engine) : base(view, engine)
        {
            C<ComboBox>("VersionList").Items.Add("Последняя версия");
            C<ComboBox>("VersionList").SelectedIndex = 0;
            C<TextBox>("Query").TextChanged += delegate
            {
                searchSession.Change(C<TextBox>("Query").Text);
                if (String.IsNullOrWhiteSpace(C<TextBox>("Query").Text))
                {
                    C<DataGrid>("Apps").ItemsSource = null;
                    ApplyActions();
                }
            };
            On("Search", delegate
            {
                string query = C<TextBox>("Query").Text.Trim();
                if (query.Length == 0) return;
                int request = searchSession.Revision;
                Work("Поиск приложений…", async delegate(CancellationToken ct)
                {
                    if (query.All(Char.IsDigit))
                    {
                        if (searchSession.IsCurrent(request)) C<DataGrid>("Apps").ItemsSource = new[]
                        {
                            new AppRow
                            {
                                Name = "Приложение по ID " + query, ID = query
                            }
                        };
                    } else
                    {
                        var local = await engine.CatalogSearch(query, ct);
                        if (!searchSession.IsCurrent(request)) return;
                        C<DataGrid>("Apps").ItemsSource = local;
                        Log("Найдено в каталогах: " + local.Count);
                        try
                        {
                            var r = await engine.Tool(new[]
                            {
                                "search", query, "--limit", "10"
                            }, ct, false);
                            Engine.Ensure(r);
                            if (!searchSession.IsCurrent(request)) return;
                            C<DataGrid>("Apps").ItemsSource = Engine.MergeApps(local, Engine.ParseApps(r.Output));
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            if (!searchSession.IsCurrent(request)) return;
                            if (local.Count == 0) throw;
                            Log("App Store: " + e.Message + ". Показаны результаты каталога.");
                        }
                    }
                    if (searchSession.IsCurrent(request)) C<DataGrid>("Apps").SelectedIndex = 0;
                });
            });
            C<TextBox>("Query").KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter && C<Button>("Search").IsEnabled) C<Button>("Search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            C<DataGrid>("Apps").SelectionChanged += delegate
            {
                versionApp = null;
                C<ComboBox>("VersionList").Items.Clear();
                C<ComboBox>("VersionList").Items.Add("Последняя версия");
                C<ComboBox>("VersionList").SelectedIndex = 0;
                ApplyActions();
            };
            On("Versions", delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Для списка версий выберите одно приложение.");
                string id = selected[0].ID;
                Work("Получение доступных версий…", async delegate(CancellationToken ct)
                {
                    var r = await engine.Tool(new[]
                    {
                        "list-versions", "-i", id
                    }, ct, false);
                    Engine.Ensure(r);
                    var values = Engine.Get(Engine.Object(r.Output), "externalVersionIdentifiers") as object[];
                    if (values == null) throw new Exception("Список версий не получен: формат ответа не поддерживается.");
                    if (SelectedApps().Count != 1 || SelectedApps()[0].ID != id) throw new Exception("Выбор приложения изменился. Повторите запрос версий.");
                    var combo = C<ComboBox>("VersionList");
                    combo.Items.Clear();
                    combo.Items.Add("Последняя версия");
                    foreach (var v in values.Reverse())
                    {
                        var s = Convert.ToString(v);
                        if (s.Length > 0 && s.All(Char.IsDigit)) combo.Items.Add(s);
                    }
                    combo.SelectedIndex = 0;
                    versionApp = id;
                    Log("Версий: " + values.Length + ". В списке отображаются идентификаторы Apple.");
                });
            });
            On("VersionInfo", delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Выберите одно приложение.");
                var row = selected[0];
                var combo = C<ComboBox>("VersionList");
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && row.ID != versionApp) throw new Exception("Обновите список версий выбранного приложения.");
                Work("Получение сведений о версии…", async delegate(CancellationToken ct)
                {
                    if (version == null)
                    {
                        var list = await engine.Tool(new[]
                        {
                            "list-versions", "-i", row.ID
                        }, ct, false);
                        Engine.Ensure(list);
                        var ids = Engine.VersionIds(list.Output);
                        if (ids.Count == 0) throw new Exception("Движок не вернул доступных версий приложения.");
                        version = ids[ids.Count - 1];
                        Log("Последняя доступная версия: ID " + version);
                    }
                    var r = await engine.Tool(new[]
                    {
                        "get-version-metadata", "-i", row.ID, "--external-version-id", version
                    }, ct, false);
                    Engine.Ensure(r);
                    var data = Engine.Object(r.Output);
                    var number = Convert.ToString(Engine.Get(data, "displayVersion"));
                    var date = Convert.ToString(Engine.Get(data, "releaseDate"));
                    MessageBox.Show(w, row.Name + Environment.NewLine + Environment.NewLine + "Версия: " + (String.IsNullOrWhiteSpace(number) ? "не указана" : number) + Environment.NewLine + "Дата выпуска: " + (String.IsNullOrWhiteSpace(date) ? "не указана" : date) + Environment.NewLine + "ID версии: " + version + Environment.NewLine + "App Store ID: " + row.ID, "Сведения о версии", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
            RowClickSelection.Attach(C<DataGrid>("Apps"), delegate(object item)
            {
                if (busy) return;
                var row = item as AppRow;
                if (row == null || chosen.Snapshot().Any(x => x.ID == row.ID)) return;
                var combo = C<ComboBox>("VersionList");
                chosen.Add(row, combo.SelectedIndex > 0 && versionApp == row.ID ? Convert.ToString(combo.SelectedItem) : null);
                RefreshChosen();
            });
            On("AddChosen", delegate
            {
                var rows = SelectedApps();
                var combo = C<ComboBox>("VersionList");
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && (rows.Count != 1 || rows[0].ID != versionApp)) throw new Exception("Для конкретной версии выберите одно приложение и обновите список версий.");
                foreach (var row in rows) chosen.Add(row, version);
                RefreshChosen();
            });
            On("ClearChosen", delegate
            {
                chosen.Clear();
                RefreshChosen();
            });
            SelectionRemove.Attach(C<ItemsControl>("ChosenApps"), delegate(string id)
            {
                if (busy) return;
                chosen.Remove(id);
                RefreshChosen();
            });
            On("Download", delegate { StartDownloads(chosen.Snapshot()); });
            On("RetryDownloads", delegate { StartDownloads(failedDownloads.Snapshot()); });
            On("ForgetDownloadErrors", delegate { failedDownloads.Clear(); ApplyActions(); });
        }
        void StartDownloads(List<DownloadItem> rows)
        {
            if (rows.Count == 0) throw new Exception("Добавьте приложения в выбранное.");
            Work("Скачивание IPA…", async delegate(CancellationToken ct)
            {
                int index = 0, success = 0;
                var failures = new List<string>();
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    view.BeginQueue("Скачивание", ++index, rows.Count, row.Name);
                    Log("В очереди: " + row.Label);
                    try
                    {
                        await engine.ApplicationDownloads.DownloadAsync(row, ct);
                        var current = chosen.Snapshot().FirstOrDefault(x => x.ID == row.ID);
                        if (current != null && current.VersionID == row.VersionID) chosen.Remove(row.ID);
                        failedDownloads.Remove(row.ID); success++;
                        RefreshChosen();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        failedDownloads.Add(new AppRow { ID = row.ID, Name = row.Name }, row.VersionID);
                        failures.Add(row.Name);
                        Log("Не удалось скачать " + row.Name + ": " + e.Message);
                    }
                    RefreshLocalFiles();
                }
                view.CompleteQueue(success, failures.Count);
                if (failures.Count > 0) throw new Exception("Не удалось скачать: " + String.Join(", ", failures) + ". Кнопка «Повторить ошибки» повторит только эти приложения.");
            });
        }
        public override void RefreshActions()
        {
            C<Button>("RetryDownloads").IsEnabled = !busy && failedDownloads.Count > 0;
            C<Button>("RetryDownloads").Content = "Повторить ошибки (" + failedDownloads.Count + ")";
            C<Button>("RetryDownloads").ToolTip = String.Join(Environment.NewLine, failedDownloads.Snapshot().Select(x => x.Label));
            C<Button>("ForgetDownloadErrors").IsEnabled = !busy && failedDownloads.Count > 0;
            C<Button>("AddChosen").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count > 0;
            C<Button>("ClearChosen").IsEnabled = !busy && chosen.Count > 0;
            C<Button>("Download").IsEnabled = !busy && chosen.Count > 0;
            C<ItemsControl>("ChosenApps").IsEnabled = !busy;
            C<Button>("VersionInfo").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count == 1;
        }
        void RefreshChosen()
        {
            C<ItemsControl>("ChosenApps").ItemsSource = chosen.Snapshot();
            C<TextBlock>("ChosenCount").Text = chosen.Count == 0 ? "Добавьте приложения из результатов поиска" : "Выбрано: " + chosen.Count + " · сохраняются при новом поиске";
            C<Button>("Download").Content = "Скачать (" + chosen.Count + ")";
            ApplyActions();
        }
        List<AppRow> SelectedApps()
        {
            var rows = C<DataGrid>("Apps").SelectedItems.Cast<AppRow>().ToList();
            if (rows.Count == 0) throw new Exception("Выберите приложение в таблице.");
            return rows;
        }
    }
}

// Presentation/SelectionRemove.cs
namespace IpaDesktop
{
    public static class SelectionRemove
    {
        public static void Attach(ItemsControl list, Action<string> remove)
        {
            list.AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                // WPF can retarget Source to an item container; OriginalSource identifies the button.
                var current = e.OriginalSource as DependencyObject;
                while (current != null && !(current is Button))
                {
                    current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
                }
                var button = current as Button;
                if (button == null || button.Tag == null) return;
                remove(Convert.ToString(button.Tag));
                e.Handled = true;
            }), true);
        }
    }
}

// Presentation/UpdatesController.cs
namespace IpaDesktop
{
    public sealed class UpdatesController : WindowController
    {
        Release guiRelease;
        public UpdatesController(Desktop view, Engine engine) : base(view, engine)
        {
            C<CheckBox>("CheckGuiUpdates").IsChecked = engine.Settings.CheckGuiUpdates;
            C<CheckBox>("CheckGuiUpdates").Click += delegate
            {
                engine.Settings.CheckGuiUpdates = C<CheckBox>("CheckGuiUpdates").IsChecked == true;
                try
                {
                    engine.Save();
                } catch (Exception e)
                {
                    Error(e);
                }
            };
            On("CheckGuiUpdate", delegate
            {
                Work("Проверка версии IPA Desktop…", CheckGui);
            });
            On("InstallGuiUpdate", delegate
            {
                if (guiRelease == null) return;
                if (MessageBox.Show(w, "Скачать и установить IPA Desktop " + guiRelease.tag_name + "? Программа закроется и запустится снова. Скачанные IPA и настройки сохранятся.", "Обновление программы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Work("Скачивание обновления IPA Desktop…", async delegate(CancellationToken ct)
                {
                    string stage = await GuiUpdater.Prepare(guiRelease, ct, delegate(double value)
                    {
                        w.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            C<ProgressBar>("Progress").IsIndeterminate = false;
                            C<ProgressBar>("Progress").Value = value;
                        }));
                    });
                    ct.ThrowIfCancellationRequested();
                    GuiUpdater.Launch(stage);
                    view.RequestRestart();
                });
            });
            On("OpenGuiReleases", delegate
            {
                Process.Start(new ProcessStartInfo("https://github.com/Lis1us/IPA-Desktop/releases")
                {
                    UseShellExecute = true
                });
            });
            On("OpenRelease", delegate
            {
                Process.Start(new ProcessStartInfo("https://github.com/Sorvigolova/ipatool/releases")
                {
                    UseShellExecute = true
                });
            });
            C<CheckBox>("AutoUpdateDeviceTools").Click += delegate
            {
                engine.Settings.AutoUpdateDeviceTools = C<CheckBox>("AutoUpdateDeviceTools").IsChecked == true;
                try { engine.Save(); } catch (Exception e) { Error(e); }
            };
            On("CheckDeviceUpdate", delegate
            {
                Work("Проверка инструментов для iPhone…", ct => UpdateDevices(ct, false));
            });
            On("RollbackDevices", delegate
            {
                if (MessageBox.Show(w, "Вернуть предыдущий комплект для iPhone и отключить его автообновление?", "Откат инструментов", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                try { engine.RollbackDeviceTools(); StateView(); ApplyActions(); }
                catch (Exception e) { Error(e); }
            });
            On("OpenDeviceReleases", delegate
            {
                Process.Start(new ProcessStartInfo("https://github.com/Lis1us/IPA-Desktop/releases?q=device-tools") { UseShellExecute = true });
            });
            On("CheckUpdate", delegate
            {
                Work("Проверка обновлений…", ct => Update(ct, false));
            });
            On("Rollback", delegate
            {
                if (MessageBox.Show(w, "Вернуть предыдущий движок и отключить автообновление?", "Откат", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    engine.Rollback();
                    StateView();
                }
            });
            C<CheckBox>("AutoUpdate").Checked += delegate
            {
                if (!setting)
                {
                    engine.Settings.AutoUpdate = true;
                    try
                    {
                        engine.Save();
                    } catch (Exception e)
                    {
                        Error(e);
                    }
                }
            };
            C<CheckBox>("AutoUpdate").Unchecked += delegate
            {
                if (!setting)
                {
                    engine.Settings.AutoUpdate = false;
                    try
                    {
                        engine.Save();
                    } catch (Exception e)
                    {
                        Error(e);
                    }
                }
            };
        }
        public override void RefreshActions()
        {
            C<Button>("InstallGuiUpdate").IsEnabled = !busy && guiRelease != null;
            C<Button>("RollbackDevices").IsEnabled = !busy && !String.IsNullOrEmpty(engine.Settings.PreviousDeviceCurrent);
        }
        public async Task Update(CancellationToken ct, bool startup)
        {
            var release = await engine.Check(ct);
            if (engine.Settings.Source == EngineUpdater.SourceRepository && String.Equals(release.tag_name, engine.Settings.Version, StringComparison.Ordinal) && !String.IsNullOrEmpty(engine.Settings.DeviceCurrent))
            {
                Log("Установлен актуальный релиз " + release.tag_name);
                return;
            }
            if (!startup && MessageBox.Show(w, "Установить официальный релиз " + release.tag_name + "?\nТекущая версия будет сохранена для отката.", "Обновление движка", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await engine.Install(release, ct);
            if (startup) Log("Автоматическое обновление завершено.");
        }
        public async Task UpdateDevices(CancellationToken ct, bool startup)
        {
            var release = await engine.CheckDeviceTools(ct);
            if (engine.DeviceToolsCurrent(release))
            {
                Log("Наш комплект для iPhone актуален: " + release.tag_name);
                return;
            }
            if (!startup && MessageBox.Show(w, "Обновить наш комплект инструментов для iPhone до " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.", "Обновление инструментов", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await engine.InstallDeviceTools(release, ct);
        }
        public async Task CheckGui(CancellationToken ct)
        {
            guiRelease = await GuiUpdater.Check(ct);
            string text = guiRelease == null ? "Установлена актуальная версия " + GuiUpdater.CurrentVersion : "Доступна версия " + guiRelease.tag_name;
            C<TextBlock>("GuiVersionStatus").Text = text;
            C<Button>("InstallGuiUpdate").Content = guiRelease == null ? "Установить обновление" : "Обновить до " + guiRelease.tag_name;
            C<TextBlock>("UpdateNotice").Text = guiRelease == null ? "" : text + " — вкладка «Обновления»";
            C<TextBlock>("UpdateNotice").Visibility = guiRelease == null ? Visibility.Collapsed : Visibility.Visible;
            Log(text);
        }
    }
}

// Presentation/WindowController.cs
namespace IpaDesktop
{
    public abstract class WindowController
    {
        protected readonly Desktop view;
        protected readonly Engine engine;
        protected Window w
        {
            get
            {
                return view.Window;
            }
        }
        protected bool busy
        {
            get
            {
                return view.IsBusy;
            }
        }
        protected bool setting
        {
            get
            {
                return view.IsUpdatingSettings;
            }
        }
        protected WindowController(Desktop view, Engine engine)
        {
            this.view = view;
            this.engine = engine;
        }
        protected T C<T>(string name) where T : class
        {
            return view.C<T>(name);
        }
        protected void On(string name, Action action)
        {
            view.On(name, action);
        }
        protected void Work(string title, Func<CancellationToken, Task> action)
        {
            view.Work(title, action);
        }
        protected void Log(string text)
        {
            view.Log(text);
        }
        protected void Error(Exception error)
        {
            view.Error(error);
        }
        protected void ApplyActions()
        {
            view.ApplyActions();
        }
        protected void StateView()
        {
            view.StateView();
        }
        protected void RefreshLocalFiles()
        {
            view.RefreshFiles();
        }
        public abstract void RefreshActions();
    }
}
