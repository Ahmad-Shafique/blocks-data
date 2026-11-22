using System.Reflection;
using FluentAssertions;

namespace XUnitTest.Architecture;

// I16-WP1 (storage neutrality). Outside the repository namespaces, no type may reference MongoDB.Driver or MongoDB.Bson.
// Today's violations are counted and the ceiling is ratcheted down only: a new type that references the driver fails
// this test. The same count is recorded in gates\baselines\blocks-data.json (key mongoTypeViolations).
public class MongoStorageDependencyTests
{
    // Ratchet. Lower it when a violating type is moved behind a repository; never raise it.
    // Measured 2026-11-22 on blocks-data trunk 2a3b397d, over the seven assemblies listed below: 54 types.
    private const int MaxMongoTypeViolations = 54;

    private static readonly string[] MongoAssemblies = { "MongoDB.Driver", "MongoDB.Bson" };

    // DataGateway.Driver is not scanned: it does not compile on upstream dev (ISchemaDefinitionService members missing
    // from DataGatewayDriverService). Add it back when upstream fixes it, and recount.
    private static readonly string[] Assemblies =
    {
        "DataGateway.DomainService", "DataGateway.GraphQL", "DataGateway.Rest",
        "Storage.DomainService", "Storage.Driver", "Api", "Worker",
    };

    [Fact]
    public void NoNewTypeOutsideRepositories_ReferencesTheMongoDriver()
    {
        var violations = FindViolations().ToList();

        violations.Count.Should().BeLessThanOrEqualTo(
            MaxMongoTypeViolations,
            $"{violations.Count} type(s) outside the repository namespaces reference MongoDB.Driver or MongoDB.Bson. " +
            "Move the data access into a class in a *.Repositories namespace. First few: " +
            string.Join(" | ", violations.Take(20)));
    }

    private static List<string> FindViolations()
    {
        var found = new List<string>();
        foreach (var name in Assemblies)
        {
            var assembly = Assembly.Load(name);
            foreach (var type in TypesOf(assembly))
            {
                if (IsRepositoryNamespace(type.Namespace)) continue;
                if (ReferencedTypes(type).Any(t => MongoAssemblies.Contains(t.Assembly.GetName().Name)))
                {
                    found.Add(type.FullName ?? type.Name);
                }
            }
        }

        return found.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Where(t => t is not null && !t.Name.StartsWith('<'));
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }

    private static bool IsRepositoryNamespace(string? ns) =>
        ns is not null && (ns.EndsWith(".Repositories", StringComparison.Ordinal) ||
                           ns.Split('.').Contains("Repositories") ||
                           ns.Split('.').Contains("Database"));

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var found = new List<Type>();
        if (type.BaseType is not null) found.Add(type.BaseType);
        found.AddRange(type.GetInterfaces());
        found.AddRange(type.GetFields(all).Select(f => f.FieldType));
        found.AddRange(type.GetProperties(all).Select(p => p.PropertyType));
        foreach (var method in type.GetMethods(all))
        {
            found.Add(method.ReturnType);
            found.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        foreach (var ctor in type.GetConstructors(all))
        {
            found.AddRange(ctor.GetParameters().Select(p => p.ParameterType));
        }

        return found.SelectMany(Expand);
    }

    // Arrays, references and generic arguments hide the real assembly, so each one is checked too.
    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Expand(type.GetElementType()!)) yield return inner;
            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var inner in Expand(argument)) yield return inner;
            }
        }
    }
}
