namespace Wernstrom;

public enum EnvVar
{
    OPENAI_API_KEY,
    CLAUDE_API_KEY,
    MOONSHOT_API_KEY,
    DISCORD_TOKEN,
    SEQ_API_KEY,
    SEQ_URL,
    SQLITE_DB_PATH,
    OPENWEATHERMAP_API_KEY,
    HA_API_URL,
    HA_TOKEN,
    GEMINI_API_KEY,
    SOCIALKIT_API_KEY,
    DISCORD_BRUECKE_ID,
    DISCORD_MASCHINENRAUM_ID
}

public interface IConfig
{
    string OpenAiApiKey { get; }
    string ClaudeApiKey { get; }
    string MoonshotApiKey { get; }
    string DiscordToken { get; }
    string SeqApiKey { get; }
    string SeqUrl { get; }
    string SqliteDbPath { get; }
    string OpenWeatherMapApiKey { get; }
    string HomeAssistantUrl { get; }
    string HomeAssistantToken { get; }
    string GeminiApiKey { get; }
    string SocialKitApiKey { get; }
    ulong DiscordBrueckeId { get; }
    ulong DiscordMaschinenraumId { get; }
}

public class Config : IConfig
{
    private readonly Dictionary<EnvVar, string> _variables;

    public string OpenAiApiKey => _variables[EnvVar.OPENAI_API_KEY];
    public string ClaudeApiKey => _variables[EnvVar.CLAUDE_API_KEY];
    public string MoonshotApiKey => _variables[EnvVar.MOONSHOT_API_KEY];
    public string DiscordToken => _variables[EnvVar.DISCORD_TOKEN];
    public string SeqApiKey => _variables[EnvVar.SEQ_API_KEY];
    public string SeqUrl => _variables[EnvVar.SEQ_URL];
    public string SqliteDbPath => _variables[EnvVar.SQLITE_DB_PATH];
    public string OpenWeatherMapApiKey => _variables[EnvVar.OPENWEATHERMAP_API_KEY];
    public string HomeAssistantUrl => _variables[EnvVar.HA_API_URL];
    public string HomeAssistantToken => _variables[EnvVar.HA_TOKEN];
    public string GeminiApiKey => _variables[EnvVar.GEMINI_API_KEY];
    public string SocialKitApiKey => _variables[EnvVar.SOCIALKIT_API_KEY];
    public ulong DiscordBrueckeId => ulong.Parse(_variables[EnvVar.DISCORD_BRUECKE_ID]);
    public ulong DiscordMaschinenraumId => ulong.Parse(_variables[EnvVar.DISCORD_MASCHINENRAUM_ID]);

    private Config(Dictionary<EnvVar, string> variables)
    {
        _variables = variables;
    }

    public static Config LoadFromEnvFile()
    {
        DotNetEnv.Env.TraversePath().Load();

        var loadedVariables = Enum.GetValues<EnvVar>()
            .ToDictionary(e => e, e =>
            {
                var str = Environment.GetEnvironmentVariable(e.ToString());
                if (string.IsNullOrWhiteSpace(str))
                    throw new KeyNotFoundException($"Environment variable {e} is not set");
                return str;
            });

        return new Config(loadedVariables);
    }
}