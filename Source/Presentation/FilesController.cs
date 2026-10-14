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
        readonly InstallationSelection installChosen = new InstallationSelection();
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
            On("DeleteFiles", delegate
            {
                if (busy) return;
                var rows = C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().ToList();
                if (rows.Count == 0) throw new Exception("Выберите IPA-файлы для удаления. Несколько файлов: Ctrl / Shift.");
                var summary = String.Join(Environment.NewLine, rows.Take(8).Select(x => x.Name));
                if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
                if (MessageBox.Show(w, "Переместить в корзину выбранные IPA (" + rows.Count + ")?" + Environment.NewLine + Environment.NewLine + summary + Environment.NewLine + Environment.NewLine + "Приложения на iPhone останутся установленными.", "Удаление IPA", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Work("Перемещение IPA в корзину…", async delegate(CancellationToken ct)
                {
                    var failures = new List<string>();
                    int removed = 0;
                    await Task.Run(delegate
                    {
                        foreach (var row in rows)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                engine.LocalFiles.Delete(row.Path);
                                removed++;
                                Log("Перемещено в корзину: " + row.Name);
                            } catch (OperationCanceledException)
                            {
                                throw;
                            } catch (Exception e)
                            {
                                failures.Add(row.Name + ": " + e.Message);
                            }
                        }
                    }, ct);
                    Log("Удалено из папки IPA: " + removed);
                    if (failures.Count > 0) throw new IOException("Не удалось удалить:" + Environment.NewLine + String.Join(Environment.NewLine, failures));
                });
            });
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
            On("Devices", delegate
            {
                Work("Поиск iPhone…", async delegate(CancellationToken ct)
                {
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
            On("InstallFiles", delegate
            {
                var device = C<ComboBox>("DeviceList").SelectedItem as DeviceRow;
                string udid = device == null ? null : device.ID;
                var rows = installChosen.Snapshot();
                if (String.IsNullOrEmpty(udid) || rows.Count == 0) throw new Exception("Выберите устройство и добавьте IPA к установке.");
                string summary = String.Join(Environment.NewLine, rows.Take(8).Select(x => x.Name));
                if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
                if (MessageBox.Show(w, "Установить IPA (" + rows.Count + ") на устройство " + device.Name + "?" + Environment.NewLine + Environment.NewLine + summary, "Установка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Work("Установка IPA…", async delegate(CancellationToken ct)
                {
                    var failed = new List<string>();
                    int index = 0;
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        index++;
                        C<TextBlock>("Status").Text = "Установка " + index + " из " + rows.Count + ": " + row.Name;
                        Log("Установка " + row.Name);
                        try
                        {
                            await engine.Devices.InstallAsync(device, row, ct);
                            installChosen.Remove(row.Path);
                            RefreshInstallChosen();
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            failed.Add(row.Name);
                            Log("Ошибка установки " + row.Name + ": " + e.Message);
                        }
                    }
                    if (failed.Count > 0) throw new Exception("Не удалось установить: " + String.Join(", ", failed) + ". Доступные IPA остались в выбранном для повтора.");
                });
            });
        }
        public override void RefreshActions()
        {
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
            var selected = new HashSet<string>(C<DataGrid>("Files").SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await engine.LocalFiles.ListAsync();
                if (generation != filesGeneration) return;
                C<DataGrid>("Files").ItemsSource = rows;
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
