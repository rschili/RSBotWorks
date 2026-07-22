using System.Text.Json;
using System.Text.Json.Nodes;
using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.SaneAI;

public class OpenRouterRequestComposerTests
{
    [Test]
    public async Task BasicMessage_ProducesValidJson()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("Hello there");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("anthropic/claude-sonnet-4");
        await Assert.That(root.GetProperty("max_tokens").GetInt32()).IsEqualTo(1024);
        await Assert.That(root.GetProperty("messages").GetArrayLength()).IsEqualTo(1);

        var msg = root.GetProperty("messages")[0];
        await Assert.That(msg.GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(msg.GetProperty("content").GetString()).IsEqualTo("Hello there");
    }

    [Test]
    public async Task SystemPrompt_PrependedAsSystemMessage()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .SetSystemPrompt("You are a pirate.")
            .AddUserMessage("Ahoy!");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        await Assert.That(messages.GetArrayLength()).IsEqualTo(2);
        await Assert.That(messages[0].GetProperty("role").GetString()).IsEqualTo("system");
        await Assert.That(messages[0].GetProperty("content").GetString()).IsEqualTo("You are a pirate.");
        await Assert.That(messages[1].GetProperty("role").GetString()).IsEqualTo("user");
    }

    [Test]
    public async Task SamplingParameters_IncludedWhenSet()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(2048)
            .SetTemperature(0.7m)
            .SetTopK(40)
            .SetTopP(0.9m)
            .AddUserMessage("Test");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("temperature").GetDecimal()).IsEqualTo(0.7m);
        await Assert.That(root.GetProperty("top_k").GetInt32()).IsEqualTo(40);
        await Assert.That(root.GetProperty("top_p").GetDecimal()).IsEqualTo(0.9m);
    }

    [Test]
    public async Task SamplingParameters_OmittedWhenNotSet()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("Test");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.TryGetProperty("temperature", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("top_k", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("top_p", out _)).IsFalse();
    }

    [Test]
    public async Task ReasoningEffort_SetsReasoningObject()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(16000)
            .SetReasoningEffort("medium")
            .AddUserMessage("Test");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.GetProperty("reasoning").GetProperty("effort").GetString())
            .IsEqualTo("medium");
    }

    [Test]
    public async Task MultipleMessages_PreservesOrder()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("First")
            .AddAssistantMessage("Response")
            .AddUserMessage("Second");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);
        await Assert.That(messages[0].GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(messages[1].GetProperty("role").GetString()).IsEqualTo("assistant");
        await Assert.That(messages[2].GetProperty("role").GetString()).IsEqualTo("user");
    }

    [Test]
    public async Task ImageMessage_ProducesImageUrlContent()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // fake PNG header
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage(
                OpenRouterMessageBlock.FromText("What's in this image?"),
                OpenRouterMessageBlock.FromImage("image/png", imageData)
            );

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        await Assert.That(content.GetArrayLength()).IsEqualTo(2);
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("image_url");
        var url = content[1].GetProperty("image_url").GetProperty("url").GetString();
        await Assert.That(url).StartsWith("data:image/png;base64,");
    }

    [Test]
    public async Task Tools_ProducesOpenAiFunctionSchema()
    {
        var tool = new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get weather for a city",
            InputSchema = JsonNode.Parse("""
            {
                "type": "object",
                "properties": {
                    "city": { "type": "string", "description": "City name" }
                },
                "required": ["city"]
            }
            """)!
        };

        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddTools(tool)
            .AddUserMessage("Weather in Berlin?");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.TryGetProperty("tools", out var tools)).IsTrue();
        await Assert.That(tools.GetArrayLength()).IsEqualTo(1);
        await Assert.That(tools[0].GetProperty("type").GetString()).IsEqualTo("function");
        await Assert.That(tools[0].GetProperty("function").GetProperty("name").GetString()).IsEqualTo("get_weather");
        await Assert.That(tools[0].GetProperty("function").GetProperty("parameters").GetProperty("type").GetString())
            .IsEqualTo("object");
        await Assert.That(root.GetProperty("tool_choice").GetString()).IsEqualTo("auto");
    }

    [Test]
    public async Task WebSearch_AddsServerTool()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .EnableWebSearch(maxResults: 3, city: "Heidelberg", country: "DE", timezone: "Europe/Berlin")
            .AddUserMessage("Latest news?");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("tools");

        await Assert.That(tools.GetArrayLength()).IsEqualTo(1);
        var webTool = tools[0];
        await Assert.That(webTool.GetProperty("type").GetString()).IsEqualTo("openrouter:web_search");
        await Assert.That(webTool.GetProperty("parameters").GetProperty("max_results").GetInt32()).IsEqualTo(3);
        await Assert.That(webTool.GetProperty("parameters").GetProperty("user_location").GetProperty("city").GetString())
            .IsEqualTo("Heidelberg");
    }

    [Test]
    public async Task WebFetch_AddsServerTool()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .EnableWebFetch(maxUses: 7)
            .AddUserMessage("Read this page");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("tools");

        await Assert.That(tools.GetArrayLength()).IsEqualTo(1);
        await Assert.That(tools[0].GetProperty("type").GetString()).IsEqualTo("openrouter:web_fetch");
        await Assert.That(tools[0].GetProperty("parameters").GetProperty("max_uses").GetInt32()).IsEqualTo(7);
    }

    [Test]
    public async Task WebSearch_CombinesWithUserTools()
    {
        var tool = new ToolDefinition
        {
            Name = "my_tool",
            Description = "A tool",
            InputSchema = JsonNode.Parse("""{"type": "object", "properties": {}}""")!
        };

        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddTools(tool)
            .EnableWebSearch(maxResults: 5)
            .EnableWebFetch()
            .AddUserMessage("Search for stuff");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("tools");

        // User function tool + web search + web fetch
        await Assert.That(tools.GetArrayLength()).IsEqualTo(3);
    }

    [Test]
    public async Task Set_EscapeHatch()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .Set("some_future_feature", JsonValue.Create(true))
            .AddUserMessage("Test");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.GetProperty("some_future_feature").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Remove_RemovesProperty()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .SetTemperature(0.7m)
            .AddUserMessage("Test");

        composer.Remove("temperature");
        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.TryGetProperty("temperature", out _)).IsFalse();
    }

    [Test]
    public async Task Fork_CreatesIndependentCopy()
    {
        var template = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .SetSystemPrompt("You are helpful");

        var conv1 = template.Fork().AddUserMessage("Hello from conv1");
        var conv2 = template.Fork().AddUserMessage("Hello from conv2");

        using var doc1 = JsonDocument.Parse(conv1.BuildJsonString());
        using var doc2 = JsonDocument.Parse(conv2.BuildJsonString());

        // Each: system + 1 user message
        await Assert.That(doc1.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(2);
        await Assert.That(doc2.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(2);

        var content1 = doc1.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        var content2 = doc2.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        await Assert.That(content1).IsEqualTo("Hello from conv1");
        await Assert.That(content2).IsEqualTo("Hello from conv2");
    }

    [Test]
    public async Task Fork_ConfigChangesDoNotAffectOriginal()
    {
        var template = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024);

        var forked = template.Fork();
        forked.SetMaxTokens(9999);
        forked.AddUserMessage("forked message");

        template.AddUserMessage("original message");
        using var doc = JsonDocument.Parse(template.BuildJsonString());

        await Assert.That(doc.RootElement.GetProperty("max_tokens").GetInt32()).IsEqualTo(1024);
        await Assert.That(doc.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public void NoModel_ThrowsOnBuild()
    {
        var composer = new OpenRouterRequestComposer()
            .SetMaxTokens(1024)
            .AddUserMessage("Test");

        Assert.Throws<InvalidOperationException>(() => composer.BuildJsonString());
    }

    [Test]
    public void NoMessages_ThrowsOnBuild()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024);

        Assert.Throws<InvalidOperationException>(() => composer.BuildJsonString());
    }

    [Test]
    public async Task ToolResult_ProducesToolRoleMessage()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("What's the weather?")
            .AddRawAssistantMessage("""{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Berlin\"}"}}]}""")
            .AddToolResult("call_1", "22°C and sunny");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);

        var assistantMsg = messages[1];
        await Assert.That(assistantMsg.GetProperty("role").GetString()).IsEqualTo("assistant");
        await Assert.That(assistantMsg.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString())
            .IsEqualTo("get_weather");

        var toolResultMsg = messages[2];
        await Assert.That(toolResultMsg.GetProperty("role").GetString()).IsEqualTo("tool");
        await Assert.That(toolResultMsg.GetProperty("tool_call_id").GetString()).IsEqualTo("call_1");
        await Assert.That(toolResultMsg.GetProperty("content").GetString()).IsEqualTo("22°C and sunny");
    }

    [Test]
    public async Task MultipleToolResults_ProduceSeparateMessages()
    {
        var composer = new OpenRouterRequestComposer()
            .SetModel("anthropic/claude-sonnet-4")
            .SetMaxTokens(1024)
            .AddUserMessage("Two things")
            .AddToolResults(new[] { ("call_1", "result one"), ("call_2", "result two") });

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        // user + 2 tool messages
        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);
        await Assert.That(messages[1].GetProperty("role").GetString()).IsEqualTo("tool");
        await Assert.That(messages[1].GetProperty("tool_call_id").GetString()).IsEqualTo("call_1");
        await Assert.That(messages[2].GetProperty("role").GetString()).IsEqualTo("tool");
        await Assert.That(messages[2].GetProperty("tool_call_id").GetString()).IsEqualTo("call_2");
    }
}
