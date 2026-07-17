using System.Collections.Generic;
using Avalonia.Controls;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

/// <summary>
/// One view instance per page VM, created lazily and REUSED across navigation. The DataTemplate
/// approach instantiated a brand-new page view on every swap — with 2k downloads that meant
/// re-building the whole DataGrid / settings tree each time (slow navigation, memory churn) and
/// losing per-page state (scroll position, expanded sections). The VMs were already singletons;
/// this makes the views match.
/// </summary>
public sealed class PageViewCache
{
    private readonly Dictionary<object, Control> _views = new();

    /// <summary>The cached view for this page VM (created on first request, DataContext pre-set).</summary>
    public Control GetView(object vm)
    {
        if (vm == null)
            return null;
        if (_views.TryGetValue(vm, out var existing))
            return existing;

        Control view = vm switch
        {
            DownloadsViewModel => new DownloadsView(),
            QueuesViewModel => new QueuesView(),
            SchedulerViewModel => new SchedulerView(),
            SettingViewModel => new SettingView(),
            _ => null
        };
        if (view == null)
            return null;

        view.DataContext = vm;
        _views[vm] = view;
        return view;
    }
}
