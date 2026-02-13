using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.SaneAI;

public class CurlGeneratorTests
{
    [Test]
    public async Task BasicRequest_ProducesValidCurl()
    {
        var request = new RawHttpRequest
        {
            Method = "POST",
            Url = "https://api.anthropic.com/v1/messages",
            Headers = new Dictionary<string, string>
            {
                ["x-api-key"] = "sk-ant-secret123",
                ["anthropic-version"] = "2023-06-01",
                ["content-type"] = "application/json"
            },
            Body = """{"model":"claude-sonnet-4-20250514","max_tokens":1024,"messages":[{"role":"user","content":"Hello"}]}"""
        };

        var curl = CurlGenerator.Generate(request);

        await Assert.That(curl).Contains("curl https://api.anthropic.com/v1/messages");
        await Assert.That(curl).Contains("$ANTHROPIC_API_KEY");
        await Assert.That(curl).DoesNotContain("sk-ant-secret123");
        await Assert.That(curl).Contains("anthropic-version: 2023-06-01");
        await Assert.That(curl).Contains("--data");
    }

    [Test]
    public async Task SensitiveHeaders_RedactedByDefault()
    {
        var request = new RawHttpRequest
        {
            Method = "POST",
            Url = "https://example.com/api",
            Headers = new Dictionary<string, string>
            {
                ["x-api-key"] = "super-secret",
                ["Authorization"] = "Bearer token123",
                ["x-custom"] = "not-secret"
            }
        };

        var curl = CurlGenerator.Generate(request);

        await Assert.That(curl).DoesNotContain("super-secret");
        await Assert.That(curl).DoesNotContain("token123");
        await Assert.That(curl).Contains("$ANTHROPIC_API_KEY");
        await Assert.That(curl).Contains("$AUTH_TOKEN");
        await Assert.That(curl).Contains("not-secret");
    }

    [Test]
    public async Task CustomRedactions_Override()
    {
        var request = new RawHttpRequest
        {
            Method = "POST",
            Url = "https://example.com/api",
            Headers = new Dictionary<string, string>
            {
                ["x-api-key"] = "sk-ant-12345",
                ["x-custom-token"] = "bearer-xyz-789"
            }
        };

        var redactions = new Dictionary<string, string>
        {
            ["x-api-key"] = "$MY_KEY",
            ["x-custom-token"] = "$CUSTOM_TOKEN"
        };

        var curl = CurlGenerator.Generate(request, redactions);

        await Assert.That(curl).Contains("$MY_KEY");
        await Assert.That(curl).Contains("$CUSTOM_TOKEN");
        await Assert.That(curl).DoesNotContain("sk-ant-12345");
        await Assert.That(curl).DoesNotContain("bearer-xyz-789");
    }

    [Test]
    public async Task NoBody_OmitsDataFlag()
    {
        var request = new RawHttpRequest
        {
            Method = "GET",
            Url = "https://example.com/api",
            Headers = new Dictionary<string, string>()
        };

        var curl = CurlGenerator.Generate(request);

        await Assert.That(curl).DoesNotContain("--data");
    }

    [Test]
    public async Task SingleQuotesInBody_Escaped()
    {
        var request = new RawHttpRequest
        {
            Method = "POST",
            Url = "https://example.com/api",
            Headers = new Dictionary<string, string>(),
            Body = """{"text":"it's a test"}"""
        };

        var curl = CurlGenerator.Generate(request);

        // Single quotes should be escaped for shell safety
        await Assert.That(curl).Contains("'\\''");
    }

    [Test]
    public async Task FromChatResult_Works()
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

        var result = new ChatResult
        {
            Request = request,
            Response = new RawHttpResponse
            {
                StatusCode = 200,
                Body = "{}",
                Headers = new Dictionary<string, string>()
            }
        };

        var curl = CurlGenerator.Generate(result);

        await Assert.That(curl).Contains("curl https://api.anthropic.com/v1/messages");
    }
}
