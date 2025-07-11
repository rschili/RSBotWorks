using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace RSHome.Services;

public interface ISqliteService : IDisposable
{
    SqliteConnection Connection { get; }
    Task AddDiscordMessageAsync(ulong id, DateTimeOffset timestamp, ulong userId, string userLabel, string body, bool isFromSelf, ulong channelId);
    Task AddMatrixMessageAsync(string id, DateTimeOffset timestamp, string userId, string userLabel, string body, bool isFromSelf, string room);
    Task<List<DiscordMessage>> GetLastDiscordMessagesForChannelAsync(ulong channelId, int count);
    Task<List<MatrixMessage>> GetLastMatrixMessagesForRoomAsync(string room, int count);
    Task<List<MatrixMessage>> GetOwnMessagesForTodayPlusLastForRoomAsync(string room);
    Task SetSettingAsync(string key, string value);
    Task<string?> GetSettingOrDefaultAsync(string key);
    Task RemoveSettingAsync(string key);
}

public class SqliteService : ISqliteService
{
    public SqliteConnection Connection { get; private init; }
    private SqliteService(SqliteConnection connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public static async Task<SqliteService> CreateAsync(IConfigService configService)
    {
        SqliteConnectionStringBuilder conStringBuilder = new();
        conStringBuilder.DataSource = configService.SqliteDbPath; // ":memory:" for in-memory database
        SqliteConnection connection = new(conStringBuilder.ConnectionString);
        await connection.OpenAsync();
        //TODO: add a revision number to the database schema to allow for future migrations
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS DiscordMessages (Id INTEGER PRIMARY KEY, Timestamp DATETIME NOT NULL, UserId INTEGER NOT NULL, UserLabel TEXT NOT NULL, Body TEXT NOT NULL, IsFromSelf BOOLEAN NOT NULL, ChannelId INTEGER NOT NULL)";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS MatrixMessages (Id TEXT PRIMARY KEY, Timestamp DATETIME NOT NULL, UserId TEXT NOT NULL, UserLabel TEXT NOT NULL, Body TEXT NOT NULL, IsFromSelf BOOLEAN NOT NULL, Room TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT)";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Dapper does not correctly handle DateTimeOffset, so we need to add a custom type handler
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.AddTypeHandler(DateTimeHandler.Default);

        return new SqliteService(connection);
    }

    public void Dispose()
    {
        Connection.Dispose();
    }

    public Task AddDiscordMessageAsync(ulong id, DateTimeOffset timestamp, ulong userId, string userLabel, string body, bool isFromSelf, ulong channelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userLabel, nameof(userLabel));
        ArgumentException.ThrowIfNullOrWhiteSpace(body, nameof(body));

        using var command = Connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO DiscordMessages(Id, Timestamp, UserId, UserLabel, Body, IsFromSelf, ChannelId) VALUES(@Id, @Timestamp, @UserId, @UserLabel, @Body, @IsFromSelf, @ChannelId)";
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Timestamp", timestamp.UtcTicks);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@UserLabel", userLabel);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@IsFromSelf", isFromSelf);
        command.Parameters.AddWithValue("@ChannelId", channelId);
        return command.ExecuteNonQueryAsync();
    }

    public Task AddMatrixMessageAsync(string id, DateTimeOffset timestamp, string userId, string userLabel, string body, bool isFromSelf, string room)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userLabel, nameof(userLabel));
        ArgumentException.ThrowIfNullOrWhiteSpace(body, nameof(body));
        ArgumentException.ThrowIfNullOrWhiteSpace(room, nameof(room));

        using var command = Connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO MatrixMessages(Id, Timestamp, UserId, UserLabel, Body, IsFromSelf, Room) VALUES(@Id, @Timestamp, @UserId, @UserLabel, @Body, @IsFromSelf, @Room)";
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Timestamp", timestamp.UtcTicks);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@UserLabel", userLabel);
        command.Parameters.AddWithValue("@Body", body);
        command.Parameters.AddWithValue("@IsFromSelf", isFromSelf);
        command.Parameters.AddWithValue("@Room", room);
        return command.ExecuteNonQueryAsync();
    }

    public async Task<List<DiscordMessage>> GetLastDiscordMessagesForChannelAsync(ulong channelId, int count)
    {
        var results = await Connection.QueryAsync<DiscordMessage>("SELECT * FROM (SELECT * FROM DiscordMessages WHERE ChannelId = @ChannelId ORDER BY Timestamp DESC LIMIT @Count) ORDER BY Timestamp ASC", new { ChannelId = channelId, Count = count });
        return results.ToList();
    }

    public async Task<List<MatrixMessage>> GetLastMatrixMessagesForRoomAsync(string room, int count)
    {
        var results = await Connection.QueryAsync<MatrixMessage>("SELECT * FROM (SELECT * FROM MatrixMessages WHERE Room = @Room ORDER BY Timestamp DESC LIMIT @Count) ORDER BY Timestamp ASC", new { Room = room, Count = count });
        return results.ToList();
    }

    public async Task<List<MatrixMessage>> GetOwnMessagesForTodayPlusLastForRoomAsync(string room)
    {
        var (startOfDay, endOfDay) = DateTimeHandler.Default.GetCurrentDayRange();

        var results = await Connection.QueryAsync<MatrixMessage>(@"
        SELECT * FROM (
            SELECT * FROM MatrixMessages 
            WHERE Room = @Room AND IsFromSelf = FALSE 
            ORDER BY Timestamp DESC 
            LIMIT 1
        )
        UNION ALL
        SELECT * FROM MatrixMessages 
        WHERE Room = @Room AND IsFromSelf = TRUE AND Timestamp BETWEEN @StartOfDay AND @EndOfDay
        ORDER BY Timestamp ASC
    ", new { Room = room, StartOfDay = startOfDay, EndOfDay = endOfDay });
        return results.ToList();
    }

    public Task SetSettingAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required", nameof(key));
        if (value == null) throw new ArgumentNullException(nameof(value));

        var sql = "INSERT OR REPLACE INTO Settings(Key, Value) VALUES(@Key, @Value)";
        var parameters = new { Key = key, Value = value };

        return Connection.ExecuteAsync(sql, parameters);
    }

    public async Task<string?> GetSettingOrDefaultAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required", nameof(key));

        var sql = "SELECT Value FROM Settings WHERE Key = @Key";
        var parameters = new { Key = key };

        var result = await Connection.QuerySingleOrDefaultAsync<string?>(sql, parameters);
        return result;
    }

    public Task RemoveSettingAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required", nameof(key));

        var sql = "DELETE FROM Settings WHERE Key = @Key";
        var parameters = new { Key = key };

        return Connection.ExecuteAsync(sql, parameters);
    }
}

public class DiscordMessage
{
    public ulong Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public ulong UserId { get; set; }
    public required string UserLabel { get; set; }
    public required string Body { get; set; }
    public bool IsFromSelf { get; set; }
    public ulong ChannelId { get; set; }
}

public class MatrixMessage
{
    public required string Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string UserId { get; set; }
    public required string UserLabel { get; set; }
    public required string Body { get; set; }
    public bool IsFromSelf { get; set; }
    public required string Room { get; set; }
}

public class DateTimeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    private readonly TimeZoneInfo databaseTimeZone = TimeZoneInfo.Local;
    public static readonly DateTimeHandler Default = new DateTimeHandler();

    public DateTimeHandler()
    {

    }

    public override DateTimeOffset Parse(object value)
    {
        if (value == null || value == DBNull.Value)
            return DateTimeOffset.MinValue;

        if (value is long l)
        {
            return new DateTimeOffset(l, TimeSpan.Zero);
        }

        throw new ArgumentException("Invalid DateTimeOffset value");
    }

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.Value = value.UtcTicks;
    }

    public (long startOfDay, long endOfDay) GetCurrentDayRange()
    {
        var now = DateTimeOffset.UtcNow;
        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

        return (startOfDay.UtcTicks, endOfDay.UtcTicks);
    }
}