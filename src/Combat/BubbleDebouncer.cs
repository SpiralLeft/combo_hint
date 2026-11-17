using System.Collections.Generic;
using System.Linq;

namespace ComboHint;

// Godot ticks this on the main thread. No continuation can outlive a turn or combat.
public sealed class BubbleDebouncer<TKey>(double delaySeconds = 0.3) where TKey : notnull
{
    private sealed class Window(HashSet<MatchedTrigger> baseline, double remaining)
    {
        public HashSet<MatchedTrigger> Baseline { get; } = baseline;
        public double Remaining { get; set; } = remaining;
    }

    public sealed record DueBubble(TKey Key, IReadOnlySet<MatchedTrigger> Baseline)
    {
        public bool HasNewMatches(IEnumerable<MatchedTrigger> matches) => matches.Any(m => !Baseline.Contains(m));
    }
    private readonly Dictionary<TKey, Window> _windows = new();

    public void EnterHand(TKey key, bool cardMatches, IEnumerable<MatchedTrigger> baseline)
    {
        if (_windows.TryGetValue(key, out var window)) window.Remaining = delaySeconds;
        else if (cardMatches) _windows[key] = new Window(baseline.ToHashSet(), delaySeconds);
    }

    public List<DueBubble> Advance(double delta)
    {
        var due = new List<DueBubble>();
        foreach (var (key, window) in _windows)
        {
            window.Remaining -= delta;
            if (window.Remaining <= 0) due.Add(new DueBubble(key, window.Baseline));
        }
        foreach (var bubble in due) _windows.Remove(bubble.Key);
        return due;
    }

    public void Clear() => _windows.Clear();
}
