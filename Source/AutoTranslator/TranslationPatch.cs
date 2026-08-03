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
    /// <summary>拦截官方 TryTranslate：查本模组是否已翻译，已译则回写译文直接返回；否则记录并提交实时翻译。</summary>
    private static void Postfix(string key)
    {
        // 总开关关闭则不拦截。
        if (!AutoTranslatorMod.AutoTranslateEnabled)
        {
            return;
        }
        // key 为空无意义，直接跳过。
        if (key == null)
        {
            return;
        }
        // 查我们是否已翻译：已翻译则回写已存译文让界面生效并直接返回，不重复记录/提交。
        // 不再以官方返回值（__result）作为是否接手的判断依据。
        if (AutoTranslationStore.IsTranslated(key)
            && AutoTranslationStore.TryGetTranslation(key, out string cached)
            && !cached.NullOrEmpty())
        {
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (active != null && active.keyedReplacements != null)
            {
                active.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
                {
                    key = key,
                    value = cached,
                };
            }
            return;
        }
        // 老版逻辑：激活语言就是默认语言（English）时无需翻译/记录。
        if (LanguageDatabase.activeLanguage == null
            || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage)
        {
            return;
        }

        // 原文必须取自默认语言包（英文基准）；取不到则直接打回，不记录无基准的 key。
        if (LanguageDatabase.defaultLanguage == null
            || !LanguageDatabase.defaultLanguage.TryGetTextFromKey(key, out TaggedString english))
        {
            return;
        }
        string source = english;

        // 记录 key 与原文（受 enableTranslationLog 开关，Store 内部 Writable 控制是否落盘）。
        AutoTranslationStore.RecordSource(key, source);

        // 启用实时翻译则提交引擎异步翻译并回写。
        if (AutoTranslatorMod.RealtimeUpdateEnabled)
        {
            AutoTranslatorEngine.RequestTranslate(key, source);
        }
    }
}
