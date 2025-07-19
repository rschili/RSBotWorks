namespace Stoll;

public enum EnvVar
{
    OPENAI_API_KEY,
    CLAUDE_API_KEY,
    MATRIX_USER_ID,
    MATRIX_PASSWORD,
    SEQ_API_KEY,
    SEQ_URL,
    SQLITE_DB_PATH,
    OPENWEATHERMAP_API_KEY,
    HA_API_URL,
    HA_TOKEN

}

public class Config
{
    private readonly Dictionary<EnvVar, string> _variables;

    public string OpenAiApiKey => _variables[EnvVar.OPENAI_API_KEY];
    public string ClaudeApiKey => _variables[EnvVar.CLAUDE_API_KEY];
    public string MatrixUserId => _variables[EnvVar.MATRIX_USER_ID];
    public string MatrixPassword => _variables[EnvVar.MATRIX_PASSWORD];
    public string SeqApiKey => _variables[EnvVar.SEQ_API_KEY];
    public string SeqUrl => _variables[EnvVar.SEQ_URL];
    public string SqliteDbPath => _variables[EnvVar.SQLITE_DB_PATH];
    public string OpenWeatherMapApiKey => _variables[EnvVar.OPENWEATHERMAP_API_KEY];
    public string HomeAssistantUrl => _variables[EnvVar.HA_API_URL];
    public string HomeAssistantToken => _variables[EnvVar.HA_TOKEN];

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