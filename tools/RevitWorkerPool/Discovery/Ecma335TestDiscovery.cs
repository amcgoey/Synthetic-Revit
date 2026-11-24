using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RevitWorkerPool.Discovery;

/// <summary>
/// Discovers NUnit test fixtures, methods, and categories directly from raw compiled ECMA-335
/// assembly bytes (<c>net48</c>, <c>net8.0</c>, <c>net10.0</c>, etc.) via
/// <see cref="PEReader"/> and <see cref="MetadataReader"/> with zero runtime assembly loading.
/// </summary>
public static class Ecma335TestDiscovery
{
    private static readonly IReadOnlyDictionary<string, string> TypedCategoryAttributes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UnitTest"] = "Unit",
            ["UnitTestAttribute"] = "Unit",
            ["ArchitectureTest"] = "Architecture",
            ["ArchitectureTestAttribute"] = "Architecture",
            ["RevitSmokeTest"] = "RevitSmoke",
            ["RevitSmokeTestAttribute"] = "RevitSmoke",
            ["RevitAdapterTest"] = "RevitAdapter",
            ["RevitAdapterTestAttribute"] = "RevitAdapter",
            ["RevitIsolatedDocTest"] = "RevitIsolatedDoc",
            ["RevitIsolatedDocTestAttribute"] = "RevitIsolatedDoc",
            ["QuarantineTest"] = "Quarantine",
            ["QuarantineTestAttribute"] = "Quarantine",
        };

    private static readonly HashSet<string> NUnitTestMethodAttributeNames = new(StringComparer.Ordinal)
    {
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "NUnit.Framework.TheoryAttribute",
    };

    private static readonly HashSet<string> NUnitTestFixtureAttributeNames = new(StringComparer.Ordinal)
    {
        "NUnit.Framework.TestFixtureAttribute",
        "NUnit.Framework.TestFixtureSourceAttribute",
    };

    private static readonly HashSet<string> NonFixtureBaseTypeNames = new(StringComparer.Ordinal)
    {
        "System.ValueType",
        "System.Enum",
        "System.Delegate",
        "System.MulticastDelegate",
        "System.Attribute",
    };

    /// <summary>
    /// Formats a logical test method full name (<c>Namespace.Type.Method</c>) into the
    /// <c>ricaun.NUnit.Services.WildcardPattern</c> filter required by <c>TestRequest.TestFilters</c>.
    /// </summary>
    /// <remarks>
    /// <c>ricaun.NUnit.Services.TestAttributeService.GetTestFullName</c> constructs full names as
    /// <c>Namespace.Type.Method.TestName</c> (for example <c>Ns.Fix.TestA.TestA</c>,
    /// <c>Ns.Fix.CaseB.CaseB(1)</c>, or <c>Ns.Fix.CaseC.CustomName</c>) and evaluates filters
    /// with anchored full-string regex matching. Appending <c>.*</c> after the exact method name
    /// matches both parameterless <c>[Test]</c> and parameterized <c>[TestCase]</c>/<c>[TestCaseSource]</c>
    /// cases for that method while preventing prefix collisions with similarly named methods.
    /// </remarks>
    public static string FormatRicaunMethodFilter(string methodFullName)
    {
        if (string.IsNullOrWhiteSpace(methodFullName))
        {
            throw new ArgumentException("Method full name must not be empty.", nameof(methodFullName));
        }

        return $"{methodFullName}.*";
    }

    /// <summary>
    /// Discovers all concrete non-abstract test fixtures and their test methods in <paramref name="assemblyPath"/>
    /// and filters them by optional <paramref name="filters"/> and <paramref name="category"/>.
    /// </summary>
    public static TestDiscoveryResult DiscoverAndFilter(
        string assemblyPath,
        IReadOnlyList<string>? filters = null,
        string? category = null)
    {
        (string fullPath, string? targetFramework, IReadOnlyList<DiscoveredFixtureMetadata> allFixtures) =
            ReadAssemblyFixtures(assemblyPath);

        string? normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Func<DiscoveredTestMethod, bool> filterPredicate = TestFilterExpression.Compile(filters);

        var matchedFixtures = new List<DiscoveredFixtureTests>();
        var flattenedTestFullNames = new List<string>();
        var ricaunFilters = new List<string>();
        var seenRicaunFilters = new HashSet<string>(StringComparer.Ordinal);

        foreach (DiscoveredFixtureMetadata fixture in allFixtures)
        {
            var matchedMethodNames = new List<string>();

            foreach (DiscoveredTestMethod method in fixture.Methods)
            {
                if (normalizedCategory is not null &&
                    !method.Categories.Contains(normalizedCategory, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!filterPredicate(method))
                {
                    continue;
                }

                matchedMethodNames.Add(method.FullName);
                flattenedTestFullNames.Add(method.FullName);

                string ricaunPattern = FormatRicaunMethodFilter(method.FullName);
                if (seenRicaunFilters.Add(ricaunPattern))
                {
                    ricaunFilters.Add(ricaunPattern);
                }
            }

            if (matchedMethodNames.Count > 0)
            {
                matchedFixtures.Add(new DiscoveredFixtureTests(
                    fixture.FixtureFullName,
                    matchedMethodNames,
                    fixture.Methods.Count));
            }
        }

        return new TestDiscoveryResult(
            fullPath,
            targetFramework,
            matchedFixtures,
            flattenedTestFullNames,
            ricaunFilters);
    }

    /// <summary>
    /// Reads all concrete non-abstract test fixtures (including empty <c>[TestFixture]</c> classes
    /// and classes inheriting test methods from base classes in the same assembly).
    /// </summary>
    public static IReadOnlyList<DiscoveredFixtureMetadata> DiscoverAllFixtures(string assemblyPath)
    {
        (_, _, IReadOnlyList<DiscoveredFixtureMetadata> fixtures) = ReadAssemblyFixtures(assemblyPath);
        return fixtures;
    }

    /// <summary>
    /// Decodes all custom attributes on an assembly, type, or method strictly via metadata
    /// without loading referenced assemblies. Exposed for direct verification of metadata decoding.
    /// </summary>
    internal static IReadOnlyList<DecodedCustomAttribute> DecodeCustomAttributes(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
    {
        var decoder = new MetadataCustomAttributeDecoder(reader);
        var results = new List<DecodedCustomAttribute>();
        foreach (CustomAttributeHandle handle in attributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(handle);
            if (decoder.TryDecodeAttribute(attr, out DecodedCustomAttribute? decoded) && decoded is not null)
            {
                results.Add(decoded);
            }
        }

        return results;
    }

    private static (string FullPath, string? TargetFramework, IReadOnlyList<DiscoveredFixtureMetadata> Fixtures)
        ReadAssemblyFixtures(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("Assembly path must not be empty.", nameof(assemblyPath));
        }

        string fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Test assembly not found: '{fullPath}'.", fullPath);
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException($"Assembly '{fullPath}' does not contain ECMA-335 CLI metadata.");
        }

        MetadataReader reader = peReader.GetMetadataReader();
        var decoder = new MetadataCustomAttributeDecoder(reader);
        string? targetFramework = ReadTargetFramework(reader, decoder);

        Dictionary<TypeDefinitionHandle, RawTypeMetadata> typesByHandle = ReadAllTypes(reader, decoder);
        Dictionary<string, TypeDefinitionHandle> handlesByFullName = BuildFullNameIndex(typesByHandle);

        var fixtures = new List<DiscoveredFixtureMetadata>();
        foreach (RawTypeMetadata type in typesByHandle.Values)
        {
            if (!IsConcreteClassCandidate(type, typesByHandle, handlesByFullName))
            {
                continue;
            }

            DiscoveredFixtureMetadata? fixture = BuildFixtureMetadata(type, typesByHandle, handlesByFullName);
            if (fixture is not null)
            {
                fixtures.Add(fixture);
            }
        }

        fixtures.Sort((a, b) => StringComparer.Ordinal.Compare(a.FixtureFullName, b.FixtureFullName));
        return (fullPath, targetFramework, fixtures);
    }

    private static string? ReadTargetFramework(MetadataReader reader, MetadataCustomAttributeDecoder decoder)
    {
        if (!reader.IsAssembly)
        {
            return null;
        }

        foreach (CustomAttributeHandle handle in reader.GetCustomAttributes(EntityHandle.AssemblyDefinition))
        {
            CustomAttribute attr = reader.GetCustomAttribute(handle);
            string? attrName = decoder.TryGetAttributeTypeFullName(attr, out _);
            if (!string.Equals(attrName, "System.Runtime.Versioning.TargetFrameworkAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (decoder.TryDecodeAttribute(attr, out DecodedCustomAttribute? decoded) &&
                decoded is not null &&
                decoded.FixedArguments.Count > 0 &&
                decoded.FixedArguments[0] is string tfm &&
                !string.IsNullOrWhiteSpace(tfm))
            {
                return tfm;
            }
        }

        return null;
    }

    private static Dictionary<TypeDefinitionHandle, RawTypeMetadata> ReadAllTypes(
        MetadataReader reader,
        MetadataCustomAttributeDecoder decoder)
    {
        var result = new Dictionary<TypeDefinitionHandle, RawTypeMetadata>();

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition td = reader.GetTypeDefinition(typeHandle);
            string shortName = reader.GetString(td.Name);
            if (string.Equals(shortName, "<Module>", StringComparison.Ordinal))
            {
                continue;
            }

            string fullName = MetadataCustomAttributeDecoder.GetTypeDefinitionFullName(reader, typeHandle);
            bool isCompilerGenerated = shortName.StartsWith("<", StringComparison.Ordinal);

            bool hasTestFixture = false;
            var ownCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CustomAttributeHandle attrHandle in td.GetCustomAttributes())
            {
                CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
                string? attrTypeName = decoder.TryGetAttributeTypeFullName(attr, out _);
                if (string.IsNullOrEmpty(attrTypeName))
                {
                    continue;
                }

                if (string.Equals(
                    attrTypeName,
                    "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
                    StringComparison.Ordinal))
                {
                    isCompilerGenerated = true;
                    continue;
                }

                if (NUnitTestFixtureAttributeNames.Contains(attrTypeName))
                {
                    hasTestFixture = true;
                    TryCollectNamedCategoryProperty(attr, decoder, ownCategories);
                    continue;
                }

                TryCollectCategoryFromAttribute(attr, attrTypeName, decoder, ownCategories);
            }

            string? baseTypeFullName = decoder.ResolveEntityFullName(td.BaseType, out TypeDefinitionHandle? baseDefHandle);
            bool isClass = (td.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Class;
            bool isAbstract = (td.Attributes & TypeAttributes.Abstract) != 0;
            bool hasGenericParams = td.GetGenericParameters().Count > 0;

            List<RawMethodMetadata> methods = isClass && !isCompilerGenerated
                ? ReadPublicMethods(reader, td, decoder)
                : new List<RawMethodMetadata>();

            result[typeHandle] = new RawTypeMetadata(
                typeHandle,
                fullName,
                isClass,
                isAbstract,
                hasGenericParams,
                isCompilerGenerated,
                hasTestFixture,
                baseTypeFullName,
                baseDefHandle,
                ownCategories,
                methods);
        }

        return result;
    }

    private static List<RawMethodMetadata> ReadPublicMethods(
        MetadataReader reader,
        TypeDefinition td,
        MetadataCustomAttributeDecoder decoder)
    {
        var methods = new List<RawMethodMetadata>();

        foreach (MethodDefinitionHandle methodHandle in td.GetMethods())
        {
            MethodDefinition md = reader.GetMethodDefinition(methodHandle);
            if ((md.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            {
                continue;
            }

            if ((md.Attributes & MethodAttributes.Abstract) != 0 ||
                (md.Attributes & MethodAttributes.SpecialName) != 0)
            {
                continue;
            }

            string methodName = reader.GetString(md.Name);
            if (string.Equals(methodName, ".ctor", StringComparison.Ordinal) ||
                string.Equals(methodName, ".cctor", StringComparison.Ordinal))
            {
                continue;
            }

            int paramCount = ReadParameterCount(reader, md);
            bool isVirtual = (md.Attributes & MethodAttributes.Virtual) != 0;
            bool isOverride = isVirtual && (md.Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.ReuseSlot;

            bool hasTestAttr = false;
            bool hasParameterizedTestAttr = false;
            var ownCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CustomAttributeHandle attrHandle in md.GetCustomAttributes())
            {
                CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
                string? attrTypeName = decoder.TryGetAttributeTypeFullName(attr, out _);
                if (string.IsNullOrEmpty(attrTypeName))
                {
                    continue;
                }

                if (NUnitTestMethodAttributeNames.Contains(attrTypeName))
                {
                    hasTestAttr = true;
                    if (!string.Equals(attrTypeName, "NUnit.Framework.TestAttribute", StringComparison.Ordinal))
                    {
                        hasParameterizedTestAttr = true;
                    }
                    TryCollectNamedCategoryProperty(attr, decoder, ownCategories);
                    continue;
                }

                TryCollectCategoryFromAttribute(attr, attrTypeName, decoder, ownCategories);
            }

            methods.Add(new RawMethodMetadata(
                methodName,
                paramCount,
                isVirtual,
                isOverride,
                hasTestAttr,
                hasParameterizedTestAttr || paramCount > 0,
                ownCategories));
        }

        return methods;
    }

    private static int ReadParameterCount(MetadataReader reader, MethodDefinition md)
    {
        try
        {
            BlobReader sigReader = reader.GetBlobReader(md.Signature);
            SignatureHeader header = sigReader.ReadSignatureHeader();
            if (header.IsGeneric)
            {
                sigReader.ReadCompressedInteger();
            }

            return sigReader.ReadCompressedInteger();
        }
        catch (BadImageFormatException)
        {
            return md.GetParameters().Count;
        }
    }

    private static void TryCollectCategoryFromAttribute(
        CustomAttribute attr,
        string attrTypeName,
        MetadataCustomAttributeDecoder decoder,
        HashSet<string> categories)
    {
        string shortName = ExtractShortTypeName(attrTypeName);
        if (TypedCategoryAttributes.TryGetValue(shortName, out string? typedCategory))
        {
            categories.Add(typedCategory);
            return;
        }

        if (string.Equals(attrTypeName, "NUnit.Framework.CategoryAttribute", StringComparison.Ordinal) ||
            string.Equals(shortName, "CategoryAttribute", StringComparison.Ordinal) ||
            string.Equals(shortName, "Category", StringComparison.Ordinal))
        {
            if (!decoder.TryDecodeAttribute(attr, out DecodedCustomAttribute? decoded) || decoded is null)
            {
                return;
            }

            if (decoded.FixedArguments.Count > 0 &&
                decoded.FixedArguments[0] is string catCtorArg &&
                !string.IsNullOrWhiteSpace(catCtorArg))
            {
                categories.Add(catCtorArg.Trim());
            }
            else if (decoded.NamedArguments.TryGetValue("Name", out object? namedCat) &&
                     namedCat is string catPropArg &&
                     !string.IsNullOrWhiteSpace(catPropArg))
            {
                categories.Add(catPropArg.Trim());
            }
        }
    }

    private static void TryCollectNamedCategoryProperty(
        CustomAttribute attr,
        MetadataCustomAttributeDecoder decoder,
        HashSet<string> categories)
    {
        if (!decoder.TryDecodeAttribute(attr, out DecodedCustomAttribute? decoded) || decoded is null)
        {
            return;
        }

        if (decoded.NamedArguments.TryGetValue("Category", out object? catObj) &&
            catObj is string catStr &&
            !string.IsNullOrWhiteSpace(catStr))
        {
            foreach (string part in catStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    categories.Add(part);
                }
            }
        }
    }

    private static string ExtractShortTypeName(string fullTypeName)
    {
        int plusIdx = fullTypeName.LastIndexOf('+');
        if (plusIdx >= 0 && plusIdx + 1 < fullTypeName.Length)
        {
            return fullTypeName[(plusIdx + 1)..];
        }

        int dotIdx = fullTypeName.LastIndexOf('.');
        return dotIdx >= 0 && dotIdx + 1 < fullTypeName.Length
            ? fullTypeName[(dotIdx + 1)..]
            : fullTypeName;
    }

    private static Dictionary<string, TypeDefinitionHandle> BuildFullNameIndex(
        Dictionary<TypeDefinitionHandle, RawTypeMetadata> typesByHandle)
    {
        var index = new Dictionary<string, TypeDefinitionHandle>(typesByHandle.Count, StringComparer.Ordinal);
        foreach (RawTypeMetadata type in typesByHandle.Values)
        {
            index.TryAdd(type.FullName, type.Handle);
        }

        return index;
    }

    private static bool IsConcreteClassCandidate(
        RawTypeMetadata type,
        Dictionary<TypeDefinitionHandle, RawTypeMetadata> typesByHandle,
        Dictionary<string, TypeDefinitionHandle> handlesByFullName)
    {
        if (!type.IsClass || type.IsAbstract || type.HasGenericParameters || type.IsCompilerGenerated)
        {
            return false;
        }

        // Walk ancestors to exclude structs, enums, delegates, and custom attribute classes.
        var visited = new HashSet<TypeDefinitionHandle>();
        RawTypeMetadata? current = type;
        while (current is not null)
        {
            if (!string.IsNullOrEmpty(current.BaseTypeFullName) &&
                NonFixtureBaseTypeNames.Contains(current.BaseTypeFullName))
            {
                return false;
            }

            current = ResolveBaseType(current, typesByHandle, handlesByFullName, visited);
        }

        return true;
    }

    private static DiscoveredFixtureMetadata? BuildFixtureMetadata(
        RawTypeMetadata concreteType,
        Dictionary<TypeDefinitionHandle, RawTypeMetadata> typesByHandle,
        Dictionary<string, TypeDefinitionHandle> handlesByFullName)
    {
        List<RawTypeMetadata> hierarchyRootToLeaf = CollectHierarchyRootToLeaf(
            concreteType,
            typesByHandle,
            handlesByFullName);

        var effectiveFixtureCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool anyTestFixtureAttr = false;
        var activeMethods = new List<EffectiveMethodState>();

        foreach (RawTypeMetadata level in hierarchyRootToLeaf)
        {
            effectiveFixtureCategories.UnionWith(level.OwnCategories);
            if (level.HasTestFixtureAttribute)
            {
                anyTestFixtureAttr = true;
            }

            foreach (RawMethodMetadata method in level.DeclaredPublicMethods)
            {
                if (method.IsOverride)
                {
                    EffectiveMethodState? overridden = FindLastMatchingVirtualMethod(
                        activeMethods,
                        method.MethodName,
                        method.ParamCount);
                    if (overridden is not null)
                    {
                        overridden.HasTestAttr |= method.HasTestAttr;
                        overridden.IsParameterized |= method.IsParameterized;
                        overridden.OwnCategories.UnionWith(method.OwnCategories);
                        continue;
                    }
                }

                activeMethods.Add(new EffectiveMethodState(
                    method.MethodName,
                    method.ParamCount,
                    method.IsVirtual,
                    method.HasTestAttr,
                    method.IsParameterized,
                    new HashSet<string>(method.OwnCategories, StringComparer.OrdinalIgnoreCase)));
            }
        }

        var testMethods = new List<DiscoveredTestMethod>();
        foreach (EffectiveMethodState state in activeMethods)
        {
            if (!state.HasTestAttr)
            {
                continue;
            }

            var methodCategories = new HashSet<string>(effectiveFixtureCategories, StringComparer.OrdinalIgnoreCase);
            methodCategories.UnionWith(state.OwnCategories);

            string methodFullName = $"{concreteType.FullName}.{state.MethodName}";
            testMethods.Add(new DiscoveredTestMethod(
                concreteType.FullName,
                state.MethodName,
                methodFullName,
                methodCategories.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray(),
                state.IsParameterized));
        }

        if (!anyTestFixtureAttr && testMethods.Count == 0)
        {
            return null;
        }

        testMethods.Sort((a, b) => StringComparer.Ordinal.Compare(a.MethodName, b.MethodName));

        return new DiscoveredFixtureMetadata(
            concreteType.FullName,
            effectiveFixtureCategories.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray(),
            testMethods);
    }

    private static List<RawTypeMetadata> CollectHierarchyRootToLeaf(
        RawTypeMetadata leaf,
        Dictionary<TypeDefinitionHandle, RawTypeMetadata> typesByHandle,
        Dictionary<string, TypeDefinitionHandle> handlesByFullName)
    {
        var chainLeafToRoot = new List<RawTypeMetadata>();
        var visited = new HashSet<TypeDefinitionHandle>();
        RawTypeMetadata? current = leaf;

        while (current is not null)
        {
            chainLeafToRoot.Add(current);
            current = ResolveBaseType(current, typesByHandle, handlesByFullName, visited);
        }

        chainLeafToRoot.Reverse();
        return chainLeafToRoot;
    }

    private static RawTypeMetadata? ResolveBaseType(
        RawTypeMetadata type,
        Dictionary<TypeDefinitionHandle, RawTypeMetadata> typesByHandle,
        Dictionary<string, TypeDefinitionHandle> handlesByFullName,
        HashSet<TypeDefinitionHandle> visited)
    {
        if (type.BaseTypeDefHandle is { } defHandle &&
            visited.Add(defHandle) &&
            typesByHandle.TryGetValue(defHandle, out RawTypeMetadata? directBase))
        {
            return directBase;
        }

        if (!string.IsNullOrEmpty(type.BaseTypeFullName))
        {
            string lookupName = StripGenericArgs(type.BaseTypeFullName);
            if (handlesByFullName.TryGetValue(lookupName, out TypeDefinitionHandle namedHandle) &&
                visited.Add(namedHandle) &&
                typesByHandle.TryGetValue(namedHandle, out RawTypeMetadata? namedBase))
            {
                return namedBase;
            }
        }

        return null;
    }

    private static string StripGenericArgs(string typeName)
    {
        int angleIdx = typeName.IndexOf('<');
        return angleIdx >= 0 ? typeName[..angleIdx] : typeName;
    }

    private static EffectiveMethodState? FindLastMatchingVirtualMethod(
        List<EffectiveMethodState> methods,
        string methodName,
        int paramCount)
    {
        for (int i = methods.Count - 1; i >= 0; i--)
        {
            EffectiveMethodState candidate = methods[i];
            if (candidate.IsVirtual &&
                candidate.ParamCount == paramCount &&
                string.Equals(candidate.MethodName, methodName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record RawTypeMetadata(
        TypeDefinitionHandle Handle,
        string FullName,
        bool IsClass,
        bool IsAbstract,
        bool HasGenericParameters,
        bool IsCompilerGenerated,
        bool HasTestFixtureAttribute,
        string? BaseTypeFullName,
        TypeDefinitionHandle? BaseTypeDefHandle,
        HashSet<string> OwnCategories,
        List<RawMethodMetadata> DeclaredPublicMethods);

    private sealed record RawMethodMetadata(
        string MethodName,
        int ParamCount,
        bool IsVirtual,
        bool IsOverride,
        bool HasTestAttr,
        bool IsParameterized,
        HashSet<string> OwnCategories);

    private sealed class EffectiveMethodState
    {
        public EffectiveMethodState(
            string methodName,
            int paramCount,
            bool isVirtual,
            bool hasTestAttr,
            bool isParameterized,
            HashSet<string> ownCategories)
        {
            MethodName = methodName;
            ParamCount = paramCount;
            IsVirtual = isVirtual;
            HasTestAttr = hasTestAttr;
            IsParameterized = isParameterized;
            OwnCategories = ownCategories;
        }

        public string MethodName { get; }
        public int ParamCount { get; }
        public bool IsVirtual { get; }
        public bool HasTestAttr { get; set; }
        public bool IsParameterized { get; set; }
        public HashSet<string> OwnCategories { get; }
    }
}
