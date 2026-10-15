using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
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
        readonly FirmwareCatalog catalog = new FirmwareCatalog();
        List<FirmwareRelease> releases = new List<FirmwareRelease>();
        void FilterReleases()
        {
            C<DataGrid>("FirmwareReleases").ItemsSource = releases.Where(x => C<CheckBox>("FirmwareSignedOnly").IsChecked != true || x.signed).ToList();
            ApplyActions();
        }
        void SelectModel()
        {
            var device = C<ComboBox>("FirmwareDevices").SelectedItem as FirmwareDevice;
            if (device == null || C<ComboBox>("FirmwareModels").ItemsSource == null) return;
            var models = C<ComboBox>("FirmwareModels").ItemsSource.Cast<FirmwareModel>();
            C<ComboBox>("FirmwareModels").SelectedItem = models.FirstOrDefault(x => x.identifier == device.ProductType);
        }
        void DownloadRelease()
        {
            var release = C<DataGrid>("FirmwareReleases").SelectedItem as FirmwareRelease;
            if (busy || !FirmwareCatalog.Downloadable(release)) return;
            var picker = new SaveFileDialog { Filter = "Прошивка Apple (*.ipsw)|*.ipsw", AddExtension = true, DefaultExt = ".ipsw",
                FileName = release.identifier + "_" + release.version.Replace('/', '_').Replace('\\', '_') + "_" + release.buildid + "_Restore.ipsw", OverwritePrompt = true };
            if (picker.ShowDialog(w) != true) return;
            ClearCheck();
            Work("Скачивание IPSW…", async ct =>
            {
                view.StartReport("Скачивание IPSW", 1, null);
                view.BeginQueue("Скачивание IPSW", 1, 1, "iOS " + release.version + " (" + release.buildid + ")");
                try
                {
                    string downloaded = await catalog.DownloadAsync(release, picker.FileName, ct, delegate(FirmwareTransfer transfer)
                    {
                        string text = "Downloading: " + Math.Floor(100.0 * transfer.Bytes / transfer.Total).ToString(CultureInfo.InvariantCulture) +
                            "% (" + (transfer.Bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB/" +
                            (transfer.Total / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB, " +
                            (transfer.BytesPerSecond / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture) + "MB/s)";
                        view.Log(text);
                    });
                    path = downloaded;
                    C<TextBlock>("FirmwarePath").Text = path;
                    ClearCheck();
                    C<TextBlock>("FirmwareInfo").Text = "IPSW скачана; размер, контрольная сумма и BuildManifest проверены. Нажмите «Проверить совместимость» для выбранного iPhone.";
                    view.ReportItem("iOS " + release.version, null); view.CompleteQueue(1, 0);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { view.ReportItem("iOS " + release.version, e); view.CompleteQueue(0, 1); throw; }
            });
        }
        public FirmwareController(Desktop view, Engine engine) : base(view, engine)
        {
            service = engine.Firmware;
            C<ComboBox>("FirmwareDevices").SelectionChanged += delegate { ClearCheck(); SelectModel(); };
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
            On("FirmwareLoadModels", delegate
            {
                Work("Загрузка моделей iPhone из IPSW.me…", async ct =>
                {
                    var models = await catalog.ModelsAsync(ct);
                    C<ComboBox>("FirmwareModels").ItemsSource = models;
                    SelectModel();
                    if (C<ComboBox>("FirmwareModels").SelectedItem == null && models.Count > 0) C<ComboBox>("FirmwareModels").SelectedIndex = 0;
                    C<TextBlock>("FirmwareCatalogStatus").Text = "Выберите модель и нажмите «Найти прошивки».";
                });
            });
            C<ComboBox>("FirmwareModels").SelectionChanged += delegate
            {
                releases.Clear(); FilterReleases();
                C<TextBlock>("FirmwareCatalogStatus").Text = "Модель изменена — нажмите «Найти прошивки».";
            };
            C<CheckBox>("FirmwareSignedOnly").Click += delegate { FilterReleases(); };
            C<DataGrid>("FirmwareReleases").SelectionChanged += delegate { ApplyActions(); };
            On("FirmwareSearch", delegate
            {
                var model = C<ComboBox>("FirmwareModels").SelectedItem as FirmwareModel;
                if (model == null) return;
                releases.Clear(); FilterReleases();
                Work("Поиск IPSW для " + model.name + "…", async ct =>
                {
                    releases = await catalog.ReleasesAsync(model.identifier, ct);
                    FilterReleases();
                    C<TextBlock>("FirmwareCatalogStatus").Text = "IPSW.me · " + model.name + " · получено версий: " + releases.Count +
                        " · " + DateTime.Now.ToString("HH:mm:ss") + ". Подпись справочная; окончательное разрешение проверит Apple TSS. Скорость загрузки — средняя за текущую загрузку.";
                });
            });
            On("FirmwareDownload", DownloadRelease);
            w.Closed += delegate { catalog.Dispose(); };
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
            C<Button>("FirmwareLoadModels").IsEnabled = !busy;
            C<Button>("FirmwareSearch").IsEnabled = !busy && C<ComboBox>("FirmwareModels").SelectedItem is FirmwareModel;
            C<Button>("FirmwareDownload").IsEnabled = !busy && FirmwareCatalog.Downloadable(C<DataGrid>("FirmwareReleases").SelectedItem as FirmwareRelease);
            C<ComboBox>("FirmwareModels").IsEnabled = !busy;
            C<CheckBox>("FirmwareSignedOnly").IsEnabled = !busy;
            C<DataGrid>("FirmwareReleases").IsEnabled = !busy;
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
