using System.Text.Json;
using System.Text.Json.Nodes;
using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.SaneAI;

public class ChatResultParsingTests
{
    [Test]
    public async Task SuccessfulTextResponse_ParsedCorrectly()
    {
        var responseBody = """
        {
            "id": "msg_01XFDUDYJgAACzvnptvVoYEL",
            "type": "message",
            "role": "assistant",
            "content": [
                {
                    "type": "text",
                    "text": "Hello! How can I assist you today?"
                }
            ],
            "model": "claude-opus-4-6",
            "stop_reason": "end_turn",
            "usage": {
                "input_tokens": 12,
                "output_tokens": 8
            }
        }
        """;

        var executor = new MockHttpExecutor(200, responseBody);
        var client = new AnthropicClient("test-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-opus-4-6")
            .SetMaxTokens(1024)
            .AddUserMessage("Hello, Claude");

        var result = await client.SendAsync(composer);

        await Assert.That(result.TextContent).IsEqualTo("Hello! How can I assist you today?");
        await Assert.That(result.StopReason).IsEqualTo("end_turn");
        await Assert.That(result.ModelId).IsEqualTo("claude-opus-4-6");
        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(result.Usage!.InputTokens).IsEqualTo(12);
        await Assert.That(result.Usage!.OutputTokens).IsEqualTo(8);
        await Assert.That(result.HasToolCalls).IsFalse();
        await Assert.That(result.ToolRoundsExecuted).IsEqualTo(0);
    }

    [Test]
    public async Task ToolCallResponse_WithExecutor_HandledImplicitly()
    {
        // The executor returns a tool_use response first, then a final text response
        var responses = new Queue<(int Status, string Body)>();
        responses.Enqueue((200, """
        {
            "id": "msg_tool",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "text", "text": "Let me check the weather." },
                { "type": "tool_use", "id": "toolu_abc123", "name": "get_weather", "input": {"city": "Berlin"} }
            ],
            "model": "claude-sonnet-4-20250514",
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 50, "output_tokens": 30 }
        }
        """));
        responses.Enqueue((200, """
        {
            "id": "msg_final",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "text", "text": "It's 22°C and sunny in Berlin." }
            ],
            "model": "claude-sonnet-4-20250514",
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 80, "output_tokens": 15 }
        }
        """));

        var executor = new QueuedMockHttpExecutor(responses);
        var client = new AnthropicClient("test-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Weather in Berlin?");

        string? capturedToolName = null;
        var result = await client.SendAsync(composer, async toolCall =>
        {
            capturedToolName = toolCall.Name;
            return "22°C and sunny";
        });

        // Final text should be from the second response
        await Assert.That(result.TextContent).IsEqualTo("It's 22°C and sunny in Berlin.");
        await Assert.That(result.StopReason).IsEqualTo("end_turn");
        await Assert.That(result.HasToolCalls).IsFalse();

        // Tool was called
        await Assert.That(capturedToolName).IsEqualTo("get_weather");

        // Aggregated usage (50+80 input, 30+15 output)
        await Assert.That(result.Usage!.InputTokens).IsEqualTo(130);
        await Assert.That(result.Usage!.OutputTokens).IsEqualTo(45);

        // Tool round tracking
        await Assert.That(result.ToolRoundsExecuted).IsEqualTo(1);
        await Assert.That(result.AllToolCallsExecuted).IsNotNull();
        await Assert.That(result.AllToolCallsExecuted!.Count).IsEqualTo(1);
        await Assert.That(result.AllToolCallsExecuted![0].Name).IsEqualTo("get_weather");
    }

    [Test]
    public async Task ToolCallResponse_WithoutExecutor_ReturnsToolCalls()
    {
        var responseBody = """
        {
            "id": "msg_tool",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "text", "text": "Let me check." },
                { "type": "tool_use", "id": "toolu_abc123", "name": "get_weather", "input": {"city": "Berlin"} }
            ],
            "model": "claude-sonnet-4-20250514",
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 50, "output_tokens": 30 }
        }
        """;

        var executor = new MockHttpExecutor(200, responseBody);
        var client = new AnthropicClient("test-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Weather in Berlin?");

        // No toolExecutor → tool calls come back as-is
        var result = await client.SendAsync(composer);

        await Assert.That(result.HasToolCalls).IsTrue();
        await Assert.That(result.ToolCalls!.Count).IsEqualTo(1);
        await Assert.That(result.ToolCalls![0].Name).IsEqualTo("get_weather");
        await Assert.That(result.ToolRoundsExecuted).IsEqualTo(0);
    }

    [Test]
    public async Task ErrorResponse_ThrowsAnthropicApiException()
    {
        var responseBody = """
        {
            "type": "error",
            "error": {
                "type": "overloaded_error",
                "message": "Overloaded"
            }
        }
        """;

        var executor = new MockHttpExecutor(529, responseBody);
        var client = new AnthropicClient("test-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Hello");

        var ex = await Assert.ThrowsAsync<AnthropicApiException>(
            async () => await client.SendAsync(composer));
        await Assert.That(ex!.StatusCode).IsEqualTo(529);
        await Assert.That(ex.ErrorType).IsEqualTo("overloaded_error");
        await Assert.That(ex.Message).Contains("Overloaded");
    }

    [Test]
    public async Task Request_ContainsCorrectHeaders()
    {
        var executor = new MockHttpExecutor(200, """{"content":[],"usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = new AnthropicClient("my-api-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Test");

        var result = await client.SendAsync(composer);

        await Assert.That(result.Request.Headers["x-api-key"]).IsEqualTo("my-api-key");
        await Assert.That(result.Request.Headers["anthropic-version"]).IsEqualTo("2023-06-01");
        await Assert.That(result.Request.Headers["content-type"]).IsEqualTo("application/json");
        await Assert.That(result.Request.Url).IsEqualTo("https://api.anthropic.com/v1/messages");
    }

    [Test]
    public async Task ToolCallLoop_CanBeComposedManually()
    {
        // Verifies the composer can still compose tool call flows manually
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddTools(new ToolDefinition
            {
                Name = "get_weather",
                Description = "Get weather",
                InputSchema = JsonNode.Parse("""{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""")!
            })
            .AddUserMessage("Weather in Berlin?");

        var rawContent = """[{"type":"text","text":"Let me check"},{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{"city":"Berlin"}}]""";

        composer
            .AddRawAssistantContent(rawContent)
            .AddToolResult("toolu_1", "22°C and sunny in Berlin");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        // user message + assistant with tool_use + user with tool_result = 3
        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);
        await Assert.That(messages[0].GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(messages[1].GetProperty("role").GetString()).IsEqualTo("assistant");
        await Assert.That(messages[2].GetProperty("role").GetString()).IsEqualTo("user");

        var toolResultContent = messages[2].GetProperty("content")[0];
        await Assert.That(toolResultContent.GetProperty("type").GetString()).IsEqualTo("tool_result");
        await Assert.That(toolResultContent.GetProperty("tool_use_id").GetString()).IsEqualTo("toolu_1");
    }

    [Test]
    public async Task CurlGeneration_FromResult()
    {
        var executor = new MockHttpExecutor(200, """{"content":[{"type":"text","text":"Hi"}],"usage":{"input_tokens":1,"output_tokens":1}}""");
        var client = new AnthropicClient("secret-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Hello");

        var result = await client.SendAsync(composer);
        var curl = CurlGenerator.Generate(result);

        await Assert.That(curl).Contains("curl https://api.anthropic.com/v1/messages");
        await Assert.That(curl).DoesNotContain("secret-key");
        await Assert.That(curl).Contains("$ANTHROPIC_API_KEY");
        await Assert.That(curl).Contains("--data");
    }

    [Test]
    public async Task TokenUsage_Add_AggregatesCorrectly()
    {
        var a = new TokenUsage { InputTokens = 10, OutputTokens = 20 };
        var b = new TokenUsage { InputTokens = 30, OutputTokens = 40, CacheCreationInputTokens = 5 };
        var sum = a.Add(b);

        await Assert.That(sum.InputTokens).IsEqualTo(40);
        await Assert.That(sum.OutputTokens).IsEqualTo(60);
        await Assert.That(sum.CacheCreationInputTokens).IsEqualTo(5);
        await Assert.That(sum.CacheReadInputTokens).IsNull();
    }

    [Test]
    public async Task AnthropicApiException_ContainsCurl()
    {
        var request = new RawHttpRequest
        {
            Method = "POST",
            Url = "https://api.anthropic.com/v1/messages",
            Headers = new Dictionary<string, string>
            {
                ["x-api-key"] = "secret",
                ["content-type"] = "application/json"
            },
            Body = """{"model":"test"}"""
        };
        var response = new RawHttpResponse
        {
            StatusCode = 401,
            Body = """{"type":"error","error":{"type":"authentication_error","message":"Invalid API key"}}""",
            Headers = new Dictionary<string, string>()
        };

        var ex = AnthropicApiException.FromResponse(request, response);

        await Assert.That(ex.StatusCode).IsEqualTo(401);
        await Assert.That(ex.ErrorType).IsEqualTo("authentication_error");
        await Assert.That(ex.Message).Contains("Invalid API key");
        await Assert.That(ex.ToCurl()).Contains("$ANTHROPIC_API_KEY");
    }

    [Test]
    public async Task SendAsync_ForksComposer_OriginalUntouched()
    {
        // Verifies that SendAsync forks internally and doesn't mutate the caller's composer
        var responses = new Queue<(int Status, string Body)>();
        responses.Enqueue((200, """
        {
            "content": [
                { "type": "text", "text": "Let me check." },
                { "type": "tool_use", "id": "t1", "name": "my_tool", "input": {} }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """));
        responses.Enqueue((200, """
        {
            "content": [{ "type": "text", "text": "Done." }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 20, "output_tokens": 3 }
        }
        """));

        var executor = new QueuedMockHttpExecutor(responses);
        var client = new AnthropicClient("test-key", executor);

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Do something");

        await client.SendAsync(composer, async _ => "tool result");

        // Original composer should still have just 1 message
        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(1);
    }

    // --- Mock executors ---

    private class MockHttpExecutor : IHttpExecutor
    {
        private readonly int _statusCode;
        private readonly string _responseBody;

        public MockHttpExecutor(int statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RawHttpResponse
            {
                StatusCode = _statusCode,
                Body = _responseBody,
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" }
            });
        }
    }

    /// <summary>Returns responses from a queue, one per call. For testing multi-round tool loops.</summary>
    private class QueuedMockHttpExecutor : IHttpExecutor
    {
        private readonly Queue<(int Status, string Body)> _responses;

        public QueuedMockHttpExecutor(Queue<(int Status, string Body)> responses)
        {
            _responses = responses;
        }

        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken = default)
        {
            var (status, body) = _responses.Dequeue();
            return Task.FromResult(new RawHttpResponse
            {
                StatusCode = status,
                Body = body,
                Headers = new Dictionary<string, string> { ["content-type"] = "application/json" }
            });
        }
    }
}
