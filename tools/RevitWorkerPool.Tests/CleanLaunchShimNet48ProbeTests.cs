using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class CleanLaunchShimNet48ProbeTests
{
    private string _tempRoot = string.Empty;
    private string _appDataRoot = string.Empty;
    private string _programDataRoot = string.Empty;
    private string _programFilesRoot = string.Empty;
    private string _toolsRoot = string.Empty;
    private string _shimDllPath = string.Empty;
    private string _launcherDllPath = string.Empty;
    private string _probeExePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _toolsRoot = ResolveToolsDirectory();
        string poolBinDir = Path.Combine(_toolsRoot, "bin", "RevitWorkerPool");
        _shimDllPath = Path.Combine(poolBinDir, "RevitCleanShim.dll");
        _launcherDllPath = Path.Combine(poolBinDir, "RevitCleanLauncher.dll");
        _probeExePath = Path.Combine(_toolsRoot, "bin", "Probes", "Net48Probe", "Net48Probe.exe");

        Assert.That(File.Exists(_shimDllPath), Is.True, $"Run tools/build.ps1 first; missing {_shimDllPath}");
        Assert.That(File.Exists(_launcherDllPath), Is.True, $"Run tools/build.ps1 first; missing {_launcherDllPath}");
        Assert.That(File.Exists(_probeExePath), Is.True, $"Run tools/build.ps1 first; missing {_probeExePath}");

        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitCleanShim_Net48Probe_" + Guid.NewGuid().ToString("N"));
        _appDataRoot = Path.Combine(_tempRoot, "AppData");
        _programDataRoot = Path.Combine(_tempRoot, "ProgramData");
        _programFilesRoot = Path.Combine(_tempRoot, "ProgramFiles");

        SeedMockDirectories("2024");
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
    public async Task Net48Probe_ShimmedLaunch_EnforcesAllowlist_PreservesInternals_AndDoesNotPropagateToGrandchild()
    {
        string outJsonPath = Path.Combine(_tempRoot, "probe_output.json");
        bool beforeResumeCalled = false;
        int capturedPid = 0;

        var launcher = new CleanDetoursProcessLauncher(_shimDllPath, _launcherDllPath);
        Assert.That(launcher.UsesCleanShim, Is.True);

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2024,
            ExecutablePath = _probeExePath,
            Arguments =
                $"--year 2024 " +
                $"--appdata \"{_appDataRoot}\" " +
                $"--programdata \"{_programDataRoot}\" " +
                $"--programfiles \"{_programFilesRoot}\" " +
                $"--out \"{outJsonPath}\" " +
                $"--spawn-grandchild",
            AdditionalEnvironmentVariables = new Dictionary<string, string?>
            {
                ["SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT"] = _appDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT"] = _programDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT"] = _programFilesRoot
            },
            OnProcessCreatedBeforeResume = pid =>
            {
                beforeResumeCalled = true;
                capturedPid = pid;
            }
        };

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        await worker.WaitForExitAsync();

        Assert.That(beforeResumeCalled, Is.True);
        Assert.That(capturedPid, Is.EqualTo(worker.ProcessId));
        Assert.That(worker.ExitCode, Is.EqualTo(0));
        Assert.That(File.Exists(outJsonPath), Is.True);

        ProbeReportDto? report = JsonConvert.DeserializeObject<ProbeReportDto>(await File.ReadAllTextAsync(outJsonPath));
        Assert.That(report, Is.Not.Null);

        // 1. Shim is loaded in the direct child process.
        Assert.That(report!.HasRevitCleanShim, Is.True);
        Assert.That(report.LoadedModules, Does.Contain("RevitCleanShim.dll").IgnoreCase);

        // 2. Parent directories remain visible.
        Assert.That(report.AppDataAddinsDirExists, Is.True);
        Assert.That(report.ProgramDataAddinsDirExists, Is.True);
        Assert.That(report.AppDataPluginsDirExists, Is.True);
        Assert.That(report.ProgramDataPluginsDirExists, Is.True);

        // 3. %APPDATA%\Autodesk\Revit\Addins\2024 exposes ONLY Synthetic2024.addin.
        Assert.That(report.AppDataAddinFiles, Is.EqualTo(new[] { "Synthetic2024.addin" }));
        Assert.That(report.AppDataAddinAllEntries, Is.EqualTo(new[] { "Synthetic2024.addin" }));
        Assert.That(report.FindFirstFileExAppDataAddins, Is.EqualTo(new[] { "Synthetic2024.addin" }));
        Assert.That(report.AppDataSyntheticAddinExists, Is.True);
        Assert.That(report.AppDataSyntheticAddinText, Does.Contain("Synthetic.Bootstrap.BootstrapApp"));
        Assert.That(report.AppDataThirdPartyAddinExists, Is.False);
        Assert.That(report.AppDataThirdPartyAddinReadError, Is.Not.Null.And.Not.Empty);

        // 4. %ProgramData%\Autodesk\Revit\Addins\2024 hides ALL *.addin entries (including stale Synthetic2024.addin) and bundles.
        Assert.That(report.ProgramDataAddinFiles, Is.Empty);
        Assert.That(report.ProgramDataAddinBundleDirs, Is.Empty);
        Assert.That(report.ProgramDataAddinAllEntries, Is.Empty);
        Assert.That(report.ProgramDataSyntheticAddinExists, Is.False);
        Assert.That(report.ProgramDataSyntheticAddinReadError, Is.Not.Null.And.Not.Empty);
        Assert.That(report.GetFileAttributesProgramDataSyntheticHidden, Is.True);
        Assert.That(report.ProgramDataThirdPartyAddinExists, Is.False);
        Assert.That(report.ProgramDataThirdPartyAddinReadError, Is.Not.Null.And.Not.Empty);

        // 5. %APPDATA%\Autodesk\ApplicationPlugins exposes ONLY ricaun.RevitTest.Application.bundle.
        Assert.That(report.AppDataBundleDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
        Assert.That(report.AppDataPluginAllDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
        Assert.That(report.AppDataRicaunPackageExists, Is.True);
        Assert.That(report.AppDataRicaunPackageText, Does.Contain("ApplicationPackage"));
        Assert.That(report.AppDataBluebeamDirExists, Is.False);
        Assert.That(report.AppDataBluebeamManifestExists, Is.False);
        Assert.That(report.AppDataBluebeamReadError, Is.Not.Null.And.Not.Empty);

        // 6. %ProgramData%\Autodesk\ApplicationPlugins hides ALL entries.
        Assert.That(report.ProgramDataBundleDirs, Is.Empty);
        Assert.That(report.ProgramDataPluginAllDirs, Is.Empty);
        Assert.That(report.ProgramDataThirdPartyBundleExists, Is.False);
        Assert.That(report.ProgramDataThirdPartyBundleManifestExists, Is.False);

        // 7. %ProgramFiles%\Autodesk\Revit 2024\AddIns\ and eTransmitForRevitDB.dll remain untouched.
        Assert.That(report.InternalAddinFiles, Is.EqualTo(new[] { "Autodesk.Keynote.addin" }));
        Assert.That(report.ETransmitDbExists, Is.True);
        Assert.That(report.ETransmitDbText, Is.EqualTo("MOCK_ETRANSMIT_DB"));

        // 8. Grandchild process MUST NOT inherit RevitCleanShim.dll and MUST see unfiltered trees.
        Assert.That(report.GrandchildError, Is.Null);
        Assert.That(report.GrandchildExitCode, Is.EqualTo(0));
        Assert.That(report.Grandchild, Is.Not.Null);

        ProbeReportDto grandchild = report.Grandchild!;
        Assert.That(grandchild.ProcessId, Is.Not.EqualTo(report.ProcessId));
        Assert.That(grandchild.HasRevitCleanShim, Is.False);
        Assert.That(grandchild.LoadedModules, Does.Not.Contain("RevitCleanShim.dll").IgnoreCase);
        Assert.That(grandchild.AppDataAddinFiles, Is.EqualTo(new[] { "BluebeamAddin.addin", "Kinship.addin", "Synthetic2024.addin" }));
        Assert.That(grandchild.ProgramDataAddinFiles, Is.EqualTo(new[] { "0_Enscape.addin", "CTC-BIM-Project-Suite.addin", "Synthetic2024.addin" }));
        Assert.That(grandchild.ProgramDataAddinBundleDirs, Is.EqualTo(new[] { "Vendor.bundle" }));
        Assert.That(grandchild.AppDataPluginAllDirs, Is.EqualTo(new[] { "BIM 42 Align.bundle", "Bluebeam Software", "ricaun.RevitTest.Application.bundle" }));
        Assert.That(grandchild.ProgramDataBundleDirs, Is.EqualTo(new[] { "CTC-BIM-Project-Suite.bundle", "ThirdParty.bundle" }));
        Assert.That(grandchild.ProgramDataSyntheticAddinExists, Is.True);
    }

    [Test]
    public async Task Net48Probe_InvalidRevitYear_FailsClosedWithoutCrashing()
    {
        string outJsonPath = Path.Combine(_tempRoot, "invalid_year_output.json");
        var launcher = new CleanDetoursProcessLauncher(_shimDllPath, _launcherDllPath);

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2024,
            ExecutablePath = _probeExePath,
            Arguments =
                $"--year 2024 " +
                $"--appdata \"{_appDataRoot}\" " +
                $"--programdata \"{_programDataRoot}\" " +
                $"--programfiles \"{_programFilesRoot}\" " +
                $"--out \"{outJsonPath}\"",
            AdditionalEnvironmentVariables = new Dictionary<string, string?>
            {
                ["SYNTHETIC_CLEAN_SHIM_REVIT_YEAR"] = "invalid",
                ["SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT"] = _appDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT"] = _programDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT"] = _programFilesRoot
            }
        };

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        await worker.WaitForExitAsync();

        Assert.That(worker.ExitCode, Is.EqualTo(0));
        ProbeReportDto? report = JsonConvert.DeserializeObject<ProbeReportDto>(await File.ReadAllTextAsync(outJsonPath));
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.HasRevitCleanShim, Is.True);
        Assert.That(report.AppDataAddinFiles, Is.Empty);
        Assert.That(report.ProgramDataAddinFiles, Is.Empty);
        Assert.That(report.AppDataBundleDirs, Is.Empty);
        Assert.That(report.ProgramDataBundleDirs, Is.Empty);
    }

    [Test]
    public void CleanDetoursProcessLauncher_FailsClosed_WhenShimDllIsMissingOrCorrupted()
    {
        string missingShimPath = Path.Combine(_tempRoot, "NonExistentShim.dll");
        bool callbackInvoked = false;

        var missingLauncher = new CleanDetoursProcessLauncher(missingShimPath, _launcherDllPath);
        CleanShimLaunchException? missingEx = Assert.ThrowsAsync<CleanShimLaunchException>(async () =>
            await missingLauncher.LaunchAsync(new WorkerLaunchRequest
            {
                RevitVersion = 2024,
                ExecutablePath = _probeExePath,
                OnProcessCreatedBeforeResume = _ => callbackInvoked = true
            }));

        Assert.That(missingEx, Is.Not.Null);
        Assert.That(missingEx!.Message, Does.Contain("NonExistentShim.dll"));
        Assert.That(callbackInvoked, Is.False);

        // Corrupt a copy of RevitCleanShim.dll so Authenticode verification fails closed.
        string corruptedShimPath = Path.Combine(_tempRoot, "CorruptedShim.dll");
        byte[] shimBytes = File.ReadAllBytes(_shimDllPath);
        shimBytes[shimBytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(corruptedShimPath, shimBytes);

        var corruptedLauncher = new CleanDetoursProcessLauncher(corruptedShimPath, _launcherDllPath);
        CleanShimLaunchException? corruptedEx = Assert.ThrowsAsync<CleanShimLaunchException>(async () =>
            await corruptedLauncher.LaunchAsync(new WorkerLaunchRequest
            {
                RevitVersion = 2024,
                ExecutablePath = _probeExePath
            }));

        Assert.That(corruptedEx, Is.Not.Null);
        Assert.That(corruptedEx!.Message, Does.Contain("Authenticode"));

        // Unsigned binary (for example Net48Probe.exe copied as a DLL) must also fail closed.
        string unsignedDllPath = Path.Combine(_tempRoot, "UnsignedLauncher.dll");
        File.Copy(_probeExePath, unsignedDllPath);

        var unsignedLauncher = new CleanDetoursProcessLauncher(_shimDllPath, unsignedDllPath);
        CleanShimLaunchException? unsignedEx = Assert.ThrowsAsync<CleanShimLaunchException>(async () =>
            await unsignedLauncher.LaunchAsync(new WorkerLaunchRequest
            {
                RevitVersion = 2024,
                ExecutablePath = _probeExePath
            }));

        Assert.That(unsignedEx, Is.Not.Null);
        Assert.That(unsignedEx!.Message, Does.Contain("Authenticode"));
    }

    [Test]
    public async Task CleanDetoursProcessLauncher_DefaultConstructor_ResolvesSignedBinariesFromBaseDirectory()
    {
        var defaultLauncher = new CleanDetoursProcessLauncher();
        Assert.That(defaultLauncher.ShimDllPath, Is.EqualTo(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "RevitCleanShim.dll"))));
        Assert.That(defaultLauncher.LauncherDllPath, Is.EqualTo(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "RevitCleanLauncher.dll"))));

        string outJsonPath = Path.Combine(_tempRoot, "default_ctor_output.json");
        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2024,
            ExecutablePath = _probeExePath,
            Arguments =
                $"--year 2024 " +
                $"--appdata \"{_appDataRoot}\" " +
                $"--programdata \"{_programDataRoot}\" " +
                $"--programfiles \"{_programFilesRoot}\" " +
                $"--out \"{outJsonPath}\"",
            AdditionalEnvironmentVariables = new Dictionary<string, string?>
            {
                ["SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT"] = _appDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT"] = _programDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT"] = _programFilesRoot
            }
        };

        await using LaunchedWorkerProcess worker = await defaultLauncher.LaunchAsync(request);
        await worker.WaitForExitAsync();

        Assert.That(worker.ExitCode, Is.EqualTo(0));
        ProbeReportDto? report = JsonConvert.DeserializeObject<ProbeReportDto>(await File.ReadAllTextAsync(outJsonPath));
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.HasRevitCleanShim, Is.True);
        Assert.That(report.AppDataAddinFiles, Is.EqualTo(new[] { "Synthetic2024.addin" }));
    }

    private void SeedMockDirectories(string year)
    {
        string appDataAddins = Path.Combine(_appDataRoot, "Autodesk", "Revit", "Addins", year);
        Directory.CreateDirectory(appDataAddins);
        File.WriteAllText(Path.Combine(appDataAddins, $"Synthetic{year}.addin"), "<RevitAddIns><FullClassName>Synthetic.Bootstrap.BootstrapApp</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(appDataAddins, "Kinship.addin"), "<RevitAddIns><FullClassName>Kinship.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(appDataAddins, "BluebeamAddin.addin"), "<RevitAddIns><FullClassName>Bluebeam.App</FullClassName></RevitAddIns>");
        Directory.CreateDirectory(Path.Combine(appDataAddins, "ThirdPartyAddinFolder"));

        string programDataAddins = Path.Combine(_programDataRoot, "Autodesk", "Revit", "Addins", year);
        Directory.CreateDirectory(programDataAddins);
        File.WriteAllText(Path.Combine(programDataAddins, $"Synthetic{year}.addin"), "<RevitAddIns><FullClassName>Synthetic.Core.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(programDataAddins, "CTC-BIM-Project-Suite.addin"), "<RevitAddIns><FullClassName>CTC.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(programDataAddins, "0_Enscape.addin"), "<RevitAddIns><FullClassName>Enscape.App</FullClassName></RevitAddIns>");
        string pdAddinBundle = Path.Combine(programDataAddins, "Vendor.bundle");
        Directory.CreateDirectory(pdAddinBundle);
        File.WriteAllText(Path.Combine(pdAddinBundle, "PackageContents.xml"), "<ApplicationPackage/>");

        string syntheticBootstrapDir = Path.Combine(_programDataRoot, "Autodesk", "Revit", "Addins", "Synthetic", "Bootstrap", "v1");
        Directory.CreateDirectory(syntheticBootstrapDir);
        File.WriteAllText(Path.Combine(syntheticBootstrapDir, $"Synthetic.Bootstrap{year}.dll"), "MOCK_BOOTSTRAP");

        string appDataPlugins = Path.Combine(_appDataRoot, "Autodesk", "ApplicationPlugins");
        string ricaunBundle = Path.Combine(appDataPlugins, "ricaun.RevitTest.Application.bundle");
        Directory.CreateDirectory(Path.Combine(ricaunBundle, "Contents"));
        File.WriteAllText(Path.Combine(ricaunBundle, "PackageContents.xml"), "<ApplicationPackage Name=\"ricaun.RevitTest.Application\"/>");
        File.WriteAllText(Path.Combine(ricaunBundle, "Contents", "ricaun.RevitTest.Application.dll"), "MOCK_RICAUN");

        string bluebeamDir = Path.Combine(appDataPlugins, "Bluebeam Software");
        Directory.CreateDirectory(bluebeamDir);
        File.WriteAllText(Path.Combine(bluebeamDir, "PackageContents.xml"), "<ApplicationPackage Name=\"Bluebeam\"/>");

        string bim42Dir = Path.Combine(appDataPlugins, "BIM 42 Align.bundle");
        Directory.CreateDirectory(bim42Dir);
        File.WriteAllText(Path.Combine(bim42Dir, "PackageContents.xml"), "<ApplicationPackage Name=\"BIM42\"/>");

        string programDataPlugins = Path.Combine(_programDataRoot, "Autodesk", "ApplicationPlugins");
        string pdThirdPartyBundle = Path.Combine(programDataPlugins, "ThirdParty.bundle");
        Directory.CreateDirectory(pdThirdPartyBundle);
        File.WriteAllText(Path.Combine(pdThirdPartyBundle, "PackageContents.xml"), "<ApplicationPackage Name=\"ThirdParty\"/>");

        string pdCtcBundle = Path.Combine(programDataPlugins, "CTC-BIM-Project-Suite.bundle");
        Directory.CreateDirectory(pdCtcBundle);
        File.WriteAllText(Path.Combine(pdCtcBundle, "PackageContents.xml"), "<ApplicationPackage Name=\"CTC\"/>");

        string internalAddins = Path.Combine(_programFilesRoot, "Autodesk", $"Revit {year}", "AddIns", "Keynote");
        Directory.CreateDirectory(internalAddins);
        File.WriteAllText(Path.Combine(internalAddins, "Autodesk.Keynote.addin"), "<RevitAddIns><FullClassName>Autodesk.Keynote</FullClassName></RevitAddIns>");

        string eTransmitDir = Path.Combine(_programFilesRoot, "Autodesk", $"eTransmit for Revit {year}");
        Directory.CreateDirectory(eTransmitDir);
        File.WriteAllText(Path.Combine(eTransmitDir, "eTransmitForRevitDB.dll"), "MOCK_ETRANSMIT_DB");
    }

    private static string ResolveToolsDirectory()
    {
        string? repoRoot = RevitInstallationDiscovery.FindRepositoryRoot();
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string candidate = Path.Combine(repoRoot, "tools");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not resolve repository 'tools' directory.");
    }

    private sealed class ProbeReportDto
    {
        [JsonProperty("processId")]
        public int ProcessId { get; init; }

        [JsonProperty("year")]
        public string Year { get; init; } = string.Empty;

        [JsonProperty("loadedModules")]
        public string[] LoadedModules { get; init; } = Array.Empty<string>();

        [JsonProperty("hasRevitCleanShim")]
        public bool HasRevitCleanShim { get; init; }

        [JsonProperty("appDataAddinsDirExists")]
        public bool AppDataAddinsDirExists { get; init; }

        [JsonProperty("programDataAddinsDirExists")]
        public bool ProgramDataAddinsDirExists { get; init; }

        [JsonProperty("appDataPluginsDirExists")]
        public bool AppDataPluginsDirExists { get; init; }

        [JsonProperty("programDataPluginsDirExists")]
        public bool ProgramDataPluginsDirExists { get; init; }

        [JsonProperty("appDataAddinFiles")]
        public string[] AppDataAddinFiles { get; init; } = Array.Empty<string>();

        [JsonProperty("appDataAddinAllEntries")]
        public string[] AppDataAddinAllEntries { get; init; } = Array.Empty<string>();

        [JsonProperty("programDataAddinFiles")]
        public string[] ProgramDataAddinFiles { get; init; } = Array.Empty<string>();

        [JsonProperty("programDataAddinBundleDirs")]
        public string[] ProgramDataAddinBundleDirs { get; init; } = Array.Empty<string>();

        [JsonProperty("programDataAddinAllEntries")]
        public string[] ProgramDataAddinAllEntries { get; init; } = Array.Empty<string>();

        [JsonProperty("appDataBundleDirs")]
        public string[] AppDataBundleDirs { get; init; } = Array.Empty<string>();

        [JsonProperty("appDataPluginAllDirs")]
        public string[] AppDataPluginAllDirs { get; init; } = Array.Empty<string>();

        [JsonProperty("programDataBundleDirs")]
        public string[] ProgramDataBundleDirs { get; init; } = Array.Empty<string>();

        [JsonProperty("programDataPluginAllDirs")]
        public string[] ProgramDataPluginAllDirs { get; init; } = Array.Empty<string>();

        [JsonProperty("internalAddinFiles")]
        public string[] InternalAddinFiles { get; init; } = Array.Empty<string>();

        [JsonProperty("eTransmitDbExists")]
        public bool ETransmitDbExists { get; init; }

        [JsonProperty("eTransmitDbText")]
        public string? ETransmitDbText { get; init; }

        [JsonProperty("appDataSyntheticAddinExists")]
        public bool AppDataSyntheticAddinExists { get; init; }

        [JsonProperty("appDataSyntheticAddinText")]
        public string? AppDataSyntheticAddinText { get; init; }

        [JsonProperty("appDataThirdPartyAddinExists")]
        public bool AppDataThirdPartyAddinExists { get; init; }

        [JsonProperty("appDataThirdPartyAddinReadError")]
        public string? AppDataThirdPartyAddinReadError { get; init; }

        [JsonProperty("programDataSyntheticAddinExists")]
        public bool ProgramDataSyntheticAddinExists { get; init; }

        [JsonProperty("programDataSyntheticAddinReadError")]
        public string? ProgramDataSyntheticAddinReadError { get; init; }

        [JsonProperty("programDataThirdPartyAddinExists")]
        public bool ProgramDataThirdPartyAddinExists { get; init; }

        [JsonProperty("programDataThirdPartyAddinReadError")]
        public string? ProgramDataThirdPartyAddinReadError { get; init; }

        [JsonProperty("appDataRicaunPackageExists")]
        public bool AppDataRicaunPackageExists { get; init; }

        [JsonProperty("appDataRicaunPackageText")]
        public string? AppDataRicaunPackageText { get; init; }

        [JsonProperty("appDataBluebeamDirExists")]
        public bool AppDataBluebeamDirExists { get; init; }

        [JsonProperty("appDataBluebeamManifestExists")]
        public bool AppDataBluebeamManifestExists { get; init; }

        [JsonProperty("appDataBluebeamReadError")]
        public string? AppDataBluebeamReadError { get; init; }

        [JsonProperty("programDataThirdPartyBundleExists")]
        public bool ProgramDataThirdPartyBundleExists { get; init; }

        [JsonProperty("programDataThirdPartyBundleManifestExists")]
        public bool ProgramDataThirdPartyBundleManifestExists { get; init; }

        [JsonProperty("findFirstFileExAppDataAddins")]
        public string[] FindFirstFileExAppDataAddins { get; init; } = Array.Empty<string>();

        [JsonProperty("getFileAttributesProgramDataSyntheticHidden")]
        public bool GetFileAttributesProgramDataSyntheticHidden { get; init; }

        [JsonProperty("grandchildExitCode")]
        public int? GrandchildExitCode { get; init; }

        [JsonProperty("grandchildError")]
        public string? GrandchildError { get; init; }

        [JsonProperty("grandchild")]
        public ProbeReportDto? Grandchild { get; init; }
    }
}
