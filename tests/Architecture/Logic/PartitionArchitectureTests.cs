using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using Synthetic.Platform.Host;
using SyntheticTests.Helpers;

namespace SyntheticTests.Architecture
{
    [TestFixture]
    [ArchitectureTest]
    public class PartitionArchitectureTests
    {
        private static readonly string[] PartitionedSourceRoots = { "Features", "RevitDOM", "Platform" };
        private static readonly string[] PartitionedTestRoots = { "Features", "RevitDOM", "Platform", "Architecture" };

        [Test]
        public void PartitionedRoots_FollowCoreAndAddin_AndLogicAndRevit_Placement()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string srcRoot = Path.Combine(projectRoot, "src");
            string testsRoot = Path.Combine(projectRoot, "tests");

            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "Host")), "Expected src/Platform/Host partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "Host")), "Expected tests/Platform/Host partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Architecture")), "Expected tests/Architecture partition root.");

            var sourceViolations = new List<string>();
            foreach (string rootName in PartitionedSourceRoots)
            {
                string rootDir = Path.Combine(srcRoot, rootName);
                if (!Directory.Exists(rootDir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(rootDir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string rel = GetRelativeNormalizedPath(srcRoot, file);
                    string[] segments = rel.Split('/');
                    bool inCore = segments.Contains("Core", StringComparer.Ordinal);
                    bool inAddin = segments.Contains("Addin", StringComparer.Ordinal);

                    if (!inCore && !inAddin)
                    {
                        sourceViolations.Add($"src/{rel} must live under a Core/ or Addin/ partition folder.");
                    }
                    else if (inCore && string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        sourceViolations.Add($"src/{rel} is a XAML file inside Core/ (XAML must live in Addin/).");
                    }
                }
            }

            var testViolations = new List<string>();
            foreach (string rootName in PartitionedTestRoots)
            {
                string rootDir = Path.Combine(testsRoot, rootName);
                if (!Directory.Exists(rootDir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(rootDir, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = GetRelativeNormalizedPath(testsRoot, file);
                    string[] segments = rel.Split('/');
                    if (!segments.Contains("Logic", StringComparer.Ordinal) &&
                        !segments.Contains("Revit", StringComparer.Ordinal))
                    {
                        testViolations.Add($"tests/{rel} must live under a Logic/ or Revit/ partition folder.");
                    }
                }
            }

            Assert.IsEmpty(sourceViolations, string.Join(Environment.NewLine, sourceViolations));
            Assert.IsEmpty(testViolations, string.Join(Environment.NewLine, testViolations));
        }

        [Test]
        public void SyntheticCore_ProjectAndSources_ContainZeroRevitRefsZeroXamlZeroRevitIfdefs()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string srcRoot = Path.Combine(projectRoot, "src");
            string coreCsprojPath = Path.Combine(srcRoot, "Synthetic.Core", "Synthetic.Core.csproj");

            Assert.IsTrue(File.Exists(coreCsprojPath), $"Expected Synthetic.Core.csproj at {coreCsprojPath}");
            XDocument doc = XDocument.Load(coreCsprojPath);
            string rawCsproj = File.ReadAllText(coreCsprojPath);

            StringAssert.DoesNotContain("Autodesk.Revit", rawCsproj, "Synthetic.Core.csproj must not reference Autodesk.Revit.*.");
            StringAssert.DoesNotContain("Revit_All_Main_Versions_API", rawCsproj, "Synthetic.Core.csproj must not reference Revit API NuGet packages.");
            StringAssert.DoesNotContain("REVIT202", rawCsproj, "Synthetic.Core.csproj must not define REVIT202* constants.");

            bool hasCoreFlag = doc.Descendants("IsCoreProject")
                .Any(el => string.Equals(el.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(hasCoreFlag, "Synthetic.Core.csproj must declare <IsCoreProject>true</IsCoreProject>.");

            var csViolations = new List<string>();
            var xamlViolations = new List<string>();

            foreach (string rootName in PartitionedSourceRoots)
            {
                string rootDir = Path.Combine(srcRoot, rootName);
                if (!Directory.Exists(rootDir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(rootDir, "*.*", SearchOption.AllDirectories))
                {
                    string rel = GetRelativeNormalizedPath(srcRoot, file);
                    if (!rel.Split('/').Contains("Core", StringComparer.Ordinal))
                    {
                        continue;
                    }

                    if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        xamlViolations.Add($"src/{rel}");
                        continue;
                    }

                    if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string text = File.ReadAllText(file);
                    if (Regex.IsMatch(text, @"\bAutodesk\.Revit\b"))
                    {
                        csViolations.Add($"src/{rel} references Autodesk.Revit");
                    }
                    if (Regex.IsMatch(text, @"#\s*(if|elif)\b[^\r\n]*\bREVIT202\d"))
                    {
                        csViolations.Add($"src/{rel} contains #if/#elif REVIT202* directive");
                    }
                }
            }

            Assert.IsEmpty(xamlViolations, "Core/ directories must contain zero .xaml files: " + string.Join(", ", xamlViolations));
            Assert.IsEmpty(csViolations, string.Join(Environment.NewLine, csViolations));
        }

        [Test]
        public void SyntheticCore_CompiledAssembly_HasZeroRevitReferencesAndZeroBamlResources()
        {
            Assembly coreAssembly = typeof(RibbonItemFilter).Assembly;
            Assert.AreEqual("Synthetic.Core", coreAssembly.GetName().Name);

            var revitAssemblyRefs = coreAssembly.GetReferencedAssemblies()
                .Where(a => !string.IsNullOrEmpty(a.Name) &&
                            (a.Name!.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                             a.Name.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase)))
                .Select(a => a.FullName)
                .ToList();

            Assert.IsEmpty(revitAssemblyRefs, "Synthetic.Core.dll must not reference Revit API assemblies.");

            var xamlResources = coreAssembly.GetManifestResourceNames()
                .Where(n => n.EndsWith(".baml", StringComparison.OrdinalIgnoreCase) ||
                            n.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                            n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.IsEmpty(xamlResources, "Synthetic.Core.dll must not embed WPF XAML/BAML resources.");
        }

        private static string GetRelativeNormalizedPath(string rootDir, string fullPath)
        {
            string normalizedRoot = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedFull = Path.GetFullPath(fullPath);
            if (normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFull.Substring(normalizedRoot.Length).Replace('\\', '/');
            }
            return normalizedFull.Replace('\\', '/');
        }
    }
}
