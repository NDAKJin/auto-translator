using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class TranslationFailureTrackerTests
{
    [Fact]
    public void ExhaustedItemsCanBeListedAndResetByLanguage()
    {
        var tracker = new TranslationFailureTracker();
        tracker.Record("zh", "one"); tracker.Record("zh", "one"); tracker.Record("zh", "one");
        tracker.Record("zh", "two");
        tracker.Record("fr", "other"); tracker.Record("fr", "other"); tracker.Record("fr", "other");
        Assert.Equal(new[] { "one" }, tracker.List("zh"));
        Assert.True(tracker.IsExhausted("zh", "one"));
        tracker.Remove("zh", "one");
        Assert.Empty(tracker.List("zh"));
        Assert.True(tracker.IsExhausted("fr", "other"));
    }
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
