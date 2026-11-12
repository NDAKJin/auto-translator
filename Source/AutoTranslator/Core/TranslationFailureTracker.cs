using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace nomadicooer.AutoTranslator;

/// <summary>按目标语言与原文记录实际翻译失败次数。</summary>
internal sealed class TranslationFailureTracker
{
    private readonly ConcurrentDictionary<string, int> failures = new(StringComparer.Ordinal);
    public const int Limit = 3;

    public int Record(string target, string source) =>
        failures.AddOrUpdate(target + "\0" + source, 1, (_, count) => Math.Min(Limit, count + 1));

    public bool IsExhausted(string target, string source) => failures.TryGetValue(target + "\0" + source, out int count) && count >= Limit;
    public void Remove(string target, string source) => failures.TryRemove(target + "\0" + source, out _);
    public List<string> List(string target)
    {
        var result = new List<string>();
        string prefix = target + "\0";
        foreach (var entry in failures)
            if (entry.Value >= Limit && entry.Key.StartsWith(prefix, StringComparison.Ordinal)) result.Add(entry.Key.Substring(prefix.Length));
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    public void Clear() => failures.Clear();
}
