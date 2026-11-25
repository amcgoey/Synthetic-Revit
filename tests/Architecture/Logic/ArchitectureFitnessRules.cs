using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SyntheticTests.Architecture
{
    /// <summary>
    /// Pure rule evaluators for the strongly-typed <see cref="FitnessRule"/> gates.
    /// </summary>
    public static class ArchitectureFitnessRules
    {
        public const int ExpectedThemedXamlViewCount = 35;
        public const int ExpectedLegacyMockTestFileCount = 25;

        private static readonly string[] AllFourteenFeatureSlices =
        {
            "AutoTagger",
            "DetailItemFactory",
            "Diagnostics",
            "FamilyManagement",
            "MaterialManagement",
            "MergeDuplicates",
            "MultiModelPdfExport",
            "SettingsDashboard",
            "ShadowStudy",
            "SharedCoordinates",
            "SheetIndex",
            "StandardsManagement",
            "ViewManagement",
            "Worksets"
        };

        private static readonly HashSet<string> PartitionedSourceRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "Features",
            "RevitDOM",
            "Platform",
            "Domain"
        };

        private static readonly HashSet<string> PartitionedTestRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "Features",
            "RevitDOM",
            "Platform",
            "Architecture"
        };

        private static readonly Regex DynamicYearHeadRegex = new Regex(@"^Synthetic(20\d{2})$", RegexOptions.CultureInvariant);
        private static readonly Regex DynamicBootstrapYearRegex = new Regex(@"^Synthetic\.Bootstrap(20\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex DynamicRevitTestProjectRegex = new Regex(@"^Synthetic\.RevitTests\.(20\d{2})$", RegexOptions.CultureInvariant);
        private static readonly Regex RevitYearPrefixRegex = new Regex(@"^REVIT20\d{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex AdapterInterfaceNameRegex = new Regex(@"^I[A-Z].*Adapter$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> InteractiveSeamRestrictedRoleFolders = new HashSet<string>(StringComparer.Ordinal)
        {
            "ViewModels",
            "Views",
            "Models",
            "Settings"
        };

        private static readonly HashSet<string> DialogPurityRestrictedRoleFolders = new HashSet<string>(StringComparer.Ordinal)
        {
            "ViewModels",
            "Views",
            "Adapters",
            "Handlers",
            "ExternalEvents",
            "Settings"
        };

        private static readonly HashSet<string> CanonicalCategoryAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnitTest",
            "UnitTestAttribute",
            "SyntheticTests.UnitTest",
            "SyntheticTests.UnitTestAttribute",
            "ArchitectureTest",
            "ArchitectureTestAttribute",
            "SyntheticTests.ArchitectureTest",
            "SyntheticTests.ArchitectureTestAttribute",
            "RevitSmokeTest",
            "RevitSmokeTestAttribute",
            "SyntheticTests.RevitSmokeTest",
            "SyntheticTests.RevitSmokeTestAttribute",
            "RevitAdapterTest",
            "RevitAdapterTestAttribute",
            "SyntheticTests.RevitAdapterTest",
            "SyntheticTests.RevitAdapterTestAttribute",
            "RevitIsolatedDocTest",
            "RevitIsolatedDocTestAttribute",
            "SyntheticTests.RevitIsolatedDocTest",
            "SyntheticTests.RevitIsolatedDocTestAttribute",
            "QuarantineTest",
            "QuarantineTestAttribute",
            "SyntheticTests.QuarantineTest",
            "SyntheticTests.QuarantineTestAttribute"
        };

        private static readonly HashSet<string> TestFixtureAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "TestFixture",
            "TestFixtureAttribute",
            "NUnit.Framework.TestFixture",
            "NUnit.Framework.TestFixtureAttribute"
        };

        private static readonly HashSet<string> TestMethodAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Test",
            "TestAttribute",
            "NUnit.Framework.Test",
            "NUnit.Framework.TestAttribute",
            "TestCase",
            "TestCaseAttribute",
            "NUnit.Framework.TestCase",
            "NUnit.Framework.TestCaseAttribute",
            "TestCaseSource",
            "TestCaseSourceAttribute",
            "NUnit.Framework.TestCaseSource",
            "NUnit.Framework.TestCaseSourceAttribute",
            "Theory",
            "TheoryAttribute",
            "NUnit.Framework.Theory",
            "NUnit.Framework.TheoryAttribute"
        };

        public static IReadOnlyList<string> EvaluateCoLocatedPartitionLayout(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<string>();
            string projectRoot = snapshot.ProjectRoot;
            string srcRoot = snapshot.SrcRoot;
            string testsRoot = snapshot.TestsRoot;

            void RequireDirectory(string fullPath, string label)
            {
                if (!Directory.Exists(fullPath))
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation($"Missing required directory: {label}"));
                }
            }

            void RequireFile(string fullPath, string label)
            {
                if (!File.Exists(fullPath))
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation($"Missing required file: {label}"));
                }
            }

            void ForbidDirectory(string fullPath, string label)
            {
                if (Directory.Exists(fullPath))
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation($"Retired directory must not exist: {label}"));
                }
            }

            void ForbidFile(string fullPath, string label)
            {
                if (File.Exists(fullPath))
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation($"Retired file must not exist: {label}"));
                }
            }

            // Required source partition roots
            RequireDirectory(Path.Combine(srcRoot, "Platform", "Host"), "src/Platform/Host");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "IO", "Core"), "src/Platform/IO/Core");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "UI", "Core"), "src/Platform/UI/Core");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "UI", "Addin"), "src/Platform/UI/Addin");
            RequireFile(Path.Combine(srcRoot, "Platform", "UI", "Addin", "SyntheticTheme.xaml"), "src/Platform/UI/Addin/SyntheticTheme.xaml");
            RequireFile(Path.Combine(srcRoot, "Platform", "UI", "Addin", "SyntheticThemeDictionary.cs"), "src/Platform/UI/Addin/SyntheticThemeDictionary.cs");
            RequireFile(Path.Combine(srcRoot, "Platform", "UI", "Addin", "ProgressCoordinator.cs"), "src/Platform/UI/Addin/ProgressCoordinator.cs");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "Revit", "Core"), "src/Platform/Revit/Core");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "Revit", "Addin"), "src/Platform/Revit/Addin");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "Settings", "Core"), "src/Platform/Settings/Core");
            RequireDirectory(Path.Combine(srcRoot, "Platform", "Settings", "Addin"), "src/Platform/Settings/Addin");
            RequireDirectory(Path.Combine(srcRoot, "Features", "SettingsDashboard", "Addin"), "src/Features/SettingsDashboard/Addin");
            RequireDirectory(Path.Combine(srcRoot, "Features", "SheetIndex", "Core"), "src/Features/SheetIndex/Core");
            RequireDirectory(Path.Combine(srcRoot, "Features", "SharedCoordinates", "Core"), "src/Features/SharedCoordinates/Core");
            RequireDirectory(Path.Combine(srcRoot, "Features", "ShadowStudy", "Core"), "src/Features/ShadowStudy/Core");
            RequireDirectory(Path.Combine(srcRoot, "Features", "ShadowStudy", "Addin"), "src/Features/ShadowStudy/Addin");
            RequireDirectory(Path.Combine(srcRoot, "Features", "MergeDuplicates", "Core"), "src/Features/MergeDuplicates/Core");
            RequireDirectory(Path.Combine(srcRoot, "Features", "StandardsManagement", "Core"), "src/Features/StandardsManagement/Core");
            RequireDirectory(Path.Combine(srcRoot, "RevitDOM", "Core"), "src/RevitDOM/Core");
            RequireDirectory(Path.Combine(srcRoot, "RevitDOM", "Addin"), "src/RevitDOM/Addin");

            foreach (string slice in AllFourteenFeatureSlices)
            {
                RequireDirectory(Path.Combine(srcRoot, "Features", slice, "Addin"), $"src/Features/{slice}/Addin");
            }

            RequireFile(
                Path.Combine(srcRoot, "Features", "Worksets", "Addin", "Defaults", "SyntheticWorksets.xlsx"),
                "src/Features/Worksets/Addin/Defaults/SyntheticWorksets.xlsx");

            // Required test partition roots
            RequireDirectory(Path.Combine(testsRoot, "Platform", "Host"), "tests/Platform/Host");
            RequireDirectory(Path.Combine(testsRoot, "Platform", "IO", "Logic"), "tests/Platform/IO/Logic");
            RequireDirectory(Path.Combine(testsRoot, "Platform", "UI"), "tests/Platform/UI");
            RequireDirectory(Path.Combine(testsRoot, "Platform", "Revit", "Logic"), "tests/Platform/Revit/Logic");
            RequireDirectory(Path.Combine(testsRoot, "Platform", "Revit", "Revit"), "tests/Platform/Revit/Revit");
            RequireDirectory(Path.Combine(testsRoot, "Platform", "Settings", "Logic"), "tests/Platform/Settings/Logic");
            RequireDirectory(Path.Combine(testsRoot, "Features", "ShadowStudy", "Logic"), "tests/Features/ShadowStudy/Logic");
            RequireDirectory(Path.Combine(testsRoot, "Features", "SettingsDashboard", "Revit"), "tests/Features/SettingsDashboard/Revit");
            RequireDirectory(Path.Combine(testsRoot, "RevitDOM", "Logic"), "tests/RevitDOM/Logic");
            RequireDirectory(Path.Combine(testsRoot, "Architecture"), "tests/Architecture");

            // Retired directories and files
            ForbidDirectory(Path.Combine(srcRoot, "SyntheticShared"), "src/SyntheticShared");
            ForbidDirectory(Path.Combine(srcRoot, "Synthetic2022"), "src/Synthetic2022");
            ForbidDirectory(Path.Combine(srcRoot, "packages"), "src/packages");
            ForbidFile(Path.Combine(srcRoot, "msbuild.log"), "src/msbuild.log");
            ForbidDirectory(Path.Combine(srcRoot, "RevitDOM", "Operations", "Standards"), "src/RevitDOM/Operations/Standards");
            ForbidDirectory(Path.Combine(srcRoot, "RevitDOM", "Operations", "Merge"), "src/RevitDOM/Operations/Merge");
            ForbidDirectory(Path.Combine(srcRoot, "RevitDOM", "Core", "Operations", "Standards"), "src/RevitDOM/Core/Operations/Standards");
            ForbidDirectory(Path.Combine(srcRoot, "RevitDOM", "Core", "Operations", "Merge"), "src/RevitDOM/Core/Operations/Merge");
            ForbidDirectory(Path.Combine(srcRoot, "RevitDOM", "Addin", "Operations", "Standards"), "src/RevitDOM/Addin/Operations/Standards");
            ForbidDirectory(Path.Combine(srcRoot, "RevitDOM", "Addin", "Operations", "Merge"), "src/RevitDOM/Addin/Operations/Merge");
            ForbidDirectory(Path.Combine(testsRoot, "test_models"), "tests/test_models");

            if (Directory.Exists(testsRoot))
            {
                foreach (string retiredTestDir in Directory.EnumerateDirectories(testsRoot, "SyntheticTests*", SearchOption.TopDirectoryOnly))
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                        $"Retired test directory must not exist: {ArchitectureWorkspaceSnapshot.GetRelativeNormalizedPath(projectRoot, retiredTestDir)}"));
                }
            }

            foreach (string legacyPy in EnumerateNonGitFiles(projectRoot, "run_integration_test.py"))
            {
                violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                    $"Retired script must not exist: {ArchitectureWorkspaceSnapshot.GetRelativeNormalizedPath(projectRoot, legacyPy)}"));
            }

            foreach (string wpfTmp in EnumerateNonGitFiles(projectRoot, "*_wpftmp.csproj"))
            {
                if (wpfTmp.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0 &&
                    wpfTmp.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                        $"Temporary *_wpftmp.csproj must not exist outside obj/bin: {ArchitectureWorkspaceSnapshot.GetRelativeNormalizedPath(projectRoot, wpfTmp)}"));
                }
            }

            // Shared assets & dynamic versioned project heads (2023..CURRENT_LATEST_VERSION)
            RequireFile(Path.Combine(testsRoot, "Assets", "test_duplicate_families.json"), "tests/Assets/test_duplicate_families.json");
            RequireFile(Path.Combine(testsRoot, "Assets", "test_duplicate_groups.json"), "tests/Assets/test_duplicate_groups.json");
            RequireFile(Path.Combine(testsRoot, "Projects", "Synthetic.LogicTests", "Synthetic.LogicTests.csproj"), "tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj");
            RequireFile(Path.Combine(testsRoot, "Projects", "Synthetic.LegacyMockTests", "Synthetic.LegacyMockTests.csproj"), "tests/Projects/Synthetic.LegacyMockTests/Synthetic.LegacyMockTests.csproj");

            for (int year = 2023; year <= snapshot.CurrentLatestVersion; year++)
            {
                string y = year.ToString(CultureInfo.InvariantCulture);
                RequireFile(Path.Combine(testsRoot, "Assets", "test_models", $"TestTemplate{y}.rvt"), $"tests/Assets/test_models/TestTemplate{y}.rvt");
                RequireFile(Path.Combine(testsRoot, "Projects", $"Synthetic.RevitTests.{y}", $"Synthetic.RevitTests.{y}.csproj"), $"tests/Projects/Synthetic.RevitTests.{y}/Synthetic.RevitTests.{y}.csproj");
                RequireFile(Path.Combine(srcRoot, $"Synthetic{y}", $"Synthetic{y}.csproj"), $"src/Synthetic{y}/Synthetic{y}.csproj");
                RequireFile(Path.Combine(srcRoot, $"Synthetic.Bootstrap{y}", $"Synthetic.Bootstrap{y}.csproj"), $"src/Synthetic.Bootstrap{y}/Synthetic.Bootstrap{y}.csproj");
            }

            // Build targets checks
            WorkspaceXmlFile? srcTargets = snapshot.ProjectAndBuildFiles
                .FirstOrDefault(f => string.Equals(f.RelativeToRepoPath, "src/Directory.Build.targets", StringComparison.OrdinalIgnoreCase));
            if (srcTargets == null)
            {
                violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation("Expected src/Directory.Build.targets."));
            }
            else
            {
                if (srcTargets.RawText.IndexOf("SyntheticShared", StringComparison.Ordinal) >= 0)
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation("src/Directory.Build.targets must not reference retired SyntheticShared."));
                }
                if (srcTargets.RawText.IndexOf("SyntheticTests", StringComparison.Ordinal) >= 0)
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation("src/Directory.Build.targets must not retain legacy SyntheticTests* InternalsVisibleTo entries."));
                }
                if (srcTargets.RawText.IndexOf("<InternalsVisibleTo Include=\"Synthetic20", StringComparison.Ordinal) >= 0)
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation("src/Directory.Build.targets must not grant InternalsVisibleTo to Synthetic20* payload assemblies."));
                }
            }

            WorkspaceXmlFile? testsTargets = snapshot.ProjectAndBuildFiles
                .FirstOrDefault(f => string.Equals(f.RelativeToRepoPath, "tests/Directory.Build.targets", StringComparison.OrdinalIgnoreCase));
            if (testsTargets == null)
            {
                violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation("Expected tests/Directory.Build.targets."));
            }
            else
            {
                if (testsTargets.RawText.IndexOf(@"Platform\**\Revit\", StringComparison.Ordinal) >= 0)
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                        @"tests/Directory.Build.targets must not use recursive Platform\**\Revit\ glob that leaks Platform/Revit/Logic/*.cs."));
                }
                if (testsTargets.RawText.IndexOf(@"Platform\*\Revit\**\*.cs", StringComparison.Ordinal) < 0)
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                        @"tests/Directory.Build.targets must scope Platform Revit test glob to Platform\*\Revit\**\*.cs."));
                }
            }

            // Zero .shproj/.projitems and valid .csproj placement
            foreach (WorkspaceXmlFile xmlFile in snapshot.ProjectAndBuildFiles)
            {
                if (string.Equals(xmlFile.Extension, ".shproj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(xmlFile.Extension, ".projitems", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                        $"Shared project files (.shproj/.projitems) must not exist: {xmlFile.RelativeToRepoPath}"));
                    continue;
                }

                if (!string.Equals(xmlFile.Extension, ".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] segments = xmlFile.RelativeToAreaPath.Split('/');
                if (xmlFile.Area == SourceArea.Src)
                {
                    bool validSrcProj = segments.Length == 2 && IsValidSrcProjectHeadFolder(segments[0], snapshot.CurrentLatestVersion);
                    if (!validSrcProj)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xmlFile.RelativeToRepoPath} must reside inside a valid src/ project head folder (Synthetic.Core, Synthetic.Bootstrap20xx, or Synthetic20xx)."));
                    }
                }
                else
                {
                    bool validTestProj =
                        (segments.Length == 3 &&
                         string.Equals(segments[0], "Projects", StringComparison.Ordinal) &&
                         IsValidTestProjectFolder(segments[1], snapshot.CurrentLatestVersion)) ||
                        (segments.Length == 2 &&
                         (string.Equals(segments[0], "RevitAPIMock", StringComparison.Ordinal) ||
                          string.Equals(segments[0], "RevitAPIUIMock", StringComparison.Ordinal)));

                    if (!validTestProj)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xmlFile.RelativeToRepoPath} must reside under tests/Projects/Synthetic.* or RevitAPI*Mock/."));
                    }
                }
            }

            // Source .cs placement + AssemblyInfo ban
            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                if (file.Area == SourceArea.Src)
                {
                    string fileName = Path.GetFileName(file.FullPath);
                    if (fileName.StartsWith("AssemblyInfo", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"AssemblyInfo*.cs files are forbidden in src/: {file.RelativeToRepoPath}"));
                    }

                    if (file.Partition == PartitionKind.ProjectHead)
                    {
                        if (!IsValidSrcProjectHeadFolder(file.TopFolder, snapshot.CurrentLatestVersion))
                        {
                            violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                                $"{file.RelativeToRepoPath} sits in an invalid or out-of-range project head folder."));
                        }
                        continue;
                    }

                    if (!PartitionedSourceRoots.Contains(file.TopFolder))
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{file.RelativeToRepoPath} must live under Features/, RevitDOM/, Platform/, Domain/, or a valid project head folder."));
                    }
                    else if (file.Partition != PartitionKind.Core && file.Partition != PartitionKind.Addin)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{file.RelativeToRepoPath} must live under a Core/ or Addin/ partition folder."));
                    }
                }
                else
                {
                    if (file.Partition == PartitionKind.Mock)
                    {
                        continue;
                    }

                    if (string.Equals(file.TopFolder, "Projects", StringComparison.Ordinal))
                    {
                        string[] segments = file.RelativeToAreaPath.Split('/');
                        if (segments.Length < 3 || !IsValidTestProjectFolder(segments[1], snapshot.CurrentLatestVersion))
                        {
                            violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                                $"{file.RelativeToRepoPath} must live under a valid tests/Projects/Synthetic.* head."));
                        }
                        continue;
                    }

                    if (string.Equals(file.TopFolder, "Fixtures", StringComparison.Ordinal))
                    {
                        if (file.Partition != PartitionKind.Shared && file.Partition != PartitionKind.Revit)
                        {
                            violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                                $"{file.RelativeToRepoPath} must live under tests/Fixtures/Shared/ or tests/Fixtures/Revit/."));
                        }
                        continue;
                    }

                    if (!PartitionedTestRoots.Contains(file.TopFolder))
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{file.RelativeToRepoPath} must live under Features/, RevitDOM/, Platform/, Architecture/, Fixtures/, or Projects/."));
                    }
                    else if (file.Partition != PartitionKind.Logic && file.Partition != PartitionKind.Revit)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{file.RelativeToRepoPath} must live under a Logic/ or Revit/ partition folder."));
                    }
                }
            }

            // XAML placement + 35 SyntheticThemeDictionary checks
            int themeMergeCount = 0;
            foreach (WorkspaceXamlFile xaml in snapshot.XamlFiles)
            {
                if (xaml.Area == SourceArea.Src)
                {
                    if (!PartitionedSourceRoots.Contains(xaml.TopFolder))
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xaml.RelativeToRepoPath} must live under a partitioned root."));
                    }
                    else if (xaml.Partition == PartitionKind.Core)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xaml.RelativeToRepoPath} is a .xaml file inside Core/ (XAML must live in Addin/)."));
                    }
                    else if (xaml.Partition != PartitionKind.Addin)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xaml.RelativeToRepoPath} must live under an Addin/ partition folder."));
                    }

                    if (xaml.ReferencesRelativeSyntheticThemeXaml)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xaml.RelativeToRepoPath} must merge <theme:SyntheticThemeDictionary /> instead of relative SyntheticTheme.xaml."));
                    }

                    if (xaml.ReferencesSyntheticSharedUi)
                    {
                        violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                            $"{xaml.RelativeToRepoPath} still references obsolete Synthetic.Shared.UI namespace."));
                    }

                    if (xaml.MergesSyntheticThemeDictionary)
                    {
                        themeMergeCount++;
                    }
                }
            }

            if (themeMergeCount != ExpectedThemedXamlViewCount)
            {
                violations.Add(FitnessRule.CoLocatedPartitionLayout.FormatViolation(
                    $"Expected exactly {ExpectedThemedXamlViewCount} themed .xaml views merging SyntheticThemeDictionary, found {themeMergeCount}."));
            }

            return violations;
        }

        public static IReadOnlyList<string> EvaluateCorePurityAndZeroPreprocessor(
            ArchitectureWorkspaceSnapshot snapshot,
            Assembly coreAssembly,
            Assembly logicTestsAssembly)
        {
            var violations = new List<string>();

            // Synthetic.Core.csproj checks
            WorkspaceXmlFile? coreCsproj = snapshot.ProjectAndBuildFiles
                .FirstOrDefault(f => string.Equals(f.RelativeToRepoPath, "src/Synthetic.Core/Synthetic.Core.csproj", StringComparison.OrdinalIgnoreCase));
            if (coreCsproj == null)
            {
                violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Missing src/Synthetic.Core/Synthetic.Core.csproj."));
            }
            else
            {
                if (coreCsproj.RawText.IndexOf("Autodesk.Revit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Synthetic.Core.csproj must not reference Autodesk.Revit.*."));
                }
                if (coreCsproj.RawText.IndexOf("Revit_All_Main_Versions_API", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Synthetic.Core.csproj must not reference Revit API NuGet packages."));
                }
                if (Regex.IsMatch(coreCsproj.RawText, @"\bREVIT20\d{2}\b", RegexOptions.CultureInvariant))
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Synthetic.Core.csproj must not define REVIT20* constants."));
                }

                bool hasCoreFlag = coreCsproj.Document != null &&
                                   coreCsproj.Document.Descendants("IsCoreProject")
                                       .Any(el => string.Equals(el.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
                if (!hasCoreFlag)
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Synthetic.Core.csproj must declare <IsCoreProject>true</IsCoreProject>."));
                }
            }

            // Synthetic.LogicTests.csproj checks
            WorkspaceXmlFile? logicCsproj = snapshot.ProjectAndBuildFiles
                .FirstOrDefault(f => string.Equals(f.RelativeToRepoPath, "tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj", StringComparison.OrdinalIgnoreCase));
            if (logicCsproj == null)
            {
                violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Missing tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj."));
            }
            else
            {
                if (logicCsproj.RawText.IndexOf("RevitAPIMock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    logicCsproj.RawText.IndexOf("RevitAPIUIMock", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Synthetic.LogicTests.csproj must not reference RevitAPIMock or RevitAPIUIMock."));
                }
                if (Regex.IsMatch(logicCsproj.RawText, @"Synthetic20\d{2}", RegexOptions.CultureInvariant))
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation("Synthetic.LogicTests.csproj must not reference Synthetic20* payload heads."));
                }
            }

            // Compiled Synthetic.Core assembly checks
            if (!string.Equals(coreAssembly.GetName().Name, "Synthetic.Core", StringComparison.Ordinal))
            {
                violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                    $"Expected Synthetic.Core assembly, got '{coreAssembly.GetName().Name}'."));
            }
            else
            {
                foreach (AssemblyName refAsm in coreAssembly.GetReferencedAssemblies())
                {
                    if (string.IsNullOrEmpty(refAsm.Name))
                    {
                        continue;
                    }

                    if (refAsm.Name!.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                        refAsm.Name.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                            $"Synthetic.Core.dll must not reference Revit assembly '{refAsm.FullName}'."));
                    }

                    if (refAsm.Name.IndexOf("Microsoft.Office.Interop.Excel", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                            $"Synthetic.Core.dll must embed Excel interop types instead of referencing '{refAsm.FullName}'."));
                    }
                }

                foreach (string resName in coreAssembly.GetManifestResourceNames())
                {
                    if (resName.EndsWith(".baml", StringComparison.OrdinalIgnoreCase) ||
                        resName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                        resName.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                            $"Synthetic.Core.dll must not embed XAML/BAML resource '{resName}'."));
                    }
                }

                string? coreDir = Path.GetDirectoryName(coreAssembly.Location);
                if (!string.IsNullOrEmpty(coreDir))
                {
                    string emittedExcelDll = Path.Combine(coreDir!, "Microsoft.Office.Interop.Excel.dll");
                    if (File.Exists(emittedExcelDll))
                    {
                        violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                            $"Emitted Microsoft.Office.Interop.Excel.dll must not exist beside Synthetic.Core.dll ('{emittedExcelDll}')."));
                    }
                }
            }

            // Compiled Synthetic.LogicTests assembly check
            foreach (AssemblyName refAsm in logicTestsAssembly.GetReferencedAssemblies())
            {
                if (!string.IsNullOrEmpty(refAsm.Name) &&
                    (refAsm.Name!.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                     refAsm.Name.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                        $"Synthetic.LogicTests.dll must not reference Revit or RevitAPIMock assembly '{refAsm.FullName}'."));
                }
            }

            // Zero XAML inside Core/
            foreach (WorkspaceXamlFile xaml in snapshot.XamlFiles)
            {
                if (xaml.Area == SourceArea.Src && xaml.Partition == PartitionKind.Core)
                {
                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                        $"{xaml.RelativeToRepoPath} must not exist inside Core/."));
                }
            }

            // Source + Test Roslyn AST purity & directive checks
            string forbiddenLatestSymbol = "REVIT" + snapshot.CurrentLatestVersion.ToString(CultureInfo.InvariantCulture);

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                bool isCore = file.Area == SourceArea.Src && file.Partition == PartitionKind.Core;
                bool isPureTest = file.Area == SourceArea.Tests &&
                                  (file.Partition == PartitionKind.Logic || file.Partition == PartitionKind.Shared);

                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    if (isCore)
                    {
                        foreach (TypeReferenceRecord r in variant.TypeReferences)
                        {
                            if (MatchesPrefix(r.NormalizedName, "Autodesk.Revit"))
                            {
                                violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} references Autodesk.Revit inside Core/."));
                            }
                            if (MatchesSymbolName(r.NormalizedName, "ProgressCoordinator"))
                            {
                                violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} references ProgressCoordinator inside Core/."));
                            }
                        }

                        foreach (PreprocessorDirectiveRecord d in variant.IfDirectives)
                        {
                            foreach (string id in d.Identifiers)
                            {
                                if (RevitYearPrefixRegex.IsMatch(id) ||
                                    id.StartsWith("REVIT", StringComparison.OrdinalIgnoreCase))
                                {
                                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                                        $"{file.RelativeToRepoPath}:{d.LineNumber} contains #{d.DirectiveKind} {id} inside Core/ (only standard runtime TFM guards such as #if NETFRAMEWORK are permitted)."));
                                }
                            }
                        }
                    }

                    if (isPureTest)
                    {
                        foreach (TypeReferenceRecord r in variant.TypeReferences)
                        {
                            if (MatchesPrefix(r.NormalizedName, "Autodesk.Revit"))
                            {
                                violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} references Autodesk.Revit inside {file.Partition}/."));
                            }
                        }

                        foreach (PreprocessorDirectiveRecord d in variant.IfDirectives)
                        {
                            foreach (string id in d.Identifiers)
                            {
                                if (RevitYearPrefixRegex.IsMatch(id) ||
                                    id.StartsWith("REVIT", StringComparison.OrdinalIgnoreCase))
                                {
                                    violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                                        $"{file.RelativeToRepoPath}:{d.LineNumber} contains #{d.DirectiveKind} {id} inside {file.Partition}/."));
                                }
                            }
                        }
                    }

                    // Global CURRENT_LATEST_VERSION preprocessor prohibition across ALL src/ and tests/
                    foreach (PreprocessorDirectiveRecord d in variant.IfDirectives)
                    {
                        if (d.Identifiers.Any(id => string.Equals(id, forbiddenLatestSymbol, StringComparison.Ordinal)))
                        {
                            violations.Add(FitnessRule.CorePurityAndZeroPreprocessor.FormatViolation(
                                $"{file.RelativeToRepoPath}:{d.LineNumber} contains forbidden #{d.DirectiveKind} {forbiddenLatestSymbol} (CURRENT_LATEST_VERSION={snapshot.CurrentLatestVersion} must live in the unconditioned #else baseline)."));
                        }
                    }
                }
            }

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> EvaluateFourBandDependencyGraph(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<string>();

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                bool isSrc = file.Area == SourceArea.Src;
                bool isPlatformHost = isSrc &&
                                      string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal) &&
                                      string.Equals(file.SliceName, "Host", StringComparison.Ordinal);

                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    // Retired namespaces across src/ and tests/ (excluding mock stubs if any, though mocks don't use them)
                    foreach (TypeReferenceRecord r in variant.TypeReferences)
                    {
                        string name = r.NormalizedName;
                        if (MatchesPrefix(name, "Synthetic.Modules") ||
                            MatchesPrefix(name, "Synthetic.Shared") ||
                            MatchesPrefix(name, "Synthetic.Infrastructure.Diagnostics") ||
                            MatchesPrefix(name, "Synthetic.Infrastructure.Persistence") ||
                            MatchesPrefix(name, "Synthetic.Settings"))
                        {
                            violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                $"{file.RelativeToRepoPath}:{r.LineNumber} references retired namespace '{name}'."));
                        }
                    }

                    if (!isSrc)
                    {
                        continue;
                    }

                    // Non-Host layers cannot import Synthetic.Platform.Host
                    if (!isPlatformHost && file.Partition != PartitionKind.ProjectHead)
                    {
                        foreach (TypeReferenceRecord r in variant.TypeReferences)
                        {
                            if (MatchesPrefix(r.NormalizedName, "Synthetic.Platform.Host"))
                            {
                                violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} references Synthetic.Platform.Host outside src/Platform/Host/."));
                            }
                        }
                    }

                    // Explicit App.AppControlled ban in Platform/Revit/Addin and ProgressCoordinator.cs
                    bool checkAppControlled =
                        (string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal) &&
                         string.Equals(file.SliceName, "Revit", StringComparison.Ordinal) &&
                         file.Partition == PartitionKind.Addin) ||
                        string.Equals(file.RelativeToRepoPath, "src/Platform/UI/Addin/ProgressCoordinator.cs", StringComparison.OrdinalIgnoreCase);

                    if (checkAppControlled)
                    {
                        foreach (MemberAccessRecord ma in variant.MemberAccesses)
                        {
                            if (string.Equals(ma.NormalizedFullName, "App.AppControlled", StringComparison.Ordinal) ||
                                ma.NormalizedFullName.EndsWith(".App.AppControlled", StringComparison.Ordinal))
                            {
                                violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{ma.LineNumber} references App.AppControlled."));
                            }
                        }
                    }

                    // Band-specific dependency checks
                    if (string.Equals(file.TopFolder, "Features", StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(file.SliceName))
                    {
                        foreach (TypeReferenceRecord r in variant.TypeReferences)
                        {
                            string? refSlice = TryExtractSecondSegment(r.NormalizedName, "Synthetic.Features");
                            if (refSlice != null &&
                                !string.Equals(refSlice, file.SliceName, StringComparison.Ordinal))
                            {
                                violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} imports sibling feature slice Synthetic.Features.{refSlice}."));
                            }
                        }
                    }
                    else if (string.Equals(file.TopFolder, "RevitDOM", StringComparison.Ordinal))
                    {
                        foreach (TypeReferenceRecord r in variant.TypeReferences)
                        {
                            string name = r.NormalizedName;
                            if (MatchesPrefix(name, "Synthetic.Features") ||
                                MatchesPrefix(name, "Synthetic.Domain") ||
                                MatchesPrefix(name, "Synthetic.Platform.Host") ||
                                MatchesPrefix(name, "Synthetic.Platform.UI") ||
                                MatchesPrefix(name, "System.Windows") ||
                                MatchesPrefix(name, "Autodesk.Revit.UI"))
                            {
                                violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} violates RevitDOM band boundary by referencing '{name}'."));
                            }
                        }
                    }
                    else if (string.Equals(file.TopFolder, "Domain", StringComparison.Ordinal))
                    {
                        foreach (TypeReferenceRecord r in variant.TypeReferences)
                        {
                            string name = r.NormalizedName;
                            if (MatchesPrefix(name, "Synthetic.Features") ||
                                MatchesPrefix(name, "Synthetic.RevitDOM") ||
                                MatchesPrefix(name, "Synthetic.Platform.Host") ||
                                MatchesPrefix(name, "Autodesk.Revit"))
                            {
                                violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                    $"{file.RelativeToRepoPath}:{r.LineNumber} violates Domain band boundary by referencing '{name}'."));
                            }
                        }
                    }
                    else if (string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal))
                    {
                        if (string.Equals(file.SliceName, "IO", StringComparison.Ordinal))
                        {
                            foreach (TypeReferenceRecord r in variant.TypeReferences)
                            {
                                string name = r.NormalizedName;
                                if (MatchesPrefix(name, "Synthetic.Features") ||
                                    MatchesPrefix(name, "Synthetic.RevitDOM") ||
                                    MatchesPrefix(name, "Synthetic.Domain") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Host") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Settings") ||
                                    MatchesPrefix(name, "Synthetic.Platform.UI") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Revit") ||
                                    MatchesPrefix(name, "Autodesk.Revit") ||
                                    MatchesPrefix(name, "System.Windows.Forms"))
                                {
                                    violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                        $"{file.RelativeToRepoPath}:{r.LineNumber} violates Platform/IO band boundary by referencing '{name}'."));
                                }
                            }
                        }
                        else if (string.Equals(file.SliceName, "Settings", StringComparison.Ordinal))
                        {
                            foreach (TypeReferenceRecord r in variant.TypeReferences)
                            {
                                string name = r.NormalizedName;
                                if (MatchesPrefix(name, "Synthetic.Features") ||
                                    MatchesPrefix(name, "Synthetic.RevitDOM") ||
                                    MatchesPrefix(name, "Synthetic.Domain") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Host") ||
                                    MatchesPrefix(name, "Synthetic.Platform.UI") ||
                                    MatchesSymbolName(name, "ProgressCoordinator"))
                                {
                                    violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                        $"{file.RelativeToRepoPath}:{r.LineNumber} violates Platform/Settings band boundary by referencing '{name}'."));
                                }
                            }
                        }
                        else if (string.Equals(file.SliceName, "UI", StringComparison.Ordinal))
                        {
                            foreach (TypeReferenceRecord r in variant.TypeReferences)
                            {
                                string name = r.NormalizedName;
                                if (MatchesPrefix(name, "Synthetic.Features") ||
                                    MatchesPrefix(name, "Synthetic.RevitDOM") ||
                                    MatchesPrefix(name, "Synthetic.Domain") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Host") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Settings") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Revit") ||
                                    (file.Partition == PartitionKind.Core &&
                                     (MatchesPrefix(name, "System.Windows.Forms") || MatchesPrefix(name, "Autodesk.Revit"))))
                                {
                                    violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                        $"{file.RelativeToRepoPath}:{r.LineNumber} violates Platform/UI band boundary by referencing '{name}'."));
                                }
                            }
                        }
                        else if (string.Equals(file.SliceName, "Revit", StringComparison.Ordinal))
                        {
                            foreach (TypeReferenceRecord r in variant.TypeReferences)
                            {
                                string name = r.NormalizedName;
                                if (MatchesPrefix(name, "Synthetic.Features") ||
                                    MatchesPrefix(name, "Synthetic.RevitDOM") ||
                                    MatchesPrefix(name, "Synthetic.Domain") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Host") ||
                                    MatchesPrefix(name, "Synthetic.Platform.UI") ||
                                    MatchesPrefix(name, "Synthetic.Platform.Settings"))
                                {
                                    violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                        $"{file.RelativeToRepoPath}:{r.LineNumber} violates Platform/Revit band boundary by referencing '{name}'."));
                                }
                            }
                        }
                    }
                }
            }

            // XAML xmlns: checks
            foreach (WorkspaceXamlFile xaml in snapshot.XamlFiles)
            {
                if (xaml.Area != SourceArea.Src)
                {
                    continue;
                }

                bool isPlatformHost = string.Equals(xaml.TopFolder, "Platform", StringComparison.Ordinal) &&
                                      string.Equals(xaml.SliceName, "Host", StringComparison.Ordinal);

                foreach (XamlNamespaceBinding binding in xaml.XmlnsBindings)
                {
                    string? ns = binding.ClrNamespace;
                    if (string.IsNullOrEmpty(ns))
                    {
                        continue;
                    }

                    if (MatchesPrefix(ns!, "Synthetic.Modules") ||
                        MatchesPrefix(ns!, "Synthetic.Shared") ||
                        MatchesPrefix(ns!, "Synthetic.Infrastructure.Diagnostics") ||
                        MatchesPrefix(ns!, "Synthetic.Infrastructure.Persistence") ||
                        MatchesPrefix(ns!, "Synthetic.Settings"))
                    {
                        violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                            $"{xaml.RelativeToRepoPath} XAML xmlns:{binding.Prefix} references retired namespace '{ns}'."));
                    }

                    if (!isPlatformHost && MatchesPrefix(ns!, "Synthetic.Platform.Host"))
                    {
                        violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                            $"{xaml.RelativeToRepoPath} XAML xmlns:{binding.Prefix} references Synthetic.Platform.Host outside Platform/Host."));
                    }

                    if (string.Equals(xaml.TopFolder, "Features", StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(xaml.SliceName))
                    {
                        string? refSlice = TryExtractSecondSegment(ns!, "Synthetic.Features");
                        if (refSlice != null && !string.Equals(refSlice, xaml.SliceName, StringComparison.Ordinal))
                        {
                            violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                $"{xaml.RelativeToRepoPath} XAML xmlns:{binding.Prefix} imports sibling feature slice Synthetic.Features.{refSlice}."));
                        }
                    }
                    else if (string.Equals(xaml.TopFolder, "Platform", StringComparison.Ordinal) &&
                             string.Equals(xaml.SliceName, "UI", StringComparison.Ordinal))
                    {
                        if (MatchesPrefix(ns!, "Synthetic.Features") ||
                            MatchesPrefix(ns!, "Synthetic.RevitDOM") ||
                            MatchesPrefix(ns!, "Synthetic.Domain") ||
                            MatchesPrefix(ns!, "Synthetic.Platform.Host") ||
                            MatchesPrefix(ns!, "Synthetic.Platform.Settings") ||
                            MatchesPrefix(ns!, "Synthetic.Platform.Revit"))
                        {
                            violations.Add(FitnessRule.FourBandDependencyGraph.FormatViolation(
                                $"{xaml.RelativeToRepoPath} XAML xmlns:{binding.Prefix} violates Platform/UI boundary by referencing '{ns}'."));
                        }
                    }
                }
            }

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> EvaluateDomainGraduationThreshold(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<string>();
            string domainDir = Path.Combine(snapshot.SrcRoot, "Domain");
            var domainFiles = snapshot.SourceFiles
                .Where(f => f.Area == SourceArea.Src && string.Equals(f.TopFolder, "Domain", StringComparison.Ordinal))
                .ToList();

            bool diskDomainExists = Directory.Exists(domainDir);
            if (!diskDomainExists && domainFiles.Count == 0)
            {
                return violations;
            }

            var subModules = new HashSet<string>(StringComparer.Ordinal);
            if (diskDomainExists)
            {
                foreach (string subDir in Directory.EnumerateDirectories(domainDir))
                {
                    subModules.Add(Path.GetFileName(subDir));
                }
            }

            foreach (WorkspaceSourceFile df in domainFiles)
            {
                if (!string.IsNullOrEmpty(df.SliceName))
                {
                    subModules.Add(df.SliceName!);
                }
            }

            if (subModules.Count < 2)
            {
                violations.Add(FitnessRule.DomainGraduationThreshold.FormatViolation(
                    $"src/Domain/ exists but defines {subModules.Count} sub-module(s); graduation requires >= 2 sub-modules."));
            }

            // Enforce zero sibling Domain.<A> -> Domain.<B> imports
            foreach (WorkspaceSourceFile df in domainFiles)
            {
                if (string.IsNullOrEmpty(df.SliceName))
                {
                    continue;
                }

                foreach (ParsedSyntaxVariant variant in df.Variants)
                {
                    foreach (TypeReferenceRecord r in variant.TypeReferences)
                    {
                        string? refSub = TryExtractSecondSegment(r.NormalizedName, "Synthetic.Domain");
                        if (refSub != null && !string.Equals(refSub, df.SliceName, StringComparison.Ordinal))
                        {
                            violations.Add(FitnessRule.DomainGraduationThreshold.FormatViolation(
                                $"{df.RelativeToRepoPath}:{r.LineNumber} imports sibling Domain sub-module Synthetic.Domain.{refSub}."));
                        }
                    }
                }
            }

            // Enforce each sub-module is referenced by >= 2 distinct Features/<Slice> modules
            var consumersBySubModule = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string sub in subModules)
            {
                consumersBySubModule[sub] = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (WorkspaceSourceFile featureFile in snapshot.SourceFiles)
            {
                if (featureFile.Area != SourceArea.Src ||
                    !string.Equals(featureFile.TopFolder, "Features", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(featureFile.SliceName))
                {
                    continue;
                }

                foreach (ParsedSyntaxVariant variant in featureFile.Variants)
                {
                    foreach (TypeReferenceRecord r in variant.TypeReferences)
                    {
                        string? refSub = TryExtractSecondSegment(r.NormalizedName, "Synthetic.Domain");
                        if (refSub != null && consumersBySubModule.TryGetValue(refSub, out HashSet<string>? consumers))
                        {
                            consumers.Add(featureFile.SliceName!);
                        }
                    }
                }
            }

            foreach (WorkspaceXamlFile featureXaml in snapshot.XamlFiles)
            {
                if (featureXaml.Area != SourceArea.Src ||
                    !string.Equals(featureXaml.TopFolder, "Features", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(featureXaml.SliceName))
                {
                    continue;
                }

                foreach (XamlNamespaceBinding binding in featureXaml.XmlnsBindings)
                {
                    if (string.IsNullOrEmpty(binding.ClrNamespace))
                    {
                        continue;
                    }

                    string? refSub = TryExtractSecondSegment(binding.ClrNamespace!, "Synthetic.Domain");
                    if (refSub != null && consumersBySubModule.TryGetValue(refSub, out HashSet<string>? consumers))
                    {
                        consumers.Add(featureXaml.SliceName!);
                    }
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> kvp in consumersBySubModule.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kvp.Value.Count < 2)
                {
                    violations.Add(FitnessRule.DomainGraduationThreshold.FormatViolation(
                        $"Domain sub-module '{kvp.Key}' is referenced by {kvp.Value.Count} feature slice(s) ({string.Join(", ", kvp.Value)}); graduation requires >= 2 distinct slices."));
                }
            }

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> EvaluateRibbonConfigIntegrity(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<string>();
            RibbonConfigSnapshot ribbon = snapshot.RibbonConfig;

            var commandTypes = new HashSet<string>(StringComparer.Ordinal);
            var availabilityTypes = new HashSet<string>(StringComparer.Ordinal);

            foreach (TypeEntry entry in snapshot.AllSrcTypes)
            {
                if (entry.File.Partition != PartitionKind.Addin ||
                    !string.Equals(entry.Type.Kind, "class", StringComparison.Ordinal) ||
                    entry.Type.IsAbstract ||
                    entry.Type.IsStatic)
                {
                    continue;
                }

                if (ImplementsOrInherits(entry, "Autodesk.Revit.UI.IExternalCommand", "IExternalCommand", "Autodesk.Revit.UI", snapshot.SrcTypesByFullName))
                {
                    commandTypes.Add(entry.Type.FullName);
                }

                if (ImplementsOrInherits(entry, "Autodesk.Revit.UI.IExternalCommandAvailability", "IExternalCommandAvailability", "Autodesk.Revit.UI", snapshot.SrcTypesByFullName))
                {
                    availabilityTypes.Add(entry.Type.FullName);
                }
            }

            if (commandTypes.Count == 0)
            {
                violations.Add(FitnessRule.RibbonConfigIntegrity.FormatViolation(
                    "Roslyn discovered zero concrete IExternalCommand classes in src/**/Addin/**."));
            }

            var classEntries = ribbon.Entries.Where(e => !string.IsNullOrWhiteSpace(e.ClassName)).ToList();
            if (classEntries.Count == 0)
            {
                violations.Add(FitnessRule.RibbonConfigIntegrity.FormatViolation(
                    "ribbon_config.json defines zero command class entries."));
            }

            foreach (RibbonEntryRecord item in ribbon.Entries)
            {
                if (!string.IsNullOrWhiteSpace(item.ClassName) && !commandTypes.Contains(item.ClassName!))
                {
                    violations.Add(FitnessRule.RibbonConfigIntegrity.FormatViolation(
                        $"ribbon_config.json command class '{item.ClassName}' did not resolve via Roslyn to a concrete IExternalCommand in src/**/Addin/**."));
                }

                if (!string.IsNullOrWhiteSpace(item.AvailabilityClass) && !availabilityTypes.Contains(item.AvailabilityClass!))
                {
                    violations.Add(FitnessRule.RibbonConfigIntegrity.FormatViolation(
                        $"ribbon_config.json availability_class '{item.AvailabilityClass}' did not resolve via Roslyn to a concrete IExternalCommandAvailability in src/**/Addin/**."));
                }

                ValidateRibbonIcon(item.Image, "image", ribbon.AssetRelativePaths, violations);
                ValidateRibbonIcon(item.LargeImage, "large_image", ribbon.AssetRelativePaths, violations);
            }

            ValidateRibbonIcon("placeholder_16.png", "fallback", ribbon.AssetRelativePaths, violations);
            ValidateRibbonIcon("placeholder_32.png", "fallback", ribbon.AssetRelativePaths, violations);

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> EvaluateSettingsRegistryTemplateParity(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<string>();
            var discoveredModules = new Dictionary<string, string>(StringComparer.Ordinal);
            string? syncSettingsKey = null;

            foreach (TypeEntry entry in snapshot.AllSrcTypes)
            {
                if ((entry.File.Partition != PartitionKind.Core && entry.File.Partition != PartitionKind.Addin) ||
                    !string.Equals(entry.Type.Kind, "class", StringComparison.Ordinal) ||
                    entry.Type.IsAbstract ||
                    entry.Type.IsStatic)
                {
                    continue;
                }

                if (!ImplementsOrInherits(
                        entry,
                        "Synthetic.Platform.Settings.ISettingModule",
                        "ISettingModule",
                        "Synthetic.Platform.Settings",
                        snapshot.SrcTypesByFullName))
                {
                    continue;
                }

                PropertyDeclarationRecord? moduleKeyProp = entry.Type.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, "ModuleKey", StringComparison.Ordinal));
                string? resolvedKey = snapshot.ConstantResolver.TryResolveString(
                    moduleKeyProp?.GetterExpression,
                    entry,
                    entry.Variant);

                if (string.IsNullOrWhiteSpace(resolvedKey))
                {
                    violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                        $"Concrete ISettingModule '{entry.Type.FullName}' ({entry.File.RelativeToRepoPath}) has an unresolvable or blank ModuleKey."));
                    continue;
                }

                if (string.Equals(entry.Type.FullName, "Synthetic.Platform.Settings.SyncSettings", StringComparison.Ordinal))
                {
                    syncSettingsKey = resolvedKey;
                    continue;
                }

                if (discoveredModules.TryGetValue(resolvedKey!, out string? existingType) &&
                    !string.Equals(existingType, entry.Type.FullName, StringComparison.Ordinal))
                {
                    violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                        $"Duplicate ModuleKey '{resolvedKey}' declared by both '{existingType}' and '{entry.Type.FullName}'."));
                }
                else
                {
                    discoveredModules[resolvedKey!] = entry.Type.FullName;
                }
            }

            if (string.IsNullOrEmpty(syncSettingsKey))
            {
                violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                    "Expected SyncSettings to implement ISettingModule with a resolvable ModuleKey constant (SettingsModuleKeys.Sync)."));
            }

            // Parse HostSettingsRegistrar registrations via Roslyn
            var registrarKeys = new HashSet<string>(StringComparer.Ordinal);
            if (!snapshot.SrcTypesByFullName.TryGetValue("Synthetic.Platform.Host.HostSettingsRegistrar", out List<TypeEntry>? registrarEntries) ||
                registrarEntries.Count == 0)
            {
                violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                    "Expected Synthetic.Platform.Host.HostSettingsRegistrar in src/Platform/Host/Addin/HostSettingsRegistrar.cs."));
            }
            else
            {
                foreach (TypeEntry registrar in registrarEntries)
                {
                    foreach (InvocationRecord inv in registrar.Variant.Invocations)
                    {
                        if (!string.Equals(inv.MethodName, "Register", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!string.Equals(inv.NormalizedTarget, "SettingsModuleRegistry.Register", StringComparison.Ordinal) &&
                            !string.Equals(inv.NormalizedTarget, "Synthetic.Platform.Settings.SettingsModuleRegistry.Register", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var args = inv.Node.ArgumentList?.Arguments;
                        if (args == null || args.Value.Count == 0)
                        {
                            continue;
                        }

                        ExpressionSyntax keyExpr = args.Value[0].Expression;
                        if (keyExpr is ObjectCreationExpressionSyntax descriptorCreate &&
                            descriptorCreate.ArgumentList?.Arguments.Count > 0)
                        {
                            keyExpr = descriptorCreate.ArgumentList.Arguments[0].Expression;
                        }
                        else if (keyExpr is ImplicitObjectCreationExpressionSyntax implicitDescriptorCreate &&
                                 implicitDescriptorCreate.ArgumentList?.Arguments.Count > 0)
                        {
                            keyExpr = implicitDescriptorCreate.ArgumentList.Arguments[0].Expression;
                        }

                        string? regKey = snapshot.ConstantResolver.TryResolveString(keyExpr, registrar, registrar.Variant);
                        if (string.IsNullOrWhiteSpace(regKey))
                        {
                            violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                                $"{registrar.File.RelativeToRepoPath}:{inv.LineNumber} has unresolvable SettingsModuleRegistry.Register key."));
                        }
                        else if (!registrarKeys.Add(regKey!))
                        {
                            violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                                $"{registrar.File.RelativeToRepoPath}:{inv.LineNumber} registers duplicate ModuleKey '{regKey}'."));
                        }
                    }
                }
            }

            // Template JSON keys
            var templateKeys = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot.SettingsTemplate.Root["Settings"] == null)
            {
                violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                    "SyntheticSettings.template.json must contain a top-level 'Settings' object."));
            }
            else
            {
                foreach (string key in snapshot.SettingsTemplate.SettingsKeys)
                {
                    if (!templateKeys.Add(key))
                    {
                        violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                            $"SyntheticSettings.template.json contains duplicate Settings key '{key}'."));
                    }
                }
            }

            if (!string.IsNullOrEmpty(syncSettingsKey))
            {
                if (registrarKeys.Contains(syncSettingsKey!))
                {
                    violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                        $"HostSettingsRegistrar must exclude SyncSettings ('{syncSettingsKey}')."));
                }
                if (templateKeys.Contains(syncSettingsKey!))
                {
                    violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                        $"SyntheticSettings.template.json must exclude SyncSettings ('{syncSettingsKey}')."));
                }
            }

            // 1-to-1 parity across definitions, HostSettingsRegistrar, and template JSON
            var definitionKeys = new HashSet<string>(discoveredModules.Keys, StringComparer.Ordinal);
            CompareKeySets("ISettingModule definitions", definitionKeys, "HostSettingsRegistrar", registrarKeys, violations);
            CompareKeySets("ISettingModule definitions", definitionKeys, "SyntheticSettings.template.json", templateKeys, violations);
            CompareKeySets("HostSettingsRegistrar", registrarKeys, "SyntheticSettings.template.json", templateKeys, violations);

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> EvaluateTestCategoryAndLegacyMockRatchet(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<string>();

            // 1. Every non-abstract test-containing class must carry/inherit a canonical typed [Category]
            //    OR decorate every declared test method with one.
            var checkedTestClasses = new HashSet<string>(StringComparer.Ordinal);
            foreach (TypeEntry entry in snapshot.AllTestTypes)
            {
                if (entry.File.Partition == PartitionKind.Mock ||
                    !string.Equals(entry.Type.Kind, "class", StringComparison.Ordinal) ||
                    entry.Type.IsAbstract)
                {
                    continue;
                }

                bool hasFixtureAttr = entry.Type.Attributes.Any(a => IsTestFixtureAttribute(a));
                List<MethodDeclarationRecord> testMethods = entry.Type.Methods
                    .Where(m => m.Attributes.Any(a => IsTestMethodAttribute(a)))
                    .ToList();

                if (!hasFixtureAttr && testMethods.Count == 0)
                {
                    continue;
                }

                string dedupKey = entry.File.RelativeToRepoPath + "::" + entry.Type.FullName;
                if (!checkedTestClasses.Add(dedupKey))
                {
                    continue;
                }

                bool hasOrInheritsClassCategory = HasOrInheritsCanonicalCategory(entry, snapshot.TestTypesByFullName, new HashSet<string>(StringComparer.Ordinal));
                if (hasOrInheritsClassCategory)
                {
                    continue;
                }

                bool everyTestMethodCategorized = testMethods.Count > 0 &&
                    testMethods.All(m => m.Attributes.Any(a => IsCanonicalCategoryAttribute(a)));

                if (!everyTestMethodCategorized)
                {
                    violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                        $"{entry.File.RelativeToRepoPath}:{entry.Type.LineNumber} class '{entry.Type.FullName}' must carry/inherit a canonical typed [Category] attribute ([UnitTest], [ArchitectureTest], [RevitSmokeTest], [RevitAdapterTest], [RevitIsolatedDocTest], or [QuarantineTest]) or decorate every test method with one."));
                }
            }

            // 2. Zero Tier1_* / Tier2_* files or types, and zero retired helpers/APIs across src/ and tests/
            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                string fileNameNoExt = Path.GetFileNameWithoutExtension(file.FullPath);
                if (fileNameNoExt.StartsWith("Tier1_", StringComparison.OrdinalIgnoreCase) ||
                    fileNameNoExt.StartsWith("Tier2_", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                        $"Retired Tier1_*/Tier2_* test file name is forbidden: {file.RelativeToRepoPath}"));
                }

                if (file.Partition == PartitionKind.Mock)
                {
                    continue;
                }

                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    foreach (TypeDeclarationRecord type in variant.Types)
                    {
                        if (type.Name.StartsWith("Tier1_", StringComparison.OrdinalIgnoreCase) ||
                            type.Name.StartsWith("Tier2_", StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                                $"{file.RelativeToRepoPath}:{type.LineNumber} declares retired Tier1_*/Tier2_* type '{type.Name}'."));
                        }
                    }

                    foreach (TypeReferenceRecord r in variant.TypeReferences)
                    {
                        if (MatchesSymbolName(r.NormalizedName, "MockFailureFactory"))
                        {
                            violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                                $"{file.RelativeToRepoPath}:{r.LineNumber} references retired MockFailureFactory."));
                        }
                        if (MatchesSymbolName(r.NormalizedName, "ConfigCollection"))
                        {
                            violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                                $"{file.RelativeToRepoPath}:{r.LineNumber} references retired ConfigCollection."));
                        }
                    }

                    foreach (MemberAccessRecord ma in variant.MemberAccesses)
                    {
                        if (string.Equals(ma.NormalizedFullName, "App.Configurations", StringComparison.Ordinal) ||
                            ma.NormalizedFullName.EndsWith(".App.Configurations", StringComparison.Ordinal))
                        {
                            violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                                $"{file.RelativeToRepoPath}:{ma.LineNumber} references retired App.Configurations."));
                        }
                    }

                    foreach (InvocationRecord inv in variant.Invocations)
                    {
                        if (string.Equals(inv.MethodName, "DeleteWarnings", StringComparison.Ordinal) ||
                            string.Equals(inv.MethodName, "ReadProjectConfig", StringComparison.Ordinal) ||
                            string.Equals(inv.MethodName, "WriteProjectConfig", StringComparison.Ordinal))
                        {
                            violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                                $"{file.RelativeToRepoPath}:{inv.LineNumber} invokes retired helper '{inv.MethodName}()'."));
                        }
                    }
                }
            }

            foreach (WorkspaceXmlFile xmlFile in snapshot.ProjectAndBuildFiles)
            {
                if (!string.Equals(xmlFile.Extension, ".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string forbiddenMock = "Mock" + "FailureFactory";
                string forbiddenDeleteWarnings = "Delete" + "Warnings(";
                if (xmlFile.RawText.IndexOf(forbiddenMock, StringComparison.Ordinal) >= 0)
                {
                    violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                        $"{xmlFile.RelativeToRepoPath} references retired {forbiddenMock}."));
                }
                if (xmlFile.RawText.IndexOf(forbiddenDeleteWarnings, StringComparison.Ordinal) >= 0)
                {
                    violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                        $"{xmlFile.RelativeToRepoPath} references retired {forbiddenDeleteWarnings}."));
                }
            }

            // 3. Exact Synthetic.LegacyMockTests .cs file count ratchet
            int legacyMockCount = snapshot.SourceFiles
                .Count(f => f.Area == SourceArea.Tests && f.IsInLegacyMockTests);
            if (legacyMockCount != ExpectedLegacyMockTestFileCount)
            {
                string guidance = legacyMockCount > ExpectedLegacyMockTestFileCount
                    ? "New quarantined tests must not be added to Synthetic.LegacyMockTests."
                    : "Lower ArchitectureFitnessRules.ExpectedLegacyMockTestFileCount to ratchet the ceiling monotonically.";
                violations.Add(FitnessRule.TestCategoryAndLegacyMockRatchet.FormatViolation(
                    $"Expected exactly {ExpectedLegacyMockTestFileCount} .cs files in tests/Projects/Synthetic.LegacyMockTests/, found {legacyMockCount}. {guidance}"));
            }

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        private static bool IsValidSrcProjectHeadFolder(string folder, int currentLatestVersion)
        {
            if (string.Equals(folder, "Synthetic.Core", StringComparison.Ordinal) ||
                string.Equals(folder, "Synthetic.Bootstrap", StringComparison.Ordinal))
            {
                return true;
            }

            Match bootstrapMatch = DynamicBootstrapYearRegex.Match(folder);
            if (bootstrapMatch.Success && bootstrapMatch.Groups[1].Success)
            {
                int year = int.Parse(bootstrapMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                return year >= 2023 && year <= currentLatestVersion;
            }

            Match headMatch = DynamicYearHeadRegex.Match(folder);
            if (headMatch.Success)
            {
                int year = int.Parse(headMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                return year >= 2023 && year <= currentLatestVersion;
            }

            return false;
        }

        private static bool IsValidTestProjectFolder(string folder, int currentLatestVersion)
        {
            if (string.Equals(folder, "Synthetic.LogicTests", StringComparison.Ordinal) ||
                string.Equals(folder, "Synthetic.LegacyMockTests", StringComparison.Ordinal))
            {
                return true;
            }

            Match revitTestMatch = DynamicRevitTestProjectRegex.Match(folder);
            if (revitTestMatch.Success)
            {
                int year = int.Parse(revitTestMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                return year >= 2023 && year <= currentLatestVersion;
            }

            return false;
        }

        private static bool MatchesPrefix(string value, string prefix)
        {
            return string.Equals(value, prefix, StringComparison.Ordinal) ||
                   value.StartsWith(prefix + ".", StringComparison.Ordinal);
        }

        private static bool MatchesSymbolName(string value, string symbol)
        {
            return string.Equals(value, symbol, StringComparison.Ordinal) ||
                   value.StartsWith(symbol + ".", StringComparison.Ordinal) ||
                   value.EndsWith("." + symbol, StringComparison.Ordinal) ||
                   value.IndexOf("." + symbol + ".", StringComparison.Ordinal) >= 0;
        }

        private static IEnumerable<string> EnumerateNonGitFiles(string rootDir, string searchPattern)
        {
            if (!Directory.Exists(rootDir))
            {
                yield break;
            }

            var stack = new Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                foreach (string sub in Directory.EnumerateDirectories(current))
                {
                    string name = Path.GetFileName(sub);
                    if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    stack.Push(sub);
                }

                foreach (string file in Directory.EnumerateFiles(current, searchPattern))
                {
                    yield return file;
                }
            }
        }

        private static string? TryExtractSecondSegment(string fullName, string rootPrefix)
        {
            string expectedStart = rootPrefix + ".";
            if (!fullName.StartsWith(expectedStart, StringComparison.Ordinal))
            {
                return null;
            }

            string remainder = fullName.Substring(expectedStart.Length);
            if (string.IsNullOrEmpty(remainder))
            {
                return null;
            }

            int dot = remainder.IndexOf('.');
            return dot < 0 ? remainder : remainder.Substring(0, dot);
        }

        private static bool ImplementsOrInherits(
            TypeEntry typeEntry,
            string expectedFullInterfaceName,
            string expectedSimpleInterfaceName,
            string expectedNamespace,
            IReadOnlyDictionary<string, List<TypeEntry>> typesByFullName)
        {
            return MatchesBaseTypePredicate(
                typeEntry,
                typesByFullName,
                (current, baseType) =>
                {
                    if (string.Equals(baseType, expectedFullInterfaceName, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    bool hasExpectedNamespaceScope =
                        string.Equals(current.Type.Namespace, expectedNamespace, StringComparison.Ordinal) ||
                        current.Variant.Usings.Any(u => string.Equals(u.NormalizedTarget, expectedNamespace, StringComparison.Ordinal));

                    return hasExpectedNamespaceScope &&
                           string.Equals(baseType, expectedSimpleInterfaceName, StringComparison.Ordinal);
                });
        }

        private static bool MatchesBaseTypePredicate(
            TypeEntry typeEntry,
            IReadOnlyDictionary<string, List<TypeEntry>> typesByFullName,
            Func<TypeEntry, string, bool> predicate)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            return MatchesBaseTypePredicateInternal(typeEntry, typesByFullName, predicate, visited);
        }

        private static bool MatchesBaseTypePredicateInternal(
            TypeEntry typeEntry,
            IReadOnlyDictionary<string, List<TypeEntry>> typesByFullName,
            Func<TypeEntry, string, bool> predicate,
            HashSet<string> visited)
        {
            if (!visited.Add(typeEntry.Type.FullName))
            {
                return false;
            }

            foreach (string baseType in typeEntry.Type.NormalizedBaseTypes)
            {
                if (predicate(typeEntry, baseType))
                {
                    return true;
                }

                if (typesByFullName.TryGetValue(baseType, out List<TypeEntry>? baseCandidates))
                {
                    foreach (TypeEntry baseEntry in baseCandidates)
                    {
                        if (MatchesBaseTypePredicateInternal(baseEntry, typesByFullName, predicate, visited))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(typeEntry.Type.Namespace))
                    {
                        string sameNs = typeEntry.Type.Namespace + "." + baseType;
                        if (typesByFullName.TryGetValue(sameNs, out List<TypeEntry>? sameNsCandidates))
                        {
                            foreach (TypeEntry baseEntry in sameNsCandidates)
                            {
                                if (MatchesBaseTypePredicateInternal(baseEntry, typesByFullName, predicate, visited))
                                {
                                    return true;
                                }
                            }
                        }
                    }

                    foreach (UsingRecord u in typeEntry.Variant.Usings)
                    {
                        if (!string.IsNullOrEmpty(u.Alias))
                        {
                            continue;
                        }

                        string candidate = u.NormalizedTarget + "." + baseType;
                        if (typesByFullName.TryGetValue(candidate, out List<TypeEntry>? usingCandidates))
                        {
                            foreach (TypeEntry baseEntry in usingCandidates)
                            {
                                if (MatchesBaseTypePredicateInternal(baseEntry, typesByFullName, predicate, visited))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static void ValidateRibbonIcon(
            string? iconName,
            string fieldLabel,
            IReadOnlyCollection<string> assetPaths,
            List<string> violations)
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return;
            }

            string normalized = iconName!.Replace('\\', '/').TrimStart('/');
            if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(FitnessRule.RibbonConfigIntegrity.FormatViolation(
                    $"ribbon_config.json {fieldLabel} '{iconName}' must reference a .png asset."));
                return;
            }

            if (!assetPaths.Contains(normalized))
            {
                violations.Add(FitnessRule.RibbonConfigIntegrity.FormatViolation(
                    $"ribbon_config.json {fieldLabel} asset 'src/Assets/{normalized}' does not exist."));
            }
        }

        private static void CompareKeySets(
            string leftName,
            HashSet<string> leftKeys,
            string rightName,
            HashSet<string> rightKeys,
            List<string> violations)
        {
            var missingInRight = leftKeys.Except(rightKeys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (missingInRight.Count > 0)
            {
                violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                    $"{leftName} contains key(s) missing from {rightName}: {string.Join(", ", missingInRight)}."));
            }

            var extraInRight = rightKeys.Except(leftKeys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (extraInRight.Count > 0)
            {
                violations.Add(FitnessRule.SettingsRegistryTemplateParity.FormatViolation(
                    $"{rightName} contains key(s) missing from {leftName}: {string.Join(", ", extraInRight)}."));
            }
        }

        private static bool HasOrInheritsCanonicalCategory(
            TypeEntry entry,
            IReadOnlyDictionary<string, List<TypeEntry>> testTypesByFullName,
            HashSet<string> visited)
        {
            if (!visited.Add(entry.Type.FullName))
            {
                return false;
            }

            if (entry.Type.Attributes.Any(IsCanonicalCategoryAttribute))
            {
                return true;
            }

            foreach (string baseType in entry.Type.NormalizedBaseTypes)
            {
                foreach (TypeEntry baseEntry in ResolveTestBaseEntries(baseType, entry, testTypesByFullName))
                {
                    if (HasOrInheritsCanonicalCategory(baseEntry, testTypesByFullName, visited))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<TypeEntry> ResolveTestBaseEntries(
            string baseType,
            TypeEntry derived,
            IReadOnlyDictionary<string, List<TypeEntry>> testTypesByFullName)
        {
            if (testTypesByFullName.TryGetValue(baseType, out List<TypeEntry>? exact))
            {
                return exact;
            }

            if (!string.IsNullOrEmpty(derived.Type.Namespace))
            {
                string sameNs = derived.Type.Namespace + "." + baseType;
                if (testTypesByFullName.TryGetValue(sameNs, out List<TypeEntry>? inNs))
                {
                    return inNs;
                }
            }

            foreach (UsingRecord u in derived.Variant.Usings)
            {
                if (!string.IsNullOrEmpty(u.Alias))
                {
                    continue;
                }

                string candidate = u.NormalizedTarget + "." + baseType;
                if (testTypesByFullName.TryGetValue(candidate, out List<TypeEntry>? viaUsing))
                {
                    return viaUsing;
                }
            }

            return Enumerable.Empty<TypeEntry>();
        }

        private static bool IsCanonicalCategoryAttribute(AttributeRecord attr)
        {
            return CanonicalCategoryAttributeNames.Contains(attr.NormalizedName) ||
                   CanonicalCategoryAttributeNames.Contains(attr.RawName);
        }

        private static bool IsTestFixtureAttribute(AttributeRecord attr)
        {
            return TestFixtureAttributeNames.Contains(attr.NormalizedName) ||
                   TestFixtureAttributeNames.Contains(attr.RawName);
        }

        private static bool IsTestMethodAttribute(AttributeRecord attr)
        {
            return TestMethodAttributeNames.Contains(attr.NormalizedName) ||
                   TestMethodAttributeNames.Contains(attr.RawName);
        }

        // =====================================================================
        // Rules 8-12: Type-Level Bi-Directionally Auto-Ratcheted Gates
        // =====================================================================

        public static IReadOnlyList<string> EvaluateInteractiveSliceSeamPurity(
            ArchitectureWorkspaceSnapshot snapshot,
            IReadOnlyList<AllowlistEntry>? allowlist = null)
        {
            return AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.InteractiveSliceSeamPurity,
                CollectInteractiveSliceSeamPurityViolations(snapshot),
                allowlist ?? ArchitectureAllowlists.AllEntries);
        }

        public static IReadOnlyList<string> EvaluateTransactionPlacement(
            ArchitectureWorkspaceSnapshot snapshot,
            IReadOnlyList<AllowlistEntry>? allowlist = null)
        {
            return AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionPlacement,
                CollectTransactionPlacementViolations(snapshot),
                allowlist ?? ArchitectureAllowlists.AllEntries);
        }

        public static IReadOnlyList<string> EvaluateTransactionFailureGuard(
            ArchitectureWorkspaceSnapshot snapshot,
            IReadOnlyList<AllowlistEntry>? allowlist = null)
        {
            return AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.TransactionFailureGuard,
                CollectTransactionFailureGuardViolations(snapshot),
                allowlist ?? ArchitectureAllowlists.AllEntries);
        }

        public static IReadOnlyList<string> EvaluateExternalEventAndDialogPurity(
            ArchitectureWorkspaceSnapshot snapshot,
            IReadOnlyList<AllowlistEntry>? allowlist = null)
        {
            return AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.ExternalEventAndDialogPurity,
                CollectExternalEventAndDialogPurityViolations(snapshot),
                allowlist ?? ArchitectureAllowlists.AllEntries);
        }

        public static IReadOnlyList<string> EvaluateSingleSchemaExtensibleStorage(
            ArchitectureWorkspaceSnapshot snapshot,
            IReadOnlyList<AllowlistEntry>? allowlist = null)
        {
            return AllowlistRatchetEvaluator.Evaluate(
                FitnessRule.SingleSchemaExtensibleStorage,
                CollectSingleSchemaExtensibleStorageViolations(snapshot),
                allowlist ?? ArchitectureAllowlists.AllEntries);
        }

        public static IReadOnlyList<TypeLevelViolation> CollectInteractiveSliceSeamPurityViolations(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<TypeLevelViolation>();

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                if (file.Area != SourceArea.Src ||
                    !string.Equals(file.TopFolder, "Features", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(file.SliceName) ||
                    string.IsNullOrEmpty(file.RoleFolder) ||
                    !InteractiveSeamRestrictedRoleFolders.Contains(file.RoleFolder!))
                {
                    continue;
                }

                string slice = file.SliceName!;
                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    TypeReferenceRecord? firstRevitRef = variant.TypeReferences
                        .FirstOrDefault(r => MatchesPrefix(r.NormalizedName, "Autodesk.Revit"));
                    if (firstRevitRef == null)
                    {
                        continue;
                    }

                    List<TypeDeclarationRecord> topLevelTypes = variant.Types
                        .Where(t => !t.IsNested)
                        .ToList();

                    if (topLevelTypes.Count > 0)
                    {
                        foreach (TypeDeclarationRecord typeDecl in topLevelTypes)
                        {
                            violations.Add(new TypeLevelViolation(
                                slice,
                                FitnessRule.InteractiveSliceSeamPurity,
                                typeDecl.TypeChainName,
                                $"{file.RelativeToRepoPath}:{firstRevitRef.LineNumber} ({typeDecl.FullName}) references '{firstRevitRef.NormalizedName}' inside {file.RoleFolder}/."));
                        }
                    }
                    else
                    {
                        string fallbackName = Path.GetFileNameWithoutExtension(file.FullPath);
                        violations.Add(new TypeLevelViolation(
                            slice,
                            FitnessRule.InteractiveSliceSeamPurity,
                            fallbackName,
                            $"{file.RelativeToRepoPath}:{firstRevitRef.LineNumber} references '{firstRevitRef.NormalizedName}' inside {file.RoleFolder}/."));
                    }
                }
            }

            foreach (WorkspaceXamlFile xaml in snapshot.XamlFiles)
            {
                if (xaml.Area != SourceArea.Src ||
                    !string.Equals(xaml.TopFolder, "Features", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(xaml.SliceName))
                {
                    continue;
                }

                string? roleFolder = ArchitectureWorkspaceSnapshot.ClassifyPath(SourceArea.Src, xaml.RelativeToAreaPath).RoleFolder;
                if (string.IsNullOrEmpty(roleFolder) || !InteractiveSeamRestrictedRoleFolders.Contains(roleFolder!))
                {
                    continue;
                }

                foreach (XamlNamespaceBinding binding in xaml.XmlnsBindings)
                {
                    if (!string.IsNullOrEmpty(binding.ClrNamespace) &&
                        MatchesPrefix(binding.ClrNamespace!, "Autodesk.Revit"))
                    {
                        string xamlTypeName = Path.GetFileNameWithoutExtension(xaml.FullPath);
                        violations.Add(new TypeLevelViolation(
                            xaml.SliceName!,
                            FitnessRule.InteractiveSliceSeamPurity,
                            xamlTypeName,
                            $"{xaml.RelativeToRepoPath} XAML xmlns:{binding.Prefix} references '{binding.ClrNamespace}' inside {roleFolder}/."));
                    }
                }
            }

            return violations;
        }

        public static IReadOnlyList<TypeLevelViolation> CollectTransactionPlacementViolations(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<TypeLevelViolation>();

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                if (file.Area != SourceArea.Src || IsPlatformRevitAddin(file))
                {
                    continue;
                }

                string slice = GetSliceScope(file);
                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    foreach (ObjectCreationRecord creation in variant.ObjectCreations)
                    {
                        bool isTx = IsRevitType(creation.NormalizedTypeName, variant, "Autodesk.Revit.DB", "Transaction");
                        bool isTxGroup = IsRevitType(creation.NormalizedTypeName, variant, "Autodesk.Revit.DB", "TransactionGroup");
                        if (!isTx && !isTxGroup)
                        {
                            continue;
                        }

                        TypeDeclarationRecord? enclosingDecl = FindTypeInVariant(variant, creation.EnclosingTypeFullName);
                        string typeName = enclosingDecl?.TypeChainName ?? ResolveFallbackTypeName(creation.EnclosingTypeFullName, file);

                        if (isTxGroup)
                        {
                            violations.Add(new TypeLevelViolation(
                                slice,
                                FitnessRule.TransactionPlacement,
                                typeName,
                                $"{file.RelativeToRepoPath}:{creation.LineNumber} instantiates raw TransactionGroup outside Platform/Revit/Addin (DocumentTransactionExtensions)."));
                            continue;
                        }

                        bool isPermittedRole = false;
                        if (enclosingDecl != null)
                        {
                            var typeEntry = new TypeEntry(file, variant, enclosingDecl);
                            isPermittedRole =
                                ImplementsOrInherits(typeEntry, "Autodesk.Revit.UI.IExternalCommand", "IExternalCommand", "Autodesk.Revit.UI", snapshot.SrcTypesByFullName) ||
                                ImplementsOrInherits(typeEntry, "Autodesk.Revit.UI.IExternalEventHandler", "IExternalEventHandler", "Autodesk.Revit.UI", snapshot.SrcTypesByFullName) ||
                                IsAdapterImplementation(typeEntry, snapshot.SrcTypesByFullName);
                        }

                        if (!isPermittedRole)
                        {
                            violations.Add(new TypeLevelViolation(
                                slice,
                                FitnessRule.TransactionPlacement,
                                typeName,
                                $"{file.RelativeToRepoPath}:{creation.LineNumber} instantiates raw Transaction inside '{typeName}' outside permitted roles (IExternalCommand, IExternalEventHandler, I*Adapter, or Platform/Revit/Addin)."));
                        }
                    }
                }
            }

            return violations;
        }

        public static IReadOnlyList<TypeLevelViolation> CollectTransactionFailureGuardViolations(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<TypeLevelViolation>();

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                if (file.Area != SourceArea.Src || IsPlatformRevitAddin(file))
                {
                    continue;
                }

                string slice = GetSliceScope(file);
                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    List<ObjectCreationRecord> txCreations = variant.ObjectCreations
                        .Where(c => IsRevitType(c.NormalizedTypeName, variant, "Autodesk.Revit.DB", "Transaction"))
                        .ToList();

                    if (txCreations.Count == 0)
                    {
                        continue;
                    }

                    var allTxCreationNodes = new HashSet<SyntaxNode>(txCreations.Select(c => c.Node));
                    foreach (ObjectCreationRecord creation in txCreations)
                    {
                        if (!IsTransactionCreationGuarded(creation.Node, allTxCreationNodes))
                        {
                            string typeName = ResolveTypeChainName(creation.EnclosingTypeFullName, variant, file);
                            violations.Add(new TypeLevelViolation(
                                slice,
                                FitnessRule.TransactionFailureGuard,
                                typeName,
                                $"{file.RelativeToRepoPath}:{creation.LineNumber} instantiates Transaction without attaching FailurePipelines / IFailuresPreprocessor / SetFailuresPreprocessor."));
                        }
                    }
                }
            }

            return violations;
        }

        public static IReadOnlyList<TypeLevelViolation> CollectExternalEventAndDialogPurityViolations(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<TypeLevelViolation>();

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                if (file.Area != SourceArea.Src)
                {
                    continue;
                }

                string slice = GetSliceScope(file);
                bool isPlatformRevitAddin = IsPlatformRevitAddin(file);
                bool isPlatformUiAddin = string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal) &&
                                         string.Equals(file.SliceName, "UI", StringComparison.Ordinal) &&
                                         file.Partition == PartitionKind.Addin;
                bool isViewModelsFolder = string.Equals(file.RoleFolder, "ViewModels", StringComparison.Ordinal);
                bool isRestrictedDialogScope = !isPlatformUiAddin &&
                                               (string.Equals(file.TopFolder, "RevitDOM", StringComparison.Ordinal) ||
                                                (!string.IsNullOrEmpty(file.RoleFolder) &&
                                                 DialogPurityRestrictedRoleFolders.Contains(file.RoleFolder!)));

                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    // 1. Zero static ExternalEvent fields anywhere in src/
                    foreach (TypeDeclarationRecord typeDecl in variant.Types)
                    {
                        foreach (FieldDeclarationRecord field in typeDecl.Fields)
                        {
                            if (field.IsStatic && IsRevitType(field.NormalizedTypeName, variant, "Autodesk.Revit.UI", "ExternalEvent"))
                            {
                                violations.Add(new TypeLevelViolation(
                                    slice,
                                    FitnessRule.ExternalEventAndDialogPurity,
                                    typeDecl.TypeChainName,
                                    $"{file.RelativeToRepoPath}:{field.LineNumber} declares static ExternalEvent field '{field.Name}'."));
                            }
                        }
                    }

                    // 2. Zero raw FailuresAccessor.DeleteAllWarnings() outside Platform/Revit/Addin
                    if (!isPlatformRevitAddin)
                    {
                        foreach (InvocationRecord inv in variant.Invocations)
                        {
                            if (string.Equals(inv.MethodName, "DeleteAllWarnings", StringComparison.Ordinal))
                            {
                                string typeName = ResolveTypeChainName(inv.EnclosingTypeFullName, variant, file);
                                violations.Add(new TypeLevelViolation(
                                    slice,
                                    FitnessRule.ExternalEventAndDialogPurity,
                                    typeName,
                                    $"{file.RelativeToRepoPath}:{inv.LineNumber} calls raw DeleteAllWarnings() outside Platform/Revit/Addin."));
                            }
                        }
                    }

                    // 3. Zero ExternalEvent.Create(...) in ViewModels/
                    if (isViewModelsFolder)
                    {
                        foreach (InvocationRecord inv in variant.Invocations)
                        {
                            if (IsExternalEventCreateInvocation(inv, variant))
                            {
                                string typeName = ResolveTypeChainName(inv.EnclosingTypeFullName, variant, file);
                                violations.Add(new TypeLevelViolation(
                                    slice,
                                    FitnessRule.ExternalEventAndDialogPurity,
                                    typeName,
                                    $"{file.RelativeToRepoPath}:{inv.LineNumber} calls ExternalEvent.Create(...) inside ViewModels/."));
                            }
                        }
                    }

                    // 4. Zero TaskDialog.Show / new TaskDialog / MessageBox.Show / .ShowDialog()
                    //    inside ViewModels/, Views/, Adapters/, Handlers/, ExternalEvents/, Settings/, or RevitDOM/
                    //    (exempting Platform/UI Addin prompt/dialog adapters and WindowsSummaryDisplayService,
                    //     and ignoring TaskDialogShowingEventArgs event interception).
                    if (isRestrictedDialogScope)
                    {
                        foreach (InvocationRecord inv in variant.Invocations)
                        {
                            string typeName = ResolveTypeChainName(inv.EnclosingTypeFullName, variant, file);
                            if (string.Equals(typeName, "WindowsSummaryDisplayService", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (IsTaskDialogShowInvocation(inv, variant) ||
                                IsMessageBoxShowInvocation(inv, variant) ||
                                string.Equals(inv.MethodName, "ShowDialog", StringComparison.Ordinal))
                            {
                                violations.Add(new TypeLevelViolation(
                                    slice,
                                    FitnessRule.ExternalEventAndDialogPurity,
                                    typeName,
                                    $"{file.RelativeToRepoPath}:{inv.LineNumber} invokes '{inv.NormalizedTarget}' inside restricted UI/handler/RevitDOM scope."));
                            }
                        }

                        foreach (ObjectCreationRecord creation in variant.ObjectCreations)
                        {
                            string typeName = ResolveTypeChainName(creation.EnclosingTypeFullName, variant, file);
                            if (string.Equals(typeName, "WindowsSummaryDisplayService", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (IsRevitType(creation.NormalizedTypeName, variant, "Autodesk.Revit.UI", "TaskDialog"))
                            {
                                violations.Add(new TypeLevelViolation(
                                    slice,
                                    FitnessRule.ExternalEventAndDialogPurity,
                                    typeName,
                                    $"{file.RelativeToRepoPath}:{creation.LineNumber} instantiates TaskDialog inside restricted UI/handler/RevitDOM scope."));
                            }
                        }
                    }
                }
            }

            return violations;
        }

        public static IReadOnlyList<TypeLevelViolation> CollectSingleSchemaExtensibleStorageViolations(ArchitectureWorkspaceSnapshot snapshot)
        {
            var violations = new List<TypeLevelViolation>();

            foreach (WorkspaceSourceFile file in snapshot.SourceFiles)
            {
                if (file.Area != SourceArea.Src)
                {
                    continue;
                }

                bool isPlatformSettingsAddin = string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal) &&
                                               string.Equals(file.SliceName, "Settings", StringComparison.Ordinal) &&
                                               file.Partition == PartitionKind.Addin;
                if (isPlatformSettingsAddin)
                {
                    continue;
                }

                string slice = GetSliceScope(file);
                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    foreach (ObjectCreationRecord creation in variant.ObjectCreations)
                    {
                        if (IsRevitType(creation.NormalizedTypeName, variant, "Autodesk.Revit.DB.ExtensibleStorage", "SchemaBuilder"))
                        {
                            string typeName = ResolveTypeChainName(creation.EnclosingTypeFullName, variant, file);
                            violations.Add(new TypeLevelViolation(
                                slice,
                                FitnessRule.SingleSchemaExtensibleStorage,
                                typeName,
                                $"{file.RelativeToRepoPath}:{creation.LineNumber} instantiates SchemaBuilder outside Platform/Settings/Addin."));
                        }
                    }
                }
            }

            return violations;
        }

        private static bool IsPlatformRevitAddin(WorkspaceSourceFile file)
        {
            return string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal) &&
                   string.Equals(file.SliceName, "Revit", StringComparison.Ordinal) &&
                   file.Partition == PartitionKind.Addin;
        }

        private static string GetSliceScope(WorkspaceSourceFile file)
        {
            if (string.Equals(file.TopFolder, "Features", StringComparison.Ordinal) && !string.IsNullOrEmpty(file.SliceName))
            {
                return file.SliceName!;
            }

            if (string.Equals(file.TopFolder, "Platform", StringComparison.Ordinal) && !string.IsNullOrEmpty(file.SliceName))
            {
                return "Platform/" + file.SliceName!;
            }

            if (string.Equals(file.TopFolder, "Domain", StringComparison.Ordinal) && !string.IsNullOrEmpty(file.SliceName))
            {
                return "Domain/" + file.SliceName!;
            }

            return file.TopFolder;
        }

        private static TypeDeclarationRecord? FindTypeInVariant(ParsedSyntaxVariant variant, string? fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                return null;
            }

            return variant.Types.FirstOrDefault(t => string.Equals(t.FullName, fullTypeName, StringComparison.Ordinal));
        }

        private static string ResolveTypeChainName(string? enclosingTypeFullName, ParsedSyntaxVariant variant, WorkspaceSourceFile file)
        {
            TypeDeclarationRecord? found = FindTypeInVariant(variant, enclosingTypeFullName);
            return found != null ? found.TypeChainName : ResolveFallbackTypeName(enclosingTypeFullName, file);
        }

        private static string ResolveFallbackTypeName(string? fullTypeName, WorkspaceSourceFile file)
        {
            if (!string.IsNullOrEmpty(fullTypeName))
            {
                int lastDot = fullTypeName!.LastIndexOf('.');
                return lastDot >= 0 && lastDot + 1 < fullTypeName.Length
                    ? fullTypeName.Substring(lastDot + 1)
                    : fullTypeName;
            }

            return Path.GetFileNameWithoutExtension(file.FullPath);
        }

        private static bool IsRevitType(
            string normalizedTypeName,
            ParsedSyntaxVariant variant,
            string expectedNamespace,
            string expectedSimpleTypeName)
        {
            if (string.IsNullOrEmpty(normalizedTypeName))
            {
                return false;
            }

            string fullExpected = expectedNamespace + "." + expectedSimpleTypeName;
            if (string.Equals(normalizedTypeName, fullExpected, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(normalizedTypeName, expectedSimpleTypeName, StringComparison.Ordinal))
            {
                return HasNamespaceUsing(variant, expectedNamespace);
            }

            return false;
        }

        private static bool HasNamespaceUsing(ParsedSyntaxVariant variant, string targetNamespace)
        {
            return variant.Usings.Any(u =>
                string.IsNullOrEmpty(u.Alias) &&
                !u.IsStatic &&
                string.Equals(u.NormalizedTarget, targetNamespace, StringComparison.Ordinal));
        }

        private static bool IsAdapterImplementation(
            TypeEntry typeEntry,
            IReadOnlyDictionary<string, List<TypeEntry>> typesByFullName)
        {
            if (!string.Equals(typeEntry.Type.Kind, "class", StringComparison.Ordinal))
            {
                return false;
            }

            return MatchesBaseTypePredicate(
                typeEntry,
                typesByFullName,
                (_, baseType) =>
                {
                    int lastDot = baseType.LastIndexOf('.');
                    string simpleName = lastDot >= 0 && lastDot + 1 < baseType.Length
                        ? baseType.Substring(lastDot + 1)
                        : baseType;

                    return AdapterInterfaceNameRegex.IsMatch(simpleName);
                });
        }

        private static bool IsTransactionCreationGuarded(SyntaxNode creationNode, HashSet<SyntaxNode> allTxCreationNodes)
        {
            UsingStatementSyntax? enclosingUsingHeader = creationNode.Ancestors()
                .OfType<UsingStatementSyntax>()
                .FirstOrDefault(u =>
                    (u.Declaration != null && u.Declaration.Span.Contains(creationNode.Span)) ||
                    (u.Expression != null && u.Expression.Span.Contains(creationNode.Span)));

            if (enclosingUsingHeader != null)
            {
                if (enclosingUsingHeader.Statement is BlockSyntax usingBlock)
                {
                    return ScanBlockStatementsForFailureGuard(
                        usingBlock.Statements,
                        startIndex: 0,
                        treatStartIndexAsTargetStatement: false,
                        creationNode,
                        allTxCreationNodes,
                        stopOnUsingStatement: false);
                }

                return ContainsFailureGuardInvocation(
                    enclosingUsingHeader.Statement,
                    creationNode,
                    allTxCreationNodes,
                    stopOnUsingStatement: false);
            }

            StatementSyntax? enclosingStatement = creationNode.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
            if (enclosingStatement == null)
            {
                return false;
            }

            if (enclosingStatement.Parent is BlockSyntax parentBlock)
            {
                int startIndex = parentBlock.Statements.IndexOf(enclosingStatement);
                if (startIndex < 0)
                {
                    return false;
                }

                return ScanBlockStatementsForFailureGuard(
                    parentBlock.Statements,
                    startIndex,
                    treatStartIndexAsTargetStatement: true,
                    creationNode,
                    allTxCreationNodes,
                    stopOnUsingStatement: true);
            }

            return ContainsFailureGuardInvocation(
                enclosingStatement,
                creationNode,
                allTxCreationNodes,
                stopOnUsingStatement: true);
        }

        private static bool ScanBlockStatementsForFailureGuard(
            SyntaxList<StatementSyntax> statements,
            int startIndex,
            bool treatStartIndexAsTargetStatement,
            SyntaxNode currentCreationNode,
            HashSet<SyntaxNode> allTxCreationNodes,
            bool stopOnUsingStatement)
        {
            for (int i = startIndex; i < statements.Count; i++)
            {
                StatementSyntax stmt = statements[i];
                bool isTargetStatement = treatStartIndexAsTargetStatement && i == startIndex;
                if (!isTargetStatement &&
                    StatementStartsAnotherTransaction(stmt, currentCreationNode, allTxCreationNodes, stopOnUsingStatement))
                {
                    break;
                }

                if (ContainsFailureGuardInvocation(stmt, currentCreationNode, allTxCreationNodes, stopOnUsingStatement))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StatementStartsAnotherTransaction(
            StatementSyntax statement,
            SyntaxNode currentCreationNode,
            HashSet<SyntaxNode> allTxCreationNodes,
            bool stopOnUsingStatement)
        {
            if (statement is UsingStatementSyntax usingStmt)
            {
                if (!stopOnUsingStatement)
                {
                    return false;
                }

                return (usingStmt.Declaration != null && ContainsOtherTxCreation(usingStmt.Declaration, currentCreationNode, allTxCreationNodes)) ||
                       (usingStmt.Expression != null && ContainsOtherTxCreation(usingStmt.Expression, currentCreationNode, allTxCreationNodes));
            }

            if (statement is LocalDeclarationStatementSyntax || statement is ExpressionStatementSyntax)
            {
                return ContainsOtherTxCreation(statement, currentCreationNode, allTxCreationNodes);
            }

            return false;
        }

        private static bool ContainsOtherTxCreation(
            SyntaxNode node,
            SyntaxNode currentCreationNode,
            HashSet<SyntaxNode> allTxCreationNodes)
        {
            return node.DescendantNodesAndSelf()
                .Any(n => n != currentCreationNode && allTxCreationNodes.Contains(n));
        }

        private static bool ContainsFailureGuardInvocation(
            SyntaxNode root,
            SyntaxNode currentCreationNode,
            HashSet<SyntaxNode> allTxCreationNodes,
            bool stopOnUsingStatement)
        {
            if (root is BlockSyntax rootBlock)
            {
                return ScanBlockStatementsForFailureGuard(
                    rootBlock.Statements,
                    startIndex: 0,
                    treatStartIndexAsTargetStatement: false,
                    currentCreationNode,
                    allTxCreationNodes,
                    stopOnUsingStatement);
            }

            foreach (SyntaxNode node in root.DescendantNodesAndSelf(n =>
            {
                if (n is UsingStatementSyntax nestedUsing)
                {
                    bool headerHasOtherTx =
                        (nestedUsing.Declaration != null && ContainsOtherTxCreation(nestedUsing.Declaration, currentCreationNode, allTxCreationNodes)) ||
                        (nestedUsing.Expression != null && ContainsOtherTxCreation(nestedUsing.Expression, currentCreationNode, allTxCreationNodes));
                    if (headerHasOtherTx)
                    {
                        return false;
                    }
                }

                if (n != root && n is BlockSyntax)
                {
                    return false;
                }

                if (n != root &&
                    (n is LocalDeclarationStatementSyntax || n is ExpressionStatementSyntax) &&
                    ContainsOtherTxCreation(n, currentCreationNode, allTxCreationNodes))
                {
                    return false;
                }

                return true;
            }))
            {
                if (node != root && node is BlockSyntax nestedBlock)
                {
                    if (ScanBlockStatementsForFailureGuard(
                        nestedBlock.Statements,
                        startIndex: 0,
                        treatStartIndexAsTargetStatement: false,
                        currentCreationNode,
                        allTxCreationNodes,
                        stopOnUsingStatement))
                    {
                        return true;
                    }

                    continue;
                }

                if (node is InvocationExpressionSyntax inv)
                {
                    string methodName = inv.Expression switch
                    {
                        IdentifierNameSyntax id => id.Identifier.ValueText,
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                        MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
                        _ => string.Empty
                    };

                    if (string.Equals(methodName, "SetFailuresPreprocessor", StringComparison.Ordinal) &&
                        inv.ArgumentList?.Arguments.Count > 0)
                    {
                        ExpressionSyntax argExpr = inv.ArgumentList.Arguments[0].Expression;
                        if (!(argExpr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsExternalEventCreateInvocation(InvocationRecord inv, ParsedSyntaxVariant variant)
        {
            if (string.Equals(inv.NormalizedTarget, "Autodesk.Revit.UI.ExternalEvent.Create", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(inv.NormalizedTarget, "ExternalEvent.Create", StringComparison.Ordinal) &&
                HasNamespaceUsing(variant, "Autodesk.Revit.UI"))
            {
                return true;
            }

            return string.Equals(inv.MethodName, "Create", StringComparison.Ordinal) &&
                   variant.Usings.Any(u => u.IsStatic && string.Equals(u.NormalizedTarget, "Autodesk.Revit.UI.ExternalEvent", StringComparison.Ordinal));
        }

        private static bool IsTaskDialogShowInvocation(InvocationRecord inv, ParsedSyntaxVariant variant)
        {
            if (string.Equals(inv.NormalizedTarget, "Autodesk.Revit.UI.TaskDialog.Show", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(inv.NormalizedTarget, "TaskDialog.Show", StringComparison.Ordinal) &&
                HasNamespaceUsing(variant, "Autodesk.Revit.UI"))
            {
                return true;
            }

            return string.Equals(inv.MethodName, "Show", StringComparison.Ordinal) &&
                   string.Equals(inv.NormalizedTarget, "Show", StringComparison.Ordinal) &&
                   variant.Usings.Any(u => u.IsStatic && string.Equals(u.NormalizedTarget, "Autodesk.Revit.UI.TaskDialog", StringComparison.Ordinal));
        }

        private static bool IsMessageBoxShowInvocation(InvocationRecord inv, ParsedSyntaxVariant variant)
        {
            if (string.Equals(inv.NormalizedTarget, "System.Windows.MessageBox.Show", StringComparison.Ordinal) ||
                string.Equals(inv.NormalizedTarget, "System.Windows.Forms.MessageBox.Show", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(inv.NormalizedTarget, "MessageBox.Show", StringComparison.Ordinal) &&
                (HasNamespaceUsing(variant, "System.Windows") || HasNamespaceUsing(variant, "System.Windows.Forms")))
            {
                return true;
            }

            return string.Equals(inv.MethodName, "Show", StringComparison.Ordinal) &&
                   string.Equals(inv.NormalizedTarget, "Show", StringComparison.Ordinal) &&
                   variant.Usings.Any(u => u.IsStatic &&
                       (string.Equals(u.NormalizedTarget, "System.Windows.MessageBox", StringComparison.Ordinal) ||
                        string.Equals(u.NormalizedTarget, "System.Windows.Forms.MessageBox", StringComparison.Ordinal)));
        }
    }
}
