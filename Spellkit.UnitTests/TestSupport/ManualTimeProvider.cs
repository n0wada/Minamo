using System.Threading;

namespace Spellkit.UnitTesting;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object syncRoot = new();
    private readonly List<ManualTimer> timers = new();
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (syncRoot)
        {
            return timestamp;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (syncRoot)
        {
            timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        ManualTimer[] snapshot;
        long now;
        lock (syncRoot)
        {
            timestamp += amount.Ticks;
            now = timestamp;
            snapshot = timers.ToArray();
        }

        foreach (var timer in snapshot)
        {
            timer.FireIfDue(now);
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (syncRoot)
        {
            timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback callback;
        private readonly ManualTimeProvider owner;
        private readonly object? state;
        private long dueAt;
        private long period;
        private bool disposed;

        public ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            this.owner = owner;
            this.callback = callback;
            this.state = state;
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed)
            {
                return false;
            }

            var now = owner.GetTimestamp();
            dueAt = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : now + dueTime.Ticks;
            this.period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
            return true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void FireIfDue(long now)
        {
            if (disposed || now < dueAt)
            {
                return;
            }

            dueAt = period > 0 ? now + period : long.MaxValue;
            callback(state);
        }
    }
}
