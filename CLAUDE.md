# General Instructions

I'm Robert Schili, github username rschili.
I'm a senior dev / tech lead in Bentley Systems' iTwin platform team.
For accessing GitHub (issues, PRs), prefer the `gh` CLI tool.
NEVER post comments or issues on my behalf, just give me the text and let me post them myself.
If a tool call fails or something seems implausible in general, halt immediately and ask me for guidance.

Our workflow treats us as complementary: you handle volume, synthesis and research. I rely on human judgment for evaluation, architectural reasoning, and catching the problems you don't know to look for.
I am aware of AI confirmation bias, I try to recognize it, but I'm not immune against it. Let's stay vigilant.

Never delegate understanding to subagents.

# SaneAI — Raw JSON AI Abstraction

SaneAI is a custom AI API abstraction layer that bypasses third-party SDKs entirely. It talks directly to provider REST APIs using raw JSON, so new model features/settings can be added immediately without waiting for SDK updates.

## Why it exists
Third-party C# SDKs (Anthropic.SDK, OpenAI, etc.) lag weeks behind API changes. Extended thinking, web search, adaptive thinking, effort levels, new sampling params — all blocked until some lib maintainer ships an update. SaneAI eliminates that bottleneck.

## Architecture
- **JSON in, JSON out.** No SDK types. Requests are composed as `JsonObject`, sent as strings, responses parsed from raw JSON.
- **`IHttpExecutor`** — Single-method HTTP abstraction (`SendAsync(RawHttpRequest) -> RawHttpResponse`). Trivial to mock for testing. `DefaultHttpExecutor` wraps `IHttpClientFactory`.
- **`AnthropicRequestComposer`** — Mutable request composer. Settings write directly to a `JsonObject` — no fixed config records that break on API changes. Config setters (SetModel, SetEffort, SetThinkingType, etc.) are NOT thread-safe — set them up once. Fork() and message-adding methods ARE thread-safe — fork templates across threads.
- **`AnthropicClient`** — Stateless, thread-safe. Sends requests with implicit tool call handling. Pass a `toolExecutor` callback and the client handles the loop automatically, aggregating token usage across rounds. Throws `AnthropicApiException` on errors — no IsSuccess checks.
- **`AnthropicApiException`** — Thrown on any API error. Parses the Anthropic error JSON for verbose diagnostics. Includes `ToCurl()` for reproducing failed requests.
- **`ChatResult`** — Contains raw request/response (for debugging), parsed fields (text, usage, stop reason), and aggregate stats (ToolRoundsExecuted, AllToolCallsExecuted, aggregated TokenUsage).
- **`CurlGenerator`** — Converts any request/result into a reproducible curl command. Auto-redacts API keys.
- **`ToolDefinition`** — Bridges to existing `LocalFunction` plugins via `FromLocalFunction()`.
- **Escape hatch** — `composer.Set("key", value)` adds arbitrary top-level JSON fields for any new API feature.

## Composer pattern
The composer is mutable. Create a template, configure it, then Fork() for each conversation:
```csharp
var template = new AnthropicRequestComposer()
    .SetModel("claude-opus-4-6")
    .SetMaxTokens(16000)
    .SetThinkingType("adaptive")
    .SetEffort("medium")
    .SetSystemPrompt("You are helpful")
    .AddTools(toolDefinitions);

// Fork per conversation:
var conv = template.Fork().AddUserMessage("Hello!");
var result = await client.SendAsync(conv, toolExecutor);
```

JSON-first config means no ThinkingConfig/WebSearchConfig records needed. New API features are one-liners:
- `SetThinkingType("adaptive")` → `{ "thinking": { "type": "adaptive" } }`
- `SetEffort("medium")` → `{ "output_config": { "effort": "medium" } }`
- `Set("some_future_param", JsonValue.Create(42))` → works immediately

## Tool call flow
Tool loops are implicit — the client handles them:
1. Pass a `Func<ToolCall, Task<string>>` toolExecutor to `SendAsync`
2. Client automatically executes tools and re-sends until the model is done (or maxToolRounds is hit)
3. Token usage is aggregated across all rounds
4. Single `ChatResult` returned with the final response

## Error handling
No IsSuccess pattern. Errors throw `AnthropicApiException`:
- Parses Anthropic error JSON for type + message
- `ex.ToCurl()` generates a curl command to reproduce the failure
- `ex.ErrorBody` has the raw response body

## Testing
- **Unit tests** in `Tests/SaneAI/` — mock `IHttpExecutor`, test composer JSON output, response parsing, implicit tool loops, curl generation. `QueuedMockHttpExecutor` supports multi-round tool call testing.
- **Explicit integration tests** in `Tests/Explicit/SaneAI/` — hit real Anthropic API with `CLAUDE_API_KEY` from `.env`. Every test logs raw JSON + curl for debugging.
- Test framework is **TUnit**. Explicit tests use `[Test, Explicit]`.
- Let's use dotnet run for testing instead of dotnet test, that way we don't need a global.json to use the new testing platform.
- You needn't run tests yourself, just build them, I will run them myself on demand.

## Current state
- First provider: **Anthropic Messages API** (`https://api.anthropic.com/v1/messages`)
- Supports: text, images (base64), tools (with implicit loop), web search, adaptive/extended thinking, effort levels, system prompts, all sampling params
- The old `UniversalAI` namespace still exists and is used by the bots. SaneAI is the replacement being built alongside it.

## SaneAI.Demo — Console chat app
`src/SaneAI.Demo/` is a standalone console app that demonstrates the full SaneAI workflow:
- Interactive chat loop with implicit tool call handling (get_current_time, calculate, roll_dice)
- Shows the composer pattern: a template primed with model/system prompt/tools, forked per conversation
- One-liner tool calling: `await client.SendAsync(conv, ExecuteTool)` — no manual loop needed
- Slash commands: `/curl`, `/json`, `/web`, `/noweb`, `/clear`
- Uses `DotNetEnv` for `CLAUDE_API_KEY`, `IHttpClientFactory` via DI, console logging
- Error handling via `catch (AnthropicApiException)` with curl output
