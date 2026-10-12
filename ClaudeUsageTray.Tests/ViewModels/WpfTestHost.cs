using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace ClaudeUsageTray.Tests.ViewModels;

internal static class WpfTestHost
{
    private static readonly Lazy<Task<Dispatcher>> DispatcherTask = new(StartDispatcher);

    public static async Task RunAsync(Action action)
    {
        var dispatcher = await DispatcherTask.Value.ConfigureAwait(false);
        await dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
    }

    public static async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await DispatcherTask.Value.ConfigureAwait(false);
        var operation = dispatcher.InvokeAsync(action);
        var innerTask = await operation.Task.ConfigureAwait(false);
        await innerTask.ConfigureAwait(false);
    }

    private static Task<Dispatcher> StartDispatcher()
    {
        var tcs = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            if (Application.Current is null)
            {
                // 뷰가 쓰는 App.xaml 리소스만 필요하다. 트레이 앱까지 띄우면 실제 설정·히스토리·
                // OpenCode 웹 세션을 쓰는 앱이 테스트 프로세스 안에서 돈다(#186).
                var app = new ClaudeUsageTray.App(startsTrayApp: false) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
            }

            tcs.SetResult(dispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test dispatcher"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}
