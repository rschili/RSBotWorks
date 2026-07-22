using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.Explicit.SaneAI;

/// <summary>
/// Live API tests for the SaneAI OpenRouter client.
/// These tests hit the actual OpenRouter API. Mark them [Explicit] and
/// configure OPENROUTER_API_KEY in your .env file.
///
/// Every test logs raw JSON in/out and a reproducible curl command.
/// </summary>
public class OpenRouterClientTests
{
    // Change this to any OpenRouter model slug that supports tool calling.
    private const string TestModel = "openai/gpt-4o-mini";

    private static OpenRouterClient CreateClient(out string apiKey)
    {
        var env = DotNetEnv.Env.NoEnvVars().TraversePath().Load().ToDotEnvDictionary();
        apiKey = env["OPENROUTER_API_KEY"];
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("OPENROUTER_API_KEY is not set in the .env file.");

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var executor = new DefaultHttpExecutor(httpClientFactory);
        return new OpenRouterClient(apiKey, executor);
    }

    [Test, Explicit]
    public async Task BasicMessage_ReturnsText()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
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
    }

    [Test, Explicit]
    public async Task SystemPrompt_Works()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(256)
            .SetSystemPrompt("You are a pirate. Always respond in pirate speak.")
            .AddUserMessage("How are you today?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Request JSON:\n{result.Request.Body}");
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

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(256)
            .AddUserMessage("My name is Rob.")
            .AddAssistantMessage("Nice to meet you, Rob!")
            .AddUserMessage("What's my name?");

        var result = await client.SendAsync(composer);

        if (logger != null)
            await logger.LogInformationAsync($"Response: {result.TextContent}");

        await Assert.That(result.TextContent).IsNotNull();
        await Assert.That(result.TextContent!.ToLowerInvariant()).Contains("rob");
    }

    [Test, Explicit]
    public async Task ToolCalling_ExecutesLocalTool()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var weatherTool = new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get the current weather for a city",
            InputSchema = System.Text.Json.Nodes.JsonNode.Parse("""
            {
                "type": "object",
                "properties": { "city": { "type": "string", "description": "City name" } },
                "required": ["city"]
            }
            """)!
        };

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(512)
            .AddTools(weatherTool)
            .AddUserMessage("What's the weather in Berlin? Use the tool.");

        string? capturedCity = null;
        var result = await client.SendAsync(composer, async toolCall =>
        {
            using var argsDoc = System.Text.Json.JsonDocument.Parse(toolCall.ArgumentsJson);
            capturedCity = argsDoc.RootElement.GetProperty("city").GetString();
            return "22°C and sunny";
        });

        if (logger != null)
        {
            await logger.LogInformationAsync($"Request JSON:\n{result.Request.Body}");
            await logger.LogInformationAsync($"Response JSON:\n{result.Response.Body}");
            await logger.LogInformationAsync($"Curl:\n{CurlGenerator.Generate(result)}");
            await logger.LogInformationAsync($"Captured city: {capturedCity}");
            await logger.LogInformationAsync($"Tool rounds: {result.ToolRoundsExecuted}");
            await logger.LogInformationAsync($"Final text: {result.TextContent}");
        }

        await Assert.That(result.ToolRoundsExecuted).IsGreaterThanOrEqualTo(1);
        await Assert.That(capturedCity).IsNotNull();
        await Assert.That(result.TextContent).IsNotNull();
    }

    [Test, Explicit]
    public async Task WebSearch_ReturnsResults()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(1024)
            .EnableWebSearch(maxResults: 3, city: "Heidelberg", country: "DE", timezone: "Europe/Berlin")
            .AddUserMessage("What are the top technology news headlines today?");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Request JSON:\n{result.Request.Body}");
            await logger.LogInformationAsync($"Response JSON:\n{result.Response.Body}");
            await logger.LogInformationAsync($"Text: {result.TextContent}");
            await logger.LogInformationAsync($"Stop: {result.StopReason}");
        }

        await Assert.That(result.TextContent).IsNotNull();
    }

    [Test, Explicit]
    public async Task WebFetch_ReadsPage()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(1024)
            .EnableWebFetch(maxUses: 3)
            .AddUserMessage("Fetch https://example.com and tell me the main heading.");

        var result = await client.SendAsync(composer);

        if (logger != null)
        {
            await logger.LogInformationAsync($"Request JSON:\n{result.Request.Body}");
            await logger.LogInformationAsync($"Response JSON:\n{result.Response.Body}");
            await logger.LogInformationAsync($"Text: {result.TextContent}");
        }

        await Assert.That(result.TextContent).IsNotNull();
    }

    [Test, Explicit]
    public async Task ReasoningEffort_Works()
    {
        var client = CreateClient(out _);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(2048)
            .SetReasoningEffort("low")
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

    [Test, Explicit]
    public async Task ErrorHandling_BadApiKey_ThrowsException()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var executor = new DefaultHttpExecutor(httpClientFactory);
        var client = new OpenRouterClient("invalid-key", executor);
        var logger = TestContext.Current?.GetDefaultLogger();

        var composer = new OpenRouterRequestComposer()
            .SetModel(TestModel)
            .SetMaxTokens(100)
            .AddUserMessage("Hello");

        try
        {
            await client.SendAsync(composer);
            throw new Exception("Should have thrown OpenRouterApiException");
        }
        catch (OpenRouterApiException ex)
        {
            if (logger != null)
            {
                await logger.LogInformationAsync($"Status: {ex.StatusCode}");
                await logger.LogInformationAsync($"Error code: {ex.ErrorCode}");
                await logger.LogInformationAsync($"Message: {ex.Message}");
                await logger.LogInformationAsync($"Curl:\n{ex.ToCurl()}");
            }

            await Assert.That(ex.StatusCode).IsEqualTo(401);
        }
    }
}
