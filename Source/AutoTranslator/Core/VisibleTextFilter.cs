using System;

namespace nomadicooer.AutoTranslator;

internal static class VisibleTextFilter
{
    public static bool ShouldTranslate(string text, string target)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 5000 || string.IsNullOrEmpty(target)) return false;
        if (text.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*[\p{L}\d._-]+\s+v\d+(?:\.\d+)+\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;

        int letters = 0;
        int foreign = 0;
        int targetLetters = 0;
        bool markup = false;
        foreach (char ch in text)
        {
            if (ch == '<') { markup = true; continue; }
            if (ch == '>') { markup = false; continue; }
            if (markup || !char.IsLetter(ch)) continue;
            letters++;
            if (IsTargetScript(ch, target)) targetLetters++;
            else foreign++;
        }
        // Mixed-language strings are sent to the model too. It decides whether the
        // foreign part is meaningful content or a name/credit that should remain unchanged.
        return letters >= 2 && foreign >= 2;
    }

    public static string GuessSource(string text)
    {
        foreach (char ch in text)
        {
            if (ch >= '\u3040' && ch <= '\u30ff') return "Japanese";
            if (ch >= '\u1100' && ch <= '\u11ff' || ch >= '\uac00' && ch <= '\ud7af') return "Korean";
            if (ch >= '\u0400' && ch <= '\u052f') return "Russian";
            if (ch >= '\u0370' && ch <= '\u03ff') return "Greek";
            if (ch >= '\u0600' && ch <= '\u06ff') return "Arabic";
        }
        return "English";
    }

    private static bool IsTargetScript(char ch, string target)
    {
        if (target.StartsWith("Chinese", StringComparison.OrdinalIgnoreCase))
            return IsHan(ch);
        if (target.StartsWith("Japanese", StringComparison.OrdinalIgnoreCase))
            return IsHan(ch) || ch >= '\u3040' && ch <= '\u30ff';
        if (target.StartsWith("Korean", StringComparison.OrdinalIgnoreCase))
            return IsHan(ch) || ch >= '\u1100' && ch <= '\u11ff' || ch >= '\uac00' && ch <= '\ud7af';
        if (target.StartsWith("Russian", StringComparison.OrdinalIgnoreCase))
            return ch >= '\u0400' && ch <= '\u052f';
        if (target.StartsWith("Greek", StringComparison.OrdinalIgnoreCase))
            return ch >= '\u0370' && ch <= '\u03ff';
        if (target.StartsWith("Arabic", StringComparison.OrdinalIgnoreCase))
            return ch >= '\u0600' && ch <= '\u06ff';
        // Latin-script languages cannot be distinguished reliably by Unicode alone.
        return ch <= 0x024f;
    }

    private static bool IsHan(char ch) => ch >= '\u3400' && ch <= '\u9fff' || ch >= '\uf900' && ch <= '\ufaff';
}
