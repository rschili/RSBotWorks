using System.Threading.Tasks;
using RSHome.Services;
using TUnit.Assertions.AssertConditions.Throws;

namespace RSHome.Tests;

public class OpenAIServiceTests
{
    [Test]
    public async Task SanitizeName_RemovesInvalidCharacters()
    {
        string input = "Invalid@Name!";
        string expected = "InvalidName";

        string result = OpenAIService.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_NormalizesUnicodeCharacters()
    {
        string input = "Náïve";
        string expected = "Naive";

        string result = OpenAIService.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Gustaff Pfiffikus", "Gustaff_Pfiffikus")]
    [Arguments("Das Serum", "Das_Serum")]
    [Arguments("sikk 🦀", "sikk")]
    public async Task SanitizeName_HandlesKnownNames(string input, string expected)
    {
        string result = OpenAIService.SanitizeName(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_HandlesEmptyString()
    {
        string input = "";
        string expected = "";

        string result = OpenAIService.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_HandlesNullString()
    {
        await Assert.That(() => OpenAIService.SanitizeName(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SanitizeName_CachesSanitizedNames()
    {
        string input = "NameWith@Special#Characters";
        string expected = "NameWithSpecialCharacters";

        string result1 = OpenAIService.SanitizeName(input);
        string result2 = OpenAIService.SanitizeName(input);

        await Assert.That(result1).IsEqualTo(expected);
        await Assert.That(result2).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeName_AllowsValidCharacters()
    {
        string input = "Valid_Name-123";
        string expected = "Valid_Name-123";

        string result = OpenAIService.SanitizeName(input);

        await Assert.That(result).IsEqualTo(expected);
    }

}