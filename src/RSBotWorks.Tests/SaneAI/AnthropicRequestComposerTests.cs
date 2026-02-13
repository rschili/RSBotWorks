using System.Text.Json;
using System.Text.Json.Nodes;
using RSBotWorks.SaneAI;

namespace RSBotWorks.Tests.SaneAI;

public class AnthropicRequestComposerTests
{
    [Test]
    public async Task BasicMessage_ProducesValidJson()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Hello, Claude");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("claude-sonnet-4-20250514");
        await Assert.That(root.GetProperty("max_tokens").GetInt32()).IsEqualTo(1024);
        await Assert.That(root.GetProperty("messages").GetArrayLength()).IsEqualTo(1);

        var msg = root.GetProperty("messages")[0];
        await Assert.That(msg.GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(msg.GetProperty("content").GetString()).IsEqualTo("Hello, Claude");
    }

    [Test]
    public async Task SystemPrompt_AppearsInJson()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .SetSystemPrompt("You are a pirate.")
            .AddUserMessage("Ahoy!");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.GetProperty("system").GetString()).IsEqualTo("You are a pirate.");
    }

    [Test]
    public async Task SamplingParameters_IncludedWhenSet()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
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
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
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
    public async Task MultipleMessages_PreservesOrder()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
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
    public async Task ImageMessage_ProducesBase64Content()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // fake PNG header
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage(
                MessageBlock.FromText("What's in this image?"),
                MessageBlock.FromImage("image/png", imageData)
            );

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        await Assert.That(content.GetArrayLength()).IsEqualTo(2);
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("image");
        await Assert.That(content[1].GetProperty("source").GetProperty("media_type").GetString()).IsEqualTo("image/png");
        await Assert.That(content[1].GetProperty("source").GetProperty("type").GetString()).IsEqualTo("base64");
    }

    [Test]
    public async Task Tools_ProducesCorrectSchema()
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

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddTools(tool)
            .AddUserMessage("Weather in Berlin?");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.TryGetProperty("tools", out var tools)).IsTrue();
        await Assert.That(tools.GetArrayLength()).IsEqualTo(1);
        await Assert.That(tools[0].GetProperty("name").GetString()).IsEqualTo("get_weather");
        await Assert.That(root.GetProperty("tool_choice").GetProperty("type").GetString()).IsEqualTo("auto");
    }

    [Test]
    public async Task WebSearch_AddsWebSearchTool()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .EnableWebSearch(maxUses: 3, city: "Heidelberg", country: "DE", timezone: "Europe/Berlin")
            .AddUserMessage("Latest news?");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("tools");

        await Assert.That(tools.GetArrayLength()).IsEqualTo(1);
        var webTool = tools[0];
        await Assert.That(webTool.GetProperty("type").GetString()).IsEqualTo("web_search_20250305");
        await Assert.That(webTool.GetProperty("max_uses").GetInt32()).IsEqualTo(3);
        await Assert.That(webTool.GetProperty("user_location").GetProperty("city").GetString()).IsEqualTo("Heidelberg");
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

        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddTools(tool)
            .EnableWebSearch(maxUses: 5)
            .AddUserMessage("Search for stuff");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("tools");

        // User tool + web search tool
        await Assert.That(tools.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task ThinkingType_Adaptive()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-opus-4-6")
            .SetMaxTokens(16000)
            .SetThinkingType("adaptive")
            .AddUserMessage("Think about this");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var thinking = doc.RootElement.GetProperty("thinking");

        await Assert.That(thinking.GetProperty("type").GetString()).IsEqualTo("adaptive");
        await Assert.That(thinking.TryGetProperty("budget_tokens", out _)).IsFalse();
    }

    [Test]
    public async Task ThinkingType_EnabledWithBudget()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(16000)
            .SetThinkingType("enabled", budgetTokens: 10000)
            .AddUserMessage("Think hard about this");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var thinking = doc.RootElement.GetProperty("thinking");

        await Assert.That(thinking.GetProperty("type").GetString()).IsEqualTo("enabled");
        await Assert.That(thinking.GetProperty("budget_tokens").GetInt32()).IsEqualTo(10000);
    }

    [Test]
    public async Task ThinkingType_Disabled()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .SetThinkingType("disabled")
            .AddUserMessage("Just answer normally");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var thinking = doc.RootElement.GetProperty("thinking");

        await Assert.That(thinking.GetProperty("type").GetString()).IsEqualTo("disabled");
    }

    [Test]
    public async Task Effort_SetsOutputConfig()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-opus-4-6")
            .SetMaxTokens(16000)
            .SetEffort("medium")
            .AddUserMessage("Test");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.GetProperty("output_config").GetProperty("effort").GetString())
            .IsEqualTo("medium");
    }

    [Test]
    public async Task Set_EscapeHatch()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
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
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
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
        var template = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .SetSystemPrompt("You are helpful");

        // Fork into two different conversations
        var conv1 = template.Fork().AddUserMessage("Hello from conv1");
        var conv2 = template.Fork().AddUserMessage("Hello from conv2");

        var json1 = conv1.BuildJsonString();
        var json2 = conv2.BuildJsonString();

        using var doc1 = JsonDocument.Parse(json1);
        using var doc2 = JsonDocument.Parse(json2);

        // Both should have 1 message, not 2
        await Assert.That(doc1.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(1);
        await Assert.That(doc2.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(1);

        // Content should differ
        var content1 = doc1.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        var content2 = doc2.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        await Assert.That(content1).IsEqualTo("Hello from conv1");
        await Assert.That(content2).IsEqualTo("Hello from conv2");
    }

    [Test]
    public async Task Fork_ConfigChangesDoNotAffectOriginal()
    {
        var template = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024);

        var forked = template.Fork();
        forked.SetMaxTokens(9999);
        forked.AddUserMessage("forked message");

        // Original should still have max_tokens 1024 and no messages
        template.AddUserMessage("original message");
        var originalJson = template.BuildJsonString();
        using var doc = JsonDocument.Parse(originalJson);

        await Assert.That(doc.RootElement.GetProperty("max_tokens").GetInt32()).IsEqualTo(1024);
        await Assert.That(doc.RootElement.GetProperty("messages").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public void NoModel_ThrowsOnBuild()
    {
        var composer = new AnthropicRequestComposer()
            .SetMaxTokens(1024)
            .AddUserMessage("Test");

        Assert.Throws<InvalidOperationException>(() => composer.BuildJsonString());
    }

    [Test]
    public void NoMessages_ThrowsOnBuild()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024);

        Assert.Throws<InvalidOperationException>(() => composer.BuildJsonString());
    }

    [Test]
    public async Task ToolResult_ProducesCorrectFormat()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("What's the weather?")
            .AddRawAssistantContent("""[{"type":"text","text":"Let me check"},{"type":"tool_use","id":"toolu_123","name":"get_weather","input":{"city":"Berlin"}}]""")
            .AddToolResult("toolu_123", "22°C and sunny");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);

        // Assistant message with tool use
        var assistantMsg = messages[1];
        await Assert.That(assistantMsg.GetProperty("role").GetString()).IsEqualTo("assistant");
        await Assert.That(assistantMsg.GetProperty("content")[1].GetProperty("type").GetString()).IsEqualTo("tool_use");

        // Tool result
        var toolResultMsg = messages[2];
        await Assert.That(toolResultMsg.GetProperty("role").GetString()).IsEqualTo("user");
        var resultContent = toolResultMsg.GetProperty("content")[0];
        await Assert.That(resultContent.GetProperty("type").GetString()).IsEqualTo("tool_result");
        await Assert.That(resultContent.GetProperty("tool_use_id").GetString()).IsEqualTo("toolu_123");
    }

    [Test]
    public async Task MultipleToolResults_SingleMessage()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Do stuff")
            .AddRawAssistantContent("""[{"type":"tool_use","id":"t1","name":"a","input":{}},{"type":"tool_use","id":"t2","name":"b","input":{}}]""")
            .AddToolResults([("t1", "result1"), ("t2", "result2")]);

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");

        // user + assistant + tool_results = 3 messages
        await Assert.That(messages.GetArrayLength()).IsEqualTo(3);

        // The tool results should be in a single user message
        var toolMsg = messages[2];
        await Assert.That(toolMsg.GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(toolMsg.GetProperty("content").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task IndentedJson_WhenRequested()
    {
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-sonnet-4-20250514")
            .SetMaxTokens(1024)
            .AddUserMessage("Test");

        var compact = composer.BuildJsonString(indented: false);
        var indented = composer.BuildJsonString(indented: true);

        await Assert.That(compact).DoesNotContain("\n");
        await Assert.That(indented).Contains("\n");
    }

    [Test]
    public async Task FullOpus46Config_ProducesExpectedJson()
    {
        // The exact use case from the user's description
        var composer = new AnthropicRequestComposer()
            .SetModel("claude-opus-4-6")
            .SetMaxTokens(16000)
            .SetThinkingType("adaptive")
            .SetEffort("medium")
            .AddUserMessage("Hello");

        var json = composer.BuildJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("claude-opus-4-6");
        await Assert.That(root.GetProperty("max_tokens").GetInt32()).IsEqualTo(16000);
        await Assert.That(root.GetProperty("thinking").GetProperty("type").GetString()).IsEqualTo("adaptive");
        await Assert.That(root.GetProperty("output_config").GetProperty("effort").GetString()).IsEqualTo("medium");
    }
}
