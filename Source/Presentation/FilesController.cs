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
    /// <summary>Управляет локальными IPA и установкой на iPhone; наборы и хранилище обслуживаются отдельно.</summary>
    public sealed class FilesController : WindowController
    {
        AppleSupportReport appleSupport;
        readonly InstallationSelection installChosen = new InstallationSelection();
        readonly InstallationSelection failedInstalls = new InstallationSelection();
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
            On("DeleteFiles", delegate { DeleteRows(installChosen.Snapshot()); });
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
        /// <summary>После подтверждения перемещает IPA в корзину, продолжая обработку при ошибке отдельного файла.</summary>
        internal void DeleteRows(List<LocalRow> rows)
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
                        try { engine.LocalFiles.Delete(row.Path); removed++; Log("Перемещено в корзину: " + row.DisplayName); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e) { failures.Add(row.DisplayName + ": " + e.Message); }
                    }
                }, ct);
                Log("Удалено из папки IPA: " + removed);
                if (failures.Count > 0) throw new IOException("Не удалось удалить:" + Environment.NewLine + String.Join(Environment.NewLine, failures));
            });
        }
        /// <summary>Подтверждает выбранное устройство и запускает последовательную установку снимка очереди.</summary>
        void StartInstall(List<LocalRow> rows)
        {
            var device = C<ComboBox>("DeviceList").SelectedItem as DeviceRow;
            if (device == null || rows.Count == 0) throw new Exception("Выберите устройство и IPA для установки.");
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(x => x.DisplayName));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (MessageBox.Show(w, "Установить IPA (" + rows.Count + ") на " + device.Name + "?" + Environment.NewLine + Environment.NewLine + summary, "Установка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Work("Установка IPA…", async delegate(CancellationToken ct)
            {
                await CheckSupport(ct);
                var result = await BatchRunner.Run(rows,
                    (row, token) => engine.Devices.InstallAsync(device, row, token),
                    (row, index) => { view.BeginQueue("Установка", index, rows.Count, row.DisplayName); Log("Установка " + row.DisplayName); },
                    row => { installChosen.Remove(row.Path); failedInstalls.Remove(row.Path); RefreshInstallChosen(); },
                    (row, error) => { failedInstalls.Add(row); Log("Ошибка установки " + row.DisplayName + ": " + error.Message); },
                    null, ct);
                view.CompleteQueue(result.Succeeded, result.Failed.Count);
                if (result.Failed.Count > 0) throw new Exception("Не удалось установить: " + String.Join(", ", result.Failed.Select(x => x.DisplayName)) + ". Кнопка «Повторить ошибки» повторит только эти IPA.");
            });
        }
        /// <summary>Обновляет статус компонентов Apple из отдельного сервиса проверки Windows.</summary>
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

        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("RetryInstalls").IsEnabled = !busy && failedInstalls.Count > 0 && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("RetryInstalls").Content = "Повторить ошибки (" + failedInstalls.Count + ")";
            C<Button>("RetryInstalls").ToolTip = String.Join(Environment.NewLine, failedInstalls.Snapshot().Select(x => x.DisplayName));
            C<Button>("ForgetInstallErrors").IsEnabled = !busy && failedInstalls.Count > 0;
            C<ItemsControl>("ChosenFiles").IsEnabled = !busy;
            C<Button>("AddInstall").IsEnabled = !busy && C<DataGrid>("Files").SelectedItems.Count > 0;
            C<Button>("DeleteFiles").IsEnabled = !busy && installChosen.Count > 0;
            C<Button>("DeleteFiles").Content = "Удалить выбранные IPA (" + installChosen.Count + ")";
            C<Button>("ClearInstall").IsEnabled = !busy && installChosen.Count > 0;
            C<Button>("InstallFiles").IsEnabled = !busy && installChosen.Count > 0 && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("Devices").Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color) System.Windows.Media.ColorConverter.ConvertFromString(C<ComboBox>("DeviceList").Items.Count == 0 ? "#16704C" : "#243247"));
        }
        void RefreshInstallChosen()
        {
            C<ItemsControl>("ChosenFiles").ItemsSource = installChosen.Snapshot();
            C<TextBlock>("InstallCount").Text = installChosen.Count == 0 ? "ЛКМ — выбрать для установки или удаления" : "Выбрано IPA: " + installChosen.Count;
            C<Button>("InstallFiles").Content = "Установить (" + installChosen.Count + ")";
            ApplyActions();
        }
        int filesGeneration;
        /// <summary>Применяет только последний результат сканирования, сохраняя выделение по пути файла.</summary>
        public async void RefreshFiles()
        {
            int generation = ++filesGeneration;
            var selected = new HashSet<string>(C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await engine.LocalFiles.ListAsync();
                if (generation != filesGeneration) return;
                C<DataGrid>("Files").ItemsSource = rows;
                view.ShowStorage(rows);
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
