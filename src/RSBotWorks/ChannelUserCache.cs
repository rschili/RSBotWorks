using System.Collections.Immutable;

namespace RSBotWorks;

public class ChannelUserCache<T>
{
    public ImmutableArray<JoinedTextChannel<T>> Channels { get; set; } = [];
}

public class JoinedTextChannel<T>
{
    public T Id { get; }
    public string Name { get; }
    public ImmutableArray<ChannelUser<T>> Users { get; set; }

    public JoinedTextChannel(T id, string name, ImmutableArray<ChannelUser<T>> users)
    {
        Id = id;
        Name = name;
        Users = users;
    }

    public override string ToString() => $"{Name} ({Users.Length} users)";

    public ChannelUser<T>? GetUser(T userId) => Users.FirstOrDefault(u => EqualityComparer<T>.Default.Equals(u.Id, userId));
}

public record ChannelUser<TUserId>(TUserId Id, string Name, string CanonicalName)
{
    public override string ToString() => CanonicalName;
}