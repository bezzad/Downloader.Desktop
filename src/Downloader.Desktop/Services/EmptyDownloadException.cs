using System;

namespace Downloader.Desktop.Services;

/// <summary>
/// A download the engine reported as finished that produced no file, or an empty one.
/// <para>
/// It is an exception so that it travels the normal failure path — another address is exactly what might
/// fix it — but its own TYPE, so the row can say what actually happened. Reporting it as one of the HTTP
/// statuses would inherit "this link has expired", which is a different problem with a different fix.
/// </para>
/// </summary>
public sealed class EmptyDownloadException : Exception
{
    public EmptyDownloadException(string message) : base(message) { }
}
