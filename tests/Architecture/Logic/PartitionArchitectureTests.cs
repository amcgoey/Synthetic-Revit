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
                    string codeOnly = StripCommentsAndStringLiterals(text);
                    if (Regex.IsMatch(codeOnly, @"\bAutodesk\.Revit\b"))
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

        [Test]
        public void FeatureSliceCore_HasZeroSiblingFeatureImports_AndZeroLegacyModulesImports()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string srcRoot = Path.Combine(projectRoot, "src");
            string featuresRoot = Path.Combine(srcRoot, "Features");

            Assert.IsTrue(Directory.Exists(Path.Combine(featuresRoot, "SheetIndex", "Core")), "Expected src/Features/SheetIndex/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(featuresRoot, "SharedCoordinates", "Core")), "Expected src/Features/SharedCoordinates/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(featuresRoot, "ShadowStudy", "Core")), "Expected src/Features/ShadowStudy/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(featuresRoot, "ShadowStudy", "Addin")), "Expected src/Features/ShadowStudy/Addin partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(projectRoot, "tests", "Features", "ShadowStudy", "Logic")), "Expected tests/Features/ShadowStudy/Logic partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(featuresRoot, "MergeDuplicates", "Core")), "Expected src/Features/MergeDuplicates/Core partition root.");
            Assert.IsTrue(Directory.Exists(Path.Combine(featuresRoot, "StandardsManagement", "Core")), "Expected src/Features/StandardsManagement/Core partition root.");

            string[] allFourteenFeatureSlices =
            {
                "AutoTagger",
                "BatchPrint",
                "DetailItemFactory",
                "Diagnostics",
                "FamilyManagement",
                "MaterialManagement",
                "MergeDuplicates",
                "SettingsDashboard",
                "ShadowStudy",
                "SharedCoordinates",
                "SheetIndex",
                "StandardsManagement",
                "ViewManagement",
                "Worksets"
            };

            foreach (string slice in allFourteenFeatureSlices)
            {
                Assert.IsTrue(
                    Directory.Exists(Path.Combine(featuresRoot, slice, "Addin")),
                    $"Expected src/Features/{slice}/Addin partition root.");
            }

            Assert.IsTrue(
                File.Exists(Path.Combine(featuresRoot, "Worksets", "Addin", "Defaults", "SyntheticWorksets.xlsx")),
                "Expected src/Features/Worksets/Addin/Defaults/SyntheticWorksets.xlsx.");

            var violations = new List<string>();
            foreach (string sliceDir in Directory.EnumerateDirectories(featuresRoot))
            {
                string sliceName = Path.GetFileName(sliceDir);
                string coreDir = Path.Combine(sliceDir, "Core");
                if (!Directory.Exists(coreDir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = GetRelativeNormalizedPath(projectRoot, file);
                    string text = File.ReadAllText(file);
                    string codeOnly = StripCommentsAndStringLiterals(text);

                    if (Regex.IsMatch(codeOnly, @"\bAutodesk\.Revit\b"))
                    {
                        violations.Add($"{rel} imports Autodesk.Revit inside Core/.");
                    }

                    if (Regex.IsMatch(text, @"\bProgressCoordinator\b"))
                    {
                        violations.Add($"{rel} references ProgressCoordinator inside Core/.");
                    }

                    if (Regex.IsMatch(text, @"\bSynthetic\.Modules\b"))
                    {
                        violations.Add($"{rel} references legacy Synthetic.Modules namespace.");
                    }

                    foreach (Match match in Regex.Matches(text, @"\bSynthetic\.Features\.([A-Za-z0-9_]+)\b"))
                    {
                        string referencedSlice = match.Groups[1].Value;
                        if (!string.Equals(referencedSlice, sliceName, StringComparison.Ordinal))
                        {
                            violations.Add($"{rel} imports sibling feature slice Synthetic.Features.{referencedSlice}.");
                        }
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void RevitDom_HasZeroFeatureUiOrRevitUiDependencies_AndCoreAndLogicTestsAreRevitFree()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string revitDomDir = Path.Combine(projectRoot, "src", "RevitDOM");
            string revitDomCoreDir = Path.Combine(revitDomDir, "Core");
            string revitDomAddinDir = Path.Combine(revitDomDir, "Addin");
            string revitDomLogicTestsDir = Path.Combine(projectRoot, "tests", "RevitDOM", "Logic");

            Assert.IsTrue(Directory.Exists(revitDomCoreDir), $"Expected src/RevitDOM/Core directory at {revitDomCoreDir}");
            Assert.IsTrue(Directory.Exists(revitDomAddinDir), $"Expected src/RevitDOM/Addin directory at {revitDomAddinDir}");
            Assert.IsTrue(Directory.Exists(revitDomLogicTestsDir), $"Expected tests/RevitDOM/Logic directory at {revitDomLogicTestsDir}");
            Assert.IsFalse(Directory.Exists(Path.Combine(revitDomDir, "Operations", "Standards")), "RevitDOM/Operations/Standards must be evicted to Features/StandardsManagement.");
            Assert.IsFalse(Directory.Exists(Path.Combine(revitDomDir, "Operations", "Merge")), "RevitDOM/Operations/Merge must be evicted to Features/MergeDuplicates.");
            Assert.IsFalse(Directory.Exists(Path.Combine(revitDomCoreDir, "Operations", "Standards")), "RevitDOM/Core/Operations/Standards must not exist.");
            Assert.IsFalse(Directory.Exists(Path.Combine(revitDomCoreDir, "Operations", "Merge")), "RevitDOM/Core/Operations/Merge must not exist.");
            Assert.IsFalse(Directory.Exists(Path.Combine(revitDomAddinDir, "Operations", "Standards")), "RevitDOM/Addin/Operations/Standards must not exist.");
            Assert.IsFalse(Directory.Exists(Path.Combine(revitDomAddinDir, "Operations", "Merge")), "RevitDOM/Addin/Operations/Merge must not exist.");
            Assert.IsFalse(Directory.Exists(Path.Combine(projectRoot, "src", "SyntheticShared", "RevitDOM")), "Legacy src/SyntheticShared/RevitDOM must be completely evicted.");

            string[] forbiddenRevitDomPatterns =
            {
                @"\bSynthetic\.Features\b",
                @"\bSynthetic\.Modules\b",
                @"\bSynthetic\.Platform\.UI\b",
                @"\bSynthetic\.Shared\.UI\b",
                @"\bSystem\.Windows\b",
                @"\bAutodesk\.Revit\.UI\b"
            };

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(revitDomDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = GetRelativeNormalizedPath(projectRoot, file);
                string text = File.ReadAllText(file);
                foreach (string pattern in forbiddenRevitDomPatterns)
                {
                    if (Regex.IsMatch(text, pattern))
                    {
                        violations.Add($"{rel} matched forbidden pattern '{pattern}'.");
                    }
                }
            }

            foreach (string file in Directory.EnumerateFiles(revitDomLogicTestsDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = GetRelativeNormalizedPath(projectRoot, file);
                string codeOnly = StripCommentsAndStringLiterals(File.ReadAllText(file));
                if (Regex.IsMatch(codeOnly, @"\bAutodesk\.Revit\b"))
                {
                    violations.Add($"{rel} references Autodesk.Revit (RevitDOM/Logic must be pure headless).");
                }
            }

            Assembly logicTestsAssembly = typeof(PartitionArchitectureTests).Assembly;
            var testRevitRefs = logicTestsAssembly.GetReferencedAssemblies()
                .Where(a => !string.IsNullOrEmpty(a.Name) &&
                            (a.Name!.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                             a.Name.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase)))
                .Select(a => a.FullName)
                .ToList();
            Assert.IsEmpty(testRevitRefs, "Synthetic.LogicTests.dll must not reference Revit API or RevitAPIMock assemblies.");

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void SourceTree_ContainsZeroSyntheticSharedLegacyClutterOrAssemblyInfo_AndConforms100PercentToThreeRootPartition()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string srcRoot = Path.Combine(projectRoot, "src");

            Assert.IsFalse(Directory.Exists(Path.Combine(srcRoot, "SyntheticShared")), "Legacy src/SyntheticShared directory must be completely removed.");
            Assert.IsFalse(Directory.Exists(Path.Combine(srcRoot, "Synthetic2022")), "Retired src/Synthetic2022 directory must not exist.");
            Assert.IsFalse(Directory.Exists(Path.Combine(srcRoot, "packages")), "Legacy src/packages directory must not exist.");
            Assert.IsFalse(File.Exists(Path.Combine(srcRoot, "msbuild.log")), "Stray src/msbuild.log file must not exist.");

            string targetsPath = Path.Combine(srcRoot, "Directory.Build.targets");
            Assert.IsTrue(File.Exists(targetsPath), "Expected src/Directory.Build.targets.");
            string targetsText = File.ReadAllText(targetsPath);
            StringAssert.DoesNotContain("SyntheticShared", targetsText, "src/Directory.Build.targets must not reference retired SyntheticShared.");

            var wpfTmpViolations = Directory.EnumerateFiles(projectRoot, "*_wpftmp.csproj", SearchOption.AllDirectories)
                .Where(f => f.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                            f.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(f => GetRelativeNormalizedPath(projectRoot, f))
                .ToList();
            Assert.IsEmpty(wpfTmpViolations, "Temporary *_wpftmp.csproj files must not exist outside obj/: " + string.Join(", ", wpfTmpViolations));

            var assemblyInfoViolations = new List<string>();
            var layoutViolations = new List<string>();
            var legacyNamespaceViolations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories))
            {
                if (file.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    file.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string fileName = Path.GetFileName(file);
                string rel = GetRelativeNormalizedPath(srcRoot, file);
                if (fileName.StartsWith("AssemblyInfo", StringComparison.OrdinalIgnoreCase) &&
                    file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    assemblyInfoViolations.Add($"src/{rel}");
                }

                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] segments = rel.Split('/');
                string topFolder = segments[0];

                bool isProjectHeadFolder =
                    string.Equals(topFolder, "Synthetic.Core", StringComparison.Ordinal) ||
                    topFolder.StartsWith("Synthetic.Bootstrap", StringComparison.Ordinal) ||
                    Regex.IsMatch(topFolder, @"^Synthetic202[3-6]$", RegexOptions.CultureInvariant);

                if (!isProjectHeadFolder)
                {
                    if (!PartitionedSourceRoots.Contains(topFolder, StringComparer.Ordinal))
                    {
                        layoutViolations.Add($"src/{rel} must live under Features/, RevitDOM/, Platform/, or a project head folder.");
                    }
                    else
                    {
                        bool inCore = segments.Contains("Core", StringComparer.Ordinal);
                        bool inAddin = segments.Contains("Addin", StringComparer.Ordinal);
                        if (!inCore && !inAddin)
                        {
                            layoutViolations.Add($"src/{rel} must live under a Core/ or Addin/ partition folder.");
                        }
                        else if (inCore && string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                        {
                            layoutViolations.Add($"src/{rel} is a XAML file inside Core/ (XAML must live in Addin/).");
                        }
                    }
                }

                string text = File.ReadAllText(file);
                if (Regex.IsMatch(text, @"\bSynthetic\.Modules\b"))
                {
                    legacyNamespaceViolations.Add($"src/{rel} references legacy Synthetic.Modules namespace.");
                }
                if (Regex.IsMatch(text, @"\bSynthetic\.Infrastructure\.Diagnostics\b"))
                {
                    legacyNamespaceViolations.Add($"src/{rel} references legacy Synthetic.Infrastructure.Diagnostics namespace.");
                }
                if (Regex.IsMatch(text, @"\bSynthetic\.Shared\b"))
                {
                    legacyNamespaceViolations.Add($"src/{rel} references legacy Synthetic.Shared namespace.");
                }
            }

            Assert.IsEmpty(assemblyInfoViolations, "Zero AssemblyInfo*.cs files may remain in src/: " + string.Join(", ", assemblyInfoViolations));
            Assert.IsEmpty(layoutViolations, string.Join(Environment.NewLine, layoutViolations));
            Assert.IsEmpty(legacyNamespaceViolations, string.Join(Environment.NewLine, legacyNamespaceViolations));
        }

        [Test]
        public void TestTree_ContainsZeroRetiredSyntheticTestsOrLegacyModelDirs_AndConforms100PercentToIsomorphicMirror()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string testsRoot = Path.Combine(projectRoot, "tests");
            string srcRoot = Path.Combine(projectRoot, "src");

            var retiredTestDirs = Directory.EnumerateDirectories(testsRoot, "SyntheticTests*", SearchOption.TopDirectoryOnly)
                .Select(d => GetRelativeNormalizedPath(projectRoot, d))
                .ToList();
            Assert.IsEmpty(retiredTestDirs, "Retired tests/SyntheticTests* directories must not exist: " + string.Join(", ", retiredTestDirs));
            Assert.IsFalse(Directory.Exists(Path.Combine(testsRoot, "test_models")), "Legacy tests/test_models directory must not exist.");

            var legacyScripts = Directory.EnumerateFiles(projectRoot, "run_integration_test.py", SearchOption.AllDirectories)
                .Select(f => GetRelativeNormalizedPath(projectRoot, f))
                .ToList();
            Assert.IsEmpty(legacyScripts, "Retired run_integration_test.py must not exist: " + string.Join(", ", legacyScripts));

            Assert.IsTrue(File.Exists(Path.Combine(testsRoot, "Assets", "test_duplicate_families.json")), "Expected tests/Assets/test_duplicate_families.json.");
            Assert.IsTrue(File.Exists(Path.Combine(testsRoot, "Assets", "test_duplicate_groups.json")), "Expected tests/Assets/test_duplicate_groups.json.");
            foreach (string year in new[] { "2023", "2024", "2025", "2026" })
            {
                Assert.IsTrue(
                    File.Exists(Path.Combine(testsRoot, "Assets", "test_models", $"TestTemplate{year}.rvt")),
                    $"Expected tests/Assets/test_models/TestTemplate{year}.rvt.");
                Assert.IsTrue(
                    File.Exists(Path.Combine(testsRoot, "Projects", $"Synthetic.RevitTests.{year}", $"Synthetic.RevitTests.{year}.csproj")),
                    $"Expected tests/Projects/Synthetic.RevitTests.{year}/Synthetic.RevitTests.{year}.csproj.");
            }

            string logicCsprojPath = Path.Combine(testsRoot, "Projects", "Synthetic.LogicTests", "Synthetic.LogicTests.csproj");
            string legacyMockCsprojPath = Path.Combine(testsRoot, "Projects", "Synthetic.LegacyMockTests", "Synthetic.LegacyMockTests.csproj");
            Assert.IsTrue(File.Exists(logicCsprojPath), "Expected tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj.");
            Assert.IsTrue(File.Exists(legacyMockCsprojPath), "Expected tests/Projects/Synthetic.LegacyMockTests/Synthetic.LegacyMockTests.csproj.");

            string logicCsprojText = File.ReadAllText(logicCsprojPath);
            StringAssert.DoesNotContain("RevitAPIMock", logicCsprojText, "Synthetic.LogicTests.csproj must not reference RevitAPIMock.");
            StringAssert.DoesNotContain("RevitAPIUIMock", logicCsprojText, "Synthetic.LogicTests.csproj must not reference RevitAPIUIMock.");
            StringAssert.DoesNotContain("Synthetic202", logicCsprojText, "Synthetic.LogicTests.csproj must not reference Synthetic202* payload heads.");

            string srcTargetsText = File.ReadAllText(Path.Combine(srcRoot, "Directory.Build.targets"));
            StringAssert.DoesNotContain("SyntheticTests", srcTargetsText, "src/Directory.Build.targets must not retain legacy SyntheticTests* InternalsVisibleTo entries.");
            StringAssert.DoesNotContain("<InternalsVisibleTo Include=\"Synthetic202", srcTargetsText, "src/Directory.Build.targets must not grant InternalsVisibleTo to Synthetic202* payload assemblies.");

            string testsTargetsText = File.ReadAllText(Path.Combine(testsRoot, "Directory.Build.targets"));
            StringAssert.DoesNotContain(@"Platform\**\Revit\", testsTargetsText, "tests/Directory.Build.targets must not use recursive Platform\\**\\Revit\\ glob that leaks Platform/Revit/Logic/*.cs into RevitTests.");
            StringAssert.Contains(@"Platform\*\Revit\**\*.cs", testsTargetsText, "tests/Directory.Build.targets must scope Platform Revit test glob to Platform\\*\\Revit\\**\\*.cs.");

            var testLayoutViolations = new List<string>();
            var logicRevitApiViolations = new List<string>();

            foreach (string file in Directory.EnumerateFiles(testsRoot, "*.*", SearchOption.AllDirectories))
            {
                if (file.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    file.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    file.IndexOf("\\TestResults\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rel = GetRelativeNormalizedPath(testsRoot, file);
                string[] segments = rel.Split('/');
                string topFolder = segments[0];

                if (string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    bool allowedProj =
                        (string.Equals(topFolder, "Projects", StringComparison.Ordinal) &&
                         segments.Length == 3 &&
                         Regex.IsMatch(segments[1], @"^Synthetic\.(LogicTests|LegacyMockTests|RevitTests\.202[3-6])$", RegexOptions.CultureInvariant)) ||
                        ((string.Equals(topFolder, "RevitAPIMock", StringComparison.Ordinal) ||
                          string.Equals(topFolder, "RevitAPIUIMock", StringComparison.Ordinal)) &&
                         segments.Length == 2);

                    if (!allowedProj)
                    {
                        testLayoutViolations.Add($"tests/{rel} is outside tests/Projects/ or RevitAPI*Mock/.");
                    }
                    continue;
                }

                if (string.Equals(topFolder, "RevitAPIMock", StringComparison.Ordinal) ||
                    string.Equals(topFolder, "RevitAPIUIMock", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(topFolder, "Projects", StringComparison.Ordinal))
                {
                    bool allowedProjectCode =
                        segments.Length >= 3 &&
                        Regex.IsMatch(segments[1], @"^Synthetic\.(LogicTests|LegacyMockTests|RevitTests\.202[3-6])$", RegexOptions.CultureInvariant);
                    if (!allowedProjectCode)
                    {
                        testLayoutViolations.Add($"tests/{rel} must live under a valid tests/Projects/Synthetic.* head.");
                    }
                    continue;
                }

                if (string.Equals(topFolder, "Fixtures", StringComparison.Ordinal))
                {
                    if (segments.Length < 3 ||
                        (!string.Equals(segments[1], "Shared", StringComparison.Ordinal) &&
                         !string.Equals(segments[1], "Revit", StringComparison.Ordinal)))
                    {
                        testLayoutViolations.Add($"tests/{rel} must live under tests/Fixtures/Shared/ or tests/Fixtures/Revit/.");
                    }
                    else if (string.Equals(segments[1], "Shared", StringComparison.Ordinal))
                    {
                        string codeOnly = StripCommentsAndStringLiterals(File.ReadAllText(file));
                        if (Regex.IsMatch(codeOnly, @"\bAutodesk\.Revit\b"))
                        {
                            logicRevitApiViolations.Add($"tests/{rel} imports Autodesk.Revit inside Fixtures/Shared/.");
                        }
                    }
                    continue;
                }

                if (!PartitionedTestRoots.Contains(topFolder, StringComparer.Ordinal))
                {
                    testLayoutViolations.Add($"tests/{rel} must live under Features/, RevitDOM/, Platform/, Architecture/, Fixtures/, or Projects/.");
                    continue;
                }

                bool inLogic = segments.Contains("Logic", StringComparer.Ordinal);
                bool inRevit = segments.Contains("Revit", StringComparer.Ordinal);
                if (!inLogic && !inRevit)
                {
                    testLayoutViolations.Add($"tests/{rel} must live under a Logic/ or Revit/ partition folder.");
                }
                else if (inLogic)
                {
                    string codeOnly = StripCommentsAndStringLiterals(File.ReadAllText(file));
                    if (Regex.IsMatch(codeOnly, @"\bAutodesk\.Revit\b"))
                    {
                        logicRevitApiViolations.Add($"tests/{rel} imports Autodesk.Revit inside Logic/.");
                    }
                }
            }

            Assert.IsEmpty(testLayoutViolations, string.Join(Environment.NewLine, testLayoutViolations));
            Assert.IsEmpty(logicRevitApiViolations, string.Join(Environment.NewLine, logicRevitApiViolations));
        }

        [Test]
        public void NonHostProductionLayers_HaveZeroPlatformHostDependencies()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string srcRoot = Path.Combine(projectRoot, "src");

            string[] nonHostRoots =
            {
                Path.Combine(srcRoot, "Features"),
                Path.Combine(srcRoot, "RevitDOM"),
                Path.Combine(srcRoot, "Platform", "IO"),
                Path.Combine(srcRoot, "Platform", "Settings"),
                Path.Combine(srcRoot, "Platform", "UI"),
                Path.Combine(srcRoot, "Platform", "Revit"),
            };

            var violations = new List<string>();
            foreach (string rootDir in nonHostRoots)
            {
                if (!Directory.Exists(rootDir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(rootDir, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        file.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    string codeOnly = StripCommentsAndStringLiterals(File.ReadAllText(file));
                    if (Regex.IsMatch(codeOnly, @"\bSynthetic\.Platform\.Host\b"))
                    {
                        violations.Add($"src/{GetRelativeNormalizedPath(srcRoot, file)} references Synthetic.Platform.Host outside src/Platform/Host/.");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
        }

        private static string StripCommentsAndStringLiterals(string source)
        {
            // Strip verbatim strings, normal strings, single-line comments, and block comments
            // so string discriminators like "Autodesk.Revit.DB.Material" are not mistaken for API imports.
            return Regex.Replace(
                source,
                @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|//[^\r\n]*|/\*[\s\S]*?\*/",
                " ");
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
