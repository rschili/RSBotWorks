using Discord;
using NSubstitute;
using Wernstrom;

namespace RSBotWorks.Tests;

public class WernstromHistoryTests
{
    private static readonly DateTimeOffset Trigger = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SelectHistory_WithoutAnchor_StopsAtCharBudget()
    {
        // 10 messages of 1000 chars, newest first: budget of 6000 fits 6
        var messages = CreateMessages(10, contentLength: 1000);

        var history = WernstromService.SelectHistory(messages, Trigger);

        await Assert.That(history.Count).IsEqualTo(6);
        await Assert.That(history[0].Message.Id).IsEqualTo(6UL); // oldest selected first
        await Assert.That(history[^1].Message.Id).IsEqualTo(1UL);
    }

    [Test]
    public async Task SelectHistory_KeepsOlderAnchorWithinStickyLimits()
    {
        var messages = CreateMessages(10, contentLength: 1000);

        var history = WernstromService.SelectHistory(messages, Trigger, anchorId: 8);

        await Assert.That(history.Count).IsEqualTo(8);
        await Assert.That(history[0].Message.Id).IsEqualTo(8UL);
    }

    [Test]
    public async Task SelectHistory_ReanchorsWhenStickyCharLimitExceeded()
    {
        var messages = CreateMessages(10, contentLength: 1000);

        // 10 x 1000 chars exceeds 1.5 x 6000
        var history = WernstromService.SelectHistory(messages, Trigger, anchorId: 10);

        await Assert.That(history.Count).IsEqualTo(6);
    }

    [Test]
    public async Task SelectHistory_ReanchorsWhenAnchorTooOld()
    {
        var messages = CreateMessages(10, contentLength: 10, spacing: TimeSpan.FromHours(1));

        var history = WernstromService.SelectHistory(messages, Trigger, anchorId: 8);

        // 4h age limit -> messages 1..4 (min 4 kept); anchor at 8h is beyond the 6h sticky limit
        await Assert.That(history[0].Message.Id).IsEqualTo(4UL);
    }

    [Test]
    public async Task SelectHistory_ImagesOnlyForNewestMessages()
    {
        var messages = CreateMessages(8, contentLength: 10);

        var history = WernstromService.SelectHistory(messages, Trigger);

        await Assert.That(history.Count(h => h.IncludeImages)).IsEqualTo(6);
        await Assert.That(history[0].IncludeImages).IsFalse();
        await Assert.That(history[^1].IncludeImages).IsTrue();
    }

    [Test]
    public async Task FormatReactions_CountsNamesAndSelf()
    {
        WernstromService.ReactionInfo[] reactions =
        [
            new("lol", 3, IncludesMe: true, Names: ["Lyeis", "Krael"]),
            new("fire", 3, IncludesMe: true, Names: null),
            new("cat", 1, IncludesMe: false, Names: null),
            new("coffee", 1, IncludesMe: true, Names: null),
        ];

        var text = WernstromService.FormatReactions(reactions);

        await Assert.That(text).IsEqualTo("lol (du, [[Lyeis]], [[Krael]]), fire×3 (inkl. du), cat, coffee (du)");
    }

    [Test]
    public async Task FormatReactions_TruncatesNamesAndCountsUnresolved()
    {
        WernstromService.ReactionInfo[] reactions = [new("lol", 7, IncludesMe: false, Names: ["A", "B", "C", "D", "E"])];

        var text = WernstromService.FormatReactions(reactions);

        await Assert.That(text).IsEqualTo("lol ([[A]], [[B]], [[C]], [[D]], +3)");
    }

    [Test]
    public async Task FormatReactions_EmptyIsNull()
    {
        await Assert.That(WernstromService.FormatReactions([])).IsNull();
    }

    [Test]
    public async Task CreateReplySnippet_CollapsesWhitespaceAndTruncates()
    {
        var snippet = WernstromService.CreateReplySnippet(
            "Ich mag die RP Community,\n die Leute sind alle super, und meine beiden chars sind jetzt schon 20");

        await Assert.That(snippet).IsEqualTo("Ich mag die RP Community, die Leute sind alle supe…");
        await Assert.That(WernstromService.CreateReplySnippet("  kurz  ")).IsEqualTo("kurz");
    }

    [Test]
    public async Task FormatTimestamp_AbsoluteAndDateOnlyForOtherDays()
    {
        // 09:00 UTC = 10:00 Berlin (CET)
        await Assert.That(WernstromService.FormatTimestamp(Trigger.AddMinutes(-46), Trigger)).IsEqualTo("09:14");
        await Assert.That(WernstromService.FormatTimestamp(Trigger.AddHours(-12), Trigger)).IsEqualTo("11.11. 22:00");
    }

    [Test]
    public async Task TruncateMessage_CutsLongMessagesOnly()
    {
        await Assert.That(WernstromService.TruncateMessage("kurz")).IsEqualTo("kurz");

        var cut = WernstromService.TruncateMessage(new string('x', 1500));
        await Assert.That(cut.Length).IsEqualTo(WernstromService.MaxMessageChars + 1);
        await Assert.That(cut).EndsWith("…");
    }

    [Test]
    public async Task TruncateForDiscord_CutsAtWordBoundaryWithinLimit()
    {
        var words = string.Concat(Enumerable.Repeat("wort <@123456789012345678> ", 100)); // ~2800 chars

        var text = WernstromService.TruncateForDiscord(words);

        await Assert.That(text.Length).IsLessThanOrEqualTo(WernstromService.MaxDiscordMessageLength);
        await Assert.That(text).EndsWith("…");
        // Mentions must not be split
        await Assert.That(text.TrimEnd('…')).EndsWith(">").Or.EndsWith("wort");
        await Assert.That(WernstromService.TruncateForDiscord("kurz")).IsEqualTo("kurz");
    }

    [Test]
    public async Task TruncateForDiscord_HardCutWithoutWhitespace()
    {
        var text = WernstromService.TruncateForDiscord(new string('x', 3000));

        await Assert.That(text.Length).IsEqualTo(WernstromService.MaxDiscordMessageLength);
    }

    /// <summary>Messages newest first, ids 1..count, one minute apart (or <paramref name="spacing"/>).</summary>
    private static List<IMessage> CreateMessages(int count, int contentLength, TimeSpan? spacing = null)
    {
        var step = spacing ?? TimeSpan.FromMinutes(1);
        var messages = new List<IMessage>();
        for (int i = 1; i <= count; i++)
        {
            var message = Substitute.For<IMessage>();
            message.Id.Returns((ulong)i);
            message.Content.Returns(new string('x', contentLength));
            message.Timestamp.Returns(Trigger - step * i);
            messages.Add(message);
        }
        return messages;
    }
}
