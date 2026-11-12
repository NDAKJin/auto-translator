using UnityEngine;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

internal sealed class FailedTranslationWindow : Window
{
    private Vector2 scroll;
    public override Vector2 InitialSize => new Vector2(760f, 560f);

    public FailedTranslationWindow()
    {
        doCloseX = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        bool previous = TranslationProgress.Drawing;
        TranslationProgress.Drawing = true;
        try { DrawItems(inRect); }
        finally { TranslationProgress.Drawing = previous; }
    }

    private void DrawItems(Rect inRect)
    {
        string target = LanguageDatabase.activeLanguage?.folderName;
        Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), TranslateKeys.FailedItems.Translate());
        if (string.IsNullOrEmpty(target)) return;
        var items = AutoTranslatorEngine.FailedItems(target);
        if (items.Count == 0)
        {
            Widgets.Label(new Rect(0f, 40f, inRect.width, 30f), TranslateKeys.FailedItemsEmpty.Translate());
            return;
        }
        Rect outer = new Rect(0f, 35f, inRect.width, inRect.height - 80f);
        float width = outer.width - 20f;
        float totalHeight = 0f;
        foreach (string item in items) totalHeight += Mathf.Max(42f, Text.CalcHeight(item, width - 190f) + 12f);
        Widgets.BeginScrollView(outer, ref scroll, new Rect(0f, 0f, width, totalHeight));
        float y = 0f;
        foreach (string item in items)
        {
            float height = Mathf.Max(42f, Text.CalcHeight(item, width - 190f) + 12f);
            Widgets.Label(new Rect(0f, y, width - 190f, height), item);
            if (Widgets.ButtonText(new Rect(width - 180f, y, 85f, 30f), TranslateKeys.RetryFailed.Translate())
                && !VisibleTextTranslations.Retry(target, item))
                Messages.Message(TranslateKeys.RetryUnavailable.Translate(), MessageTypeDefOf.RejectInput, false);
            if (Widgets.ButtonText(new Rect(width - 90f, y, 90f, 30f), TranslateKeys.AbandonFailed.Translate()))
                VisibleTextTranslations.Abandon(target, item);
            y += height;
        }
        Widgets.EndScrollView();
    }
}
