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
        internal void Log(string text)
        {
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
            setting = false;
        }
        internal async void Work(string title, Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true;
            cancellation = new CancellationTokenSource();
            string[] buttons =
            {
                "Login", "AccountInfo", "Logout", "Search", "Versions", "VersionInfo", "Download", "RefreshFiles", "ImportFile", "DeleteFiles", "Devices", "InstallFiles", "CheckUpdate", "Rollback", "CheckGuiUpdate", "InstallGuiUpdate", "AddChosen", "ClearChosen", "AddInstall", "ClearInstall", "CheckAppleSupport"
            };
            foreach (var name in buttons) C<Button>(name).IsEnabled = false;
            C<ItemsControl>("ChosenFiles").IsEnabled = false;
            C<ItemsControl>("ChosenApps").IsEnabled = false;
            C<CheckBox>("CheckGuiUpdates").IsEnabled = false;
            C<CheckBox>("AutoUpdate").IsEnabled = false;
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
                C<Button>("Cancel").IsEnabled = false;
                C<ProgressBar>("Progress").IsIndeterminate = false;
                C<ProgressBar>("Progress").Value = 0;
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
            using (var stream = File.OpenRead(Path.Combine(assetDirectory, "MainWindow.xaml"))) w = (Window) XamlReader.Load(stream);
            journalTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, w.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            JournalView.Attach(C<TextBox>("Journal"), journalBuffer);
            journalTimer.Tick += delegate
            {
                FlushJournal();
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
            using (var icon = File.OpenRead(Path.Combine(assetDirectory, "App.ico")))
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
                Log("IPA Desktop 0.1.12. Файлы: " + engine.Downloads);
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
