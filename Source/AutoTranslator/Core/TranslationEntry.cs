namespace nomadicooer.AutoTranslator;

/// <summary>单条自动翻译记录（不可变数据载体）。两阶段：先有源文(translated=false)，翻译后译文(translated=true)。
/// 译文为空表示仅源文阶段；译文等于源文表示翻译失败。Source/Translated 落盘为 xml 属性，游戏加载忽略。</summary>
public record TranslationEntry(
    string Source,      // 源文（原始文本，默认语言）
    string Translation, // 译文（落盘到 auto.xml 的元素文本；空=仅源文）
    bool Translated);   // 是否翻译成功（落盘为 xml 属性，游戏加载忽略）
