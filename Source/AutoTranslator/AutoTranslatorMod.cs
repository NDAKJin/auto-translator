using RimWorld;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

public class AutoTranslatorMod : Mod
{
  private readonly AutoTranslatorSettings settings;
  public override string SettingsCategory() => TranslateKeys.SettingsCategory.Translate();
  public AutoTranslatorMod(ModContentPack content) : base(content)
  {
    settings = GetSettings<AutoTranslatorSettings>();

  }
  public override void DoSettingsWindowContents(Rect inRect)
  {
    settings.DoSettingsWindowContents(inRect);
  }
}