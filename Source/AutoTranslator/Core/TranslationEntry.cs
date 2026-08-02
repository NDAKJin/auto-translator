namespace nomadicooer.AutoTranslator;

/// <summary>单条自动翻译记录。两阶段：先有源文(translated=false)，翻译后译文(translated=true)。
/// 译文为空表示仅源文阶段；译文等于源文表示翻译失败。Source/Translated/Verified 落盘为 xml 属性，游戏加载忽略。
/// Verified=true 表示人工已校验/改过译文，自动 Record 路径不覆盖。Key 为翻译键（元素名）。</summary>
public sealed class TranslationEntry
{
  public string Key { get; set; }          // 翻译键（对应 auto.xml 元素名）
  public string Source { get; set; }       // 源文（原始文本，默认语言）
  public string Translation { get; set; }  // 译文（落盘到 auto.xml 的元素文本；空=仅源文）
  public bool Translated { get; set; }     // 是否翻译成功（落盘为 xml 属性，游戏加载忽略）
  public bool Verified { get; set; }       // 人工校验/改过译文标记（落盘为 xml 属性；自动 Record 不覆盖）

  public TranslationEntry(string key, string source, string translation, bool translated, bool verified = false)
  {
    Key = key;
    Source = source;
    Translation = translation;
    Translated = translated;
    Verified = verified;
  }

  public void Deconstruct(out string key, out string source, out string translation, out bool translated, out bool verified)
  {
    key = Key;
    source = Source;
    translation = Translation;
    translated = Translated;
    verified = Verified;
  }

  public override bool Equals(object obj)
    => obj is TranslationEntry other
       && Key == other.Key
       && Source == other.Source
       && Translation == other.Translation
       && Translated == other.Translated
       && Verified == other.Verified;

  public override int GetHashCode()
    => (Key, Source, Translation, Translated, Verified).GetHashCode();
}
