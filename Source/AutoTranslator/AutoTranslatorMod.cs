using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 模组主入口。RimWorld 通过 Mod 程序集扫描此类加载设置页。
/// 此类在启动流程中加载得非常早（早于 Def 就绪），仅用于设置页与生命周期入口，
/// 不要在此处做依赖 Def、纹理或图形资源的初始化。
/// </summary>
public class AutoTranslatorMod : Mod
{
    public AutoTranslatorMod(ModContentPack content) : base(content)
    {
        // 后续在此读取/绑定设置（Settings）。
        Log.Message("[AutoTranslator] 已加载。");
    }

    public override string SettingsCategory()
    {
        return (AutoTranslatorConstants.TranslationKeyPrefix + "SettingsCategory").Translate();
    }

    public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
    {
        // 后续在此绘制设置页（源/目标语言、翻译引擎、API 密钥等）。
    }
}
