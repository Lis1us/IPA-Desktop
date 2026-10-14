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
                    view.RegisterPrivate(set.Name);
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
                        foreach (var device in devices) { view.RegisterPrivate(device.ID); view.RegisterPrivate(device.Name); }
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
