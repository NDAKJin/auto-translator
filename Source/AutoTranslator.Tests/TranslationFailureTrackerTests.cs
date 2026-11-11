using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class TranslationFailureTrackerTests
{
    [Fact]
    public void CountsFailuresPerLanguageAndSourceAndResets()
    {
        var tracker = new TranslationFailureTracker();
        Assert.Equal(1, tracker.Record("zh", "text"));
        Assert.Equal(2, tracker.Record("zh", "text"));
        Assert.Equal(1, tracker.Record("ja", "text"));
        Assert.Equal(TranslationFailureTracker.Limit, tracker.Record("zh", "text"));
        Assert.Equal(TranslationFailureTracker.Limit, tracker.Record("zh", "text"));
        tracker.Clear();
        Assert.Equal(1, tracker.Record("zh", "text"));
    }
}
