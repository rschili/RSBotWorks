using RSHome.Services;
using TUnit.Assertions.Extensions;

namespace RSHome.Tests;

public class WordleServiceTests
{
    [Test]
    //[DisplayName("Test CheckTipps on $wordle")] seems to break test explorer for now
    [Arguments("BADGE", new[] { "HONEY", "AUDIT", "BADGE" }, new[] { "OOOMO", "MOXOO", "X" })]
    [Arguments("SCRUM", new[] { "HONEY", "AUDIT", "PLUMP", "SCRUM" }, new[] { "OOOOO", "OMOOO", "OOMMO", "X" })]
    [Arguments("RELIC", new[] { "HONEY", "AUDIT", "PIXIE", "RELIC" }, new[] { "OOOMO", "OOOXO", "OOOXM", "X" })]
    public async Task TestCheckTipps(string wordle, string[] inputs, string[] expectedResults)
    {
        var service = new WordleService();
        service.WordleWord = wordle;
        service.WordleWordDay = DateTime.Now.DayOfYear;

        var result = service.CheckTipps(inputs);
        await Assert.That(result.Count).IsEqualTo(expectedResults.Length);
        foreach(var (response, expected) in result.Zip(expectedResults))
        {
            await Assert.That(response).IsEqualTo(expected).Because(wordle);
        }
    }

    [Test]
    [Arguments("SCRUM", "AUDIT", 1, false)]
    public async Task CheckAlreadyHinted(string wordle, string input, int index, bool expectedResult)
    {
        var service = new WordleService();
        service.WordleWord = wordle;
        service.WordleWordDay = DateTime.Now.DayOfYear;

        var result = service.AlreadyHinted(input, index);
        await Assert.That(result).IsEqualTo(expectedResult);
    }
}