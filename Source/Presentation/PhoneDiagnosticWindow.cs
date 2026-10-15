using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace IpaDesktop
{
    /// <summary>Отдельное окно проверки: таблица показаний, видимые ограничения и единая настройка скрытия идентификаторов.</summary>
    public sealed class PhoneDiagnosticWindow
    {
        public Window Window { get; private set; }
        public ComboBox Devices { get; private set; }
        public Button Find { get; private set; }
        public Button Read { get; private set; }
        public Button Cancel { get; private set; }
        public Button Copy { get; private set; }
        public Button Save { get; private set; }
        public CheckBox HideIdentifiers { get; private set; }
        public DataGrid Values { get; private set; }
        public TextBlock Status { get; private set; }
        public TextBlock Warnings { get; private set; }
        public ProgressBar Progress { get; private set; }
        PhoneDiagnostic report;
        public PhoneDiagnostic Report { get { return report; } }
        public PhoneDiagnosticWindow(Window owner)
        {
            Window = AppDialogs.Create(owner, "Диагностика iPhone", 960, 740);
            Window.MinWidth = 700; Window.MinHeight = 520;
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock { Text = "Диагностика iPhone", FontSize = 22, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = "Подключите разблокированный iPhone и подтвердите доверие компьютеру. Проверка только считывает данные.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.LightSteelBlue });
            layout.Children.Add(header);
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            Find = new Button { Content = "Найти iPhone" };
            Devices = new ComboBox { MinWidth = 280, MaxWidth = 500, Margin = new Thickness(8, 0, 8, 0) };
            Read = new Button { Content = "Проверить", IsEnabled = false };
            Cancel = new Button { Content = "Отмена", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
            toolbar.Children.Add(Find); toolbar.Children.Add(Devices); toolbar.Children.Add(Read); toolbar.Children.Add(Cancel);
            Grid.SetRow(toolbar, 1); layout.Children.Add(toolbar);
            Values = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                CanUserSortColumns = false, EnableRowVirtualization = true, EnableColumnVirtualization = false };
            var wrap = new Style(typeof(TextBlock));
            wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            Values.Columns.Add(new DataGridTextColumn { Header = "Показатель", Binding = new Binding("Name"), Width = new DataGridLength(250), ElementStyle = wrap });
            Values.Columns.Add(new DataGridTextColumn { Header = "Значение", Binding = new Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 180, ElementStyle = wrap });
            Values.Columns.Add(new DataGridTextColumn { Header = "Источник", Binding = new Binding("Source"), Width = new DataGridLength(140), ElementStyle = wrap });
            Grid.SetRow(Values, 2); layout.Children.Add(Values);
            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            Status = new TextBlock { Text = "Найдите iPhone и нажмите «Проверить».", TextWrapping = TextWrapping.Wrap };
            Warnings = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            footer.Children.Add(Status);
            footer.Children.Add(new ScrollViewer { MaxHeight = 85, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Warnings });
            Progress = new ProgressBar { Height = 4, Margin = new Thickness(0, 8, 0, 8) };
            footer.Children.Add(Progress);
            HideIdentifiers = new CheckBox { Content = "Скрывать имя, серийный номер, IMEI и UDID в окне, копии и отчёте", IsChecked = true, Foreground = Window.Foreground };
            footer.Children.Add(HideIdentifiers);
            footer.Children.Add(new TextBlock { Text = "Доступность показателей зависит от iPhone и iOS. Расчёт по ёмкости может отличаться от состояния АКБ в настройках iOS.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 8, 0, 8) });
            var actions = new WrapPanel();
            Copy = new Button { Content = "Копировать отчёт", IsEnabled = false };
            Save = new Button { Content = "Сохранить отчёт…", IsEnabled = false, Margin = new Thickness(8, 0, 8, 0) };
            var close = new Button { Content = "Закрыть", IsCancel = true };
            close.Click += delegate { Window.Close(); };
            actions.Children.Add(Copy); actions.Children.Add(Save); actions.Children.Add(close); footer.Children.Add(actions);
            Grid.SetRow(footer, 3); layout.Children.Add(footer); Window.Content = layout;
            HideIdentifiers.Checked += delegate { Render(); };
            HideIdentifiers.Unchecked += delegate { Render(); };
        }
        public void Show(PhoneDiagnostic value)
        {
            report = value; Render();
            Warnings.Text = String.Join(Environment.NewLine, value.Warnings);
            Status.Text = "Проверено: " + value.Created.ToString("dd.MM.yyyy HH:mm:ss") +
                (value.Warnings.Count == 0 ? "" : " · часть данных недоступна");
        }
        public void Clear()
        {
            report = null; Values.ItemsSource = null; Warnings.Text = "";
            Status.Text = "Нажмите «Проверить» для выбранного iPhone.";
        }
        void Render() { if (report != null) Values.ItemsSource = report.Display(HideIdentifiers.IsChecked == true); }
        public string Export() { return report == null ? "" : report.Export(HideIdentifiers.IsChecked == true); }
    }
}
