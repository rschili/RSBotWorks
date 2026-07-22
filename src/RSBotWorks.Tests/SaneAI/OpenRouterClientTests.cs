using System.Text.Json;
using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.SaneAI;

public class OpenRouterClientTests
{
    [Test]
    public async Task SuccessfulTextResponse_ParsedCorrectly()
    {
        var responseBody = """
        {
            "id": "gen-123",
            "object": "chat.completion",
            "created": 1677652288,
            "model": "anthropic/claude-sonnet-4",
            "choices": [
                {
                    "index": 0,
                    "finish_reason": "stop",
                    "message": {
                        "role": "assistant",
                        "content": "Hello! How can I assist you today?"
                    }
                }
            ],
            "usage": {
                "prompt_tokens": 12,
                "completion_tokens": 8,
                "total_tokens": 20
            }
        }
        """;

        var executor = new MockHttpExecutor(200, responseBody);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("Hello");

        var result = await client.SendAsync(composer);

        await Assert.That(result.TextContent).IsEqualTo("Hello! How can I assist you today?");
        await Assert.That(result.StopReason).IsEqualTo("stop");
        await Assert.That(result.ModelId).IsEqualTo("anthropic/claude-sonnet-4");
        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(result.Usage!.InputTokens).IsEqualTo(12);
        await Assert.That(result.Usage!.OutputTokens).IsEqualTo(8);
        await Assert.That(result.HasToolCalls).IsFalse();
        await Assert.That(result.ToolRoundsExecuted).IsEqualTo(0);
    }

    [Test]
    public async Task Usage_CachedTokensMapped()
    {
        var responseBody = """
        {
            "id": "gen-1", "object": "chat.completion", "created": 1, "model": "m",
            "choices": [{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"hi"}}],
            "usage": {
                "prompt_tokens": 100, "completion_tokens": 10, "total_tokens": 110,
                "prompt_tokens_details": { "cached_tokens": 40, "cache_write_tokens": 20 }
            }
        }
        """;

        var executor = new MockHttpExecutor(200, responseBody);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("hi");

        var result = await client.SendAsync(composer);

        await Assert.That(result.Usage!.CacheReadInputTokens).IsEqualTo(40);
        await Assert.That(result.Usage!.CacheCreationInputTokens).IsEqualTo(20);
    }

    [Test]
    public async Task ToolCallResponse_WithExecutor_HandledImplicitly()
    {
        var responses = new Queue<(int Status, string Body)>();
        responses.Enqueue((200, """
        {
            "id": "gen-tool", "object": "chat.completion", "created": 1, "model": "anthropic/claude-sonnet-4",
            "choices": [
                {
                    "index": 0,
                    "finish_reason": "tool_calls",
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            { "id": "call_abc", "type": "function", "function": { "name": "get_weather", "arguments": "{\"city\":\"Berlin\"}" } }
                        ]
                    }
                }
            ],
            "usage": { "prompt_tokens": 50, "completion_tokens": 30, "total_tokens": 80 }
        }
        """));
        responses.Enqueue((200, """
        {
            "id": "gen-final", "object": "chat.completion", "created": 2, "model": "anthropic/claude-sonnet-4",
            "choices": [
                {
                    "index": 0,
                    "finish_reason": "stop",
                    "message": { "role": "assistant", "content": "It's 22°C and sunny in Berlin." }
                }
            ],
            "usage": { "prompt_tokens": 80, "completion_tokens": 15, "total_tokens": 95 }
        }
        """));

        var executor = new QueuedMockHttpExecutor(responses);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("Weather in Berlin?");

        string? capturedToolName = null;
        string? capturedArgs = null;
        var result = await client.SendAsync(composer, async toolCall =>
        {
            capturedToolName = toolCall.Name;
            capturedArgs = toolCall.ArgumentsJson;
            return "22°C and sunny";
        });

        await Assert.That(result.TextContent).IsEqualTo("It's 22°C and sunny in Berlin.");
        await Assert.That(result.StopReason).IsEqualTo("stop");
        await Assert.That(result.HasToolCalls).IsFalse();

        await Assert.That(capturedToolName).IsEqualTo("get_weather");
        await Assert.That(capturedArgs).Contains("Berlin");

        // Aggregated usage (50+80 input, 30+15 output)
        await Assert.That(result.Usage!.InputTokens).IsEqualTo(130);
        await Assert.That(result.Usage!.OutputTokens).IsEqualTo(45);

        await Assert.That(result.ToolRoundsExecuted).IsEqualTo(1);
        await Assert.That(result.AllToolCallsExecuted).IsNotNull();
        await Assert.That(result.AllToolCallsExecuted!.Count).IsEqualTo(1);
        await Assert.That(result.AllToolCallsExecuted![0].Name).IsEqualTo("get_weather");
    }

    [Test]
    public async Task ToolCallResponse_ToolMessageSentBackCorrectly()
    {
        var responses = new Queue<(int Status, string Body)>();
        responses.Enqueue((200, """
        {
            "id": "g1", "object": "chat.completion", "created": 1, "model": "m",
            "choices": [{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":null,
                "tool_calls":[{"id":"call_xyz","type":"function","function":{"name":"do_thing","arguments":"{}"}}]}}],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """));
        responses.Enqueue((200, """
        {
            "id": "g2", "object": "chat.completion", "created": 2, "model": "m",
            "choices": [{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}],
            "usage": { "prompt_tokens": 12, "completion_tokens": 2, "total_tokens": 14 }
        }
        """));

        var executor = new CapturingQueuedMockHttpExecutor(responses);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("go");

        await client.SendAsync(composer, async _ => "tool output");

        // The second request should contain the assistant tool_call message and a tool result message
        var secondRequest = executor.CapturedRequests[1];
        using var doc = JsonDocument.Parse(secondRequest.Body!);
        var messages = doc.RootElement.GetProperty("messages");

        // user + assistant(tool_calls) + tool result
        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);
        await Assert.That(messages[1].GetProperty("role").GetString()).IsEqualTo("assistant");
        await Assert.That(messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString()).IsEqualTo("call_xyz");
        await Assert.That(messages[2].GetProperty("role").GetString()).IsEqualTo("tool");
        await Assert.That(messages[2].GetProperty("tool_call_id").GetString()).IsEqualTo("call_xyz");
        await Assert.That(messages[2].GetProperty("content").GetString()).IsEqualTo("tool output");
    }

    [Test]
    public async Task ToolCallResponse_WithoutExecutor_ReturnsToolCalls()
    {
        var responseBody = """
        {
            "id": "gen-tool", "object": "chat.completion", "created": 1, "model": "m",
            "choices": [
                {
                    "index": 0,
                    "finish_reason": "tool_calls",
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            { "id": "call_abc", "type": "function", "function": { "name": "get_weather", "arguments": "{\"city\":\"Berlin\"}" } }
                        ]
                    }
                }
            ],
            "usage": { "prompt_tokens": 50, "completion_tokens": 30, "total_tokens": 80 }
        }
        """;

        var executor = new MockHttpExecutor(200, responseBody);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("Weather?");

        var result = await client.SendAsync(composer);

        await Assert.That(result.HasToolCalls).IsTrue();
        await Assert.That(result.ToolCalls!.Count).IsEqualTo(1);
        await Assert.That(result.ToolCalls![0].Name).IsEqualTo("get_weather");
        await Assert.That(result.ToolCalls![0].Id).IsEqualTo("call_abc");
        await Assert.That(result.ToolRoundsExecuted).IsEqualTo(0);
    }

    [Test]
    public async Task WebSearchResponse_ParsedAsPlainText()
    {
        // OpenRouter runs web search server-side; from the client's view it's just a text answer.
        var responseBody = """
        {
            "id": "gen-web", "object": "chat.completion", "created": 1, "model": "m",
            "choices": [
                { "index": 0, "finish_reason": "stop",
                  "message": { "role": "assistant", "content": "Here are the latest results..." } }
            ],
            "usage": {
                "prompt_tokens": 200, "completion_tokens": 50, "total_tokens": 250,
                "server_tool_use_details": { "web_search_requests": 2, "tool_calls_requested": 2, "tool_calls_executed": 2 }
            }
        }
        """;

        var executor = new MockHttpExecutor(200, responseBody);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(1024)
            .EnableWebSearch(maxResults: 3)
            .AddUserMessage("Latest news?");

        var result = await client.SendAsync(composer);

        await Assert.That(result.TextContent).IsEqualTo("Here are the latest results...");
        await Assert.That(result.HasToolCalls).IsFalse();
        await Assert.That(result.StopReason).IsEqualTo("stop");
    }

    [Test]
    public async Task ErrorResponse_ThrowsOpenRouterApiException()
    {
        var responseBody = """
        {
            "error": {
                "code": 429,
                "message": "Rate limit exceeded"
            }
        }
        """;

        var executor = new MockHttpExecutor(429, responseBody);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("Hello");

        var ex = await Assert.ThrowsAsync<OpenRouterApiException>(
            async () => await client.SendAsync(composer));
        await Assert.That(ex!.StatusCode).IsEqualTo(429);
        await Assert.That(ex.ErrorCode).IsEqualTo(429);
        await Assert.That(ex.Message).Contains("Rate limit exceeded");
    }

    [Test]
    public async Task Request_ContainsCorrectHeadersAndUrl()
    {
        var executor = new MockHttpExecutor(200,
            """{"id":"g","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"hi"}}],"usage":{"prompt_tokens":0,"completion_tokens":0,"total_tokens":0}}""");
        var client = new OpenRouterClient("my-api-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("Test");

        var result = await client.SendAsync(composer);

        await Assert.That(result.Request.Headers["authorization"]).IsEqualTo("Bearer my-api-key");
        await Assert.That(result.Request.Headers["content-type"]).IsEqualTo("application/json");
        await Assert.That(result.Request.Url).IsEqualTo("https://openrouter.ai/api/v1/chat/completions");
    }

    [Test]
    public async Task CurlGeneration_RedactsApiKey()
    {
        var executor = new MockHttpExecutor(200,
            """{"id":"g","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"hi"}}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""");
        var client = new OpenRouterClient("secret-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("Hello");

        var result = await client.SendAsync(composer);
        var curl = CurlGenerator.Generate(result);

        await Assert.That(curl).Contains("curl https://openrouter.ai/api/v1/chat/completions");
        await Assert.That(curl).DoesNotContain("secret-key");
        await Assert.That(curl).Contains("--data");
    }

    [Test]
    public async Task ErrorResponse_ExceptionCurlRedactsKey()
    {
        var request = new RawHttpRequest
        {
            Method = "POST",
            Url = "https://openrouter.ai/api/v1/chat/completions",
            Headers = new Dictionary<string, string>
            {
                ["authorization"] = "Bearer secret-key",
                ["content-type"] = "application/json"
            },
            Body = """{"model":"m"}"""
        };
        var response = new RawHttpResponse
        {
            StatusCode = 401,
            Body = """{"error":{"code":401,"message":"Missing Authentication header"}}""",
            Headers = new Dictionary<string, string>()
        };

        var ex = OpenRouterApiException.FromResponse(request, response);

        await Assert.That(ex.StatusCode).IsEqualTo(401);
        await Assert.That(ex.ErrorCode).IsEqualTo(401);
        await Assert.That(ex.Message).Contains("Missing Authentication header");
        var curl = ex.ToCurl();
        await Assert.That(curl).DoesNotContain("secret-key");
        await Assert.That(curl).Contains("$OPENROUTER_API_KEY");
    }

    [Test]
    public async Task SendAsync_ForksComposer_OriginalUntouched()
    {
        var responses = new Queue<(int Status, string Body)>();
        responses.Enqueue((200, """
        {
            "id": "g1", "object": "chat.completion", "created": 1, "model": "m",
            "choices": [{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":null,
                "tool_calls":[{"id":"t1","type":"function","function":{"name":"my_tool","arguments":"{}"}}]}}],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """));
        responses.Enqueue((200, """
        {
            "id": "g2", "object": "chat.completion", "created": 2, "model": "m",
            "choices": [{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"Done."}}],
            "usage": { "prompt_tokens": 20, "completion_tokens": 3, "total_tokens": 23 }
        }
        """));

        var executor = new QueuedMockHttpExecutor(responses);
        var client = new OpenRouterClient("test-key", executor);

        var composer = new OpenRouterRequestComposer()
            .SetModel("m").SetMaxTokens(64).AddUserMessage("Do something");

        await client.SendAsync(composer, async _ => "tool result");

        // Original composer should still have just 1 message
        using var doc = JsonDocument.Parse(composer.BuildJsonString());
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

    /// <summary>Like QueuedMockHttpExecutor but records every request it sends.</summary>
    private class CapturingQueuedMockHttpExecutor : IHttpExecutor
    {
        private readonly Queue<(int Status, string Body)> _responses;
        public List<RawHttpRequest> CapturedRequests { get; } = [];

        public CapturingQueuedMockHttpExecutor(Queue<(int Status, string Body)> responses)
        {
            _responses = responses;
        }

        public Task<RawHttpResponse> SendAsync(RawHttpRequest request, CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(request);
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
