using RimWorld;
using Verse;

namespace AutoTranslator
{
    /// <summary>
    /// 模组主入口。RimWorld 通过 Mod 程序集扫描此特性加载设置页。
    /// </summary>
    [StaticConstructorOnStartup]
    public class AutoTranslatorMod : Mod
    {
        public AutoTranslatorMod(ModContentPack content) : base(content)
        {
            // 后续在此读取/绑定设置（Settings），并初始化翻译引擎。
            Log.Message("[AutoTranslator] 已加载。");
        }

        public override string SettingsCategory()
        {
            return "AutoTranslator";
        }

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            // 后续在此绘制设置页（源/目标语言、翻译引擎、API 密钥等）。
        }
    }
}
