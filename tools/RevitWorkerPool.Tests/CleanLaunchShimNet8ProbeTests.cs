using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitWorkerPool.Discovery;
using RevitWorkerPool.Launching;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class CleanLaunchShimNet8ProbeTests
{
    private const uint StatusSuccess = 0x00000000;
    private const uint StatusNoMoreFiles = 0x80000006;
    private const uint StatusNoSuchFile = 0xC000000F;
    private const uint StatusObjectNameNotFound = 0xC0000034;

    private string _tempRoot = string.Empty;
    private string _appDataRoot = string.Empty;
    private string _programDataRoot = string.Empty;
    private string _programFilesRoot = string.Empty;
    private string _profileRoot = string.Empty;
    private string _shimDllPath = string.Empty;
    private string _launcherDllPath = string.Empty;
    private string _probeExePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        string toolsRoot = ResolveToolsDirectory();
        string poolBinDir = Path.Combine(toolsRoot, "bin", "RevitWorkerPool");
        _shimDllPath = Path.Combine(poolBinDir, "RevitCleanShim.dll");
        _launcherDllPath = Path.Combine(poolBinDir, "RevitCleanLauncher.dll");
        _probeExePath = Path.Combine(toolsRoot, "bin", "Probes", "Net8Probe", "Net8Probe.exe");

        Assert.That(File.Exists(_shimDllPath), Is.True, $"Run tools/build.ps1 first; missing {_shimDllPath}");
        Assert.That(File.Exists(_launcherDllPath), Is.True, $"Run tools/build.ps1 first; missing {_launcherDllPath}");
        Assert.That(File.Exists(_probeExePath), Is.True, $"Run tools/build.ps1 first; missing {_probeExePath}");

        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitCleanShim_Net8Probe_" + Guid.NewGuid().ToString("N"));
        _appDataRoot = Path.Combine(_tempRoot, "AppData");
        _programDataRoot = Path.Combine(_tempRoot, "ProgramData");
        _programFilesRoot = Path.Combine(_tempRoot, "ProgramFiles");
        _profileRoot = Path.Combine(_tempRoot, "CleanProfile");
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
    public async Task Net8Probe_Revit2027_ShimmedLaunch_FiltersNtApis_VirtualizesSettings_AndDoesNotPropagateToGrandchild()
    {
        const string hostAppDataSettings = "{\"DisableAllAddIns\":true,\"AddInItemSettings\":[{\"Id\":\"host-only\"}]}";
        const string hostProgramDataAdminSettings = "{\"DisableAllAddIns\":true,\"HostMarker\":\"programdata-host\"}";
        const string hostProgramFilesAdminSettings = "{\"DisableAllAddIns\":true,\"HostMarker\":\"programfiles-host\"}";

        SeedMockTree(
            "2027",
            seedHostSettings: true,
            hostAppDataSettings: hostAppDataSettings,
            hostProgramDataAdminSettings: hostProgramDataAdminSettings,
            hostProgramFilesAdminSettings: hostProgramFilesAdminSettings,
            seedRicaunInAllPluginsRoots: false);

        string outJsonPath = Path.Combine(_tempRoot, "probe2027_output.json");
        var launcher = new CleanDetoursProcessLauncher(_shimDllPath, _launcherDllPath);

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2027,
            ExecutablePath = _probeExePath,
            Arguments =
                $"--year 2027 " +
                $"--appdata \"{_appDataRoot}\" " +
                $"--programdata \"{_programDataRoot}\" " +
                $"--programfiles \"{_programFilesRoot}\" " +
                $"--out \"{outJsonPath}\" " +
                $"--spawn-grandchild",
            AdditionalEnvironmentVariables = new Dictionary<string, string?>
            {
                ["SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT"] = _appDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT"] = _programDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT"] = _programFilesRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROFILE_DIR"] = _profileRoot
            }
        };

        string pidProfileDir;
        int workerPid;

        await using (LaunchedWorkerProcess worker = await launcher.LaunchAsync(request))
        {
            await worker.WaitForExitAsync();
            Assert.That(worker.ExitCode, Is.EqualTo(0));
            workerPid = worker.ProcessId;
            pidProfileDir = Path.Combine(_profileRoot, workerPid.ToString(CultureInfo.InvariantCulture));

            Assert.That(File.Exists(outJsonPath), Is.True);
            ProbeReportDto? report = JsonSerializer.Deserialize<ProbeReportDto>(await File.ReadAllTextAsync(outJsonPath));
            Assert.That(report, Is.Not.Null);

            // 1. Shim is loaded in direct child.
            Assert.That(report!.HasRevitCleanShim, Is.True);
            Assert.That(report.LoadedModules, Does.Contain("RevitCleanShim.dll").IgnoreCase);

            // 2. Parent directories remain visible.
            Assert.That(report.AppDataAddinsDirExists, Is.True);
            Assert.That(report.ProgramDataAddinsDirExists, Is.True);
            Assert.That(report.ProgramFilesAddinsDirExists, Is.True);
            Assert.That(report.AppDataPluginsDirExists, Is.True);
            Assert.That(report.ProgramDataPluginsDirExists, Is.True);
            Assert.That(report.ProgramFilesPluginsDirExists, Is.True);

            // 3. Manifest roots filtering across %APPDATA%, %ProgramData%, and Revit 2027 %ProgramFiles%.
            Assert.That(report.AppDataAddinFiles, Is.EqualTo(new[] { "Synthetic2027.addin" }));
            Assert.That(report.AppDataAddinAllEntries, Is.EqualTo(new[] { "Synthetic2027.addin" }));
            Assert.That(report.AppDataSyntheticAddinExists, Is.True);
            Assert.That(report.AppDataSyntheticAddinText, Does.Contain("Synthetic.Bootstrap.BootstrapApp"));
            Assert.That(report.AppDataThirdPartyAddinExists, Is.False);
            Assert.That(report.AppDataThirdPartyAddinReadError, Is.Not.Null.And.Not.Empty);

            Assert.That(report.ProgramDataAddinFiles, Is.Empty);
            Assert.That(report.ProgramDataAddinBundleDirs, Is.Empty);
            Assert.That(report.ProgramDataAddinAllEntries, Is.Empty);
            Assert.That(report.ProgramDataSyntheticAddinExists, Is.False);
            Assert.That(report.ProgramDataThirdPartyAddinExists, Is.False);

            Assert.That(report.ProgramFilesAddinFiles, Is.Empty);
            Assert.That(report.ProgramFilesAddinBundleDirs, Is.Empty);
            Assert.That(report.ProgramFilesAddinAllEntries, Is.Empty);
            Assert.That(report.ProgramFilesSyntheticAddinExists, Is.False);
            Assert.That(report.ProgramFilesSyntheticAddinReadError, Is.Not.Null.And.Not.Empty);
            Assert.That(report.ProgramFilesThirdPartyAddinExists, Is.False);
            Assert.That(report.ProgramFilesThirdPartyAddinReadError, Is.Not.Null.And.Not.Empty);

            // 4. ApplicationPlugins across all 3 roots.
            Assert.That(report.AppDataBundleDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
            Assert.That(report.AppDataPluginAllDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
            Assert.That(report.AppDataRicaunPackageExists, Is.True);
            Assert.That(report.AppDataBluebeamDirExists, Is.False);
            Assert.That(report.AppDataBluebeamManifestExists, Is.False);

            Assert.That(report.ProgramDataBundleDirs, Is.Empty);
            Assert.That(report.ProgramDataPluginAllDirs, Is.Empty);
            Assert.That(report.ProgramDataThirdPartyBundleExists, Is.False);

            Assert.That(report.ProgramFilesBundleDirs, Is.Empty);
            Assert.That(report.ProgramFilesPluginAllDirs, Is.Empty);
            Assert.That(report.ProgramFilesThirdPartyBundleExists, Is.False);
            Assert.That(report.ProgramFilesThirdPartyBundleManifestExists, Is.False);

            // 5. Internal built-ins and Revit 2027 internal eTransmitForRevitDB.dll remain untouched.
            Assert.That(report.InternalAddinFiles, Is.EqualTo(new[] { "Autodesk.Keynote.addin" }));
            Assert.That(report.InternalETransmitDbExists, Is.True);
            Assert.That(report.InternalETransmitDbText, Is.EqualTo("MOCK_ETRANSMIT_DB_2027"));

            // 6. Direct NT hooks across all 6 FILE_INFORMATION_CLASS layouts + NtCreateFile / NtOpenFile.
            foreach (string infoClass in new[] { "1", "2", "3", "12", "37", "38" })
            {
                Assert.That(report.NtQuerySingleEntryAppDataClasses[infoClass], Is.EqualTo(new[] { "Synthetic2027.addin" }), $"Single-entry NtQueryDirectoryFile failed for class {infoClass}");
                Assert.That(report.NtQueryExMultiEntryAppDataClasses[infoClass], Is.EqualTo(new[] { "Synthetic2027.addin" }), $"Multi-entry NtQueryDirectoryFileEx failed for class {infoClass}");
                Assert.That(report.NtQueryNullPatternAllEntriesAppDataClasses[infoClass], Is.EqualTo(new[] { "Synthetic2027.addin" }), $"Null-pattern NtQueryDirectoryFile failed for class {infoClass}");
            }

            Assert.That(report.NtQueryProgramDataEmptyFirstStatus, Is.EqualTo(StatusNoSuchFile));
            Assert.That(report.NtQueryProgramFilesExEmptyFirstStatus, Is.EqualTo(StatusNoSuchFile));
            Assert.That(report.NtQueryAppDataSecondCallAfterMatchStatus, Is.EqualTo(StatusNoMoreFiles));

            Assert.That(report.NtOpenAppDataSyntheticStatus, Is.EqualTo(StatusSuccess));
            Assert.That(report.NtOpenProgramFilesThirdPartyAddinStatus, Is.EqualTo(StatusObjectNameNotFound));
            Assert.That(report.NtCreateRelativeProgramDataSyntheticStatus, Is.EqualTo(StatusObjectNameNotFound));
            Assert.That(report.NtOpenRelativeAppDataSyntheticStatus, Is.EqualTo(StatusSuccess));

            // 7. Per-PID settings virtualization (AddInsSettings.json and both AdminSettings.json paths).
            Assert.That(report.AppDataAddInsSettingsExistsBeforeWrite, Is.True);
            Assert.That(report.AppDataAddInsSettingsInitialRead, Is.EqualTo("{\"DisableAllAddIns\":false,\"AddInItemSettings\":[]}"));
            Assert.That(report.AppDataAddInsSettingsNtOpenRead, Is.EqualTo("{\"DisableAllAddIns\":false,\"AddInItemSettings\":[]}"));
            Assert.That(report.AppDataAddInsSettingsAfterWriteRead, Is.EqualTo($"{{\"DisableAllAddIns\":false,\"AddInItemSettings\":[{{\"Id\":\"net8-probe-{workerPid}\",\"LoadTime\":17}}]}}"));

            Assert.That(report.ProgramDataAdminSettingsExistsBeforeWrite, Is.True);
            Assert.That(report.ProgramDataAdminSettingsInitialRead, Is.EqualTo("{\"DisableAllAddIns\":false}"));
            Assert.That(report.ProgramDataAdminSettingsAfterWriteRead, Is.EqualTo($"{{\"DisableAllAddIns\":false,\"Source\":\"programdata-{workerPid}\"}}"));

            Assert.That(report.ProgramFilesAdminSettingsExistsBeforeWrite, Is.True);
            Assert.That(report.ProgramFilesAdminSettingsInitialRead, Is.EqualTo("{\"DisableAllAddIns\":false}"));
            Assert.That(report.ProgramFilesAdminSettingsNtCreateRead, Is.EqualTo("{\"DisableAllAddIns\":false}"));
            Assert.That(report.ProgramFilesAdminSettingsAfterWriteRead, Is.EqualTo($"{{\"DisableAllAddIns\":false,\"Source\":\"programfiles-{workerPid}\"}}"));

            // Verify writes landed in disposable per-PID profile folder.
            string virtualAddInsSettingsPath = Path.Combine(pidProfileDir, "AddInsSettings.json");
            string virtualAdminSettingsPath = Path.Combine(pidProfileDir, "AdminSettings.json");
            Assert.That(File.Exists(virtualAddInsSettingsPath), Is.True);
            Assert.That(File.Exists(virtualAdminSettingsPath), Is.True);
            Assert.That(await File.ReadAllTextAsync(virtualAddInsSettingsPath), Does.Contain($"net8-probe-{workerPid}"));
            Assert.That(await File.ReadAllTextAsync(virtualAdminSettingsPath), Does.Contain($"programfiles-{workerPid}"));

            // Verify host mock settings files remained 100% unmutated!
            string appDataSettingsFile = Path.Combine(_appDataRoot, "Autodesk", "Revit", "Autodesk Revit 2027", "AddinsData", "AddInsSettings.json");
            string programDataAdminFile = Path.Combine(_programDataRoot, "Autodesk", "RVT 2027", "Admin", "AdminSettings.json");
            string programFilesAdminFile = Path.Combine(_programFilesRoot, "Autodesk", "Revit", "Admin", "2027", "AdminSettings.json");
            Assert.That(await File.ReadAllTextAsync(appDataSettingsFile), Is.EqualTo(hostAppDataSettings));
            Assert.That(await File.ReadAllTextAsync(programDataAdminFile), Is.EqualTo(hostProgramDataAdminSettings));
            Assert.That(await File.ReadAllTextAsync(programFilesAdminFile), Is.EqualTo(hostProgramFilesAdminSettings));

            // 8. Grandchild does not inherit shim and sees unfiltered trees + original host settings.
            Assert.That(report.GrandchildError, Is.Null);
            Assert.That(report.GrandchildExitCode, Is.EqualTo(0));
            Assert.That(report.Grandchild, Is.Not.Null);

            ProbeReportDto grandchild = report.Grandchild!;
            Assert.That(grandchild.ProcessId, Is.Not.EqualTo(report.ProcessId));
            Assert.That(grandchild.HasRevitCleanShim, Is.False);
            Assert.That(grandchild.LoadedModules, Does.Not.Contain("RevitCleanShim.dll").IgnoreCase);
            Assert.That(grandchild.AppDataAddinFiles, Is.EqualTo(new[] { "0_Alpha.addin", "BluebeamAddin.addin", "Kinship.addin", "Synthetic2027.addin", "Z_Trailing.addin" }));
            Assert.That(grandchild.ProgramDataAddinFiles, Is.EqualTo(new[] { "0_Enscape.addin", "CTC-BIM-Project-Suite.addin", "Synthetic2027.addin" }));
            Assert.That(grandchild.ProgramFilesAddinFiles, Is.EqualTo(new[] { "Revit2027ThirdParty.addin", "Synthetic2027.addin" }));
            Assert.That(grandchild.AppDataPluginAllDirs, Is.EqualTo(new[] { "BIM 42 Align.bundle", "Bluebeam Software", "ricaun.RevitTest.Application.bundle" }));
            Assert.That(grandchild.ProgramDataBundleDirs, Is.EqualTo(new[] { "CTC-BIM-Project-Suite.bundle", "ThirdParty.bundle" }));
            Assert.That(grandchild.ProgramFilesBundleDirs, Is.EqualTo(new[] { "AutoCAD-MCP-Server-2027.bundle", "ThirdParty2027.bundle" }));
            Assert.That(grandchild.AppDataAddInsSettingsInitialRead, Is.EqualTo(hostAppDataSettings));
            Assert.That(grandchild.ProgramDataAdminSettingsInitialRead, Is.EqualTo(hostProgramDataAdminSettings));
            Assert.That(grandchild.ProgramFilesAdminSettingsInitialRead, Is.EqualTo(hostProgramFilesAdminSettings));
        }

        // Verify LaunchedWorkerProcess.DisposeAsync() cleaned up per-PID virtual profile directory.
        Assert.That(Directory.Exists(pidProfileDir), Is.False);
    }

    [Test]
    public async Task Net8Probe_Revit2026_DefaultTempProfile_AndUniformBundleRuleAcrossAllThreeRoots()
    {
        SeedMockTree(
            "2026",
            seedHostSettings: false,
            hostAppDataSettings: null,
            hostProgramDataAdminSettings: null,
            hostProgramFilesAdminSettings: null,
            seedRicaunInAllPluginsRoots: true);

        string outJsonPath = Path.Combine(_tempRoot, "probe2026_output.json");
        var launcher = new CleanDetoursProcessLauncher(_shimDllPath, _launcherDllPath);

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2026,
            ExecutablePath = _probeExePath,
            Arguments =
                $"--year 2026 " +
                $"--appdata \"{_appDataRoot}\" " +
                $"--programdata \"{_programDataRoot}\" " +
                $"--programfiles \"{_programFilesRoot}\" " +
                $"--out \"{outJsonPath}\" " +
                $"--no-nt-apis",
            AdditionalEnvironmentVariables = new Dictionary<string, string?>
            {
                ["SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT"] = _appDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT"] = _programDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT"] = _programFilesRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROFILE_DIR"] = null
            }
        };

        string defaultPidProfileDir;
        await using (LaunchedWorkerProcess worker = await launcher.LaunchAsync(request))
        {
            await worker.WaitForExitAsync();
            Assert.That(worker.ExitCode, Is.EqualTo(0));

            defaultPidProfileDir = Path.Combine(
                Path.GetTempPath(),
                "SyntheticCleanProfile",
                worker.ProcessId.ToString(CultureInfo.InvariantCulture));

            ProbeReportDto? report = JsonSerializer.Deserialize<ProbeReportDto>(await File.ReadAllTextAsync(outJsonPath));
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.HasRevitCleanShim, Is.True);

            // Uniform ApplicationPlugins behavior across all 3 roots: expose ONLY ricaun.RevitTest.Application.bundle.
            Assert.That(report.AppDataBundleDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
            Assert.That(report.ProgramDataBundleDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
            Assert.That(report.ProgramFilesBundleDirs, Is.EqualTo(new[] { "ricaun.RevitTest.Application.bundle" }));
            Assert.That(report.ProgramDataRicaunPackageExists, Is.True);
            Assert.That(report.ProgramFilesRicaunPackageExists, Is.True);
            Assert.That(report.ProgramDataThirdPartyBundleExists, Is.False);
            Assert.That(report.ProgramFilesThirdPartyBundleExists, Is.False);

            // Legacy 2023-2026 eTransmitForRevitDB.dll path untouched.
            Assert.That(report.LegacyETransmitDbExists, Is.True);
            Assert.That(report.LegacyETransmitDbText, Is.EqualTo("MOCK_ETRANSMIT_DB_2026"));

            // Settings virtualized into %TEMP%\SyntheticCleanProfile\<PID> even when host files do not exist.
            Assert.That(report.AppDataAddInsSettingsExistsBeforeWrite, Is.True);
            Assert.That(report.AppDataAddInsSettingsInitialRead, Is.EqualTo("{\"DisableAllAddIns\":false,\"AddInItemSettings\":[]}"));
            Assert.That(report.ProgramDataAdminSettingsExistsBeforeWrite, Is.True);
            Assert.That(report.ProgramDataAdminSettingsInitialRead, Is.EqualTo("{\"DisableAllAddIns\":false}"));
            Assert.That(Directory.Exists(defaultPidProfileDir), Is.True);
            Assert.That(File.Exists(Path.Combine(defaultPidProfileDir, "AddInsSettings.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(defaultPidProfileDir, "AdminSettings.json")), Is.True);

            // Verify missing host files were NOT created on disk.
            Assert.That(File.Exists(Path.Combine(_appDataRoot, "Autodesk", "Revit", "Autodesk Revit 2026", "AddinsData", "AddInsSettings.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(_programDataRoot, "Autodesk", "RVT 2026", "Admin", "AdminSettings.json")), Is.False);
        }

        Assert.That(Directory.Exists(defaultPidProfileDir), Is.False);
    }

    [Test]
    public async Task Net8Probe_InvalidRevitYear_FailsClosedAcrossAllRoots()
    {
        SeedMockTree(
            "2027",
            seedHostSettings: false,
            hostAppDataSettings: null,
            hostProgramDataAdminSettings: null,
            hostProgramFilesAdminSettings: null,
            seedRicaunInAllPluginsRoots: false);

        string outJsonPath = Path.Combine(_tempRoot, "invalid_year_output.json");
        var launcher = new CleanDetoursProcessLauncher(_shimDllPath, _launcherDllPath);

        var request = new WorkerLaunchRequest
        {
            RevitVersion = 2027,
            ExecutablePath = _probeExePath,
            Arguments =
                $"--year 2027 " +
                $"--appdata \"{_appDataRoot}\" " +
                $"--programdata \"{_programDataRoot}\" " +
                $"--programfiles \"{_programFilesRoot}\" " +
                $"--out \"{outJsonPath}\" " +
                $"--no-nt-apis",
            AdditionalEnvironmentVariables = new Dictionary<string, string?>
            {
                ["SYNTHETIC_CLEAN_SHIM_REVIT_YEAR"] = "invalid",
                ["SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT"] = _appDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT"] = _programDataRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT"] = _programFilesRoot,
                ["SYNTHETIC_CLEAN_SHIM_PROFILE_DIR"] = _profileRoot
            }
        };

        await using LaunchedWorkerProcess worker = await launcher.LaunchAsync(request);
        await worker.WaitForExitAsync();

        Assert.That(worker.ExitCode, Is.EqualTo(0));
        ProbeReportDto? report = JsonSerializer.Deserialize<ProbeReportDto>(await File.ReadAllTextAsync(outJsonPath));
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.HasRevitCleanShim, Is.True);
        Assert.That(report.AppDataAddinFiles, Is.Empty);
        Assert.That(report.ProgramDataAddinFiles, Is.Empty);
        Assert.That(report.ProgramFilesAddinFiles, Is.Empty);
        Assert.That(report.AppDataBundleDirs, Is.Empty);
        Assert.That(report.ProgramDataBundleDirs, Is.Empty);
        Assert.That(report.ProgramFilesBundleDirs, Is.Empty);
        Assert.That(report.InternalETransmitDbExists, Is.True);
    }

    private void SeedMockTree(
        string year,
        bool seedHostSettings,
        string? hostAppDataSettings,
        string? hostProgramDataAdminSettings,
        string? hostProgramFilesAdminSettings,
        bool seedRicaunInAllPluginsRoots)
    {
        string appDataAddins = Path.Combine(_appDataRoot, "Autodesk", "Revit", "Addins", year);
        Directory.CreateDirectory(appDataAddins);
        File.WriteAllText(Path.Combine(appDataAddins, "0_Alpha.addin"), "<RevitAddIns><FullClassName>Alpha.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(appDataAddins, "BluebeamAddin.addin"), "<RevitAddIns><FullClassName>Bluebeam.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(appDataAddins, "Kinship.addin"), "<RevitAddIns><FullClassName>Kinship.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(appDataAddins, $"Synthetic{year}.addin"), "<RevitAddIns><FullClassName>Synthetic.Bootstrap.BootstrapApp</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(appDataAddins, "Z_Trailing.addin"), "<RevitAddIns><FullClassName>Trailing.App</FullClassName></RevitAddIns>");
        Directory.CreateDirectory(Path.Combine(appDataAddins, "ThirdPartyAddinFolder"));

        string programDataAddins = Path.Combine(_programDataRoot, "Autodesk", "Revit", "Addins", year);
        Directory.CreateDirectory(programDataAddins);
        File.WriteAllText(Path.Combine(programDataAddins, $"Synthetic{year}.addin"), "<RevitAddIns><FullClassName>Synthetic.Core.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(programDataAddins, "CTC-BIM-Project-Suite.addin"), "<RevitAddIns><FullClassName>CTC.App</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(programDataAddins, "0_Enscape.addin"), "<RevitAddIns><FullClassName>Enscape.App</FullClassName></RevitAddIns>");
        string pdAddinBundle = Path.Combine(programDataAddins, "Vendor.bundle");
        Directory.CreateDirectory(pdAddinBundle);
        File.WriteAllText(Path.Combine(pdAddinBundle, "PackageContents.xml"), "<ApplicationPackage/>");

        string programFilesAddins = Path.Combine(_programFilesRoot, "Autodesk", "Revit", "Addins", year);
        Directory.CreateDirectory(programFilesAddins);
        File.WriteAllText(Path.Combine(programFilesAddins, $"Synthetic{year}.addin"), "<RevitAddIns><FullClassName>Synthetic.Core.StalePF</FullClassName></RevitAddIns>");
        File.WriteAllText(Path.Combine(programFilesAddins, "Revit2027ThirdParty.addin"), "<RevitAddIns><FullClassName>ThirdPartyPF.App</FullClassName></RevitAddIns>");
        string pfAddinBundle = Path.Combine(programFilesAddins, "Vendor2027.bundle");
        Directory.CreateDirectory(pfAddinBundle);
        File.WriteAllText(Path.Combine(pfAddinBundle, "PackageContents.xml"), "<ApplicationPackage/>");

        string appDataPlugins = Path.Combine(_appDataRoot, "Autodesk", "ApplicationPlugins");
        SeedRicaunBundle(appDataPlugins);
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
        if (seedRicaunInAllPluginsRoots)
        {
            SeedRicaunBundle(programDataPlugins);
        }

        string programFilesPlugins = Path.Combine(_programFilesRoot, "Autodesk", "ApplicationPlugins");
        string pfMcpBundle = Path.Combine(programFilesPlugins, "AutoCAD-MCP-Server-2027.bundle");
        Directory.CreateDirectory(pfMcpBundle);
        File.WriteAllText(Path.Combine(pfMcpBundle, "PackageContents.xml"), "<ApplicationPackage Name=\"AutoCAD-MCP\"/>");
        string pfThirdPartyBundle = Path.Combine(programFilesPlugins, "ThirdParty2027.bundle");
        Directory.CreateDirectory(pfThirdPartyBundle);
        File.WriteAllText(Path.Combine(pfThirdPartyBundle, "PackageContents.xml"), "<ApplicationPackage Name=\"ThirdParty2027\"/>");
        if (seedRicaunInAllPluginsRoots)
        {
            SeedRicaunBundle(programFilesPlugins);
        }

        string internalAddins = Path.Combine(_programFilesRoot, "Autodesk", $"Revit {year}", "AddIns", "Keynote");
        Directory.CreateDirectory(internalAddins);
        File.WriteAllText(Path.Combine(internalAddins, "Autodesk.Keynote.addin"), "<RevitAddIns><FullClassName>Autodesk.Keynote</FullClassName></RevitAddIns>");

        string internalETransmitDir = Path.Combine(_programFilesRoot, "Autodesk", $"Revit {year}", "AddIns", "eTransmitForRevit");
        Directory.CreateDirectory(internalETransmitDir);
        File.WriteAllText(Path.Combine(internalETransmitDir, "eTransmitForRevitDB.dll"), $"MOCK_ETRANSMIT_DB_{year}");

        string legacyETransmitDir = Path.Combine(_programFilesRoot, "Autodesk", $"eTransmit for Revit {year}");
        Directory.CreateDirectory(legacyETransmitDir);
        File.WriteAllText(Path.Combine(legacyETransmitDir, "eTransmitForRevitDB.dll"), $"MOCK_ETRANSMIT_DB_{year}");

        if (seedHostSettings)
        {
            string appDataSettingsDir = Path.Combine(_appDataRoot, "Autodesk", "Revit", $"Autodesk Revit {year}", "AddinsData");
            Directory.CreateDirectory(appDataSettingsDir);
            File.WriteAllText(Path.Combine(appDataSettingsDir, "AddInsSettings.json"), hostAppDataSettings ?? "{}");

            string programDataAdminDir = Path.Combine(_programDataRoot, "Autodesk", $"RVT {year}", "Admin");
            Directory.CreateDirectory(programDataAdminDir);
            File.WriteAllText(Path.Combine(programDataAdminDir, "AdminSettings.json"), hostProgramDataAdminSettings ?? "{}");

            string programFilesAdminDir = Path.Combine(_programFilesRoot, "Autodesk", "Revit", "Admin", year);
            Directory.CreateDirectory(programFilesAdminDir);
            File.WriteAllText(Path.Combine(programFilesAdminDir, "AdminSettings.json"), hostProgramFilesAdminSettings ?? "{}");
        }
    }

    private static void SeedRicaunBundle(string pluginsRoot)
    {
        string ricaunBundle = Path.Combine(pluginsRoot, "ricaun.RevitTest.Application.bundle");
        Directory.CreateDirectory(Path.Combine(ricaunBundle, "Contents"));
        File.WriteAllText(Path.Combine(ricaunBundle, "PackageContents.xml"), "<ApplicationPackage Name=\"ricaun.RevitTest.Application\"/>");
        File.WriteAllText(Path.Combine(ricaunBundle, "Contents", "ricaun.RevitTest.Application.dll"), "MOCK_RICAUN");
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
        [JsonPropertyName("processId")]
        public int ProcessId { get; init; }

        [JsonPropertyName("year")]
        public string Year { get; init; } = string.Empty;

        [JsonPropertyName("loadedModules")]
        public string[] LoadedModules { get; init; } = Array.Empty<string>();

        [JsonPropertyName("hasRevitCleanShim")]
        public bool HasRevitCleanShim { get; init; }

        [JsonPropertyName("appDataAddinsDirExists")]
        public bool AppDataAddinsDirExists { get; init; }

        [JsonPropertyName("programDataAddinsDirExists")]
        public bool ProgramDataAddinsDirExists { get; init; }

        [JsonPropertyName("programFilesAddinsDirExists")]
        public bool ProgramFilesAddinsDirExists { get; init; }

        [JsonPropertyName("appDataPluginsDirExists")]
        public bool AppDataPluginsDirExists { get; init; }

        [JsonPropertyName("programDataPluginsDirExists")]
        public bool ProgramDataPluginsDirExists { get; init; }

        [JsonPropertyName("programFilesPluginsDirExists")]
        public bool ProgramFilesPluginsDirExists { get; init; }

        [JsonPropertyName("appDataAddinFiles")]
        public string[] AppDataAddinFiles { get; init; } = Array.Empty<string>();

        [JsonPropertyName("appDataAddinAllEntries")]
        public string[] AppDataAddinAllEntries { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programDataAddinFiles")]
        public string[] ProgramDataAddinFiles { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programDataAddinBundleDirs")]
        public string[] ProgramDataAddinBundleDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programDataAddinAllEntries")]
        public string[] ProgramDataAddinAllEntries { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programFilesAddinFiles")]
        public string[] ProgramFilesAddinFiles { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programFilesAddinBundleDirs")]
        public string[] ProgramFilesAddinBundleDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programFilesAddinAllEntries")]
        public string[] ProgramFilesAddinAllEntries { get; init; } = Array.Empty<string>();

        [JsonPropertyName("appDataBundleDirs")]
        public string[] AppDataBundleDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("appDataPluginAllDirs")]
        public string[] AppDataPluginAllDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programDataBundleDirs")]
        public string[] ProgramDataBundleDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programDataPluginAllDirs")]
        public string[] ProgramDataPluginAllDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programFilesBundleDirs")]
        public string[] ProgramFilesBundleDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("programFilesPluginAllDirs")]
        public string[] ProgramFilesPluginAllDirs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("internalAddinFiles")]
        public string[] InternalAddinFiles { get; init; } = Array.Empty<string>();

        [JsonPropertyName("internalETransmitDbExists")]
        public bool InternalETransmitDbExists { get; init; }

        [JsonPropertyName("internalETransmitDbText")]
        public string? InternalETransmitDbText { get; init; }

        [JsonPropertyName("legacyETransmitDbExists")]
        public bool LegacyETransmitDbExists { get; init; }

        [JsonPropertyName("legacyETransmitDbText")]
        public string? LegacyETransmitDbText { get; init; }

        [JsonPropertyName("appDataSyntheticAddinExists")]
        public bool AppDataSyntheticAddinExists { get; init; }

        [JsonPropertyName("appDataSyntheticAddinText")]
        public string? AppDataSyntheticAddinText { get; init; }

        [JsonPropertyName("appDataThirdPartyAddinExists")]
        public bool AppDataThirdPartyAddinExists { get; init; }

        [JsonPropertyName("appDataThirdPartyAddinReadError")]
        public string? AppDataThirdPartyAddinReadError { get; init; }

        [JsonPropertyName("programDataSyntheticAddinExists")]
        public bool ProgramDataSyntheticAddinExists { get; init; }

        [JsonPropertyName("programDataSyntheticAddinReadError")]
        public string? ProgramDataSyntheticAddinReadError { get; init; }

        [JsonPropertyName("programDataThirdPartyAddinExists")]
        public bool ProgramDataThirdPartyAddinExists { get; init; }

        [JsonPropertyName("programDataThirdPartyAddinReadError")]
        public string? ProgramDataThirdPartyAddinReadError { get; init; }

        [JsonPropertyName("programFilesSyntheticAddinExists")]
        public bool ProgramFilesSyntheticAddinExists { get; init; }

        [JsonPropertyName("programFilesSyntheticAddinReadError")]
        public string? ProgramFilesSyntheticAddinReadError { get; init; }

        [JsonPropertyName("programFilesThirdPartyAddinExists")]
        public bool ProgramFilesThirdPartyAddinExists { get; init; }

        [JsonPropertyName("programFilesThirdPartyAddinReadError")]
        public string? ProgramFilesThirdPartyAddinReadError { get; init; }

        [JsonPropertyName("appDataRicaunPackageExists")]
        public bool AppDataRicaunPackageExists { get; init; }

        [JsonPropertyName("appDataRicaunPackageText")]
        public string? AppDataRicaunPackageText { get; init; }

        [JsonPropertyName("appDataBluebeamDirExists")]
        public bool AppDataBluebeamDirExists { get; init; }

        [JsonPropertyName("appDataBluebeamManifestExists")]
        public bool AppDataBluebeamManifestExists { get; init; }

        [JsonPropertyName("appDataBluebeamReadError")]
        public string? AppDataBluebeamReadError { get; init; }

        [JsonPropertyName("programDataRicaunPackageExists")]
        public bool ProgramDataRicaunPackageExists { get; init; }

        [JsonPropertyName("programDataThirdPartyBundleExists")]
        public bool ProgramDataThirdPartyBundleExists { get; init; }

        [JsonPropertyName("programDataThirdPartyBundleManifestExists")]
        public bool ProgramDataThirdPartyBundleManifestExists { get; init; }

        [JsonPropertyName("programFilesRicaunPackageExists")]
        public bool ProgramFilesRicaunPackageExists { get; init; }

        [JsonPropertyName("programFilesThirdPartyBundleExists")]
        public bool ProgramFilesThirdPartyBundleExists { get; init; }

        [JsonPropertyName("programFilesThirdPartyBundleManifestExists")]
        public bool ProgramFilesThirdPartyBundleManifestExists { get; init; }

        [JsonPropertyName("ntQuerySingleEntryAppDataClasses")]
        public Dictionary<string, string[]> NtQuerySingleEntryAppDataClasses { get; init; } = new();

        [JsonPropertyName("ntQueryExMultiEntryAppDataClasses")]
        public Dictionary<string, string[]> NtQueryExMultiEntryAppDataClasses { get; init; } = new();

        [JsonPropertyName("ntQueryNullPatternAllEntriesAppDataClasses")]
        public Dictionary<string, string[]> NtQueryNullPatternAllEntriesAppDataClasses { get; init; } = new();

        [JsonPropertyName("ntQueryAppDataSecondCallAfterMatchStatus")]
        public uint NtQueryAppDataSecondCallAfterMatchStatus { get; init; }

        [JsonPropertyName("ntQueryProgramDataEmptyFirstStatus")]
        public uint NtQueryProgramDataEmptyFirstStatus { get; init; }

        [JsonPropertyName("ntQueryProgramFilesExEmptyFirstStatus")]
        public uint NtQueryProgramFilesExEmptyFirstStatus { get; init; }

        [JsonPropertyName("ntOpenAppDataSyntheticStatus")]
        public uint NtOpenAppDataSyntheticStatus { get; init; }

        [JsonPropertyName("ntOpenProgramFilesThirdPartyAddinStatus")]
        public uint NtOpenProgramFilesThirdPartyAddinStatus { get; init; }

        [JsonPropertyName("ntCreateRelativeProgramDataSyntheticStatus")]
        public uint NtCreateRelativeProgramDataSyntheticStatus { get; init; }

        [JsonPropertyName("ntOpenRelativeAppDataSyntheticStatus")]
        public uint NtOpenRelativeAppDataSyntheticStatus { get; init; }

        [JsonPropertyName("appDataAddInsSettingsExistsBeforeWrite")]
        public bool AppDataAddInsSettingsExistsBeforeWrite { get; init; }

        [JsonPropertyName("appDataAddInsSettingsInitialRead")]
        public string? AppDataAddInsSettingsInitialRead { get; init; }

        [JsonPropertyName("appDataAddInsSettingsNtOpenRead")]
        public string? AppDataAddInsSettingsNtOpenRead { get; init; }

        [JsonPropertyName("appDataAddInsSettingsAfterWriteRead")]
        public string? AppDataAddInsSettingsAfterWriteRead { get; init; }

        [JsonPropertyName("programDataAdminSettingsExistsBeforeWrite")]
        public bool ProgramDataAdminSettingsExistsBeforeWrite { get; init; }

        [JsonPropertyName("programDataAdminSettingsInitialRead")]
        public string? ProgramDataAdminSettingsInitialRead { get; init; }

        [JsonPropertyName("programDataAdminSettingsAfterWriteRead")]
        public string? ProgramDataAdminSettingsAfterWriteRead { get; init; }

        [JsonPropertyName("programFilesAdminSettingsExistsBeforeWrite")]
        public bool ProgramFilesAdminSettingsExistsBeforeWrite { get; init; }

        [JsonPropertyName("programFilesAdminSettingsInitialRead")]
        public string? ProgramFilesAdminSettingsInitialRead { get; init; }

        [JsonPropertyName("programFilesAdminSettingsNtCreateRead")]
        public string? ProgramFilesAdminSettingsNtCreateRead { get; init; }

        [JsonPropertyName("programFilesAdminSettingsAfterWriteRead")]
        public string? ProgramFilesAdminSettingsAfterWriteRead { get; init; }

        [JsonPropertyName("grandchildExitCode")]
        public int? GrandchildExitCode { get; init; }

        [JsonPropertyName("grandchildError")]
        public string? GrandchildError { get; init; }

        [JsonPropertyName("grandchild")]
        public ProbeReportDto? Grandchild { get; init; }
    }
}
