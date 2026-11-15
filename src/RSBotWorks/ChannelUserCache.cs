using System.Collections.Immutable;

namespace RSBotWorks;

/// <summary>
/// Thread-safe cache of joined channels. Lookups read an immutable snapshot;
/// additions use compare-and-swap so concurrent message handlers never lose an entry or add duplicates.
/// </summary>
public class ChannelUserCache<T>
{
    private ImmutableArray<JoinedTextChannel<T>> _channels = [];

    public ImmutableArray<JoinedTextChannel<T>> Channels => _channels;

    public JoinedTextChannel<T>? GetChannel(T channelId)
        => _channels.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Id, channelId));

    /// <summary>
    /// Adds the channel unless one with the same id exists already.
    /// Returns the cached instance — callers must use the return value, not their argument.
    /// </summary>
    public JoinedTextChannel<T> GetOrAddChannel(JoinedTextChannel<T> channel)
    {
        JoinedTextChannel<T> result = channel;
        ImmutableInterlocked.Update(ref _channels, channels =>
        {
            var existing = channels.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Id, channel.Id));
            result = existing ?? channel;
            return existing != null ? channels : channels.Add(channel);
        });
        return result;
    }
}

public class JoinedTextChannel<T>
{
    private ImmutableArray<ChannelUser<T>> _users;

    public T Id { get; }
    public string Name { get; }
    public ImmutableArray<ChannelUser<T>> Users => _users;

    public JoinedTextChannel(T id, string name, ImmutableArray<ChannelUser<T>> users)
    {
        Id = id;
        Name = name;
        _users = users;
    }

    public override string ToString() => $"{Name} ({Users.Length} users)";

    public ChannelUser<T>? GetUser(T userId) => _users.FirstOrDefault(u => EqualityComparer<T>.Default.Equals(u.Id, userId));

    /// <summary>
    /// Returns the cached user, or creates and adds it. Thread-safe; <paramref name="factory"/> may be
    /// called even if another thread wins the race, in which case its result is discarded.
    /// </summary>
    public ChannelUser<T> GetOrAddUser(T userId, Func<ChannelUser<T>> factory)
    {
        var existing = GetUser(userId);
        if (existing != null)
            return existing;

        var created = factory();
        ChannelUser<T> result = created;
        ImmutableInterlocked.Update(ref _users, users =>
        {
            var found = users.FirstOrDefault(u => EqualityComparer<T>.Default.Equals(u.Id, userId));
            result = found ?? created;
            return found != null ? users : users.Add(created);
        });
        return result;
    }
}

public record ChannelUser<TUserId>(TUserId Id, string Name, string SanitizedName)
{
    public override string ToString() => SanitizedName;
}
