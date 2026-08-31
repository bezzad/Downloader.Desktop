using System;

namespace Downloader.Desktop.Services;

/// <summary>
/// An attempt that stopped responding: no progress and no completion for long enough that nothing else
/// was ever going to end it.
/// <para>
/// The engine normally reports every outcome, but against a server that refuses every request it can
/// finish without raising a completion at all — the awaited task returns and the row stays "downloading"
/// for ever, with no error and nothing to retry. This is the app noticing that on its own, so the same
/// recovery (another address, an honest failure) applies as for any other failed attempt.
/// </para>
/// </summary>
public sealed class DownloadStalledException : Exception
{
    public DownloadStalledException(string message) : base(message) { }
}
