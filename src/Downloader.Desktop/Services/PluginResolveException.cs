using System;

namespace Downloader.Desktop.Services;

/// <summary>
/// A plugin resolver claimed a link and then could not turn it into a download. Carries the resolver's own
/// explanation (a live stream, a protected page, a site that wants a session, a tool that failed its
/// checksum) so the failed row can say what actually happened.
/// <para>
/// It exists because the alternative — swallowing the error and downloading the page as an ordinary URL —
/// replaces a precise reason with whatever the HTML turns into, which is how a page that a plugin
/// explicitly refused ended up reported as an invalid link.
/// </para>
/// </summary>
public sealed class PluginResolveException : Exception
{
    public PluginResolveException(string message, Exception inner) : base(message, inner) { }
}
