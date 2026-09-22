using System.Threading.Tasks;
using Avalonia.Headless;
using Downloader.Desktop.Tests;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(HeadlessSessionFirst))]

namespace Downloader.Desktop.Tests;

/// <summary>
/// Starts the headless session before any test runs, so that <c>Dispatcher.UIThread</c> is bound to the
/// session's own thread.
///
/// <para><b>This is the fix for the recurring CI "test host hang".</b> <c>Dispatcher.UIThread</c> is a
/// process-global singleton that binds to whichever thread reaches it FIRST. Tests run sequentially but
/// their order is not fixed, so whenever a plain <c>[Fact]</c> that reaches dispatcher-touching
/// production code (<c>DownloadManager.OnUi</c>, the UI pump, the tray/notch services) happened to run
/// before the first <c>[AvaloniaFact]</c>, the singleton bound to the xunit thread. The session thread's
/// <c>EnsureSharedApplication()</c> → <c>SetupUnsafe()</c> → <c>new Compositor(...)</c> →
/// <c>RenderLoop.Add</c> then failed its own <c>Dispatcher.VerifyAccess()</c>, faulting the shared
/// session's dispatch loop — after which every later test awaited a completion source nothing would ever
/// set. Nothing fails and nothing times out; the run just stops, and the abort blames whichever innocent
/// test was next in line.</para>
///
/// <para>Measured, not assumed (2026-09-21). Two arms, one variable, each in its own process because the
/// binding is a process-global one-shot: touching <c>Dispatcher.UIThread</c> before starting the session
/// hung a single trivial test until it was killed at 240 s; the identical control without that touch
/// passed in 173 ms.</para>
///
/// <para><b>Why a pipeline startup and not a <c>[ModuleInitializer]</c>:</b> that was tried first and it
/// broke the run outright — a module initializer also runs in the DISCOVERY process, where building the
/// Avalonia application blocks xunit v3's handshake ("Test process did not respond within 60 seconds").
/// <c>ITestPipelineStartup</c> runs once at execution start only.</para>
///
/// <para>Why here and not a rule about which tests may touch the dispatcher: the offending call is
/// ordinary production code doing the right thing (marshalling to the UI thread), reached through
/// hundreds of call sites, and a convention would have to hold at every one of them forever. Binding the
/// dispatcher first makes the order irrelevant instead — the failure mode stops existing rather than
/// being avoided. <c>GetOrStartForAssembly</c> is the same call <c>[AvaloniaFact]</c> itself makes, so
/// every later test shares this session; running it first only settles WHICH thread wins the race.</para>
///
/// <para>This does not replace <see cref="ThreadAffinityWatch"/>: that still prints the violation's call
/// site if one is ever raised again.</para>
/// </summary>
public sealed class HeadlessSessionFirst : ITestPipelineStartup
{
    public async ValueTask StartAsync(IMessageSink diagnosticMessageSink)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestAppBuilder).Assembly);

        // The DISPATCH is the point, not the start. EnsureSharedApplication() is called from
        // DispatchCore — lazily, on the first dispatch — so merely starting the session binds nothing
        // and the first [AvaloniaFact] would still be racing whatever touched the dispatcher first.
        // (Measured: starting without dispatching left the hang exactly as it was.)
        await session.Dispatch(() => Task.CompletedTask, default);
    }

    public ValueTask StopAsync() => default;
}
