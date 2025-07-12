using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RSHome.Services;

public interface IConfigService
{
    string DiscordToken { get; }
    ulong DiscordAdminId { get; }
    string OpenAiApiKey { get; }
    string MatrixUserId { get; }
    string MatrixPassword { get; }
    bool DiscordEnable { get; }
    bool MatrixEnable { get; }
    string SeqApiKey { get; }
    string SeqUrl { get; }
    string WebLoginHash { get; }
    string WebKeyStore { get; }
    string SqliteDbPath { get; }
    string CertificatePath { get; }
    string PfxPassword { get; }
    string OpenWeatherMapApiKey { get; }
    string HomeAssistantUrl { get; }
    string HomeAssistantToken { get; }
}


public enum EnvVar
{
    DISCORD_TOKEN,
    DISCORD_ADMIN_ID,
    OPENAI_API_KEY,
    MATRIX_USER_ID,
    MATRIX_PASSWORD,
    DISCORD_ENABLE,
    MATRIX_ENABLE,
    SEQ_API_KEY,
    SEQ_URL,
    WEB_LOGIN_HASH,
    WEB_KEY_STORE,
    SQLITE_DB_PATH,
    CERTIFICATE_PATH,
    PFX_PASSWORD,
    OPENWEATHERMAP_API_KEY,
    HA_API_URL,
    HA_TOKEN

}

public class ConfigService : IConfigService
{
    private readonly Dictionary<EnvVar, string> _variables;

    public string DiscordToken => _variables[EnvVar.DISCORD_TOKEN];
    public ulong DiscordAdminId => ulong.Parse(_variables[EnvVar.DISCORD_ADMIN_ID]);
    public string OpenAiApiKey => _variables[EnvVar.OPENAI_API_KEY];
    public string MatrixUserId => _variables[EnvVar.MATRIX_USER_ID];
    public string MatrixPassword => _variables[EnvVar.MATRIX_PASSWORD];
    public bool DiscordEnable => bool.Parse(_variables[EnvVar.DISCORD_ENABLE]);
    public bool MatrixEnable => bool.Parse(_variables[EnvVar.MATRIX_ENABLE]);
    public string SeqApiKey => _variables[EnvVar.SEQ_API_KEY];
    public string SeqUrl => _variables[EnvVar.SEQ_URL];
    public string WebLoginHash => _variables[EnvVar.WEB_LOGIN_HASH];
    public string WebKeyStore => _variables[EnvVar.WEB_KEY_STORE];
    public string SqliteDbPath => _variables[EnvVar.SQLITE_DB_PATH];
    public string CertificatePath => _variables[EnvVar.CERTIFICATE_PATH];
    public string PfxPassword => _variables[EnvVar.PFX_PASSWORD];
    public string OpenWeatherMapApiKey => _variables[EnvVar.OPENWEATHERMAP_API_KEY];
    public string HomeAssistantUrl => _variables[EnvVar.HA_API_URL];
    public string HomeAssistantToken => _variables[EnvVar.HA_TOKEN];

    private ConfigService(Dictionary<EnvVar, string> variables)
    {
        _variables = variables;
    }

    public static ConfigService LoadFromEnvFile()
    {
        DotNetEnv.Env.TraversePath().Load();

        var requiredVariables = Enum.GetValues<EnvVar>()
            .ToDictionary(e => e, e =>
            {
                var str = Environment.GetEnvironmentVariable(e.ToString());
                if (string.IsNullOrWhiteSpace(str))
                    throw new KeyNotFoundException($"Environment variable {e} is not set");
                return str;
            });

        return new ConfigService(requiredVariables);
    }
}