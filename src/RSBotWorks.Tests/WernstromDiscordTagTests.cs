using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RSBotWorks.SaneAI;
using Wernstrom;

namespace RSBotWorks.Tests;

public class WernstromDiscordTagTests
{
    [Test]
    [Arguments(null)]
    [Arguments("not a user")]
    public async Task ReplaceDiscordTags_UnresolvedUserIsRemovedWithoutThrowing(string? value)
    {
        using var service = CreateService();
        var channel = new JoinedTextChannel<ulong>(1, "test", []);
        const string message = "Hello <@123>!";
        var tag = CreateTag(message, "<@123>", TagType.UserMention, 123, value);

        var result = service.ReplaceDiscordTags([tag], message, channel);

        await Assert.That(result).IsEqualTo("Hello !");
        await Assert.That(channel.Users.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ReplaceDiscordTags_UnresolvedMentionBetweenResolvedMentionsKeepsOffsetsCorrect()
    {
        using var service = CreateService();
        var channel = new JoinedTextChannel<ulong>(1, "test", [new(123, "Alice", "Alice")]);
        const string message = "Hi <@123>, <@456> and <@789>!";
        var alice = Substitute.For<IUser>();
        alice.Id.Returns(123UL);
        var bob = Substitute.For<IUser>();
        bob.Id.Returns(789UL);
        bob.GlobalName.Returns("Bob");
        ITag[] tags =
        [
            CreateTag(message, "<@123>", TagType.UserMention, 123, alice),
            CreateTag(message, "<@456>", TagType.UserMention, 456, null),
            CreateTag(message, "<@789>", TagType.UserMention, 789, bob)
        ];

        var result = service.ReplaceDiscordTags(tags, message, channel);

        await Assert.That(result).IsEqualTo("Hi [[Alice]],  and [[Bob]]!");
        await Assert.That(channel.Users.Length).IsEqualTo(2);
        await Assert.That(channel.GetUser(789)!.SanitizedName).IsEqualTo("Bob");
    }

    [Test]
    public async Task ReplaceDiscordTags_NonUserMentionWithNullValueIsUnchanged()
    {
        using var service = CreateService();
        var channel = new JoinedTextChannel<ulong>(1, "test", []);
        const string message = "See <#123>";
        var tag = CreateTag(message, "<#123>", TagType.ChannelMention, 123, null);

        var result = service.ReplaceDiscordTags([tag], message, channel);

        await Assert.That(result).IsEqualTo(message);
    }

    private static ITag CreateTag(string message, string mention, TagType type, ulong id, object? value)
    {
        var tag = Substitute.For<ITag>();
        tag.Type.Returns(type);
        tag.Key.Returns(id);
        tag.Index.Returns(message.IndexOf(mention, StringComparison.Ordinal));
        tag.Length.Returns(mention.Length);
        tag.Value.Returns(value);
        return tag;
    }

    private static WernstromService CreateService() => new(
        NullLogger<WernstromService>.Instance,
        Substitute.For<IHttpClientFactory>(),
        new WernstromServiceConfig { DiscordToken = "test", BrueckeId = 1 },
        new OpenRouterClient("test", Substitute.For<IHttpExecutor>()),
        null);
}
