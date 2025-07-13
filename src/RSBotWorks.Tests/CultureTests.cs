using System.Globalization;

namespace RSBotWorks.Tests;

public class CultureTests
{
    [Test]
    public async Task CheckRequiredCulturesAreAvailable()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        await Assert.That(culture).IsNotNull();
    }
}