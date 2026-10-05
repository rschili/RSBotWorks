using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RSBotWorks.SaneAI;
using Wernstrom;

namespace RSBotWorks.Tests;

public class WernstromDiagnosticsTests
{
    [Test]
    public async Task LogAiResult_RecordsMetadataAndUtf8SizeWithoutLoggingConversation()
    {
        var logger = new CapturingLogger();
        using var service = CreateService(logger);
        var result = CreateResult("visible-answer-marker") with
        {
            Usage = new TokenUsage { InputTokens = 120, OutputTokens = 50, ReasoningTokens = 40 },
            ModelId = "anthropic/claude-opus-5.5",
            Provider = "Anthropic",
            ResponseId = "gen-123",
            StopReason = "stop",
            NativeStopReason = "end_turn",
            ToolRoundsExecuted = 1
        };

        service.LogAiResult("Chat", result, TimeSpan.FromSeconds(2.5));

        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        var entry = logger.Entries[0];
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Information);
        var fields = entry.Fields;
        await Assert.That((int)fields["InputTokens"]!).IsEqualTo(120);
        await Assert.That((int)fields["OutputTokens"]!).IsEqualTo(50);
        await Assert.That((int)fields["ReasoningTokens"]!).IsEqualTo(40);
        await Assert.That((string)fields["Model"]!).IsEqualTo(result.ModelId);
        await Assert.That((string)fields["Provider"]!).IsEqualTo(result.Provider);
        await Assert.That((string)fields["ResponseId"]!).IsEqualTo(result.ResponseId);
        await Assert.That((string)fields["FinishReason"]!).IsEqualTo("stop");
        await Assert.That((string)fields["NativeFinishReason"]!).IsEqualTo("end_turn");
        await Assert.That((int)fields["ToolRounds"]!).IsEqualTo(1);
        await Assert.That((int)fields["StatusCode"]!).IsEqualTo(200);
        await Assert.That((int)fields["TextChars"]!).IsEqualTo(result.TextContent!.Length);
        await Assert.That((int)fields["RequestBytes"]!).IsEqualTo(Encoding.UTF8.GetByteCount(result.Request.Body!));
        await Assert.That((int)fields["RequestBytes"]!).IsGreaterThan(result.Request.Body!.Length);
        await Assert.That((int)fields["ResponseBytes"]!).IsEqualTo(Encoding.UTF8.GetByteCount(result.Response.Body));
        var loggedStrings = string.Join("\n", fields.Values.OfType<string>());
        await Assert.That(loggedStrings).DoesNotContain("request-history-marker");
        await Assert.That(loggedStrings).DoesNotContain("secret-api-key");
        await Assert.That(loggedStrings).DoesNotContain("visible-answer-marker");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" \n ")]
    public async Task LogAiResult_EmptyTextLogsResponseAndPreservesUnknownCounts(string? text)
    {
        var logger = new CapturingLogger();
        using var service = CreateService(logger);
        var result = CreateResult(text) with { ResponseId = "gen-empty" };

        service.LogAiResult("Chat", result, TimeSpan.FromSeconds(2));

        await Assert.That(logger.Entries.Count).IsEqualTo(2);
        var summary = logger.Entries[0].Fields;
        await Assert.That(summary["InputTokens"]).IsNull();
        await Assert.That(summary["OutputTokens"]).IsNull();
        await Assert.That(summary["ReasoningTokens"]).IsNull();
        var warning = logger.Entries[1];
        await Assert.That(warning.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That((string)warning.Fields["ResponseId"]!).IsEqualTo("gen-empty");
        await Assert.That((string)warning.Fields["ResponseBody"]!).IsEqualTo(result.Response.Body);
        await Assert.That((bool)warning.Fields["ResponseBodyTruncated"]!).IsFalse();
        var loggedStrings = string.Join("\n", logger.Entries.SelectMany(e => e.Fields.Values).OfType<string>());
        await Assert.That(loggedStrings).DoesNotContain("request-history-marker");
        await Assert.That(loggedStrings).DoesNotContain("secret-api-key");
    }

    [Test]
    public async Task LogAiResult_AnomalyResponseExcerptIsBoundedAndMarkedTruncated()
    {
        var logger = new CapturingLogger();
        using var service = CreateService(logger);
        var result = CreateResult(null);
        result = result with { Response = result.Response with { Body = new string('x', 20_000) } };

        service.LogAiResult("Chat", result, TimeSpan.Zero);

        var warning = logger.Entries[1].Fields;
        await Assert.That(((string)warning["ResponseBody"]!).Length).IsEqualTo(16_384);
        await Assert.That((bool)warning["ResponseBodyTruncated"]!).IsTrue();
        await Assert.That((int)logger.Entries[0].Fields["ResponseBytes"]!).IsEqualTo(20_000);
    }

    private static WernstromService CreateService(CapturingLogger logger) => new(
        logger,
        Substitute.For<IHttpClientFactory>(),
        new WernstromServiceConfig { DiscordToken = "test", BrueckeId = 1 },
        new OpenRouterClient("test", Substitute.For<IHttpExecutor>()),
        null);

    private static ChatResult CreateResult(string? text) => new()
    {
        Request = new RawHttpRequest
        {
            Method = "POST",
            Url = OpenRouterClient.DefaultApiUrl,
            Headers = new Dictionary<string, string> { ["authorization"] = "Bearer secret-api-key" },
            Body = """{"messages":[{"role":"user","content":"Grüße ☕ request-history-marker"}]}"""
        },
        Response = new RawHttpResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string>(),
            Body = """{"choices":[{"message":{"content":null,"reasoning":"Grüße"},"finish_reason":"length"}]}"""
        },
        TextContent = text
    };

    private sealed class CapturingLogger : ILogger<WernstromService>
    {
        public List<(LogLevel Level, Dictionary<string, object?> Fields)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = (IEnumerable<KeyValuePair<string, object?>>)(object)state!;
            Entries.Add((logLevel, fields.ToDictionary(p => p.Key, p => p.Value)));
        }
    }
}
