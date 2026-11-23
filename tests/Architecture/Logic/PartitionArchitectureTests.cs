using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using Synthetic.Platform.Host;
using Synthetic.Platform.IO;
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
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "IO", "Core")), "Expected src/Platform/IO/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "UI")), "Expected src/Platform/UI partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "Revit", "Core")), "Expected src/Platform/Revit/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "Revit", "Addin")), "Expected src/Platform/Revit/Addin partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "Settings", "Core")), "Expected src/Platform/Settings/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Platform", "Settings", "Addin")), "Expected src/Platform/Settings/Addin partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(srcRoot, "Features", "SettingsDashboard", "Addin")), "Expected src/Features/SettingsDashboard/Addin partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "Host")), "Expected tests/Platform/Host partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "IO", "Logic")), "Expected tests/Platform/IO/Logic partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "UI")), "Expected tests/Platform/UI partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "Revit", "Logic")), "Expected tests/Platform/Revit/Logic partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "Revit", "Revit")), "Expected tests/Platform/Revit/Revit partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Platform", "Settings", "Logic")), "Expected tests/Platform/Settings/Logic partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(testsRoot, "Features", "SettingsDashboard", "Revit")), "Expected tests/Features/SettingsDashboard/Revit partition root.");
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

        [Test]
        public void PlatformRevitAddin_HasZeroFeatureRevitDomOrAppControlledDependencies()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string addinDir = Path.Combine(projectRoot, "src", "Platform", "Revit", "Addin");
            Assert.IsTrue(Directory.Exists(addinDir), $"Expected Platform/Revit/Addin directory at {addinDir}");

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(addinDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = GetRelativeNormalizedPath(projectRoot, file);
                string text = File.ReadAllText(file);

                if (Regex.IsMatch(text, @"\bSynthetic\.(Features|Modules)\b"))
                {
                    violations.Add($"{rel} imports Synthetic.Features or Synthetic.Modules.");
                }
                if (Regex.IsMatch(text, @"\bSynthetic\.RevitDOM\b"))
                {
                    violations.Add($"{rel} imports Synthetic.RevitDOM.");
                }
                if (Regex.IsMatch(text, @"\bApp\.AppControlled\b"))
                {
                    violations.Add($"{rel} references App.AppControlled.");
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Repository_ContainsZeroRetiredFailureTestHelpersOrLegacyApis()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string[] roots = { Path.Combine(projectRoot, "src"), Path.Combine(projectRoot, "tests") };
            var violations = new List<string>();

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string rel = GetRelativeNormalizedPath(projectRoot, file);
                    string text = File.ReadAllText(file);
                    string forbiddenMock = "Mock" + "FailureFactory";
                    string forbiddenDeleteWarnings = "Delete" + "Warnings(";
                    if (text.Contains(forbiddenMock))
                    {
                        violations.Add($"{rel} references {forbiddenMock}.");
                    }
                    if (text.Contains(forbiddenDeleteWarnings))
                    {
                        violations.Add($"{rel} references legacy {forbiddenDeleteWarnings}).");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void PlatformUICore_ContainsZeroWinFormsFeatureOrRevitDomImports_AndNoAppControlledDependency()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string uiCoreDir = Path.Combine(projectRoot, "src", "Platform", "UI", "Core");
            string uiAddinDir = Path.Combine(projectRoot, "src", "Platform", "UI", "Addin");

            Assert.IsTrue(Directory.Exists(uiCoreDir), "Expected src/Platform/UI/Core directory.");
            Assert.IsTrue(File.Exists(Path.Combine(uiAddinDir, "SyntheticTheme.xaml")), "Expected src/Platform/UI/Addin/SyntheticTheme.xaml.");
            Assert.IsTrue(File.Exists(Path.Combine(uiAddinDir, "SyntheticThemeDictionary.cs")), "Expected src/Platform/UI/Addin/SyntheticThemeDictionary.cs.");

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(uiCoreDir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string rel = GetRelativeNormalizedPath(projectRoot, file);
                if (Regex.IsMatch(text, @"\bSystem\.Windows\.Forms\b"))
                {
                    violations.Add($"{rel} references System.Windows.Forms");
                }
                if (Regex.IsMatch(text, @"\bSynthetic\.Features\b"))
                {
                    violations.Add($"{rel} references Synthetic.Features");
                }
                if (Regex.IsMatch(text, @"\bSynthetic\.RevitDOM\b"))
                {
                    violations.Add($"{rel} references Synthetic.RevitDOM");
                }
            }

            string progressCoordinatorPath = Path.Combine(uiAddinDir, "ProgressCoordinator.cs");
            Assert.IsTrue(File.Exists(progressCoordinatorPath), "Expected src/Platform/UI/Addin/ProgressCoordinator.cs.");
            string coordinatorText = File.ReadAllText(progressCoordinatorPath);
            if (Regex.IsMatch(coordinatorText, @"\bApp\.AppControlled\b"))
            {
                violations.Add("src/Platform/UI/Addin/ProgressCoordinator.cs must not reference App.AppControlled.");
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void ThemedXamlViews_UseSyntheticThemeDictionary_WithoutRelativeThemeSource()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string srcRoot = Path.Combine(projectRoot, "src");
            var violations = new List<string>();
            int themeMergeCount = 0;

            foreach (string xamlFile in Directory.EnumerateFiles(srcRoot, "*.xaml", SearchOption.AllDirectories))
            {
                if (xamlFile.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    xamlFile.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string text = File.ReadAllText(xamlFile);
                string rel = GetRelativeNormalizedPath(srcRoot, xamlFile);

                if (text.IndexOf("SyntheticTheme.xaml", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    violations.Add($"src/{rel} must merge <theme:SyntheticThemeDictionary /> instead of relative SyntheticTheme.xaml.");
                }
                if (text.IndexOf("Synthetic.Shared.UI", StringComparison.Ordinal) >= 0)
                {
                    violations.Add($"src/{rel} still references obsolete Synthetic.Shared.UI namespace.");
                }
                if (text.IndexOf("SyntheticThemeDictionary", StringComparison.Ordinal) >= 0)
                {
                    themeMergeCount++;
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
            Assert.AreEqual(35, themeMergeCount, "Expected exactly 35 themed .xaml views merging SyntheticThemeDictionary.");
        }

        [Test]
        public void PlatformIO_Core_HasZeroForbiddenDependencies_AndZeroEmittedExcelInteropDll()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string ioCoreDir = Path.Combine(projectRoot, "src", "Platform", "IO", "Core");
            Assert.IsTrue(Directory.Exists(ioCoreDir), $"Expected Platform/IO/Core directory at {ioCoreDir}");

            string[] forbiddenPatterns =
            {
                @"\bAutodesk\.Revit\b",
                @"\bSystem\.Windows\.Forms\b",
                @"\bSynthetic\.Settings\b",
                @"\bSynthetic\.Platform\.Settings\b",
                @"\bSynthetic\.Platform\.UI\b",
                @"\bSynthetic\.Shared\.UI\b",
                @"\bSynthetic\.RevitDOM\b",
                @"\bSynthetic\.Features\b",
                @"\bSynthetic\.Modules\b",
                @"\bSynthetic\.Infrastructure\.Persistence\b"
            };

            var sourceViolations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(ioCoreDir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string pattern in forbiddenPatterns)
                {
                    if (Regex.IsMatch(text, pattern))
                    {
                        sourceViolations.Add($"src/Platform/IO/Core/{Path.GetFileName(file)} matched forbidden pattern '{pattern}'.");
                    }
                }
            }

            Assert.IsEmpty(sourceViolations, string.Join(Environment.NewLine, sourceViolations));

            Assembly coreAssembly = typeof(FileUtil).Assembly;
            Assert.AreEqual("Synthetic.Core", coreAssembly.GetName().Name);

            var excelRefs = coreAssembly.GetReferencedAssemblies()
                .Where(a => !string.IsNullOrEmpty(a.Name) &&
                            a.Name!.IndexOf("Microsoft.Office.Interop.Excel", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(a => a.FullName)
                .ToList();
            Assert.IsEmpty(excelRefs, "Synthetic.Core.dll must embed Excel interop types instead of emitting a runtime assembly reference.");

            string? coreDir = Path.GetDirectoryName(coreAssembly.Location);
            Assert.IsFalse(string.IsNullOrEmpty(coreDir));
            string emittedExcelDll = Path.Combine(coreDir!, "Microsoft.Office.Interop.Excel.dll");
            Assert.IsFalse(File.Exists(emittedExcelDll), $"Did not expect Microsoft.Office.Interop.Excel.dll at '{emittedExcelDll}'.");
        }

        [Test]
        public void PlatformSettings_CoreAndAddin_HaveZeroForbiddenDependencies()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string settingsCoreDir = Path.Combine(projectRoot, "src", "Platform", "Settings", "Core");
            string settingsAddinDir = Path.Combine(projectRoot, "src", "Platform", "Settings", "Addin");
            Assert.IsTrue(Directory.Exists(settingsCoreDir), $"Expected Platform/Settings/Core directory at {settingsCoreDir}");
            Assert.IsTrue(Directory.Exists(settingsAddinDir), $"Expected Platform/Settings/Addin directory at {settingsAddinDir}");

            string[] forbiddenCorePatterns =
            {
                @"\bAutodesk\.Revit\b",
                @"\bSynthetic\.Platform\.UI\b",
                @"\bSynthetic\.Shared\.UI\b",
                @"\bSynthetic\.Features\b",
                @"\bSynthetic\.Modules\b",
                @"\bSynthetic\.RevitDOM\b"
            };

            string[] forbiddenAddinPatterns =
            {
                @"\bSynthetic\.Platform\.UI\b",
                @"\bSynthetic\.Shared\.UI\b",
                @"\bSynthetic\.Features\b",
                @"\bSynthetic\.Modules\b",
                @"\bProgressCoordinator\b"
            };

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(settingsCoreDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = GetRelativeNormalizedPath(projectRoot, file);
                string text = File.ReadAllText(file);
                foreach (string pattern in forbiddenCorePatterns)
                {
                    if (Regex.IsMatch(text, pattern))
                    {
                        violations.Add($"{rel} matched forbidden Core pattern '{pattern}'.");
                    }
                }
            }

            foreach (string file in Directory.EnumerateFiles(settingsAddinDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = GetRelativeNormalizedPath(projectRoot, file);
                string text = File.ReadAllText(file);
                foreach (string pattern in forbiddenAddinPatterns)
                {
                    if (Regex.IsMatch(text, pattern))
                    {
                        violations.Add($"{rel} matched forbidden Addin pattern '{pattern}'.");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void FeaturesSettingsDashboard_HasZeroSiblingFeatureImports_InCSharpAndXaml()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string dashboardDir = Path.Combine(projectRoot, "src", "Features", "SettingsDashboard", "Addin");
            Assert.IsTrue(Directory.Exists(dashboardDir), $"Expected Features/SettingsDashboard/Addin directory at {dashboardDir}");

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(dashboardDir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rel = GetRelativeNormalizedPath(projectRoot, file);
                string text = File.ReadAllText(file);

                foreach (Match match in Regex.Matches(text, @"\bSynthetic\.Features\.([A-Za-z0-9_]+)"))
                {
                    string featureName = match.Groups[1].Value;
                    if (!string.Equals(featureName, "SettingsDashboard", StringComparison.Ordinal))
                    {
                        violations.Add($"{rel} references sibling feature namespace Synthetic.Features.{featureName}.");
                    }
                }

                if (Regex.IsMatch(text, @"\bSynthetic\.Modules\b"))
                {
                    violations.Add($"{rel} references legacy Synthetic.Modules namespace.");
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void Repository_ContainsZeroRetiredSettingsGlobalsOrProjectConfigStubs()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string[] roots = { Path.Combine(projectRoot, "src"), Path.Combine(projectRoot, "tests") };
            var violations = new List<string>();

            string forbiddenType = "Config" + "Collection";
            string forbiddenAppProp = "App." + "Configurations";
            string forbiddenReadProj = "Read" + "ProjectConfig(";
            string forbiddenWriteProj = "Write" + "ProjectConfig(";

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = GetRelativeNormalizedPath(projectRoot, file);
                    string text = File.ReadAllText(file);

                    if (text.Contains(forbiddenType))
                    {
                        violations.Add($"{rel} references retired {forbiddenType}.");
                    }
                    if (text.Contains(forbiddenAppProp))
                    {
                        violations.Add($"{rel} references retired {forbiddenAppProp}.");
                    }
                    if (text.Contains(forbiddenReadProj))
                    {
                        violations.Add($"{rel} references retired {forbiddenReadProj}.");
                    }
                    if (text.Contains(forbiddenWriteProj))
                    {
                        violations.Add($"{rel} references retired {forbiddenWriteProj}.");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
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
