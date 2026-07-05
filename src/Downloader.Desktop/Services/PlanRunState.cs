using System;
using System.Threading;

namespace Downloader.Desktop.Services;

/// <summary>
/// Live, thread-safe progress board for a multi-part plan run: one slot per part (segment). The plan
/// runner writes from download threads; the details dialog polls it to render every segment as a
/// "connection" row — waiting / downloading (up to the parallel cap) / completed — instead of the
/// engine's per-part chunks (each segment is single-chunk, so those would show one resetting row).
/// </summary>
public sealed class PlanRunState
{
    public enum PartState : byte { Waiting = 0, Active = 1, Done = 2 }

    private readonly double[] _fraction;
    private readonly double[] _speed;
    private readonly long[] _received;
    private readonly long[] _total;
    private readonly int[] _state;

    public PlanRunState(int count)
    {
        Count = count;
        _fraction = new double[count];
        _speed = new double[count];
        _received = new long[count];
        _total = new long[count];
        _state = new int[count];
    }

    public int Count { get; }

    public void SetTotal(int index, long total) => Interlocked.Exchange(ref _total[index], total);

    public void SetActive(int index) => Interlocked.Exchange(ref _state[index], (int)PartState.Active);

    public void Report(int index, double fraction, double speed, long received)
    {
        _fraction[index] = fraction;
        _speed[index] = speed;
        Interlocked.Exchange(ref _received[index], received);
    }

    public void SetDone(int index, long finalBytes = 0)
    {
        _fraction[index] = 1;
        _speed[index] = 0;
        if (finalBytes > 0)
        {
            Interlocked.Exchange(ref _received[index], finalBytes);
            Interlocked.Exchange(ref _total[index], finalBytes);
        }
        Interlocked.Exchange(ref _state[index], (int)PartState.Done);
    }

    public (PartState State, double Fraction, double Speed, long Received, long Total) Get(int index) =>
        ((PartState)Volatile.Read(ref _state[index]), _fraction[index], _speed[index],
            Volatile.Read(ref _received[index]), Volatile.Read(ref _total[index]));
}
