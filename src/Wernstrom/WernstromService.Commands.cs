using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wernstrom;

public partial class WernstromService : BackgroundService
{

    private List<string> RollCommandNames = new()
    {
        "rnd",
        "roll",
        "rand",
        "random"
    };

    private async Task RegisterCommandsAsync()
    {
        if (_client == null)
            return;

        foreach (var commandName in RollCommandNames)
        {
            var commandBuilder = new SlashCommandBuilder()
                .WithContextTypes(InteractionContextType.Guild, InteractionContextType.PrivateChannel)
                .WithName(commandName)
                .WithDescription("Rolls a random number.")
                .AddOption("range", ApplicationCommandOptionType.String, "One or two numbers separated by a space or slash.", isRequired: false);
            await _client.CreateGlobalApplicationCommandAsync(commandBuilder.Build()).ConfigureAwait(false);

            Logger.LogInformation("Created slash command: {CommandName}", commandName);
        }
    }

    private async Task SlashCommandExecuted(SocketSlashCommand command)
    {
        try
        {
            string commandName = command.Data.Name;
            if (RollCommandNames.Contains(commandName))
            {
                await RollCommandExecuted(command);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while processing a slash command. Name: {Name}, Input was: {Input}", command.CommandName, command.Data.ToString());
            await command.RespondAsync($"Sorry das hat nicht funktioniert.", ephemeral: true).ConfigureAwait(false);
        }
    }

    private async Task RollCommandExecuted(SocketSlashCommand command)
    {
        int lowerBound = 1;
        int upperBound = 100;
        var rangeOption = command.Data.Options.FirstOrDefault(o => o.Name == "range");
        if (rangeOption != null)
        {
            var rangeString = rangeOption.Value.ToString();
            if (!string.IsNullOrWhiteSpace(rangeString))
            {
                (lowerBound, upperBound) = ParseRangeOption(rangeString);
            }
        }

        int result = 0;
        if (lowerBound == upperBound)
            result = lowerBound;
        else
        {
            var random = new Random();
            result = random.Next(lowerBound, upperBound + 1);
        }
        await command.RespondAsync($"{MentionUtils.MentionUser(command.User.Id)} rolled a {result} ({lowerBound}-{upperBound})");
    }

    private (int LowerBound, int UpperBound) ParseRangeOption(string rangeOption)
    {
        if (string.IsNullOrWhiteSpace(rangeOption))
            throw new ArgumentException("Range option cannot be null or empty.", nameof(rangeOption));

        var parts = rangeOption.Split([' ', '-'], 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out int singleValue) && singleValue > 0)
        {
            return (1, singleValue);
        }
        else if (parts.Length == 2 && int.TryParse(parts[0], out int min) && int.TryParse(parts[1], out int max)
            && min > 0 && max > 0 && min <= max)
        {
            return (min, max);
        }

        return (1, 100); // default range
    }
}
