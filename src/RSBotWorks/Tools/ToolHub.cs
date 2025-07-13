using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RSBotWorks.Tools;

public abstract class ToolProvider
{
    private ImmutableArray<Tool> _tools = ImmutableArray<Tool>.Empty;
    public ImmutableArray<Tool> Tools => _tools;

    protected void ExposeTool(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        _tools = _tools.Add(tool);
    }
}

public class ToolHub
{
    public ILogger Logger { get; private init; }

    private ImmutableArray<ToolProvider> _toolProviders = ImmutableArray<ToolProvider>.Empty;
    public ImmutableArray<ToolProvider> ToolProviders => _toolProviders;

    private ImmutableArray<Tool> _tools = ImmutableArray<Tool>.Empty;

    public ImmutableArray<Tool> Tools => _tools;

    public bool EnableWebSearch { get; set; } = false;

    public ToolHub(ILogger<ToolHub>? logger = null)
    {
        Logger = logger ?? NullLogger<ToolHub>.Instance;
    }

    public void RegisterToolProvider(ToolProvider toolProvider)
    {
        if (toolProvider == null) throw new ArgumentNullException(nameof(toolProvider));

        _toolProviders = _toolProviders.Add(toolProvider);
        foreach (var tool in toolProvider.Tools)
        {
            _tools = _tools.Add(tool);
        }
        Logger.LogInformation("Registered tool provider: {ToolProviderType}", toolProvider.GetType().Name);
    }


    public async Task<string> CallAsync(string toolName, Dictionary<string, string> parameterValues)
    {
        try
        {
            Logger.LogInformation("Calling tool: {ToolName} with parameters: {ParameterValues}", toolName, parameterValues);
            var tool = _tools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
            {
                Logger.LogWarning("Tool '{ToolName}' not found.", toolName);
                return $"Tool '{toolName}' not found.";
            }

            var parameters = tool.Parameters;

            // Validate all required parameters are provided
            foreach (var parameter in parameters)
            {
                if (parameter.IsRequired && !parameterValues.ContainsKey(parameter.Name))
                {
                    Logger.LogWarning("Missing required parameter: {ParameterName} for tool: {ToolName}", parameter.Name, toolName);
                    return $"Missing required parameter: {parameter.Name} for tool: {toolName}";
                }
            }

            // Validate no extra parameters are provided
            foreach (var param in parameterValues.Keys)
            {
                if (!parameters.Any(p => p.Name.Equals(param, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.LogWarning("Extra parameter '{ParameterName}' provided for tool '{ToolName}'", param, toolName);
                }
            }

            // Execute the tool with the provided parameters
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await tool.ExecuteAsync(parameterValues);
            stopwatch.Stop();
            Logger.LogInformation("Tool '{ToolName}' executed in {ElapsedSeconds:F3} seconds.", toolName, stopwatch.Elapsed.TotalSeconds);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error executing tool '{ToolName}' with parameters: {Parameters}", toolName, parameterValues);
            return $"Tool call to '{toolName}' failed with error: {ex.Message}";
        }
    }
}