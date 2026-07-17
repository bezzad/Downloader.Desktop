namespace Downloader.Desktop.Tests;

/// <summary>
/// The default per-test timeout applied to every test via <c>Timeout = TestTimeouts.DefaultMs</c> on the
/// Fact/Theory/AvaloniaFact/AvaloniaTheory attributes. A hung test then fails fast and visibly instead of
/// stalling (or silently killing) the whole run. Generous on purpose — it catches genuine hangs, not
/// slow-but-progressing tests. One place to change the value; a single test that legitimately needs longer
/// can override its own <c>Timeout = N</c>.
/// </summary>
public static class TestTimeouts
{
    /// <summary>Default per-test timeout, in milliseconds.</summary>
    public const int DefaultMs = 60_000;

    /// <summary>A longer cap for the few genuinely slow tests (e.g. ones that bind many HttpListeners,
    /// which is markedly slow on the macOS CI runner — one took ~1m17s). Still bounded so a real hang fails.</summary>
    public const int SlowMs = 180_000;
}
