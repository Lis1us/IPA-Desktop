using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Отдельная экспериментальная вкладка; явное подтверждение режима и конкретного ECID перед запуском.</summary>
    public sealed class FirmwareController : WindowController
    {
        readonly FirmwareService service;
        string path;
        FirmwareManifest manifest;
        bool critical;
        public FirmwareController(Desktop view, Engine engine) : base(view, engine)
        {
            service = engine.Firmware;
            C<ComboBox>("FirmwareDevices").SelectionChanged += delegate { ClearCheck(); };
            C<RadioButton>("FirmwareErase").Checked += delegate { ClearCheck(); };
            C<RadioButton>("FirmwareUpdate").Checked += delegate { ClearCheck(); };
            On("FirmwareFind", delegate
            {
                ClearCheck();
                Work("Поиск iPhone для прошивки…", async ct =>
                {
                    var devices = await service.FindAsync(ct);
                    C<ComboBox>("FirmwareDevices").ItemsSource = devices;
                    if (devices.Count > 0) C<ComboBox>("FirmwareDevices").SelectedIndex = 0;
                    else C<TextBlock>("FirmwareInfo").Text = "iPhone не найден. Нужны обычный режим, разблокировка и доверие компьютеру.";
                    foreach (var device in devices) { view.RegisterPrivate(device.Udid); view.RegisterPrivate(device.Ecid); }
                });
            });
            On("FirmwareChoose", delegate
            {
                var picker = new OpenFileDialog { Filter = "Прошивка Apple (*.ipsw)|*.ipsw", CheckFileExists = true };
                if (picker.ShowDialog(w) != true) return;
                path = picker.FileName; ClearCheck(); C<TextBlock>("FirmwarePath").Text = path;
            });
            On("FirmwareCheck", delegate
            {
                var device = C<ComboBox>("FirmwareDevices").SelectedItem as FirmwareDevice;
                bool erase = C<RadioButton>("FirmwareErase").IsChecked == true;
                ClearCheck();
                Work("Проверка совместимости IPSW…", async ct =>
                {
                    var checkedManifest = await System.Threading.Tasks.Task.Run(() => FirmwareManifest.Read(path), ct);
                    checkedManifest.Validate(device, erase);
                    manifest = checkedManifest;
                    C<TextBlock>("FirmwareInfo").Text = "Совместимо: iOS " + manifest.Version + " (" + manifest.Build + "). " +
                        (erase ? "ВСЕ данные будут удалены." : "Выбран вариант обновления с сохранением данных.") +
                        " Подписывание проверит Apple при запуске; проверка совместимости не гарантирует восстановление.";
                });
            });
            On("FirmwareStart", Start);
            C<CheckBox>("FirmwareBackup").Click += delegate { ApplyActions(); };
            C<TextBox>("FirmwareConfirm").TextChanged += delegate { ApplyActions(); };
        }
        void ClearCheck()
        {
            manifest = null;
            C<CheckBox>("FirmwareBackup").IsChecked = false;
            C<TextBox>("FirmwareConfirm").Text = "";
            C<TextBlock>("FirmwareInfo").Text = "Выберите iPhone, IPSW и режим, затем проверьте совместимость.";
            ApplyActions();
        }
        void Start()
        {
            if (busy || manifest == null) return;
            var device = C<ComboBox>("FirmwareDevices").SelectedItem as FirmwareDevice;
            bool erase = C<RadioButton>("FirmwareErase").IsChecked == true;
            if (device == null || C<CheckBox>("FirmwareBackup").IsChecked != true || (erase && C<TextBox>("FirmwareConfirm").Text != "СТЕРЕТЬ")) return;
            string summary = device + "\nECID: " + device.Ecid + "\niOS " + manifest.Version + " (" + manifest.Build + ")\n\n" +
                (erase ? "Восстановление УДАЛИТ ВСЕ ДАННЫЕ." : "Обновление с сохранением данных. При сбое возможна потеря данных.") +
                "\nПосле запуска остановка и закрытие приложения будут заблокированы. Не отключайте USB и питание. Продолжить?";
            if (MessageBox.Show(w, summary, "Подтверждение прошивки", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Work("Подготовка прошивки iPhone…", async ct =>
            {
                view.StartReport("Прошивка iPhone", 1, null);
                view.BeginQueue("Прошивка", 1, 1, device.Name);
                try
                {
                    await service.RestoreAsync(device, path, manifest, erase, ct, delegate { critical = true; view.SetFirmwareCritical(true); RefreshActions(); },
                        delegate { critical = false; view.SetFirmwareCritical(false); });
                    view.ReportItem(device.Name, null); view.CompleteQueue(1, 0);
                    C<TextBlock>("FirmwareInfo").Text = "Прошивка завершена. Проверьте запуск iPhone; блокировка активации сохраняется.";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    view.ReportItem(device.Name, e); view.CompleteQueue(0, 1);
                    throw new IOException("Прошивка не завершена. Проверьте состояние iPhone и подробности ошибки. При необходимости используйте восстановление в Apple Devices.\n" + e.Message, e);
                }
                finally { manifest = null; C<CheckBox>("FirmwareBackup").IsChecked = false; }
            });
        }
        public override void RefreshActions()
        {
            bool selected = C<ComboBox>("FirmwareDevices").SelectedItem is FirmwareDevice;
            foreach (var name in new[] { "FirmwareFind", "FirmwareChoose" }) C<Button>(name).IsEnabled = !busy;
            C<Button>("FirmwareCheck").IsEnabled = !busy && selected && path != null;
            bool erase = C<RadioButton>("FirmwareErase").IsChecked == true;
            C<Button>("FirmwareStart").IsEnabled = !busy && !critical && manifest != null && selected &&
                C<CheckBox>("FirmwareBackup").IsChecked == true && (!erase || C<TextBox>("FirmwareConfirm").Text == "СТЕРЕТЬ");
            C<ComboBox>("FirmwareDevices").IsEnabled = !busy;
            C<RadioButton>("FirmwareUpdate").IsEnabled = !busy;
            C<RadioButton>("FirmwareErase").IsEnabled = !busy;
            C<CheckBox>("FirmwareBackup").IsEnabled = !busy;
            C<TextBox>("FirmwareConfirm").IsEnabled = !busy && erase;
        }
    }
}
