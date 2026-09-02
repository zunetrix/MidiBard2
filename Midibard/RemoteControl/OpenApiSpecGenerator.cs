using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace MidiBard.RemoteControl;

internal sealed class OpenApiSpecGenerator
{
    private readonly SortedDictionary<string, object> _schemas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _componentOrigins = new(StringComparer.Ordinal);
    private readonly NullabilityInfoContext _nullability = new();

    public static byte[] Generate(IReadOnlyList<RemoteControlEndpointDefinition> endpoints)
    {
        var generator = new OpenApiSpecGenerator();
        return generator.GenerateDocument(endpoints);
    }

    private byte[] GenerateDocument(IReadOnlyList<RemoteControlEndpointDefinition> endpoints)
    {
        ValidateEndpoints(endpoints);

        var paths = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var pathGroup in endpoints
                     .OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal)
                     .ThenBy(endpoint => endpoint.Method, StringComparer.Ordinal)
                     .GroupBy(endpoint => endpoint.Path))
        {
            var operations = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var endpoint in pathGroup)
                operations[endpoint.Method.ToLowerInvariant()] = CreateOperation(endpoint);
            paths[pathGroup.Key] = operations;
        }

        var securitySchemes = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["bearerAuth"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["scheme"] = "bearer",
                ["type"] = "http",
            },
        };

        var components = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["schemas"] = _schemas,
            ["securitySchemes"] = securitySchemes,
        };

        var document = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["components"] = components,
            ["info"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["description"] = "Loopback-only HTTP API for controlling MidiBard 2 playback.",
                ["title"] = "MidiBard 2 Remote Control API",
                ["version"] = "1.0.0",
            },
            ["openapi"] = "3.0.3",
            ["paths"] = paths,
            ["security"] = new[]
            {
                new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["bearerAuth"] = Array.Empty<string>(),
                },
            },
            ["servers"] = new[]
            {
                new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["description"] = "MidiBard loopback server",
                    ["url"] = "/",
                },
            },
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private object CreateOperation(RemoteControlEndpointDefinition endpoint)
    {
        var operation = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["description"] = endpoint.Description,
            ["operationId"] = endpoint.OperationId,
        };

        if (endpoint.QueryParameters.Count > 0)
        {
            operation["parameters"] = endpoint.QueryParameters
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .Select(CreateQueryParameter)
                .ToArray();
        }

        if (endpoint.RequestType != null)
        {
            operation["requestBody"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["content"] = JsonContent(CreateSchema(endpoint.RequestType)),
                ["required"] = true,
            };
        }

        var responses = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            [endpoint.SuccessStatusCode.ToString()] = CreateSuccessResponse(endpoint),
        };

        if (endpoint.ErrorStatusCodes.Count > 0)
        {
            var errorSchema = CreateSchema(typeof(ErrorResponse));
            foreach (var statusCode in endpoint.ErrorStatusCodes.OrderBy(code => code))
            {
                responses[statusCode.ToString()] = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["content"] = JsonContent(errorSchema),
                    ["description"] = ResponseDescription(statusCode),
                };
            }
        }

        operation["responses"] = responses;
        return operation;
    }

    private object CreateQueryParameter(RemoteControlQueryParameter parameter)
    {
        var schema = AsDictionary(CreateSchema(parameter.Type));
        if (parameter.DefaultValue != null)
            schema["default"] = parameter.DefaultValue;

        return new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["description"] = parameter.Description,
            ["in"] = "query",
            ["name"] = parameter.Name,
            ["required"] = parameter.Required,
            ["schema"] = schema,
        };
    }

    private object CreateSuccessResponse(RemoteControlEndpointDefinition endpoint)
    {
        var response = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["description"] = ResponseDescription(endpoint.SuccessStatusCode),
        };

        if (endpoint.ResponseType != null)
            response["content"] = JsonContent(CreateSchema(endpoint.ResponseType));

        return response;
    }

    private static SortedDictionary<string, object> JsonContent(object schema)
    {
        return new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["application/json"] = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["schema"] = schema,
            },
        };
    }

    private object CreateSchema(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
            return MakeNullable(CreateSchema(nullableType));

        if (type == typeof(string))
            return Scalar("string");
        if (type == typeof(bool))
            return Scalar("boolean");
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int))
            return Scalar("integer", "int32");
        if (type == typeof(long))
            return Scalar("integer", "int64");
        if (type == typeof(float))
            return Scalar("number", "float");
        if (type == typeof(double) || type == typeof(decimal))
            return Scalar("number", "double");
        if (type == typeof(Guid))
            return Scalar("string", "uuid");

        if (type.IsEnum)
        {
            return new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["enum"] = Enum.GetNames(type),
                ["type"] = "string",
            };
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            return new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["items"] = CreateSchema(elementType),
                ["type"] = "array",
            };
        }

        if (!IsContractObject(type))
            throw new InvalidOperationException("Unsupported OpenAPI contract type: " + type.FullName);

        var componentName = ComponentName(type);
        RegisterComponentOrigin(componentName, type);
        if (!_schemas.ContainsKey(componentName))
        {
            _schemas[componentName] = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
            };
            _schemas[componentName] = CreateObjectComponent(type);
        }

        return new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["$ref"] = "#/components/schemas/" + componentName,
        };
    }

    private object CreateObjectComponent(Type type)
    {
        var properties = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var required = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var property in type
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
        {
            var propertyName = RemoteControlJson.Options.PropertyNamingPolicy?.ConvertName(property.Name)
                ?? property.Name;
            var propertySchema = CreateSchema(property.PropertyType);
            var nullability = _nullability.Create(property).ReadState;
            if (nullability == NullabilityState.Nullable)
                propertySchema = MakeNullable(propertySchema);
            else
                required.Add(propertyName);

            properties[propertyName] = propertySchema;
        }

        var schema = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["properties"] = properties,
            ["type"] = "object",
        };

        if (required.Count > 0)
            schema["required"] = required.ToArray();

        return schema;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = null!;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerable == null)
        {
            elementType = null!;
            return false;
        }

        elementType = enumerable.GetGenericArguments()[0];
        return true;
    }

    private static bool IsContractObject(Type type)
        => type.IsClass && type != typeof(object) && type.Namespace == typeof(StatusResponse).Namespace;

    private static string ComponentName(Type type)
    {
        var tick = type.Name.IndexOf((char)96);
        return tick < 0 ? type.Name : type.Name[..tick];
    }

    private void RegisterComponentOrigin(string componentName, Type type)
    {
        if (_componentOrigins.TryGetValue(componentName, out var existing) && existing != type)
        {
            throw new InvalidOperationException(
                "OpenAPI component name collision for '" + componentName + "': "
                + existing.FullName + " and " + type.FullName);
        }

        _componentOrigins[componentName] = type;
    }

    private static object MakeNullable(object schema)
    {
        var dictionary = AsDictionary(schema);
        if (dictionary.ContainsKey("$ref"))
        {
            return new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["allOf"] = new[] { dictionary },
                ["nullable"] = true,
            };
        }

        dictionary["nullable"] = true;
        return dictionary;
    }

    private static SortedDictionary<string, object> Scalar(string type, string? format = null)
    {
        var schema = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = type,
        };
        if (format != null)
            schema["format"] = format;
        return schema;
    }

    private static SortedDictionary<string, object> AsDictionary(object value)
    {
        if (value is SortedDictionary<string, object> dictionary)
            return new SortedDictionary<string, object>(dictionary, StringComparer.Ordinal);

        throw new InvalidOperationException("Expected an object schema.");
    }

    private static string ResponseDescription(int statusCode)
    {
        return statusCode switch
        {
            200 => "Successful operation",
            204 => "No content",
            400 => "Bad request",
            401 => "Unauthorized",
            404 => "Not found",
            409 => "Conflict",
            410 => "Event history no longer available",
            500 => "Internal server error",
            _ => "Response",
        };
    }

    private static void ValidateEndpoints(IReadOnlyList<RemoteControlEndpointDefinition> endpoints)
    {
        var routeKeys = new HashSet<string>(StringComparer.Ordinal);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            if (!endpoint.Path.StartsWith("/api/v1/", StringComparison.Ordinal))
                throw new InvalidOperationException("Remote API path must be under /api/v1/: " + endpoint.Path);
            if (!routeKeys.Add(endpoint.Method + " " + endpoint.Path))
                throw new InvalidOperationException("Duplicate remote API route: " + endpoint.Method + " " + endpoint.Path);
            if (!operationIds.Add(endpoint.OperationId))
                throw new InvalidOperationException("Duplicate remote API operationId: " + endpoint.OperationId);
        }
    }
}
