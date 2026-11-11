using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class TranslationProgressTests
{
    [Fact]
    public void CompletedTextRemainsCompleteWhenWorkerFinishesAfterCallback()
    {
        TranslationProgress.Reset();
        TranslationProgress.Capture("example");
        TranslationProgress.Enqueue("example");
        TranslationProgress.Complete("example");
        TranslationProgress.Finish("example", false);

        Assert.Equal((1, 1, 0), TranslationProgress.Snapshot());
    }
}
