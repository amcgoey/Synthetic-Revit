using System;
using System.Collections.Generic;

namespace RevitWorkerPool.Discovery;

/// <summary>
/// Represents a concrete test method discovered from raw ECMA-335 metadata.
/// </summary>
public sealed record DiscoveredTestMethod(
    string FixtureFullName,
    string MethodName,
    string FullName,
    IReadOnlyCollection<string> Categories,
    bool IsParameterized);

/// <summary>
/// Full metadata for a concrete non-abstract test fixture discovered from an assembly.
/// </summary>
public sealed record DiscoveredFixtureMetadata(
    string FixtureFullName,
    IReadOnlyCollection<string> Categories,
    IReadOnlyList<DiscoveredTestMethod> Methods);

/// <summary>
/// Matched test methods grouped by concrete <c>[TestFixture]</c> class alongside
/// total test method count in that fixture (used by downstream LPT sharding).
/// </summary>
public sealed record DiscoveredFixtureTests(
    string FixtureFullName,
    IReadOnlyList<string> TestFullNames,
    int TotalMethodsInFixture);

/// <summary>
/// Result of ECMA-335 metadata discovery and filter/category expansion against a compiled test assembly.
/// </summary>
public sealed record TestDiscoveryResult(
    string AssemblyPath,
    string? TargetFramework,
    IReadOnlyList<DiscoveredFixtureTests> Fixtures,
    IReadOnlyList<string> TestFullNames,
    IReadOnlyList<string> RicaunTestFilters)
{
    /// <summary>
    /// Gets whether one or more tests matched the discovery criteria.
    /// </summary>
    public bool HasMatches => TestFullNames.Count > 0;
}

/// <summary>
/// Decoded metadata-only custom attribute constructor and named property/field values.
/// </summary>
public sealed record DecodedCustomAttribute(
    string AttributeFullName,
    IReadOnlyList<object?> FixedArguments,
    IReadOnlyDictionary<string, object?> NamedArguments);
