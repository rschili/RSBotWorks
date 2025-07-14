using System.Threading.Tasks;
using RSBotWorks;
using TUnit.Assertions.AssertConditions.Throws;

namespace RSBotWorks.Tests;

public class NameSanitizerTests
{
    [Test]
    public async Task SanitizeName_RemovesInvalidCharacters()
    {
        string input = "Invalid@Name!";
        string expected = "InvalidName";

        string result = NameSanitizer.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_NormalizesUnicodeCharacters()
    {
        string input = "Náïve";
        string expected = "Naive";

        string result = NameSanitizer.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Gustaff Pfiffikus", "Gustaff_Pfiffikus")]
    [Arguments("Das Serum", "Das_Serum")]
    [Arguments("sikk 🦀", "sikk")]
    public async Task SanitizeName_HandlesKnownNames(string input, string expected)
    {
        string result = NameSanitizer.SanitizeName(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_HandlesEmptyString()
    {
        string input = "";
        string expected = "";

        string result = NameSanitizer.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_HandlesNullString()
    {
        await Assert.That(() => NameSanitizer.SanitizeName(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SanitizeName_CachesSanitizedNames()
    {
        string input = "NameWith@Special#Characters";
        string expected = "NameWithSpecialCharacters";

        string result1 = NameSanitizer.SanitizeName(input);
        string result2 = NameSanitizer.SanitizeName(input);

        await Assert.That(result1).IsEqualTo(expected);
        await Assert.That(result2).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_AllowsValidCharacters()
    {
        string input = "Valid_Name-123";
        string expected = "Valid_Name-123";

        string result = NameSanitizer.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

}