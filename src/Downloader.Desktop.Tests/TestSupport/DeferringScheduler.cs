using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Avalonia.Threading;

namespace Downloader.Desktop.Tests;

/// <summary>
/// A main-thread scheduler that always defers onto the dispatcher.
///
/// The shell schedules its initialisation onto <c>RxApp.MainThreadScheduler</c> from its constructor,
/// and the app assigns the window AFTER constructing the view model — so init must run on a LATER
/// dispatcher turn or it finds no window and skips wiring the shell entirely. In the running app that
/// ordering is free (nothing pumps the dispatcher during startup). Under the headless runtime the test
/// thread IS the UI thread, so the default scheduler runs the work inline and the ordering silently
/// inverts, making the shell look like it does nothing. Installing this restores the app's real order.
/// </summary>
internal sealed class DeferringScheduler : IScheduler
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        var sub = new SingleAssignmentDisposable();
        Dispatcher.UIThread.Post(() => { if (!sub.IsDisposed) sub.Disposable = action(this, state); });
        return sub;
    }

    public IDisposable Schedule<TState>(TState state, TimeSpan dueTime,
        Func<IScheduler, TState, IDisposable> action) => Schedule(state, action);

    public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime,
        Func<IScheduler, TState, IDisposable> action) => Schedule(state, action);
}
