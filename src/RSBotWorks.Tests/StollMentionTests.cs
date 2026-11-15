using Stoll;

namespace RSBotWorks.Tests;

public class StollMentionTests
{
    private const string Self = "@stoll:matrix.example.org";

    private static string? Resolve(string userId) => userId switch
    {
        "@alice:matrix.example.org" => "Alice",
        _ => null
    };

    [Test]
    [Arguments("Hi <@alice:matrix.example.org>!", "Hi [[Alice]]!")]
    [Arguments("Hi @alice:matrix.example.org!", "Hi [[Alice]]!")]
    [Arguments("frag @bob:other.server.de.", "frag [[bob]].")]
    [Arguments("frag <@bob:other.server.de:8448> mal", "frag [[bob]] mal")]
    [Arguments("mail an bob@example.org", "mail an bob@example.org")]
    [Arguments("kein Mention hier", "kein Mention hier")]
    public async Task ReplaceMatrixMentions_ResolvesNamesAndNeverLeaksFullIds(string input, string expected)
    {
        var result = StollService.ReplaceMatrixMentions(input, Self, Resolve, out var selfMentioned);

        await Assert.That(result).IsEqualTo(expected);
        await Assert.That(selfMentioned).IsFalse();
    }

    [Test]
    public async Task ReplaceMatrixMentions_DetectsSelfMention()
    {
        var result = StollService.ReplaceMatrixMentions($"Na <{Self}>, alles kalt auf der Sonne?", Self,
            id => id == Self ? "Herr Stoll" : null, out var selfMentioned);

        await Assert.That(selfMentioned).IsTrue();
        await Assert.That(result).IsEqualTo("Na [[Herr Stoll]], alles kalt auf der Sonne?");
    }
}
