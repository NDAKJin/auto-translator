using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自定义 IMGUI 组件：在 Listing_Standard 中绘制一个「标题」行。
/// 标题比普通 Listing_Standard.Label 更大（默认 GameFont.Medium），
/// 并在下方画一条半透明分隔线，用于分组区块标题（如「基本信息」）。
/// 标题文本由调用方负责解析（建议缓存 Translate 结果，避免每帧重复拼接）。
/// </summary>
public static class Title
{
    /// <summary>在 listing 中绘制标题。</summary>
    /// <param name="listing">目标 Listing_Standard。</param>
    /// <param name="text">已解析的标题文本（建议上层缓存，勿每帧 Translate）。</param>
    /// <param name="font">标题字号，默认 GameFont.Medium。</param>
    /// <param name="drawLine">标题下方是否画分隔线，默认 true。</param>
    /// <param name="gapBefore">标题上方留白（像素），默认 0。</param>
    /// <param name="gapAfter">标题下方（分隔线之后）留白（像素），默认 6。</param>
    public static void Draw(
        Listing_Standard listing,
        string text,
        GameFont font = GameFont.Medium,
        bool drawLine = true,
        float gapBefore = 0f,
        float gapAfter = 6f)
    {
        if (gapBefore > 0f)
        {
            listing.Gap(gapBefore);
        }

        // Text.Font 是全局状态，绘制后必须还原，避免影响后续控件字号。
        GameFont prevFont = Text.Font;
        Text.Font = font;
        listing.Label(text);
        Text.Font = prevFont;

        if (drawLine)
        {
            listing.GapLine(12f);
        }

        if (gapAfter > 0f)
        {
            listing.Gap(gapAfter);
        }
    }
}
