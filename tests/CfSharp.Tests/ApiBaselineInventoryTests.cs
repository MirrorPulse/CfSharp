using System.Reflection;
using System.Text.Json;

namespace CfSharp.Tests;

public sealed class ApiBaselineInventoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    [Trait("Category", "ApiBaseline")]
    public void WritePublicApiInventory()
    {
        string? outputPath = Environment.GetEnvironmentVariable("CFSHARP_API_BASELINE_OUTPUT");
        Assert.False(string.IsNullOrWhiteSpace(outputPath));
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CfSharp.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        string configuration = Environment.GetEnvironmentVariable(
            "CFSHARP_API_BASELINE_CONFIGURATION") ?? "Release";
        string[] assemblyPaths =
        [
            Path.Combine(root!.FullName, "src", "CfSharp.Native", "bin", configuration, "net10.0-windows", "CfSharp.Native.dll"),
            Path.Combine(root.FullName, "src", "CfSharp", "bin", configuration, "net10.0-windows", "CfSharp.dll"),
        ];
        Dictionary<string, string[]> inventory = [];
        foreach (string assemblyPath in assemblyPaths)
        {
            Assert.True(File.Exists(assemblyPath), $"Built assembly is missing: {assemblyPath}");
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            inventory[assembly.GetName().Name!] = DescribeAssembly(assembly).ToArray();
        }

        string fullOutputPath = Path.GetFullPath(outputPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllText(
            fullOutputPath,
            JsonSerializer.Serialize(
                new { format = 1, assemblies = inventory },
                JsonOptions) + Environment.NewLine);
    }

    private static IEnumerable<string> DescribeAssembly(Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes()
            .Where(static type => type.IsPublic || type.IsNestedPublic)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            yield return $"type {FormatType(type)}";
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (ConstructorInfo constructor in type.GetConstructors(flags)
                .OrderBy(FormatConstructor, StringComparer.Ordinal))
            {
                yield return FormatConstructor(constructor);
            }

            foreach (MethodInfo method in type.GetMethods(flags)
                .Where(static method => !method.IsSpecialName)
                .OrderBy(FormatMethod, StringComparer.Ordinal))
            {
                yield return FormatMethod(method);
            }

            foreach (PropertyInfo property in type.GetProperties(flags)
                .OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                yield return $"property {FormatType(property.PropertyType)} {property.Name}"
                    + FormatParameters(property.GetIndexParameters());
            }

            foreach (FieldInfo field in type.GetFields(flags)
                .OrderBy(static field => field.Name, StringComparer.Ordinal))
            {
                yield return $"field {FormatType(field.FieldType)} {field.Name}";
            }

            foreach (EventInfo @event in type.GetEvents(flags)
                .OrderBy(static @event => @event.Name, StringComparer.Ordinal))
            {
                yield return $"event {FormatType(@event.EventHandlerType!)} {@event.Name}";
            }
        }
    }

    private static string FormatConstructor(ConstructorInfo constructor) =>
        $"ctor {constructor.DeclaringType!.FullName}{FormatParameters(constructor.GetParameters())}";

    private static string FormatMethod(MethodInfo method) =>
        $"method {FormatType(method.ReturnType)} {method.Name}"
        + (method.IsGenericMethodDefinition
            ? $"<{string.Join(',', method.GetGenericArguments().Select(static argument => argument.Name))}>"
            : string.Empty)
        + FormatParameters(method.GetParameters());

    private static string FormatParameters(IEnumerable<ParameterInfo> parameters) =>
        "(" + string.Join(',', parameters.Select(static parameter =>
            (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty)
            + FormatType(parameter.ParameterType)
            + " "
            + parameter.Name)) + ")";

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
        {
            return FormatType(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return $"{FormatType(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName!;
            name = name[..name.IndexOf('`')];
            return $"{name}<{string.Join(',', type.GetGenericArguments().Select(FormatType))}>";
        }

        return type.FullName ?? type.Name;
    }
}
