namespace NexaDock.Core.Productivity;

public enum FocusPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>
/// Pomodoro state machine. Time is read from <see cref="TimeProvider"/> so it is exact across sleeps
/// and testable; call <see cref="Tick"/> regularly to detect phase completion.
/// </summary>
public sealed class FocusSession(TimeProvider time)
{
    public const int RoundsBeforeLongBreak = 4;

    private DateTimeOffset _endsAt;
    private TimeSpan _remainingWhenPaused;

    public FocusPhase Phase { get; private set; } = FocusPhase.Idle;

    public bool IsRunning { get; private set; }

    /// <summary>Focus rounds finished since the last reset.</summary>
    public int CompletedRounds { get; private set; }

    public TimeSpan FocusLength { get; set; } = TimeSpan.FromMinutes(25);

    public TimeSpan ShortBreakLength { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LongBreakLength { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan Remaining => Phase == FocusPhase.Idle
        ? FocusLength
        : IsRunning ? Max(_endsAt - time.GetUtcNow()) : _remainingWhenPaused;

    public TimeSpan PhaseLength => Phase switch
    {
        FocusPhase.ShortBreak => ShortBreakLength,
        FocusPhase.LongBreak => LongBreakLength,
        _ => FocusLength,
    };

    /// <summary>0..1 progress through the current phase.</summary>
    public double Progress => PhaseLength <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining / PhaseLength, 0, 1);

    public void Start()
    {
        if (Phase == FocusPhase.Idle)
        {
            Begin(FocusPhase.Focus);
        }
        else if (!IsRunning)
        {
            _endsAt = time.GetUtcNow() + _remainingWhenPaused;
            IsRunning = true;
        }
    }

    public void Pause()
    {
        if (IsRunning)
        {
            _remainingWhenPaused = Max(_endsAt - time.GetUtcNow());
            IsRunning = false;
        }
    }

    /// <summary>Ends the current phase now and moves on (counts a skipped focus round as done).</summary>
    public FocusPhase Skip() => Advance(countRound: Phase == FocusPhase.Focus);

    public void Reset()
    {
        Phase = FocusPhase.Idle;
        IsRunning = false;
        CompletedRounds = 0;
    }

    /// <summary>Returns the phase that just finished, or <c>null</c>.</summary>
    public FocusPhase? Tick()
    {
        if (!IsRunning || time.GetUtcNow() < _endsAt)
        {
            return null;
        }

        var finished = Phase;
        Advance(countRound: finished == FocusPhase.Focus);
        return finished;
    }

    private FocusPhase Advance(bool countRound)
    {
        if (Phase == FocusPhase.Idle)
        {
            return Phase;
        }

        if (countRound)
        {
            CompletedRounds++;
        }

        var next = Phase == FocusPhase.Focus
            ? (CompletedRounds % RoundsBeforeLongBreak == 0 ? FocusPhase.LongBreak : FocusPhase.ShortBreak)
            : FocusPhase.Focus;
        Begin(next);
        return next;
    }

    private void Begin(FocusPhase phase)
    {
        Phase = phase;
        _endsAt = time.GetUtcNow() + PhaseLength;
        _remainingWhenPaused = PhaseLength;
        IsRunning = true;
    }

    private static TimeSpan Max(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}

/// <summary>A simple countdown.</summary>
public sealed class Countdown(TimeProvider time)
{
    private DateTimeOffset _endsAt;
    private TimeSpan _remainingWhenPaused = TimeSpan.FromMinutes(5);

    public TimeSpan Length { get; private set; } = TimeSpan.FromMinutes(5);

    public bool IsRunning { get; private set; }

    public bool IsFinished { get; private set; }

    public TimeSpan Remaining => IsRunning ? Max(_endsAt - time.GetUtcNow()) : _remainingWhenPaused;

    public double Progress => Length <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining / Length, 0, 1);

    public void Set(TimeSpan length)
    {
        Length = length < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : length;
        _remainingWhenPaused = Length;
        IsRunning = false;
        IsFinished = false;
    }

    public void Start()
    {
        if (IsFinished || _remainingWhenPaused <= TimeSpan.Zero)
        {
            Set(Length);
        }

        _endsAt = time.GetUtcNow() + _remainingWhenPaused;
        IsRunning = true;
    }

    public void Pause()
    {
        if (IsRunning)
        {
            _remainingWhenPaused = Remaining;
            IsRunning = false;
        }
    }

    public void Reset() => Set(Length);

    /// <summary>True exactly once, when the countdown reaches zero.</summary>
    public bool Tick()
    {
        if (!IsRunning || time.GetUtcNow() < _endsAt)
        {
            return false;
        }

        IsRunning = false;
        IsFinished = true;
        _remainingWhenPaused = TimeSpan.Zero;
        return true;
    }

    private static TimeSpan Max(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}

/// <summary>Stopwatch with laps.</summary>
public sealed class LapStopwatch(TimeProvider time)
{
    private readonly List<TimeSpan> _laps = [];
    private DateTimeOffset _startedAt;
    private TimeSpan _accumulated;

    public bool IsRunning { get; private set; }

    public TimeSpan Elapsed => _accumulated + (IsRunning ? time.GetUtcNow() - _startedAt : TimeSpan.Zero);

    /// <summary>Lap split times (time of each lap, not cumulative), newest last.</summary>
    public IReadOnlyList<TimeSpan> Laps => _laps;

    public void Start()
    {
        if (!IsRunning)
        {
            _startedAt = time.GetUtcNow();
            IsRunning = true;
        }
    }

    public void Pause()
    {
        if (IsRunning)
        {
            _accumulated += time.GetUtcNow() - _startedAt;
            IsRunning = false;
        }
    }

    public void Lap()
    {
        var total = Elapsed;
        var previous = _laps.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        if (total > previous)
        {
            _laps.Add(total - previous);
        }
    }

    public void Reset()
    {
        IsRunning = false;
        _accumulated = TimeSpan.Zero;
        _laps.Clear();
    }
}

/// <summary>A to-do item (persisted as JSON in the local store).</summary>
public sealed record TodoItem(string Id, string Text, bool Done, DateTimeOffset Created);
