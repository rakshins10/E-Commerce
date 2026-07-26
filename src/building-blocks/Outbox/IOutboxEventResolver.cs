using System.Reflection;

using ECommerce.EventBus;

namespace ECommerce.Outbox;

/// <summary>
/// Turns the event name stored in the outbox back into a CLR type.
/// </summary>
/// <remarks>
/// <para>
/// The publisher stores a name, not an assembly-qualified type name, and this is why. An
/// assembly-qualified name in the database ties every stored row to the exact assembly, namespace and
/// version that wrote it — rename the namespace and last night's unpublished messages become
/// undeserialisable. Worse, deserialising a type named by untrusted input is a well-known remote code
/// execution vector.
/// </para>
/// <para>
/// A registry of known names is both safer and more stable: only types this service has explicitly
/// registered can ever be constructed, and the wire name is decoupled from where the class happens to
/// live in the codebase.
/// </para>
/// </remarks>
public interface IOutboxEventResolver
{
    /// <summary>The type for an event name, or <c>null</c> if this build does not know it.</summary>
    Type? Resolve(string eventName);
}

/// <summary>
/// Resolves event names by scanning assemblies for <see cref="IntegrationEvent"/> subclasses.
/// </summary>
/// <remarks>
/// Scanning rather than a hand-maintained dictionary, because a hand-maintained dictionary is a list
/// somebody forgets to update, and the failure only shows up at runtime in the publisher. Registration
/// is still explicit at the assembly level, so nothing outside the assemblies you name can be resolved.
/// </remarks>
public sealed class AssemblyScanningOutboxEventResolver : IOutboxEventResolver
{
    private readonly Dictionary<string, Type> _types;

    public AssemblyScanningOutboxEventResolver(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        _types = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsClass: true }
                           && typeof(IntegrationEvent).IsAssignableFrom(type))
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
    }

    public Type? Resolve(string eventName) =>
        _types.TryGetValue(eventName, out Type? type) ? type : null;
}
