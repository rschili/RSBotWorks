using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RSBotWorks.UniversalAI;


public class LocalFunction
{
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<LocalFunctionParameter> Parameters { get; }

    private readonly Func<JsonNode, Task<string>> _handler;

    public LocalFunction(
        string name,
        string description,
        IEnumerable<LocalFunctionParameter>? parameters,
        Func<JsonNode, Task<string>> handler)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Parameters = parameters?.ToList().AsReadOnly() ?? new List<LocalFunctionParameter>().AsReadOnly();
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public Task<string> ExecuteAsync(JsonNode parameters)
    {
        return _handler(parameters);
    }

    public static IEnumerable<LocalFunction> FromType<T>(T instance)
    {
        var methods = typeof(T).GetMethods()
            .Where(m => m.GetCustomAttribute<LocalFunctionAttribute>() != null);
        
        foreach (var method in methods)
        {
            yield return FromMethod(instance, method.Name);
        }
    }

    public static LocalFunction FromMethod<T>(T instance, string methodName)
    {
        Type type = typeof(T);
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        MethodInfo methodInfo = type.GetMethod(methodName) ?? throw new InvalidOperationException($"Failed to find a valid method for {type.FullName}.{methodName}()");

        // Get the LocalFunctionAttribute to determine the function name
        var localFunctionAttr = methodInfo.GetCustomAttribute<LocalFunctionAttribute>();
        if (localFunctionAttr == null)
        {
            throw new InvalidOperationException($"Method {methodName} must have a LocalFunctionAttribute");
        }

        // Get the description from DescriptionAttribute
        var descriptionAttr = methodInfo.GetCustomAttribute<DescriptionAttribute>();
        string description = descriptionAttr?.Description ?? $"Function {localFunctionAttr.Name}";

        // Build parameters from method parameters
        var parameters = new List<LocalFunctionParameter>();
        var methodParams = methodInfo.GetParameters();

        foreach (var param in methodParams)
        {
            var paramDescriptionAttr = param.GetCustomAttribute<DescriptionAttribute>();
            string paramDescription = paramDescriptionAttr?.Description ?? $"Parameter {param.Name}";
            bool isRequired = !param.HasDefaultValue;
            LocalFunctionParameterType paramType = GetParameterType(param.ParameterType);

            parameters.Add(new LocalFunctionParameter(param.Name!, paramDescription, paramType, isRequired));
        }

        // Create the handler that invokes the method
        Func<JsonNode, Task<string>> handler = async (jsonNode) =>
        {
            try
            {
                // Ensure the JsonNode is an object
                if (jsonNode is not JsonObject jsonObject)
                {
                    throw new ArgumentException("Parameters must be provided as a JSON object");
                }
                // Convert JSON arguments to method parameter types
                object?[] methodArgs = new object?[methodParams.Length];
                for (int i = 0; i < methodParams.Length; i++)
                {
                    var param = methodParams[i];
                    if (param.Name != null && jsonObject.TryGetPropertyValue(param.Name, out JsonNode? value))
                    {
                        methodArgs[i] = JsonSerializer.Deserialize(value, param.ParameterType);
                    }
                    else if (param.HasDefaultValue)
                    {
                        methodArgs[i] = param.DefaultValue;
                    }
                    else
                    {
                        throw new ArgumentException($"Required parameter '{param.Name}' not provided");
                    }
                }

                // Invoke the method and return the result directly
                object? result = methodInfo.Invoke(instance, methodArgs);
                
                // Since we assume all methods return Task<string>, we can await and return directly
                if (result is Task<string> stringTask)
                {
                    return await stringTask;
                }
                else
                {
                    throw new InvalidOperationException($"Method {localFunctionAttr.Name} must return Task<string>");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error executing function {localFunctionAttr.Name}: {ex.Message}", ex);
            }
        };

        return new LocalFunction(localFunctionAttr.Name, description, parameters, handler);
    }

    private static LocalFunctionParameterType GetParameterType(Type parameterType)
    {
        // Handle nullable types
        if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            parameterType = Nullable.GetUnderlyingType(parameterType)!;
        }

        return parameterType.Name switch
        {
            nameof(String) => LocalFunctionParameterType.String,
            nameof(Boolean) => LocalFunctionParameterType.Bool,
            nameof(Int32) => LocalFunctionParameterType.Int,
            nameof(Double) => LocalFunctionParameterType.Double,
            _ => throw new InvalidOperationException($"Unsupported parameter type: {parameterType.Name}. Only string, bool, int, and double are supported.")
        };
    }
}

public class LocalFunctionParameter
{
    public string Name { get; }
    public string Description { get; }
    public bool IsRequired { get; }
    public LocalFunctionParameterType Type { get; }

    public LocalFunctionParameter(string name, string description, LocalFunctionParameterType type, bool isRequired = true)
    {
        Name = name;
        Description = description;
        Type = type;
        IsRequired = isRequired;
    }
}

public enum LocalFunctionParameterType
{
    String,
    Bool,
    Int,
    Double
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LocalFunctionAttribute : Attribute
{
    public LocalFunctionAttribute(string name) => this.Name = name;

    public string Name { get; }
}
