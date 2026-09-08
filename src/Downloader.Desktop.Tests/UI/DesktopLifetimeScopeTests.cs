using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// Diagnostics for the recurring "test host hung" CI abort.
/// </summary>
public class DesktopLifetimeScopeTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Disposing_the_scope_must_not_ask_the_application_to_shut_down()
    {
        var fired = false;
        using (new DesktopLifetimeScope())
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            lifetime.ShutdownRequested += (_, _) => fired = true;
        }

        Assert.False(fired,
            "closing the scope's window asked the application to shut down — under the headless session " +
            "that tears the runtime down and every later test parks forever waiting for a dispatcher.");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_dispatcher_still_runs_jobs_after_the_scope_is_disposed()
    {
        using (new DesktopLifetimeScope()) { }

        var ran = false;
        Dispatcher.UIThread.Post(() => ran = true);
        Dispatcher.UIThread.RunJobs();

        Assert.True(ran, "the dispatcher stopped running posted jobs after the scope was disposed");
    }
}
