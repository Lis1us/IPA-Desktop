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
                Process.Start(new ProcessStartInfo("https://github.com/kda2495/IPA_Downloader/releases") { UseShellExecute = true });
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
                Log("Комплект для iPhone актуален: IPA_Downloader " + release.tag_name);
                return;
            }
            if (!startup && MessageBox.Show(w, "Обновить инструменты для iPhone из IPA_Downloader " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.", "Обновление инструментов", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
