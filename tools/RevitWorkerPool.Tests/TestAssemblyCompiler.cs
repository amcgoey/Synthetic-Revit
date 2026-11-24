using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RevitWorkerPool.Tests;

internal static class TestAssemblyCompiler
{
    public static string CompileToFile(
        string outputPath,
        string assemblyName,
        string sourceCode,
        bool targetNet48 = false,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        byte[] bytes = CompileToBytes(
            assemblyName,
            sourceCode,
            targetNet48,
            extraReferences);

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(outputPath, bytes);
        return outputPath;
    }

    public static byte[] CompileToBytes(
        string assemblyName,
        string sourceCode,
        bool targetNet48 = false,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceCode,
            new CSharpParseOptions(LanguageVersion.CSharp12));

        var references = new List<MetadataReference>();
        references.AddRange(targetNet48 ? GetNet48References() : GetNet8References());
        if (extraReferences is not null)
        {
            references.AddRange(extraReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        if (!emitResult.Success)
        {
            string errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException(
                $"Failed to compile synthetic test assembly '{assemblyName}':{Environment.NewLine}{errors}");
        }

        return peStream.ToArray();
    }

    private static IEnumerable<MetadataReference> GetNet8References()
    {
        string coreLibDir = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot locate .NET runtime directory.");

        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,
            typeof(Attribute).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(NUnit.Framework.TestAttribute).Assembly.Location,
            Path.Combine(coreLibDir, "System.Runtime.dll"),
            Path.Combine(coreLibDir, "System.Collections.dll"),
            Path.Combine(coreLibDir, "netstandard.dll"),
        };

        return candidatePaths
            .Where(File.Exists)
            .Select(p => MetadataReference.CreateFromFile(p));
    }

    private static IEnumerable<MetadataReference> GetNet48References()
    {
        string? refDir = FindNet48ReferenceDir();
        if (refDir is null)
        {
            throw new DirectoryNotFoundException("Could not locate .NET Framework 4.8 reference assemblies.");
        }

        var refs = new List<MetadataReference>();
        foreach (string dllName in new[] { "mscorlib.dll", "System.dll", "System.Core.dll" })
        {
            string path = Path.Combine(refDir, dllName);
            if (File.Exists(path))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        string? net45Nunit = FindNet45NUnitAssembly();
        refs.Add(MetadataReference.CreateFromFile(
            net45Nunit ?? typeof(NUnit.Framework.TestAttribute).Assembly.Location));

        return refs;
    }

    private static string? FindNet48ReferenceDir()
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            string sysRef = Path.Combine(
                programFilesX86,
                "Reference Assemblies",
                "Microsoft",
                "Framework",
                ".NETFramework",
                "v4.8");
            if (File.Exists(Path.Combine(sysRef, "mscorlib.dll")))
            {
                return sysRef;
            }
        }

        string nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string pkgDir = Path.Combine(nugetRoot, "microsoft.netframework.referenceassemblies.net48");
        if (Directory.Exists(pkgDir))
        {
            string? match = Directory.EnumerateFiles(pkgDir, "mscorlib.dll", SearchOption.AllDirectories)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is not null)
            {
                return Path.GetDirectoryName(match);
            }
        }

        string winFramework64 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET",
            "Framework64",
            "v4.0.30319");
        if (File.Exists(Path.Combine(winFramework64, "mscorlib.dll")))
        {
            return winFramework64;
        }

        return null;
    }

    private static string? FindNet45NUnitAssembly()
    {
        string nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string pkgDir = Path.Combine(nugetRoot, "nunit");
        if (!Directory.Exists(pkgDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(pkgDir, "nunit.framework.dll", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains($"{Path.DirectorySeparatorChar}net45{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }
}
