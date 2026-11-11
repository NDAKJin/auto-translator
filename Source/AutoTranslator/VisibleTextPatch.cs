using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

// Observe text at the IMGUI draw boundary, including text supplied by third-party Mods.
[HarmonyPatch]
internal static class VisibleTextPatch
{
    private static bool seen;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "Label", "Button", "Box", "Toggle" })
        {
            foreach (MethodInfo method in typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != name) continue;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.ParameterType == typeof(GUIContent) || parameter.ParameterType == typeof(string))
                    {
                        yield return method;
                        break;
                    }
                }
            }
        }
    }

    private static void Prefix(object[] __args)
    {
        if (TranslationProgress.Drawing) return;
        if (!seen)
        {
            seen = true;
            AutoLog.Info("界面绘制文字补丁已触发");
        }
        for (int i = 0; i < __args.Length; i++)
        {
            if (__args[i] is string source)
            {
                __args[i] = VisibleTextTranslations.Observe(source);
                continue;
            }
            if (__args[i] is not GUIContent content || string.IsNullOrEmpty(content.text)) continue;
            string translated = VisibleTextTranslations.Observe(content.text);
            if (translated != content.text)
            {
                GUIContent copy = new(content);
                copy.text = translated;
                __args[i] = copy;
            }
        }
    }
}

[HarmonyPatch]
internal static class TranslationProgressPatch
{
    private static bool seen;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(UIRoot_Entry), nameof(UIRoot.UIRootOnGUI));
        yield return AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot.UIRootOnGUI));
    }

    private static void Postfix(UIRoot __instance)
    {
        if (!seen)
        {
            seen = true;
            AutoLog.Info($"翻译计数绘制补丁已触发，事件={Event.current?.type}");
        }
        if (Event.current?.type != EventType.Repaint || !AutoTranslatorMod.AutoTranslateEnabled
            || LanguageDatabase.activeLanguage == null
            || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage) return;

        (int completed, int total, int pending) = TranslationProgress.Snapshot();
        TranslationProgress.Drawing = true;
        try
        {
            // The entry screen displays the game version in the upper-left corner.
            Rect rect = new(8f, __instance is UIRoot_Entry ? 106f : 4f, 226f, 24f);
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.09f, 0.1f, 0.85f));
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.Label(rect, $"待翻译 {pending}  已完成 {completed}/{total}", Text.CurFontStyle);
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }
        finally
        {
            TranslationProgress.Drawing = false;
        }
    }
}
