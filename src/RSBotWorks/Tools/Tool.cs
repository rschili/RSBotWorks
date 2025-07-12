using System.Collections.Immutable;

namespace RSBotWorks.Tools;


public abstract class Tool
{
    public string Name { get; }
    public string Description { get; }

    private ImmutableArray<ToolParameter> _parameters = ImmutableArray<ToolParameter>.Empty;
    public IReadOnlyList<ToolParameter> Parameters => _parameters.AsReadOnly();

    protected Tool(string name, string description)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    protected void AddParameter(ToolParameter parameter)
    {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));
        _parameters.Add(parameter);
    }

    public abstract Task<string> ExecuteAsync(Dictionary<string, string> parameters);
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