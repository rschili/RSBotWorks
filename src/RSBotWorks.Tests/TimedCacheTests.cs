namespace RSBotWorks.Tests;

public class TimedCacheTests
{
    [Test]
    public async Task TryGet_EmptyCache_ReturnsFalse()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMinutes(1));
        
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsFalse();
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Set_ThenTryGet_ReturnsTrue()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMinutes(1));
        const string expectedValue = "test value";
        
        cache.Set(expectedValue);
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsTrue();
        await Assert.That(value).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task TryGet_AfterExpiration_ReturnsFalse()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMilliseconds(50));
        
        cache.Set("test value");
        await Task.Delay(100); // Wait for expiration
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsFalse();
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task TryGet_BeforeExpiration_ReturnsTrue()
    {
        var cache = new TimedCache<string>(TimeSpan.FromSeconds(1));
        const string expectedValue = "test value";
        
        cache.Set(expectedValue);
        await Task.Delay(50); // Wait a bit but not until expiration
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsTrue();
        await Assert.That(value).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task Set_OverwritesPreviousValue()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMinutes(1));
        
        cache.Set("first value");
        cache.Set("second value");
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsTrue();
        await Assert.That(value).IsEqualTo("second value");
    }

    [Test]
    public async Task Set_ResetsExpirationTime()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMilliseconds(100));
        
        cache.Set("first value");
        await Task.Delay(80); // Almost expired
        cache.Set("second value"); // Should reset timer
        await Task.Delay(80); // Should still be valid
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsTrue();
        await Assert.That(value).IsEqualTo("second value");
    }

    [Test]
    public async Task Clear_RemovesValueAndExpiration()
    {
        var cache = new TimedCache<string>(TimeSpan.FromMinutes(1));
        
        cache.Set("test value");
        cache.Clear();
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsFalse();
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task ZeroLifetime_AlwaysExpired()
    {
        var cache = new TimedCache<string>(TimeSpan.Zero);
        
        cache.Set("test value");
        var result = cache.TryGet(out var value);
        
        await Assert.That(result).IsFalse();
        await Assert.That(value).IsNull();
    }
}