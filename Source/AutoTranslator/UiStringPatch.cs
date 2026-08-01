using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 统一拦截 UI 硬编码字符串（mod 作者直接写死在 GUI/Widgets 调用里的英文字面量，
/// 不经 Translator.TryTranslate，故 TranslationPatch 拦不到）。
/// 机制：以"字符串签名"作为自定义翻译 key，原文作 value 写入 auto.xml，
/// 与 keyed 翻译系统共用同一套落盘/持久化（AutoXmlWriter），但译文落地走本类自管内存缓存
/// （UiCache），不污染 keyedReplacements——因为这里是我们在 prefix 里主动替换显示参数。
///
/// 覆盖范围：仅 Verse.Widgets 中接收 string label 的重载（Label / CheckboxLabeled /
/// ButtonText / ButtonTextSubtle）。不拦 UnityEngine.GUI（原生 UI 多已本地化，且调用量巨大有性能风险），
/// 不拦 TaggedString / GUIContent 重载（走 keyed 系统，无需重复拦截）。
///
/// 显示策略：prefix 把 label 替换为译文（命中时）；未命中则记录原文并提交异步翻译，
/// 下一帧（译文回来写入 UiCache 后）起显示中文，与 keyed 路径下一帧生效的时序一致。
/// </summary>
public static class UiStringPatch
{
    /// <summary>自定义 key 前缀，与 keyed 系统的 auto. 区分。</summary>
    private const string UiKeyPrefix = "ui.";

    /// <summary>
    /// UI 字符串译文内存缓存：签名 key -> 译文。值为 null 表示"已记录/翻译中"，该帧仍显示原文。
    /// 懒加载：首次拦截时从 auto.xml 载入已翻译记录，避免每帧查文件。
    /// </summary>
    private static readonly Dictionary<string, string> UiCache =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>UiCache 对应的语言 folderName；切语言后据此判定缓存失效并重建。</summary>
    private static string _cachedFolderName;

    /// <summary>
    /// 懒加载已翻译的 UI 记录进缓存（假定调用方已判定非默认语言）。
    /// 切语言（auto.xml 换目录）后旧语言译文失效，必须清空重建，否则会返回错误语言的译文。
    /// </summary>
    private static void EnsureCache()
    {
        string folder = LanguageDatabase.activeLanguage.folderName;
        if (_cachedFolderName == folder)
        {
            return;
        }

        UiCache.Clear();
        _cachedFolderName = folder;
        foreach (KeyValuePair<string, AutoXmlWriter.AutoKeyEntry> kv in AutoXmlWriter.LoadAutoKeys())
        {
            if (kv.Value.Translated && !string.IsNullOrEmpty(kv.Value.Value))
            {
                UiCache[kv.Key] = kv.Value.Value;
            }
        }
    }

    /// <summary>
    /// 由原文计算稳定自定义 key：ui.&lt;原文s引子&gt;_&lt;FNV-1a哈希8位&gt;。
    /// 原文参与前缀便于在 auto.xml 中肉眼辨识；哈希尾巴消解碰撞（跨平台确定性）。
    /// </summary>
    private static string MakeUiKey(string original)
    {
        string sane = SanitizeForElement(original);
        uint hash = Fnv1a(original);
        return UiKeyPrefix + sane + "_" + hash.ToString("x8");
    }

    /// <summary>
    /// 将任意字符串规整为合法的 XML 元素名片段：小写、去首尾空白、
    /// 非 [a-z0-9] 替换为 _、截断到 40 字符。
    /// </summary>
    private static string SanitizeForElement(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "empty";
        }

        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
            {
                sb.Append('_');
            }
        }

        string r = sb.ToString().Trim('_');
        return r.Length > 40 ? r.Substring(0, 40) : (r.Length == 0 ? "empty" : r);
    }

    /// <summary>FNV-1a 32 位哈希，跨平台确定（不依赖 string.GetHashCode）。</summary>
    private static uint Fnv1a(string s)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        uint hash = offset;
        foreach (char c in s)
        {
            hash ^= (byte)(c & 0xFF);
            hash *= prime;
            hash ^= (byte)((c >> 8) & 0xFF);
            hash *= prime;
        }

        return hash;
    }

    /// <summary>
    /// 拦截入口：返回应显示的文本。命中缓存返回译文；否则记录原文（实时翻译提交），本帧返回原文。
    /// </summary>
    private static string Resolve(string original)
    {
        if (string.IsNullOrEmpty(original)
            || !AutoTranslatorMod.AutoTranslateEnabled
            || LanguageDatabase.activeLanguage == null
            || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage)
        {
            return original;
        }

        EnsureCache();

        string key = MakeUiKey(original);
        string cached;
        if (UiCache.TryGetValue(key, out cached) && cached != null)
        {
            return cached;
        }

        // 首次见到该字符串：记录原文到 auto.xml，实时翻译开启则提交异步翻译（成功回调填缓存）。
        if (!UiCache.ContainsKey(key))
        {
            UiCache[key] = null; // 标记处理中，避免重复提交
            AutoXmlWriter.WriteAutoKey(key, original, translated: false, overwrite: false);
            if (AutoTranslatorMod.RealtimeUpdateEnabled)
            {
                // 捕获提交时的语言：切语言后旧任务回来不应污染新语言缓存。
                string folderAtSubmit = LanguageDatabase.activeLanguage.folderName;
                AutoTranslatorEngine.RequestTranslate(
                    key,
                    LanguageDatabase.defaultLanguage.folderName,
                    folderAtSubmit,
                    original,
                    (k, v) =>
                    {
                        if (LanguageDatabase.activeLanguage?.folderName == folderAtSubmit)
                        {
                            UiCache[k] = v;
                        }
                    });
            }
        }

        return original;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new Type[] { typeof(Rect), typeof(string) })]
    public static class PatchLabel
    {
        [HarmonyPrefix]
        public static void Prefix(ref string __1)
        {
            __1 = Resolve(__1);
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.CheckboxLabeled))]
    public static class PatchCheckboxLabeled
    {
        [HarmonyPrefix]
        public static void Prefix(ref string __1)
        {
            __1 = Resolve(__1);
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new Type[] { typeof(Rect), typeof(string) })]
    public static class PatchButtonText
    {
        [HarmonyPrefix]
        public static void Prefix(ref string __1)
        {
            __1 = Resolve(__1);
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextSubtle), new Type[] { typeof(Rect), typeof(string) })]
    public static class PatchButtonTextSubtle
    {
        [HarmonyPrefix]
        public static void Prefix(ref string __1)
        {
            __1 = Resolve(__1);
        }
    }
}
