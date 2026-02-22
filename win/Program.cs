using System;
using ace_run;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace ace_run;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        var mainInstance = AppInstance.FindOrRegisterForKey("AceRun-Main");

        if (!mainInstance.IsCurrent)
        {
            // 已有實例在執行：發送啟動事件給既有實例後退出
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            mainInstance.RedirectActivationToAsync(activatedArgs).AsTask().GetAwaiter().GetResult();
            return;
        }

        // 本實例是第一個：監聽後續重複啟動的通知
        mainInstance.Activated += (_, _) =>
        {
            if (Application.Current is App app)
                app.BringToForeground();
        };

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
