namespace RSBotWorks.Tests;

public class ChannelUserCacheTests
{
    [Test]
    public async Task GetOrAddUser_ConcurrentAddsKeepEveryUserExactlyOnce()
    {
        var channel = new JoinedTextChannel<int>(1, "test", []);

        // 50 distinct users, each added from 8 threads at once
        await Task.WhenAll(Enumerable.Range(0, 400).Select(i => Task.Run(() =>
        {
            int id = i % 50;
            channel.GetOrAddUser(id, () => new ChannelUser<int>(id, $"User{id}", $"User{id}"));
        })));

        await Assert.That(channel.Users.Length).IsEqualTo(50);
        await Assert.That(channel.Users.Select(u => u.Id).Distinct().Count()).IsEqualTo(50);
    }

    [Test]
    public async Task GetOrAddUser_ReturnsExistingInstance()
    {
        var existing = new ChannelUser<int>(7, "Alice", "Alice");
        var channel = new JoinedTextChannel<int>(1, "test", [existing]);

        var result = channel.GetOrAddUser(7, () => new ChannelUser<int>(7, "Other", "Other"));

        await Assert.That(result).IsSameReferenceAs(existing);
        await Assert.That(channel.Users.Length).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrAddChannel_ConcurrentAddsReturnTheSameInstance()
    {
        var cache = new ChannelUserCache<int>();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            cache.GetOrAddChannel(new JoinedTextChannel<int>(1, "test", [])))));

        await Assert.That(cache.Channels.Length).IsEqualTo(1);
        await Assert.That(results.All(r => ReferenceEquals(r, cache.Channels[0]))).IsTrue();
    }
}
