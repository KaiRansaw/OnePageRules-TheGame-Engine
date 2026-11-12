using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FDG.SaveLoad
{
    /// <summary>
    /// The single <see cref="ISerializationBinder"/> for every <b>untrusted</b> polymorphic
    /// (<c>TypeNameHandling.Auto</c>) deserialization: the network wire (#186) AND file loads —
    /// saves, the store rebuild that save/load and the full-state sync share, and terrain layouts
    /// (#265). A <c>$type</c> in any of those is attacker-controlled; handing it to Newtonsoft's
    /// <see cref="DefaultSerializationBinder"/> resolves arbitrary assembly-qualified names, which is
    /// the classic Newtonsoft deserialization-gadget remote-code-execution vector.
    ///
    /// <para><b>Writing</b> is identical to the former <c>StableTypeSerializationBinder</c> (#070):
    /// a registered type is written as its <see cref="SaveTypeRegistry"/> stable ID with no assembly
    /// token; anything else falls back to FullName + assembly. The on-disk / on-wire format is
    /// unchanged, so this is a drop-in for the old permissive binder — only what a RECEIVED name may
    /// resolve to is restricted.</para>
    ///
    /// <para><b>Reading</b> resolves, in order:</para>
    /// <list type="number">
    ///   <item>Assembly-less names that are registered <see cref="SaveTypeRegistry"/> IDs (what we
    ///   emit for registered types). Any OTHER assembly-less name is refused — the writer only ever
    ///   omits the assembly for registered types, so an unknown assembly-less name is forged.</item>
    ///   <item>Types that resolve into the engine assembly. Every legitimate persisted / wire payload
    ///   is an engine type (data holders); the dangerous gadgets all live in framework/library
    ///   assemblies, which this excludes wholesale. Wire builds are already pinned identical by the
    ///   #075 handshake; saves are this build's own output.</item>
    ///   <item>Benign framework shapes composed of allowed parts: primitives, and
    ///   arrays / <see cref="List{T}"/> / <see cref="Dictionary{TKey,TValue}"/> / <see cref="HashSet{T}"/>
    ///   (and their read-only interfaces) whose element types recursively pass — Newtonsoft records
    ///   concrete collection types under <c>TypeNameHandling.Auto</c> (see the <c>List&lt;IZone&gt;</c>
    ///   registry note).</item>
    /// </list>
    /// Everything else throws <see cref="JsonSerializationException"/>. On the wire the read loop's
    /// catch turns that into a disconnect; on a file load it surfaces as a readable load failure.
    ///
    /// <para><b>Standing invariant:</b> because the engine assembly is trusted wholesale, no engine
    /// type may have side-effectful deserialization (a ctor/setter that touches files, processes, or
    /// resolves types by name) — that would be a gadget inside the fence. Cheap to honor; worth a
    /// glance in review of any new serialized type.</para>
    /// </summary>
    public sealed class AllowlistSerializationBinder : ISerializationBinder
    {
        private static readonly DefaultSerializationBinder Fallback = new DefaultSerializationBinder();

        // The engine assembly — home of every legitimate polymorphic payload. Resolved from this
        // type, so it is the engine even when the binder is instantiated by the application layer.
        private static readonly System.Reflection.Assembly EngineAssembly = typeof(AllowlistSerializationBinder).Assembly;

        // Benign leaf types that may appear as generic arguments of allowed collections.
        private static readonly HashSet<Type> AllowedFrameworkLeafTypes = new HashSet<Type>
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(char), typeof(string), typeof(Guid), typeof(DateTime), typeof(TimeSpan),
            typeof(object), // List<object> of allowed items: each element's own $type is still checked.
        };

        private static readonly HashSet<Type> AllowedGenericCollectionDefinitions = new HashSet<Type>
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>),
            typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyDictionary<,>),
        };

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            if (SaveTypeRegistry.TryGetId(serializedType, out string id))
            {
                assemblyName = null;
                typeName = id;
                return;
            }

            Fallback.BindToName(serializedType, out assemblyName, out typeName);
        }

        public Type BindToType(string? assemblyName, string typeName)
        {
            if (assemblyName == null)
            {
                if (SaveTypeRegistry.TryGetType(typeName, out Type registered))
                {
                    return registered;
                }

                throw new JsonSerializationException(
                    $"Refusing $type '{typeName}': assembly-less names must be registered stable IDs (#186/#265).");
            }

            Type resolved = Resolve(assemblyName, typeName)
                ?? throw new JsonSerializationException($"Could not resolve type '{typeName}, {assemblyName}'.");

            if (!IsAllowed(resolved))
            {
                throw new JsonSerializationException(
                    $"Refusing $type '{typeName}, {assemblyName}': type is outside the deserialization allowlist (#186/#265).");
            }

            return resolved;
        }

        /// <summary>
        /// Resolves an assembly-qualified name the way the ENGINE sees it: every assembly the name (or one of its generic
        /// arguments) mentions is looked up in the engine's own load context first. Godot's exported game loads the
        /// engine into its own load context while the same files also sit on the app's default list; Newtonsoft's
        /// default binder resolved "FutureOfDarkGrimness" there, handing back a second copy of the engine whose types
        /// fail the <see cref="IsAllowed"/> identity check (every exported game ended at deployment with "type is outside
        /// the deserialization allowlist" - Kai, 2026-11-12). The editor and the Raylib build have one copy, so nothing
        /// changes for them.
        /// </summary>
        private static Type? Resolve(string assemblyName, string typeName)
        {
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(EngineAssembly) ?? AssemblyLoadContext.Default;
            Assembly? ResolveAssembly(AssemblyName wanted)
            {
                foreach (Assembly loaded in context.Assemblies)
                    if (string.Equals(loaded.GetName().Name, wanted.Name, StringComparison.OrdinalIgnoreCase)) return loaded;
                try { return context.LoadFromAssemblyName(wanted); }
                catch (Exception) { return null; }
            }
            try
            {
                return Type.GetType($"{typeName}, {assemblyName}", ResolveAssembly, null, throwOnError: false)
                    ?? Fallback.BindToType(assemblyName, typeName);
            }
            catch (Exception e) when (e is not JsonSerializationException)
            {
                return null;
            }
        }

        /// <summary>The allowlist rule, exposed so other trusted-boundary code (e.g. the save's
        /// type-map resolver) can apply the same gate.</summary>
        public static bool IsAllowed(Type type)
        {
            if (type.Assembly == EngineAssembly)
            {
                return true;
            }

            if (AllowedFrameworkLeafTypes.Contains(type))
            {
                return true;
            }

            if (type.IsArray)
            {
                Type? element = type.GetElementType();
                return element != null && IsAllowed(element);
            }

            if (type.IsConstructedGenericType &&
                AllowedGenericCollectionDefinitions.Contains(type.GetGenericTypeDefinition()))
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    if (!IsAllowed(argument))
                    {
                        return false;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
