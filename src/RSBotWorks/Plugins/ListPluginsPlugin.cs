using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;

namespace RSBotWorks.Plugins;

// Sample code taken from https://github.com/microsoft/semantic-kernel/blob/5332c2e3306ab0f782a596f57128b676505cdc40/dotnet/samples/Concepts/Plugins/DescribeAllPluginsAndFunctions.cs
public class ListPluginsPlugin
{
    private readonly KernelPluginCollection _plugins;
    public ListPluginsPlugin(KernelPluginCollection plugins)
    {
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
    }

    [KernelFunction("list_plugins")]
    [Description("Gets a list of plugins and their function")]
    public Task<string> ListPluginsAsync()
    {
        var metadata = _plugins.GetFunctionsMetadata();
        StringBuilder sb = new();
        sb.AppendLine("functions:");
        
        foreach (var function in metadata)
        {
            sb.AppendLine($"  - name: \"{function.Name}\"");
            sb.AppendLine($"    plugin: \"{function.PluginName}\"");
            sb.AppendLine($"    description: \"{function.Description}\"");
            if (function.Parameters.Any())
            {
                sb.AppendLine("    parameters:");
                foreach (var parameter in function.Parameters)
                {
                    sb.AppendLine($"      - name: \"{parameter.Name}\"");
                    sb.AppendLine($"        description: \"{parameter.Description}\"");
                }
            }
        }
        
        return Task.FromResult(sb.ToString());
    }

}