using HarmonyLib;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// Harmony 补丁：拦截官方 Translator.TryTranslate 失败（即官方无该 key 译文）的情形。
/// ① 记录该 key 与英文原文（默认语言包中取）到 auto.xml（受 enableTranslationLog 开关控制）；
/// ② 若启用实时翻译，把原文提交引擎异步翻译并回写 keyedReplacements 使界面即时显示。
/// 以新逻辑为主：RimWorld 1.6 的 TryTranslate 为扩展方法 (string key, out TaggedString result)，
/// 回写目标为 LanguageDatabase.activeLanguage.keyedReplacements（实例字典，非旧版静态字段）。
/// </summary>
[HarmonyPatch(typeof(Translator), nameof(Translator.TryTranslate))]
internal static class TranslationPatch
{
    /// <summary>官方翻译失败时（out 结果回退为 key 或为空）触发记录与回写。</summary>
    private static void Postfix(string key, ref TaggedString __result)
    {
        // 总开关关闭则不拦截。
        if (!AutoTranslatorMod.AutoTranslateEnabled)
        {
            return;
        }
        // 官方翻译成功（拿到了非 key 的译文）不处理。
        if (!__result.NullOrEmpty() && __result != key)
        {
            return;
        }

        // 原文取英文基准（默认语言包）；无英文基准则回退为 key 本身。
        string source = key;
        if (LanguageDatabase.defaultLanguage != null
            && LanguageDatabase.defaultLanguage.TryGetTextFromKey(key, out TaggedString english))
        {
            source = english;
        }

        // 记录 key 与原文（受 enableTranslationLog 开关，Store 内部 Writable 控制是否落盘）。
        AutoTranslationStore.RecordSource(key, source);

        // 启用实时翻译则提交引擎异步翻译并回写。
        if (AutoTranslatorMod.RealtimeUpdateEnabled)
        {
            AutoTranslatorEngine.RequestTranslate(key, source);
        }
    }
}
