using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace IpaDesktop
{
    /// <summary>Создаёт диалоги с общими стилями приложения, владельцем и тёмной рамкой Windows.</summary>
    public static class AppDialogs
    {
        public static Window Create(Window owner, string title, double width, double height)
        {
            var dialog = new Window
            {
                Owner = owner, Title = title, Width = width, Height = height,
                MinWidth = 460, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner.Background, Foreground = owner.Foreground,
                FontFamily = owner.FontFamily, FontSize = owner.FontSize,
                ShowInTaskbar = false, Icon = owner.Icon
            };
            dialog.Resources.MergedDictionaries.Add(owner.Resources);
            dialog.SourceInitialized += delegate { WindowTheme.Apply(dialog); };
            return dialog;
        }
        /// <summary>Полный текст доступен для выделения и копирования, без автоматического открытия при ошибке.</summary>
        public static Window Text(Window owner, string title, string details)
        {
            var dialog = Create(owner, title, 740, 530);
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBox
            {
                Text = details ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            layout.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var copy = new Button { Content = "Копировать" };
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            copy.Click += delegate
            {
                try { Clipboard.SetText(text.Text); status.Text = "Скопировано"; }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Буфер занят; выделите текст и нажмите Ctrl+C."; }
            };
            var close = new Button { Content = "Закрыть", IsCancel = true };
            close.Click += delegate { dialog.Close(); };
            actions.Children.Add(copy); actions.Children.Add(close); actions.Children.Add(status);
            Grid.SetRow(actions, 1); layout.Children.Add(actions); dialog.Content = layout;
            return dialog;
        }
        public static void ShowText(Window owner, string title, string details) { Text(owner, title, details).ShowDialog(); }
        /// <summary>Данные уже прочитаны репозиторием; просмотр IPA не запускает дополнительное сканирование диска.</summary>
        public static Window Ipa(Window owner, LocalRow row)
        {
            return Text(owner, "Сведения об IPA — " + row.DisplayName,
                row.FileDetails + Environment.NewLine + "App Store ID: " + (row.AppStoreID ?? "не указан") +
                Environment.NewLine + "Bundle ID: " + (row.BundleID ?? "не указан") +
                Environment.NewLine + "Сборка: " + (row.Build ?? "не указана") +
                Environment.NewLine + "Размер: " + row.Size);
        }
        /// <summary>Временно переносит существующие настройки в диалог, сохраняя обработчики и namescope главного окна.</summary>
        public static void Settings(Window owner, Panel host, UIElement content)
        {
            int index = host.Children.IndexOf(content);
            host.Children.Remove(content);
            var dialog = Create(owner, "Настройки IPA Desktop", 800, 680);
            content.Visibility = Visibility.Visible;
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            var notice = new TextBlock { Margin = new Thickness(24, 12, 24, 0), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Salmon };
            notice.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = owner.FindName("NoticeText") });
            notice.SetBinding(TextBlock.ToolTipProperty, new Binding("ToolTip") { Source = owner.FindName("NoticeText") });
            notice.SetBinding(TextBlock.VisibilityProperty, new Binding("Visibility") { Source = owner.FindName("OperationNotice") });
            layout.Children.Add(notice); Grid.SetRow(content, 1); layout.Children.Add(content);
            dialog.Content = layout;
            try { dialog.ShowDialog(); }
            finally
            {
                layout.Children.Remove(content); Grid.SetRow(content, 0);
                dialog.Content = null; content.Visibility = Visibility.Collapsed;
                host.Children.Insert(index, content);
            }
        }
    }
}
