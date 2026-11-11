using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace nomadicooer.AutoTranslator;

// Def labels are read through LabelCap in ordinary game UI. Observe the description with
// the label so both fields can be translated without scanning every loaded mod at startup.
[HarmonyPatch(typeof(Def), nameof(Def.LabelCap), MethodType.Getter)]
internal static class DefTranslationPatch
{
    private static readonly FieldInfo CachedLabelCap = AccessTools.Field(typeof(Def), "cachedLabelCap");
    private static readonly Dictionary<string, DateTime> nextAttempt = new();
    private static readonly Dictionary<string, OriginalText> originals = new();

    private sealed class OriginalText
    {
        public Def Def;
        public string Field;
        public string Source;
    }

    private static void Postfix(Def __instance)
    {
        if (!PlayDataLoader.Loaded || !AutoTranslatorMod.AutoTranslateEnabled
            || LanguageDatabase.activeLanguage == null
            || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage
            || __instance?.modContentPack == null || __instance.modContentPack.IsOfficialMod
            || __instance.modContentPack.PackageId == AutoTranslatorGlobals.ModId
            || string.IsNullOrWhiteSpace(__instance.defName))
        {
            return;
        }

        string target = LanguageDatabase.activeLanguage.folderName;
        string defType = __instance.GetType().Name;
        Observe(__instance, defType, "label", __instance.label, target);
        Observe(__instance, defType, "description", __instance.description, target);
    }

    private static void Observe(Def def, string defType, string field, string source, string target)
    {
        if (!VisibleTextFilter.ShouldTranslate(source, target)) return;
        string key = target + "\0" + DefTranslationXml.Key(defType, def.defName, field);
        if (!originals.ContainsKey(key))
        {
            string original = DefTranslationStore.TryGetSource(defType, def.defName, field, out string savedSource)
                ? savedSource : source;
            originals[key] = new OriginalText { Def = def, Field = field, Source = original };
        }
        if (nextAttempt.TryGetValue(key, out DateTime next) && DateTime.UtcNow < next) return;
        if (HasInjection(def, field))
        {
            nextAttempt[key] = DateTime.MaxValue;
            return;
        }

        AutoTranslatorEngine.EnsureSourceCacheLoaded(target);
        if (SourceTranslationCache.TryGet(target, source, out string reusable))
        {
            Apply(def, field, reusable);
            nextAttempt[key] = DateTime.MaxValue;
            return;
        }

        if (DefTranslationStore.TryGetTranslation(defType, def.defName, field, out string saved))
        {
            Apply(def, field, saved);
            nextAttempt[key] = DateTime.MaxValue;
            return;
        }

        DefTranslationStore.RecordSource(defType, def.defName, field, source);
        nextAttempt[key] = DateTime.UtcNow.AddMinutes(1);
        if (!AutoTranslatorMod.RealtimeUpdateEnabled) return;

        AutoTranslatorEngine.RequestDefTranslate(key, source,
            LanguageDatabase.defaultLanguage.folderName, target, translated =>
            {
                if (LanguageDatabase.activeLanguage?.folderName != target || HasInjection(def, field)
                    || GetText(def, field) != source) return;
                DefTranslationStore.RecordTranslation(defType, def.defName, field, source, translated);
                Apply(def, field, SourceTranslationCache.Remember(target, source, translated));
                nextAttempt[key] = DateTime.MaxValue;
            });
    }

    private static bool HasInjection(Def def, string field)
    {
        string path = def.defName + "." + field;
        foreach (DefInjectionPackage package in LanguageDatabase.activeLanguage.defInjections)
        {
            if (package.defType == def.GetType()
                && package.injections.TryGetValue(path, out DefInjectionPackage.DefInjection injection)
                && injection.injected && !injection.isPlaceholder)
            {
                return true;
            }
        }
        return false;
    }

    private static string GetText(Def def, string field) => field == "label" ? def.label : def.description;

    private static void Apply(Def def, string field, string translated)
    {
        if (field == "label")
        {
            def.label = translated;
            CachedLabelCap?.SetValue(def, default(TaggedString));
        }
        else
        {
            def.description = translated;
        }
    }

    public static void ClearCache(List<DefTranslationRecord> records)
    {
        nextAttempt.Clear();
        foreach (DefTranslationRecord record in records)
        {
            string key = (LanguageDatabase.activeLanguage?.folderName ?? string.Empty) + "\0"
                + DefTranslationXml.Key(record.DefType, record.DefName, record.Field);
            if (!originals.TryGetValue(key, out OriginalText original)) continue;
            if (original.Field == "label")
            {
                original.Def.label = original.Source;
                CachedLabelCap?.SetValue(original.Def, default(TaggedString));
            }
            else
            {
                original.Def.description = original.Source;
            }
        }
        originals.Clear();
    }
}
