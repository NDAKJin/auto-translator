using System;
using System.Collections.Generic;

namespace nomadicooer.AutoTranslator;

/// <summary>按语言和完整原文保存可复用的译文，人工校验的记录优先。</summary>
internal static class SourceTranslationCache
{
    private sealed class Entry { public string Text; public bool Verified; }
    private static readonly object sync = new();
    private static readonly Dictionary<string, Dictionary<string, Entry>> languages = new(StringComparer.Ordinal);

    public static bool TryGet(string language, string source, out string translation)
    {
        lock (sync)
        {
            if (language != null && source != null && languages.TryGetValue(language, out var entries)
                && entries.TryGetValue(source, out var entry))
            {
                translation = entry.Text;
                return true;
            }
        }
        translation = null;
        return false;
    }

    public static string Remember(string language, string source, string translation, bool verified = false)
    {
        if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(translation)) return translation;
        lock (sync)
        {
            if (!languages.TryGetValue(language, out var entries))
                languages[language] = entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (entries.TryGetValue(source, out var existing))
            {
                if (!verified || existing.Verified) return existing.Text;
            }
            entries[source] = new Entry { Text = translation, Verified = verified };
            return translation;
        }
    }

    public static void Clear()
    {
        lock (sync) languages.Clear();
    }
}
