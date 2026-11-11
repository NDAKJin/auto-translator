using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace nomadicooer.AutoTranslator;

internal static class VisibleTextTranslations
{
    private static readonly Dictionary<string, string> translations = new();
    private static readonly Dictionary<string, string> keys = new();
    private static readonly HashSet<string> generated = new();
    private static readonly Dictionary<string, DateTime> nextAttempt = new();
    private static string language;

    public static void ClearCache()
    {
        translations.Clear();
        keys.Clear();
        generated.Clear();
        nextAttempt.Clear();
        TranslationProgress.Reset();
    }

    public static string Observe(string source)
    {
        if (!PlayDataLoader.Loaded || !AutoTranslatorMod.AutoTranslateEnabled
            || LanguageDatabase.activeLanguage == null
            || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage) return source;

        string target = LanguageDatabase.activeLanguage.folderName;
        if (language != target)
        {
            ClearCache();
            language = target;
            AutoTranslatorEngine.EnsureSourceCacheLoaded(target);
            foreach (string saved in AutoTranslationStore.GetSavedTranslations()) generated.Add(saved);
        }
        if (string.IsNullOrWhiteSpace(source)) return source;
        AutoTranslatorEngine.EnsureSourceCacheLoaded(target);
        if (SourceTranslationCache.TryGet(target, source, out string cached))
        {
            Remember(source, cached);
            return cached;
        }
        if (generated.Contains(source)
            || !VisibleTextFilter.ShouldTranslate(source, target)) return source;

        TranslationProgress.Capture(source);
        if (translations.TryGetValue(source, out string translated)) return translated;

        if (!keys.TryGetValue(source, out string key))
        {
            key = Key(source);
            keys[source] = key;
        }
        if (AutoTranslationStore.TryGetTranslation(key, out translated))
        {
            Remember(source, translated);
            return translated;
        }

        AutoTranslationStore.RecordSource(key, source);
        if (AutoTranslatorMod.RealtimeUpdateEnabled
            && (!nextAttempt.TryGetValue(source, out DateTime next) || next <= DateTime.UtcNow))
        {
            if (AutoTranslatorEngine.RequestVisibleTranslate(key, source, target, result =>
            {
                if (language != target || !AutoTranslatorMod.AutoTranslateEnabled) return;
                if (result.Decision == TranslationDecision.NotNeeded)
                {
                    RememberUnchanged(source);
                    AutoTranslationStore.RecordTranslation(key, source);
                    return;
                }
                if (result.Decision != TranslationDecision.Success) return;
                string chosen = SourceTranslationCache.Remember(target, source, result.TranslatedText);
                Remember(source, chosen);
                AutoTranslationStore.RecordTranslation(key, chosen);
            })) nextAttempt[source] = DateTime.UtcNow.AddMinutes(1);
        }
        return source;
    }

    private static void Remember(string source, string translated)
    {
        translated = SourceTranslationCache.Remember(language, source, translated);
        translations[source] = translated;
        generated.Add(translated);
        TranslationProgress.Complete(source);
    }

    private static void RememberUnchanged(string source)
    {
        SourceTranslationCache.Remember(language, source, source);
        translations[source] = source;
        generated.Add(source);
        TranslationProgress.Complete(source);
    }

    private static string Key(string source)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
        return "auto.ui." + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
