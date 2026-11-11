using System.Collections.Generic;

namespace nomadicooer.AutoTranslator;

internal static class TranslationProgress
{
    private static readonly object sync = new();
    private static readonly HashSet<string> captured = new();
    private static readonly HashSet<string> completed = new();
    private static readonly Dictionary<string, int> pending = new();
    [System.ThreadStatic] public static bool Drawing;

    public static void Reset()
    {
        lock (sync)
        {
            captured.Clear();
            completed.Clear();
            pending.Clear();
        }
    }

    public static void Complete(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        lock (sync)
        {
            captured.Add(source);
            completed.Add(source);
            pending.Remove(source);
        }
    }

    public static void Capture(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        lock (sync)
        {
            captured.Add(source);
            if (!completed.Contains(source)) pending[source] = 1;
        }
    }

    public static bool IsPending(string source)
    {
        lock (sync) return pending.ContainsKey(source);
    }

    public static void Enqueue(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        lock (sync)
        {
            captured.Add(source);
            pending.TryGetValue(source, out int count);
            pending[source] = count + 1;
        }
    }

    public static void Finish(string source, bool success)
    {
        if (string.IsNullOrEmpty(source)) return;
        lock (sync)
        {
            if (success)
            {
                pending.Remove(source);
                completed.Add(source);
            }
            else if (!completed.Contains(source))
            {
                // A failed request is no longer in flight, but it remains untranslated
                // and will be retried when the source is observed after its backoff.
                pending[source] = 1;
            }
        }
    }

    public static (int completed, int total, int pending) Snapshot()
    {
        lock (sync) return (completed.Count, captured.Count, pending.Count);
    }
}
