using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Связывает окно с общим менеджером операций: диагностика не выполняется параллельно с прошивкой или установкой.</summary>
    public sealed class PhoneDiagnosticsController : WindowController
    {
        PhoneDiagnosticWindow dialog;
        bool running, closePending;
        public PhoneDiagnosticsController(Desktop view, Engine engine) : base(view, engine)
        {
            On("OpenPhoneDiagnostics", Open);
        }
        void Run(string title, Func<CancellationToken, Task> action)
        {
            if (busy || running || dialog == null) return;
            running = true;
            RefreshActions();
            Work(title, async ct =>
            {
                try { await action(ct); }
                catch (OperationCanceledException) { if (dialog != null) dialog.Status.Text = "Проверка отменена."; throw; }
                catch (Exception e) { if (dialog != null) { dialog.Status.Text = "Ошибка проверки"; dialog.Warnings.Text = e.Message; } throw; }
                finally
                {
                    running = false;
                    if (dialog != null && closePending) dialog.Window.Dispatcher.BeginInvoke(new Action(delegate { if (dialog != null) dialog.Window.Close(); }));
                }
            });
        }
        void Open()
        {
            if (busy || dialog != null) return;
            dialog = new PhoneDiagnosticWindow(w); closePending = false;
            dialog.Devices.SelectionChanged += delegate { dialog.Clear(); RefreshActions(); };
            dialog.Find.Click += delegate
            {
                dialog.Clear();
                dialog.Devices.ItemsSource = null;
                Run("Поиск iPhone для диагностики…", async ct =>
                {
                    var devices = await engine.PhoneDiagnostics.FindAsync(ct);
                    dialog.Devices.ItemsSource = devices;
                    if (devices.Count > 0) dialog.Devices.SelectedIndex = 0;
                    dialog.Status.Text = devices.Count == 0 ? "iPhone не найден. Проверьте USB, разблокировку, доверие и драйвер Apple." : "Выберите iPhone и нажмите «Проверить».";
                });
            };
            dialog.Read.Click += delegate
            {
                var device = dialog.Devices.SelectedItem as DeviceRow;
                if (device == null) return;
                dialog.Clear();
                Run("Диагностика iPhone…", async ct =>
                {
                    var report = await engine.PhoneDiagnostics.ReadAsync(device, ct, text => dialog.Status.Text = text);
                    foreach (var value in report.Values) if (value.Sensitive && value.Name != "Имя iPhone" && value.Value != "Недоступно") view.RegisterPrivate(value.Value);
                    view.RegisterPrivate(device.ID);
                    dialog.Show(report);
                });
            };
            dialog.Cancel.Click += delegate { if (running) { view.CancelCurrentOperation(); dialog.Status.Text = "Отмена проверки…"; } };
            dialog.Copy.Click += delegate
            {
                try { Clipboard.SetText(dialog.Export()); dialog.Status.Text = "Отчёт скопирован."; }
                catch (System.Runtime.InteropServices.ExternalException) { dialog.Warnings.Text = "Буфер обмена занят. Повторите копирование."; }
            };
            dialog.Save.Click += delegate
            {
                var picker = new SaveFileDialog { Filter = "Текстовый отчёт (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, FileName = "iPhone-Diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
                if (picker.ShowDialog(dialog.Window) != true) return;
                try { AtomicFile.WriteUtf8(picker.FileName, dialog.Export()); dialog.Status.Text = "Отчёт сохранён."; }
                catch (Exception e) { dialog.Warnings.Text = "Не удалось сохранить отчёт: " + e.Message; }
            };
            dialog.Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (running)
                {
                    e.Cancel = true; closePending = true; view.CancelCurrentOperation();
                    dialog.Status.Text = "Закрытие после отмены проверки…";
                }
            };
            try { RefreshActions(); dialog.Window.ShowDialog(); }
            finally { dialog = null; closePending = false; RefreshActions(); }
        }
        public override void RefreshActions()
        {
            C<System.Windows.Controls.Button>("OpenPhoneDiagnostics").IsEnabled = !busy && dialog == null;
            if (dialog == null) return;
            bool active = running || busy;
            dialog.Find.IsEnabled = !active;
            dialog.Devices.IsEnabled = !active;
            dialog.Read.IsEnabled = !active && dialog.Devices.SelectedItem is DeviceRow;
            dialog.Cancel.IsEnabled = running;
            dialog.Copy.IsEnabled = dialog.Save.IsEnabled = !active && dialog.Report != null;
            dialog.HideIdentifiers.IsEnabled = !active;
            dialog.Progress.IsIndeterminate = running;
        }
    }
}
