using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.Explicit.SaneAI;

/// <summary>
/// Live API tests for SaneAI Anthropic client.
/// These tests hit the actual Anthropic API. Mark them [Explicit] and 
/// configure CLAUDE_API_KEY in your .env file.
/// 
/// The whole point: you can see the raw JSON going in and out,
/// and generate curl commands from any failed request.
/// </summary>
public class AnthropicClientTests
{
    private static AnthropicClient CreateClient(out string apiKey)
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        apiKey = env["CLAUDE_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("CLAUDE_API_KEY is not set in the .env file.");

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var executor = new DefaultHttpExecutor(httpClientFactory);
        return new AnthropicClient(apiKey, executor);
    }

    [Test, Explicit]
    public async Task BasicMessage_ReturnsText()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(256)
            .AddUserMessage("Say hello in exactly 3 words.");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Request JSON:\n{result.Request.Body}");
            await logger.LogInformationAsync($"Response JSON:\n{result.Response.Body}");
            await logger.LogInformationAsync($"Curl:\n{CurlGenerator.Generate(result)}");
            await logger.LogInformationAsync($"Text: {result.TextContent}");
            await logger.LogInformationAsync($"Tokens: in={result.Usage?.InputTokens} out={result.Usage?.OutputTokens}");
        }

        await Assert.That(result.TextContent).IsNotNull().And.IsNotEmpty();
        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(result.StopReason).IsEqualTo("end_turn");
    }

    [Test, Explicit]
    public async Task SystemPrompt_Works()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(256)
            .SetSystemPrompt("You are a pirate. Always respond in pirate speak.")
            .AddUserMessage("How are you today?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Response: {result.TextContent}");
            await logger.LogInformationAsync($"Curl:\n{CurlGenerator.Generate(result)}");
        }

        await Assert.That(result.TextContent).IsNotNull();
    }

    [Test, Explicit]
    public async Task MultiTurnConversation_Works()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(256)
            .AddUserMessage("My name is Rob.")
            .AddAssistantMessage("Nice to meet you, Rob!")
            .AddUserMessage("What's my name?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Response: {result.TextContent}");
        }

        await Assert.That(result.TextContent).IsNotNull();
        await Assert.That(result.TextContent!.ToLowerInvariant()).Contains("rob");
    }

    [Test, Explicit]
    public async Task RawJsonInspection_CanLogAndCurl()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(100)
            .SetTemperature(0.5m)
            .AddUserMessage("What is 2+2?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync("=== RAW REQUEST ===");
            await logger.LogInformationAsync(result.Request.Body ?? "(no body)");
            await logger.LogInformationAsync("=== RAW RESPONSE ===");
            await logger.LogInformationAsync(result.Response.Body);
            await logger.LogInformationAsync("=== CURL ===");
            await logger.LogInformationAsync(CurlGenerator.Generate(result));
            await logger.LogInformationAsync("=== PARSED ===");
            await logger.LogInformationAsync($"Status: {result.Response.StatusCode}");
            await logger.LogInformationAsync($"Text: {result.TextContent}");
            await logger.LogInformationAsync($"Model: {result.ModelId}");
            await logger.LogInformationAsync($"Stop: {result.StopReason}");
            await logger.LogInformationAsync($"Tokens: {result.Usage?.InputTokens}in / {result.Usage?.OutputTokens}out");
        }
    }

    [Test, Explicit]
    public async Task WebSearch_ReturnsResults()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .EnableWebSearch(maxUses: 3, city: "Heidelberg", country: "DE", timezone: "Europe/Berlin")
            .AddUserMessage("What movies are coming out next week?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Response JSON:\n{result.Response.Body}");
            await logger.LogInformationAsync($"Text: {result.TextContent}");
            await logger.LogInformationAsync($"Stop: {result.StopReason}");
        }

        await Assert.That(result.TextContent).IsNotNull();
    }

    [Test, Explicit]
    public async Task ThinkingDisabled_Works()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(256)
            .SetThinkingType("disabled")
            .AddUserMessage("What is the capital of France?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Response: {result.TextContent}");
            await logger.LogInformationAsync($"Curl:\n{CurlGenerator.Generate(result)}");
        }

        await Assert.That(result.TextContent).IsNotNull();
    }

    [Test, Explicit]
    public async Task ErrorHandling_BadApiKey_ThrowsException()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var executor = new DefaultHttpExecutor(httpClientFactory);
        var client = new AnthropicClient("invalid-key", executor);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(100)
            .AddUserMessage("Hello");

        try
        {
            await client.SendAsync(composer);
            throw new Exception("Should have thrown AnthropicApiException");
        }
        catch (AnthropicApiException ex)
        {
            if (logger != null)
            {
                await logger.LogInformationAsync($"Status: {ex.StatusCode}");
                await logger.LogInformationAsync($"Error type: {ex.ErrorType}");
                await logger.LogInformationAsync($"Message: {ex.Message}");
                await logger.LogInformationAsync($"Curl:\n{ex.ToCurl()}");
            }

            await Assert.That(ex.StatusCode).IsEqualTo(401);
        }
    }

    [Test, Explicit]
    public async Task AdaptiveThinking_WithEffort()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(4096)
            .SetThinkingType("adaptive")
            .SetEffort("medium")
            .AddUserMessage("What is the square root of 144?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Request JSON:\n{result.Request.Body}");
            await logger.LogInformationAsync($"Response: {result.TextContent}");
            await logger.LogInformationAsync($"Tokens: {result.Usage?.InputTokens}in / {result.Usage?.OutputTokens}out");
        }

        await Assert.That(result.TextContent).IsNotNull();
    }
}
