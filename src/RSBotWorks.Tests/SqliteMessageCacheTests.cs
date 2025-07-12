
using Moq;
using RSHome.Services;

namespace RSHome.Tests;

public class SqliteMessageCacheTests
{
    [Test]
    public async Task RoundtripDiscordMessages()
    {
    using var sqliteService = await SqliteMessageCache.CreateAsync(":memory:");
    DateTimeOffset dto1 = DateTimeOffset.Now;
    DateTimeOffset dto2 = dto1.AddSeconds(1);
    await sqliteService.AddDiscordMessageAsync(1, dto1, 1, "MyUsername", "Test Message", true, 1);
    await sqliteService.AddDiscordMessageAsync(2, dto2, 2, "Other Username", "Test Response", false, 1);
    await sqliteService.AddDiscordMessageAsync(3, dto1, 4, "Test", "Test", false, 2); // another channel, should not be returned

    var messages = await sqliteService.GetLastDiscordMessagesForChannelAsync(1, 5);
    await Assert.That(messages.Count).IsEqualTo(2);
    var first = messages[0];
    await Assert.That(first.Id).IsEqualTo<ulong>(1);
    await Assert.That(first.Timestamp).IsEqualTo(dto1);
    await Assert.That(first.UserId).IsEqualTo<ulong>(1);
    await Assert.That(first.UserLabel).IsEqualTo("MyUsername");
    await Assert.That(first.Body).IsEqualTo("Test Message");
    await Assert.That(first.IsFromSelf).IsTrue();
    await Assert.That(first.ChannelId).IsEqualTo<ulong>(1);

    var second = messages[1];
    await Assert.That(second.Id).IsEqualTo<ulong>(2);
    await Assert.That(second.Timestamp).IsEqualTo(dto2);
    await Assert.That(second.UserId).IsEqualTo<ulong>(2);
    await Assert.That(second.UserLabel).IsEqualTo("Other Username");
    await Assert.That(second.Body).IsEqualTo("Test Response");
    await Assert.That(second.IsFromSelf).IsFalse();
    await Assert.That(second.ChannelId).IsEqualTo<ulong>(1);
    }

    [Test]
    public async Task RoundtripMatrixMessages()
    {
        using var sqliteService = await SqliteMessageCache.CreateAsync(":memory:");
        DateTimeOffset dto1 = DateTimeOffset.Now;
        DateTimeOffset dto2 = dto1.AddSeconds(1);
        await sqliteService.AddMatrixMessageAsync("1", dto1, "user1", "User One", "Test Message", true, "room1");
        await sqliteService.AddMatrixMessageAsync("2", dto2, "user2", "User Two", "Test Response", false, "room1");
        await sqliteService.AddMatrixMessageAsync("3", dto1, "user3", "User Three", "Test", false, "room2"); // another room, should not be returned

        var messages = await sqliteService.GetLastMatrixMessagesForRoomAsync("room1", 5);
        await Assert.That(messages.Count).IsEqualTo(2);
        var first = messages[0];
        await Assert.That(first.Id).IsEqualTo("1");
        await Assert.That(first.Timestamp).IsEqualTo(dto1);
        await Assert.That(first.UserId).IsEqualTo("user1");
        await Assert.That(first.UserLabel).IsEqualTo("User One");
        await Assert.That(first.Body).IsEqualTo("Test Message");
        await Assert.That(first.IsFromSelf).IsTrue();
        await Assert.That(first.Room).IsEqualTo("room1");

        var second = messages[1];
        await Assert.That(second.Id).IsEqualTo("2");
        await Assert.That(second.Timestamp).IsEqualTo(dto2);
        await Assert.That(second.UserId).IsEqualTo("user2");
        await Assert.That(second.UserLabel).IsEqualTo("User Two");
        await Assert.That(second.Body).IsEqualTo("Test Response");
        await Assert.That(second.IsFromSelf).IsFalse();
        await Assert.That(second.Room).IsEqualTo("room1");
    }

    [Test]
    public async Task GetOwnMessagesForTodayPlusLastForRoomAsync_ReturnsCorrectMessages()
    {
        using var sqliteService = await SqliteMessageCache.CreateAsync(":memory:");

        var now = DateTimeOffset.UtcNow;
        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

        // Add messages to the database
        await sqliteService.AddMatrixMessageAsync("1", startOfDay.AddHours(1), "user1", "User One", "Own Message 1", true, "room1"); // yes
        await sqliteService.AddMatrixMessageAsync("2", startOfDay.AddHours(2), "user2", "User Two", "User Message 1", false, "room1");
        await sqliteService.AddMatrixMessageAsync("3", startOfDay.AddHours(3), "user1", "User One", "Own Message 2", true, "room1"); // yes
        await sqliteService.AddMatrixMessageAsync("4", startOfDay.AddHours(3), "user2", "User Two", "User Message 2", false, "room1");
        await sqliteService.AddMatrixMessageAsync("5", startOfDay.AddHours(23), "user2", "User Two", "User Message 3", false, "room1"); // yes

        // Call the method
        var messages = await sqliteService.GetLastMatrixMessageAndOwnAsync("room1");

        // Assert the results
        await Assert.That(messages.Count).IsEqualTo(3);
        var x = messages[0];
        await Assert.That(x.Id).IsEqualTo("1");
        await Assert.That(x.Timestamp).IsEqualTo(startOfDay.AddHours(1));
        await Assert.That(x.UserId).IsEqualTo("user1");
        await Assert.That(x.UserLabel).IsEqualTo("User One");
        await Assert.That(x.Body).IsEqualTo("Own Message 1");
        await Assert.That(x.IsFromSelf).IsTrue();
        await Assert.That(x.Room).IsEqualTo("room1");

        var second = messages[1];
        await Assert.That(second.Id).IsEqualTo("3");
        await Assert.That(second.Timestamp).IsEqualTo(startOfDay.AddHours(3));
        await Assert.That(second.UserId).IsEqualTo("user1");
        await Assert.That(second.UserLabel).IsEqualTo("User One");
        await Assert.That(second.Body).IsEqualTo("Own Message 2");
        await Assert.That(second.IsFromSelf).IsTrue();
        await Assert.That(second.Room).IsEqualTo("room1");

        var third = messages[2];
        await Assert.That(third.Id).IsEqualTo("5");
        await Assert.That(third.Timestamp).IsEqualTo(startOfDay.AddHours(23));
        await Assert.That(third.UserId).IsEqualTo("user2");
        await Assert.That(third.UserLabel).IsEqualTo("User Two");
        await Assert.That(third.Body).IsEqualTo("User Message 3");
        await Assert.That(third.IsFromSelf).IsFalse();
        await Assert.That(third.Room).IsEqualTo("room1");
    }
}