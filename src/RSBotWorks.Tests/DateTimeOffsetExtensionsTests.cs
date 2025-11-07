using System;
using System.Threading.Tasks;

namespace RSBotWorks.Tests;

public class DateTimeOffsetExtensionsTests
{
    [Test]
    public async Task ToRelativeLabel_ReturnsNow_ForCurrentTime()
    {
        var now = DateTimeOffset.Now;
        
        string result = now.ToRelativeToNowLabel();
        
        await Assert.That(result).IsEqualTo("now");
    }

    [Test]
    public async Task ToRelativeLabel_ReturnsSecondsAgo_ForRecentTime()
    {
        var fiveSecondsAgo = DateTimeOffset.Now.AddSeconds(-5);
        
        string result = fiveSecondsAgo.ToRelativeToNowLabel();
        
        await Assert.That(result).IsEqualTo("5s ago");
    }

    [Test]
    public async Task ToRelativeLabel_ReturnsMinutesAgo_ForMinutesInPast()
    {
        var twoMinutesAgo = DateTimeOffset.Now.AddMinutes(-2);
        
        string result = twoMinutesAgo.ToRelativeToNowLabel();
        
        await Assert.That(result).IsEqualTo("2m ago");
    }

    [Test]
    public async Task ToRelativeLabel_ReturnsHoursAgo_ForHoursInPast()
    {
        var threeHoursAgo = DateTimeOffset.Now.AddHours(-3);
        
        string result = threeHoursAgo.ToRelativeToNowLabel();
        
        await Assert.That(result).IsEqualTo("3h ago");
    }

    [Test]
    public async Task ToRelativeLabel_ReturnsDaysAgo_ForDaysInPast()
    {
        var twoDaysAgo = DateTimeOffset.Now.AddDays(-2);
        
        string result = twoDaysAgo.ToRelativeToNowLabel();
        
        await Assert.That(result).IsEqualTo("2d ago");
    }
}
