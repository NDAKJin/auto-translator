using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

internal static class DefTranslationStore
{
    private static readonly object sync = new();
    private static readonly object flushSync = new();
    private static readonly Dictionary<string, DefTranslationRecord> records = new();
    private static string language;
    private static bool dirty;
    private static Timer flushTimer;
    private static bool exitHookRegistered;

    public static bool TryGetTranslation(string defType, string defName, string field, out string translation)
    {
        EnsureLoaded();
        lock (sync)
        {
            if (records.TryGetValue(DefTranslationXml.Key(defType, defName, field), out DefTranslationRecord record)
                && record.Translated)
            {
                translation = record.Translation;
                return true;
            }
        }
        translation = null;
        return false;
    }

    public static bool TryGetSource(string defType, string defName, string field, out string source)
    {
        EnsureLoaded();
        lock (sync)
        {
            if (records.TryGetValue(DefTranslationXml.Key(defType, defName, field), out DefTranslationRecord record)
                && !string.IsNullOrEmpty(record.Source))
            {
                source = record.Source;
                return true;
            }
        }
        source = null;
        return false;
    }

    public static void LoadSourceTranslations(string target)
    {
        EnsureLoaded();
        lock (sync)
        {
            foreach (DefTranslationRecord record in records.Values)
                if (record.Translated && !string.IsNullOrEmpty(record.Source))
                    SourceTranslationCache.Remember(target, record.Source, record.Translation);
        }
    }

    public static void RecordSource(string defType, string defName, string field, string source)
    {
        if (AutoTranslatorMod.Settings?.enableTranslationLog != true || string.IsNullOrWhiteSpace(source)) return;
        EnsureLoaded();
        lock (sync)
        {
            string key = DefTranslationXml.Key(defType, defName, field);
            if (records.ContainsKey(key)) return;
            records.Add(key, new DefTranslationRecord
            {
                DefType = defType,
                DefName = defName,
                Field = field,
                Source = source
            });
            dirty = true;
        }
        ScheduleFlush();
    }

    public static void RecordTranslation(string defType, string defName, string field, string source, string translation)
    {
        EnsureLoaded();
        lock (sync)
        {
            string key = DefTranslationXml.Key(defType, defName, field);
            records.TryGetValue(key, out DefTranslationRecord existing);
            records[key] = new DefTranslationRecord
            {
                DefType = defType,
                DefName = defName,
                Field = field,
                Source = existing?.Source ?? source,
                Translation = translation
            };
            if (AutoTranslatorMod.Settings?.enableTranslationLog == true) dirty = true;
        }
        if (AutoTranslatorMod.Settings?.enableTranslationLog == true) ScheduleFlush();
    }

    private static void EnsureLoaded()
    {
        LoadedLanguage active = LanguageDatabase.activeLanguage;
        string target = active == null || active == LanguageDatabase.defaultLanguage ? null : active.folderName;
        if (target == language) return;
        Flush();
        lock (sync)
        {
            records.Clear();
            language = target;
            dirty = false;
            if (target == null) return;
            string path = RecordPath(target);
            if (!File.Exists(path)) return;
            try
            {
                foreach (DefTranslationRecord record in DefTranslationXml.ReadRecords(File.ReadAllText(path)))
                {
                    records[DefTranslationXml.Key(record.DefType, record.DefName, record.Field)] = record;
                }
            }
            catch (Exception ex)
            {
                AutoLog.Warn("读取 Def 翻译记录失败", ex);
            }
        }
    }

    private static string RecordPath(string target) => Path.Combine(
        AutoTranslatorGlobals.ModRootDir, "DefTranslations", target, "auto.xml");

    private static string InjectionPath(string target, string defType) => Path.Combine(
        AutoTranslatorGlobals.ModRootDir, "Languages", target, "DefInjected", defType, "AutoTranslator.xml");

    public static List<DefTranslationRecord> ClearCurrentLanguage()
    {
        EnsureLoaded();
        lock (flushSync)
        {
            lock (sync)
            {
                if (language == null) return new List<DefTranslationRecord>();
                string recordPath = RecordPath(language);
                string injectionRoot = Path.Combine(AutoTranslatorGlobals.ModRootDir,
                    "Languages", language, "DefInjected");
                if (File.Exists(recordPath)) File.Delete(recordPath);
                if (Directory.Exists(injectionRoot))
                {
                    foreach (string path in Directory.GetFiles(injectionRoot,
                                 "AutoTranslator.xml", SearchOption.AllDirectories)) File.Delete(path);
                }
                flushTimer?.Dispose();
                flushTimer = null;
                var removed = records.Values.ToList();
                records.Clear();
                dirty = false;
                return removed;
            }
        }
    }

    private static void ScheduleFlush()
    {
        lock (sync)
        {
            if (!exitHookRegistered)
            {
                exitHookRegistered = true;
                Application.quitting += Flush;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            }
            if (flushTimer != null) return;
            flushTimer = new Timer(_ =>
            {
                Flush();
                lock (sync)
                {
                    flushTimer?.Dispose();
                    flushTimer = null;
                    if (dirty) ScheduleFlush();
                }
            }, null, 10000, Timeout.Infinite);
        }
    }

    public static void Flush()
    {
        lock (flushSync)
        {
            DefTranslationRecord[] snapshot;
            string target;
            lock (sync)
            {
                if (!dirty || language == null) return;
                target = language;
                snapshot = records.Values.ToArray();
                dirty = false;
            }
            try
            {
                string recordPath = RecordPath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(recordPath));
                File.WriteAllText(recordPath, DefTranslationXml.WriteRecords(snapshot));
                foreach (IGrouping<string, DefTranslationRecord> group in snapshot.Where(r => r.Translated).GroupBy(r => r.DefType))
                {
                    string path = InjectionPath(target, group.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, DefTranslationXml.WriteDefInjected(group));
                }
            }
            catch (Exception ex)
            {
                lock (sync) dirty = true;
                AutoLog.Warn("写入 Def 翻译文件失败", ex);
            }
        }
    }
}
