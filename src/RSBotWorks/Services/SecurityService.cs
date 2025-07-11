using RSMatrix.Http;

namespace RSHome.Services;

public class SecurityService
{
    private readonly IConfigService _config;
    private readonly LeakyBucketRateLimiter _rateLimiter = new(3, 10);
    private readonly ILogger<SecurityService> _logger;

    public SecurityService(IConfigService config, ILogger<SecurityService> logger)
    {
        _config = config;
        _logger = logger;
    }

    internal bool Authenticate(string password, string? ipAddress, out bool rateLimitExceeded)
    {
        rateLimitExceeded = !_rateLimiter.Leak();
        if (rateLimitExceeded)
        {
            _logger.LogError("Rate limit exceeded for login attempts. Attempted password: {Password}, Request IP: {IP}", password, ipAddress ?? "unknown");
            return false;
        }

        return BCrypt.Net.BCrypt.EnhancedVerify(password, _config.WebLoginHash);
    }
}