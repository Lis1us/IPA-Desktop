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
    public sealed class AccountController : WindowController
    {
        bool signedIn;
        public AccountController(Desktop view, Engine engine) : base(view, engine)
        {
            On("Login", delegate
            {
                Work("Вход: завершите авторизацию в открывшемся окне", async delegate(CancellationToken ct)
                {
                    var r = await engine.Run(engine.Binary("ipatool*.exe"), new[]
                    {
                        "auth", "login"
                    }, ct, true, false);
                    Engine.Ensure(r);
                    await Account(ct);
                });
            });
            On("AccountInfo", delegate
            {
                Work("Проверка аккаунта…", Account);
            });
            On("Logout", delegate
            {
                if (MessageBox.Show(w, "Выйти из текущей сессии ipatool? Это затронет и исходный скрипт.", "Выход", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Work("Выход…", async delegate(CancellationToken ct)
                {
                    Engine.Ensure(await engine.Tool(new[]
                    {
                        "auth", "revoke"
                    }, ct, false));
                    signedIn = false;
                    C<TextBlock>("AccountLabel").Text = "Вход не выполнен";
                });
            });
        }
        public override void RefreshActions()
        {
            C<Button>("Login").IsEnabled = !busy && !signedIn;
            C<Button>("Logout").IsEnabled = !busy && signedIn;
        }
        public async Task Account(CancellationToken ct)
        {
            var r = await engine.Tool(new[]
            {
                "auth", "info"
            }, ct, false);
            signedIn = false;
            C<TextBlock>("AccountLabel").Text = "Вход не выполнен";
            Engine.Ensure(r);
            var data = Engine.Object(r.Output);
            var email = Convert.ToString(Engine.Get(data, "email"));
            if (String.IsNullOrWhiteSpace(email) || !Object.Equals(Engine.Get(data, "success"), true)) throw new Exception("Не удалось подтвердить сохранённый вход в аккаунт.");
            signedIn = true;
            C<TextBlock>("AccountLabel").Text = email;
            Log("Аккаунт проверен.");
        }
    }
}
