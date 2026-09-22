using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Names the call site of the thread-affinity violation that kills CI runs.
///
/// <para>The recurring "test host hung" abort (Windows/macOS, a few runs in every dozen) always looks the
/// same: the run stops mid-suite, the blamed test is an innocent fast one, and the hang dump shows every
/// xunit worker parked in <c>AvaloniaTestCase.Run</c> with no dispatcher alive. The 2026-09-11 dump
/// (run 34609494951) finally gave the reason — the headless session's dispatch loop is FAULTED, holding
/// <c>InvalidOperationException: The calling thread cannot access this object because a different thread
/// owns it</c>. Once that loop is dead, every later test waits on a completion source nothing will set,
/// so nothing fails and nothing times out: it just stops.</para>
///
/// <para>What the dump could NOT say is WHERE it was thrown: the captured exception carries no stack
/// (<c>StackTraceString: &lt;none&gt;</c>). A first-chance handler sees the exception at the moment it is
/// raised, stack intact, so the next occurrence prints the offending call site straight into the CI log —
/// which is the one piece of evidence this hunt has never had.</para>
///
/// <para>Read-only: it prints and returns, changing nothing about how the exception propagates.</para>
/// </summary>
internal static class ThreadAffinityWatch
{
    [ModuleInitializer]
    internal static void Install() =>
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;

    private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
    {
        // Only the one shape. Every other first-chance exception is ordinary traffic (tests deliberately
        // throw plenty), and printing those would bury the signal.
        if (e.Exception is not InvalidOperationException ex ||
            ex.Message?.Contains("different thread owns it", StringComparison.Ordinal) != true)
            return;

        try
        {
            Console.Error.WriteLine("=== THREAD-AFFINITY VIOLATION (the CI hang's cause) ===");
            Console.Error.WriteLine($"    thread {Environment.CurrentManagedThreadId}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace ?? "    <no stack>");
            Console.Error.WriteLine("=== end ===");
            Console.Error.Flush();
        }
        catch
        {
            // A diagnostic must never become the failure it is diagnosing.
        }
    }
}
