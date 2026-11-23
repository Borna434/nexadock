using NexaDock.Core.Clipboard;

namespace NexaDock.Tests;

public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    private static ClipEntry Text(string s, int minute = 0) => ClipEntry.FromText(s, T0.AddMinutes(minute));

    [Fact]
    public void Newest_first_and_copying_again_moves_to_the_top()
    {
        var h = new ClipboardHistory();
        h.Add(Text("a"));
        h.Add(Text("b", 1));
        Assert.False(h.Add(Text("b", 2))); // same as the newest
        Assert.True(h.Add(Text("a", 3)));

        Assert.Equal(["a", "b"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Empty_text_is_ignored_and_long_text_is_cut()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add(Text("   ")));
        h.Add(Text(new string('x', ClipboardHistory.MaxTextLength + 50)));
        Assert.Equal(ClipboardHistory.MaxTextLength, h.Items[0].Text!.Length);
    }

    [Fact]
    public void Pinned_items_survive_the_cap_and_clearing()
    {
        var h = new ClipboardHistory();
        h.Add(Text("keep"));
        h.TogglePin(h.Items[0].Id);
        for (var i = 0; i < ClipboardHistory.MaxItems + 5; i++)
        {
            h.Add(Text("item " + i, i + 1));
        }

        Assert.Contains(h.Items, i => i.Text == "keep");
        Assert.Equal(ClipboardHistory.MaxItems + 1, h.Items.Count);
        Assert.Equal("keep", h.View(null)[0].Text);

        h.ClearUnpinned();
        Assert.Equal("keep", Assert.Single(h.Items).Text);
    }

    [Fact]
    public void Images_are_capped_separately_and_not_saved()
    {
        var h = new ClipboardHistory();
        for (var i = 0; i < ClipboardHistory.MaxImages + 3; i++)
        {
            h.Add(ClipEntry.FromImage([(byte)i, 1, 2], 10, 10, T0.AddMinutes(i)));
        }

        h.Add(Text("hello", 100));
        Assert.Equal(ClipboardHistory.MaxImages, h.Items.Count(i => i.Kind == ClipKind.Image));

        var restored = new ClipboardHistory();
        restored.Load(h.Serialize());
        Assert.Equal("hello", Assert.Single(restored.Items).Text);
    }

    [Fact]
    public void Search_ignores_case_and_diacritics()
    {
        var h = new ClipboardHistory();
        h.Add(Text("Kolokvij iz Matematike"));
        h.Add(Text("https://example.com", 1));

        Assert.Equal("Kolokvij iz Matematike", Assert.Single(h.View("matem")).Text);
        Assert.Single(h.View("ŽÉXAMPLE".Replace("Ž", string.Empty)));
        Assert.Empty(h.View("nothing"));
    }
}
