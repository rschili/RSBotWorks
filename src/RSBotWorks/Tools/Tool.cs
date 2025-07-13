using System.Collections.Immutable;

namespace RSBotWorks.Tools;


public class Tool
{
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<ToolParameter> Parameters { get; }

    private readonly Func<Dictionary<string, string>, Task<string>> _handler;

    public Tool(
        string name,
        string description,
        IEnumerable<ToolParameter>? parameters,
        Func<Dictionary<string, string>, Task<string>> handler)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Parameters = parameters?.ToList().AsReadOnly() ?? new List<ToolParameter>().AsReadOnly();
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public Task<string> ExecuteAsync(Dictionary<string, string> parameters)
    {
        return _handler(parameters);
    }
}

public class ToolParameter
{
    public string Name { get; }
    public string Description { get; }
    public bool IsRequired { get; }

    public string Type { get; }

    public ToolParameter(string name, string description, bool isRequired = true)
    {
        Name = name;
        Description = description;
        IsRequired = isRequired;
        Type = "string"; // Default type, can be extended later
    }
}