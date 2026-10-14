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
    public abstract class WindowController
    {
        protected readonly Desktop view;
        protected readonly Engine engine;
        protected Window w
        {
            get
            {
                return view.Window;
            }
        }
        protected bool busy
        {
            get
            {
                return view.IsBusy;
            }
        }
        protected bool setting
        {
            get
            {
                return view.IsUpdatingSettings;
            }
        }
        protected WindowController(Desktop view, Engine engine)
        {
            this.view = view;
            this.engine = engine;
        }
        protected T C<T>(string name) where T : class
        {
            return view.C<T>(name);
        }
        protected void On(string name, Action action)
        {
            view.On(name, action);
        }
        protected void Work(string title, Func<CancellationToken, Task> action)
        {
            view.Work(title, action);
        }
        protected void Log(string text)
        {
            view.Log(text);
        }
        protected void Error(Exception error)
        {
            view.Error(error);
        }
        protected void ApplyActions()
        {
            view.ApplyActions();
        }
        protected void StateView()
        {
            view.StateView();
        }
        protected void RefreshLocalFiles()
        {
            view.RefreshFiles();
        }
        public abstract void RefreshActions();
    }
}
