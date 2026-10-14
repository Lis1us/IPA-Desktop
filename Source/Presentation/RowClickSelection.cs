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
    public static class RowClickSelection
    {
        public static void Attach(DataGrid grid, Action<object> add)
        {
            object pressed = null;
            Point origin = new Point();
            grid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler(delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
                pressed = row == null ? null : row.Item;
                origin = e.GetPosition(grid);
            }), true);
            grid.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler(delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
                var distance = e.GetPosition(grid) - origin;
                if (row != null && Object.ReferenceEquals(pressed, row.Item) && Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) add(row.Item);
                pressed = null;
            }), true);
        }
    }
}
