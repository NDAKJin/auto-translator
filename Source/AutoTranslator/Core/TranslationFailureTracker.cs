using System;
using System.Collections.Concurrent;

namespace nomadicooer.AutoTranslator;

/// <summary>按目标语言与原文记录实际翻译失败次数。</summary>
internal sealed class TranslationFailureTracker
{
    private readonly ConcurrentDictionary<string, int> failures = new(StringComparer.Ordinal);
    public const int Limit = 3;

    public int Record(string target, string source) =>
        failures.AddOrUpdate(target + "\0" + source, 1, (_, count) => Math.Min(Limit, count + 1));

    public void Clear() => failures.Clear();
}
