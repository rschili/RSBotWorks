using System;
using System.Threading.Tasks;
using TUnit.Assertions.AssertConditions.Throws;

namespace RSBotWorks.Tests;

public class TimeSpanExtensionsTests
{
    [Test]
    public async Task ToRelativeLabel_ReturnsNow_ForZeroTimeSpan()
    {
        var timeSpan = TimeSpan.Zero;
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo("now");
    }

    [Test]
    public async Task ToRelativeLabel_ReturnsNow_ForNegativeTimeSpan()
    {
        var timeSpan = TimeSpan.FromSeconds(-10);
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo("now");
    }

    [Test]
    [Arguments(30, "30s ago")]
    [Arguments(45, "45s ago")]
    [Arguments(1, "now")]
    public async Task ToRelativeLabel_ReturnsSecondsAgo_ForSecondsTimeSpan(int seconds, string expected)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments(5, "5m ago")]
    [Arguments(30, "30m ago")]
    [Arguments(59, "59m ago")]
    public async Task ToRelativeLabel_ReturnsMinutesAgo_ForMinutesTimeSpan(int minutes, string expected)
    {
        var timeSpan = TimeSpan.FromMinutes(minutes);
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments(2, "2h ago")]
    [Arguments(12, "12h ago")]
    [Arguments(23, "23h ago")]
    public async Task ToRelativeLabel_ReturnsHoursAgo_ForHoursTimeSpan(int hours, string expected)
    {
        var timeSpan = TimeSpan.FromHours(hours);
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments(1, "1d ago")]
    [Arguments(7, "7d ago")]
    [Arguments(30, "30d ago")]
    public async Task ToRelativeLabel_ReturnsDaysAgo_ForDaysTimeSpan(int days, string expected)
    {
        var timeSpan = TimeSpan.FromDays(days);
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ToRelativeLabel_RoundsCorrectly_ForFractionalValues()
    {
        var timeSpan = TimeSpan.FromMinutes(1.7); // Should round to 2m
        
        string result = timeSpan.ToRelativeLabel();
        
        await Assert.That(result).IsEqualTo("2m ago");
    }
}