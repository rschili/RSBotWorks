namespace RSBotWorks;

public class TimedCache<T>
{
    private readonly object _lock = new();
    private readonly TimeSpan _lifetime;
    private T? _value;
    private DateTimeOffset? _expirationTime;

    public TimedCache(TimeSpan lifetime)
    {
        _lifetime = lifetime;
    }

    public bool TryGet(out T? value)
    {
        lock (_lock)
        {
            if (_expirationTime == null || DateTimeOffset.UtcNow >= _expirationTime)
            {
                value = default;
                return false;
            }

            value = _value;
            return true;
        }
    }

    public void Set(T value)
    {
        lock (_lock)
        {
            _value = value;
            _expirationTime = DateTimeOffset.UtcNow.Add(_lifetime);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _value = default;
            _expirationTime = null;
        }
    }
}