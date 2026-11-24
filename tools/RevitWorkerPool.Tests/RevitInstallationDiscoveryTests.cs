using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RevitWorkerPool.Discovery;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class RevitInstallationDiscoveryTests
{
    private string _tempRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_DiscoveryTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public void DiscoverInstalledVersions_FindsDirectoryAndRegistryInstalls_WithoutHardcodedYears()
    {
        string autodeskRoot = Path.Combine(_tempRoot, "Program Files", "Autodesk");
        string revit2023 = Path.Combine(autodeskRoot, "Revit 2023");
        string revit2027 = Path.Combine(autodeskRoot, "Revit 2027");
        string revit2028Empty = Path.Combine(autodeskRoot, "Revit 2028");
        string revitAssistant = Path.Combine(autodeskRoot, "Revit Assistant 2027");
        string customRevit2029 = Path.Combine(_tempRoot, "CustomInstall", "Revit 2029");
        string openStudio2024 = Path.Combine(_tempRoot, "NREL", "OpenStudio CLI For Revit 2024");

        Directory.CreateDirectory(revit2023);
        Directory.CreateDirectory(revit2027);
        Directory.CreateDirectory(revit2028Empty);
        Directory.CreateDirectory(revitAssistant);
        Directory.CreateDirectory(customRevit2029);
        Directory.CreateDirectory(openStudio2024);

        File.WriteAllText(Path.Combine(revit2023, "Revit.exe"), string.Empty);
        File.WriteAllText(Path.Combine(revit2027, "Revit.exe"), string.Empty);
        File.WriteAllText(Path.Combine(customRevit2029, "Revit.exe"), string.Empty);

        var registryEntries = new[]
        {
            new UninstallRegistryEntry("OpenStudio CLI For Revit 2024", openStudio2024),
            new UninstallRegistryEntry("Autodesk Revit 2029", customRevit2029),
            new UninstallRegistryEntry("Autodesk Revit 2025", Path.Combine(autodeskRoot, "Missing2025"))
        };

        var discovery = new RevitInstallationDiscovery(
            () => new[] { autodeskRoot },
            () => registryEntries);

        var installed = discovery.DiscoverInstalledVersions();

        Assert.That(installed.Select(i => i.Version).ToArray(), Is.EqualTo(new[] { 2023, 2027, 2029 }));
        Assert.That(installed[0].ExecutablePath, Is.EqualTo(Path.Combine(revit2023, "Revit.exe")));
        Assert.That(installed[1].ExecutablePath, Is.EqualTo(Path.Combine(revit2027, "Revit.exe")));
        Assert.That(installed[2].ExecutablePath, Is.EqualTo(Path.Combine(customRevit2029, "Revit.exe")));
    }

    [Test]
    public void ReadCurrentLatestVersion_ResolvesFromRepoConfig_AndSupportsWorktreeGitFile()
    {
        string fakeRepo = Path.Combine(_tempRoot, "WorktreeRepo");
        string nestedDir = Path.Combine(fakeRepo, "tools", "bin");
        string docsAgents = Path.Combine(fakeRepo, "docs", "agents");
        Directory.CreateDirectory(nestedDir);
        Directory.CreateDirectory(docsAgents);

        // Git worktrees use a .git file rather than a .git directory.
        File.WriteAllText(Path.Combine(fakeRepo, ".git"), "gitdir: C:/fake/.git/worktrees/wt1");
        File.WriteAllText(
            Path.Combine(docsAgents, "revit-config.md"),
            "| `CURRENT_LATEST_VERSION` | `2027` | Forward version |");

        int parsedFake = RevitInstallationDiscovery.ReadCurrentLatestVersion(nestedDir);
        Assert.That(parsedFake, Is.EqualTo(2027));

        // Also validate against the live workspace repository config.
        int actualLatest = RevitInstallationDiscovery.ReadCurrentLatestVersion(TestContext.CurrentContext.TestDirectory);
        Assert.That(actualLatest, Is.GreaterThanOrEqualTo(2026));
    }
}
