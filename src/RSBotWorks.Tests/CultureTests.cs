using System.Globalization;
using RSHome.Services;
using TUnit.Assertions.Extensions;

namespace RSHome.Tests;

public class CultureTests
{
    [Test]
    public async Task CheckRequiredCulturesAreAvailable()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        await Assert.That(culture).IsNotNull();
    }
}