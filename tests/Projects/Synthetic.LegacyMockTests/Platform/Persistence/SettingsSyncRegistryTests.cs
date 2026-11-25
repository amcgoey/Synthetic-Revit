using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Features.MaterialManagement.Settings;
using Synthetic.Features.StandardsManagement.Settings;
using Synthetic.Features.ViewManagement.Settings;
using Synthetic.Features.Worksets.Settings;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Platform.Host;
using Synthetic.Platform.Settings;
using SyntheticTests.Helpers;

namespace SyntheticTests.Infrastructure.Persistence
{
    [TestFixture]
    [QuarantineTest]
    public class SettingsSyncRegistryTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            HostSettingsRegistrar.EnsureRegistered();
        }

        private static readonly string[] ExpectedSyncedModuleKeys = new[]
        {
            WorksetSettings.Name,
            ViewAutoNumSettings.Name,
            MaterialLibrarySettings.Name,
            ProjectMaterialSettings.Name,
            FileUtilitySettings.Name,
            StandardsSettings.Name,
            DetailItemFactorySettings.Name,
            ShadowStudySettings.Name
        };

        private static string FindTemplateJsonPath()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string candidate = Path.Combine(
                projectRoot,
                "src",
                "Platform",
                "Settings",
                "Addin",
                "Defaults",
                "SyntheticSettings.template.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            throw new FileNotFoundException(
                "Could not locate src/Platform/Settings/Addin/Defaults/SyntheticSettings.template.json.",
                candidate);
        }

        private static Dictionary<string, object> CreateDefaultDocumentSettings()
        {
            var dict = new Dictionary<string, object>
            {
                { WorksetSettings.Name, new WorksetSettings().Defaults() },
                { ViewAutoNumSettings.Name, new ViewAutoNumSettings().Defaults() },
                { MaterialLibrarySettings.Name, new MaterialLibrarySettings().Defaults() },
                { ProjectMaterialSettings.Name, new ProjectMaterialSettings().Defaults() },
                { FileUtilitySettings.Name, new FileUtilitySettings().Defaults() },
                { StandardsSettings.Name, new StandardsSettings().Defaults() },
                { DetailItemFactorySettings.Name, new DetailItemFactorySettings().Defaults() },
                { ShadowStudySettings.Name, new ShadowStudySettings().Defaults() }
            };
            return dict;
        }

        [Test]
        public void SyncedModules_ContainsExpectedEightModules_AndExcludesSyncSettings()
        {
            var modules = SettingsModuleRegistry.SyncedModules;
            Assert.IsNotNull(modules);

            var actualKeys = modules.Select(m => m.ModuleKey).ToList();
            CollectionAssert.AreEquivalent(ExpectedSyncedModuleKeys, actualKeys);
            Assert.AreEqual(ExpectedSyncedModuleKeys.Length, actualKeys.Count);
            Assert.IsFalse(actualKeys.Contains(SyncSettings.Name));

            foreach (var descriptor in modules)
            {
                Assert.IsNotNull(descriptor.SettingsType);
                Assert.IsTrue(typeof(ISettingModule).IsAssignableFrom(descriptor.SettingsType),
                    $"{descriptor.SettingsType.Name} must implement ISettingModule.");

                ISettingModule created = descriptor.CreateDefault();
                Assert.IsNotNull(created);
                Assert.IsInstanceOf(descriptor.SettingsType, created);
                Assert.AreEqual(descriptor.ModuleKey, created.ModuleKey);
            }
        }

        [Test]
        public void AssemblyReflection_AllConcreteSettingModules_AreRegisteredInSyncedModulesConfigAndTemplate()
        {
            HostSettingsRegistrar.EnsureRegistered();
            var settingModuleInterface = typeof(ISettingModule);
            var assemblies = new[]
            {
                typeof(ISettingModule).Assembly,
                typeof(App).Assembly
            }.Distinct();

            var rawTypes = new List<Type?>();
            foreach (System.Reflection.Assembly assembly in assemblies)
            {
                try
                {
                    rawTypes.AddRange(assembly.GetTypes());
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    rawTypes.AddRange(ex.Types);
                }
            }

            List<Type> concreteSettingTypes = rawTypes
                .Where(t => t != null && !t.IsAbstract && !t.IsInterface && settingModuleInterface.IsAssignableFrom(t))
                .Cast<Type>()
                .Distinct()
                .Where(t => t != typeof(SyncSettings))
                .ToList();

            Assert.IsNotEmpty(concreteSettingTypes, "Expected at least one concrete ISettingModule type.");

            var expectedKeysByType = new List<string>(concreteSettingTypes.Count);
            foreach (Type type in concreteSettingTypes)
            {
                ISettingModule? instance = Activator.CreateInstance(type) as ISettingModule;
                Assert.IsNotNull(instance, $"Failed to instantiate {type.FullName}.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(instance!.ModuleKey),
                    $"{type.FullName}.ModuleKey must not be blank.");
                expectedKeysByType.Add(instance.ModuleKey);
            }

            List<string> registryKeys = SettingsModuleRegistry.SyncedModules
                .Select(d => d.ModuleKey)
                .ToList();
            CollectionAssert.AreEquivalent(expectedKeysByType, registryKeys,
                "SettingsModuleRegistry.SyncedModules must match every concrete ISettingModule in the assembly (excluding SyncSettings).");

            var config = new Config().Defaults();
            Assert.IsNotNull(config.settings);
            CollectionAssert.AreEquivalent(expectedKeysByType, config.settings.Keys,
                "Config.Defaults() must populate every concrete ISettingModule in the assembly (excluding SyncSettings).");

            string templatePath = FindTemplateJsonPath();
            Config? templateConfig = Config.ReadFromFile(templatePath);
            Assert.IsNotNull(templateConfig);
            Assert.IsNotNull(templateConfig!.settings);
            CollectionAssert.AreEquivalent(expectedKeysByType, templateConfig.settings.Keys,
                "SyntheticSettings.template.json must cover every concrete ISettingModule in the assembly (excluding SyncSettings).");
        }

        [Test]
        public void HasSyncDrift_LegacyFourKeyJsonMatchingDocument_ReturnsFalse()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var legacySubset = new Dictionary<string, object>
            {
                { WorksetSettings.Name, docSettings[WorksetSettings.Name] },
                { ViewAutoNumSettings.Name, docSettings[ViewAutoNumSettings.Name] },
                { MaterialLibrarySettings.Name, docSettings[MaterialLibrarySettings.Name] },
                { ProjectMaterialSettings.Name, docSettings[ProjectMaterialSettings.Name] }
            };

            string legacyJson = JsonConvert.SerializeObject(legacySubset, Formatting.Indented);

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(legacyJson, docSettings);
            Assert.IsFalse(hasDrift);
        }

        [Test]
        public void HasSyncDrift_ModifiedKeyInExternalJson_ReturnsTrue()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var modifiedExternal = new Dictionary<string, object>(docSettings)
            {
                [StandardsSettings.Name] = new StandardsSettings
                {
                    StandardsFilePath = @"C:\Standards\firm-standards.json"
                }
            };

            string externalJson = JsonConvert.SerializeObject(modifiedExternal, Formatting.Indented);

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(externalJson, docSettings);
            Assert.IsTrue(hasDrift);
        }

        [Test]
        public void HasSyncDrift_ExternalJsonWithSyncSettingsOnlyDifference_ReturnsFalse()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var externalDict = new Dictionary<string, object>(docSettings)
            {
                [SyncSettings.Name] = new SyncSettings
                {
                    LinkedFilePath = @"C:\Firm\other-linked.json"
                }
            };

            string externalJson = JsonConvert.SerializeObject(externalDict, Formatting.Indented);

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(externalJson, docSettings);
            Assert.IsFalse(hasDrift);
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("{ invalid json")]
        public void HasSyncDrift_NullOrInvalidJson_ReturnsTrue(string externalJson)
        {
            var docSettings = CreateDefaultDocumentSettings();

            bool hasDrift = SettingsModuleRegistry.HasSyncDrift(externalJson, docSettings);
            Assert.IsTrue(hasDrift);
        }

        [Test]
        public void HasSyncDrift_LegacyJsonMissingShadowStudy_ReturnsFalse_AndModifiedShadowStudyReturnsTrue()
        {
            var docSettings = CreateDefaultDocumentSettings();
            var legacyWithoutShadowStudy = new Dictionary<string, object>(docSettings);
            legacyWithoutShadowStudy.Remove(ShadowStudySettings.Name);

            string legacyJson = JsonConvert.SerializeObject(legacyWithoutShadowStudy, Formatting.Indented);
            Assert.IsFalse(SettingsModuleRegistry.HasSyncDrift(legacyJson, docSettings));

            var modifiedShadowStudy = new Dictionary<string, object>(docSettings)
            {
                [ShadowStudySettings.Name] = new ShadowStudySettings
                {
                    DefaultTimeIncrementMinutes = 30,
                    DefaultMinSunAltitudeDegrees = 10.0,
                    DefaultMaxCompositeDarknessPercent = 65,
                    DefaultPreserveVisualStyle = true,
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Studio Firm Preset",
                            TimeIncrementMinutes = 15,
                            MinSunAltitudeDegrees = 8.0,
                            MaxCompositeDarknessPercent = 70,
                            PreserveVisualStyle = true,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec { AstronomicalEvent = AstronomicalEventKind.SummerSolstice }
                            }
                        }
                    }
                }
            };

            string driftedJson = JsonConvert.SerializeObject(modifiedShadowStudy, Formatting.Indented);
            Assert.IsTrue(SettingsModuleRegistry.HasSyncDrift(driftedJson, docSettings));
        }

        [Test]
        public void ConfigDefaults_PopulatesAllEightSyncedModules()
        {
            var config = new Config().Defaults();

            Assert.IsNotNull(config.settings);
            CollectionAssert.AreEquivalent(ExpectedSyncedModuleKeys, config.settings.Keys);
            Assert.IsFalse(config.Contains(SyncSettings.Name));
        }

        [Test]
        public void TemplateJson_CoversAllEightSyncedModules()
        {
            string templatePath = FindTemplateJsonPath();
            Config? templateConfig = Config.ReadFromFile(templatePath);

            Assert.IsNotNull(templateConfig);
            Assert.IsNotNull(templateConfig!.settings);
            foreach (string key in ExpectedSyncedModuleKeys)
            {
                Assert.IsTrue(templateConfig.Contains(key),
                    $"SyntheticSettings.template.json is missing '{key}'.");
            }

            ShadowStudySettings shadowDefaults = templateConfig.GetSettings<ShadowStudySettings>(ShadowStudySettings.Name);
            Assert.AreEqual(1, shadowDefaults.SchemaVersion);
            Assert.AreEqual(60, shadowDefaults.DefaultTimeIncrementMinutes);
            Assert.AreEqual(5.0, shadowDefaults.DefaultMinSunAltitudeDegrees);
            Assert.AreEqual(80, shadowDefaults.DefaultMaxCompositeDarknessPercent);
            Assert.IsFalse(shadowDefaults.DefaultPreserveVisualStyle);
            Assert.IsNotNull(shadowDefaults.UserPresets);
            Assert.AreEqual(0, shadowDefaults.UserPresets.Count);
        }

        [Test]
        public void SettingsManagerClearCache_DoesNotThrowWhenCalledWithNull()
        {
            Assert.DoesNotThrow(() => SettingsManager.ClearCache());
        }

        [Test]
        public void RibbonManager_ResolveActiveVersion_PrioritizesHost_FallsBackToMetadata_AndFailsFast()
        {
            var uiAppWithHost = new UIControlledApplication();
            uiAppWithHost.ControlledApplication.VersionNumber = "2027";
            Assert.AreEqual(2027, RibbonManager.ResolveActiveVersion(uiAppWithHost));

            var uiAppWithoutHost = new UIControlledApplication();
            uiAppWithoutHost.ControlledApplication.VersionNumber = "   ";
            Assert.AreEqual(2026, RibbonManager.ResolveActiveVersion(uiAppWithoutHost));
            Assert.AreEqual(2026, RibbonManager.ResolveActiveVersion((UIControlledApplication?)null));

            Assembly missingMetadata = CreateDynamicAssembly(("OtherKey", "2026"));
            Assembly invalidMetadata = CreateDynamicAssembly(("RevitVersion", "invalid"));
            Assembly twoDigitMetadata = CreateDynamicAssembly(("RevitVersion", "26"));

            InvalidOperationException missingEx = Assert.Throws<InvalidOperationException>(
                () => RibbonManager.ResolveActiveVersion((string?)null, missingMetadata))!;
            StringAssert.Contains("RevitVersion", missingEx.Message);

            foreach (Assembly badAsm in new[] { invalidMetadata, twoDigitMetadata })
            {
                InvalidOperationException invalidEx = Assert.Throws<InvalidOperationException>(
                    () => RibbonManager.ResolveActiveVersion(string.Empty, badAsm))!;
                StringAssert.Contains("RevitVersion", invalidEx.Message);
            }
        }

        [Test]
        public void BootstrapApp_EmitsRevitVersionMetadata_PrioritizesHost_FallsBack_AndFailsFastWithDiagnostics()
        {
            string bootstrapDllPath = FindBootstrap2026AssemblyPath();
            Assembly bootstrapAssembly = LoadBootstrapAssembly(bootstrapDllPath);

            AssemblyMetadataAttribute? metadata = bootstrapAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attr => string.Equals(attr.Key, "RevitVersion", StringComparison.Ordinal));
            Assert.IsNotNull(metadata, "Synthetic.Bootstrap2026 must emit [AssemblyMetadata(\"RevitVersion\", \"2026\")].");
            Assert.AreEqual("2026", metadata!.Value);

            Type bootstrapAppType = bootstrapAssembly.GetType("Synthetic.Bootstrap.BootstrapApp", throwOnError: true)!;
            Type hooksType = bootstrapAssembly.GetType("Synthetic.Bootstrap.ShadowCacheHooks", throwOnError: true)!;
            Type launchPlanType = bootstrapAssembly.GetType("Synthetic.Bootstrap.LaunchPlan", throwOnError: true)!;

            MethodInfo prepareHostLaunch = bootstrapAppType.GetMethod(
                "PrepareHostLaunch",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo onStartupCore = bootstrapAppType.GetMethod(
                "OnStartupCore",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            string tempRoot = Path.Combine(Path.GetTempPath(), "SyntheticBootstrapMetadataTests", Guid.NewGuid().ToString("N"));
            try
            {
                string payload2026Dir = Path.Combine(tempRoot, "Payload", "2026");
                string payload2027Dir = Path.Combine(tempRoot, "Payload", "2027");
                string localAppDataDir = Path.Combine(tempRoot, "LocalAppData");
                Directory.CreateDirectory(payload2026Dir);
                Directory.CreateDirectory(payload2027Dir);
                Directory.CreateDirectory(localAppDataDir);
                File.WriteAllText(Path.Combine(payload2026Dir, "Synthetic2026.dll"), "payload-2026");
                File.WriteAllText(Path.Combine(payload2027Dir, "Synthetic2027.dll"), "payload-2027");

                string fakeBootstrapPath = Path.Combine(tempRoot, "Bootstrap", "v1", "Synthetic.Bootstrap2026.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(fakeBootstrapPath)!);

                object CreateHooks(string? payloadOverride = null)
                {
                    object hooks = Activator.CreateInstance(hooksType)!;
                    hooksType.GetProperty("LocalAppDataDir")!.SetValue(hooks, localAppDataDir);
                    if (payloadOverride != null)
                    {
                        hooksType.GetProperty("PayloadDirOverride")!.SetValue(hooks, payloadOverride);
                    }
                    hooksType.GetProperty("CurrentProcessId")!.SetValue(hooks, 12345);
                    hooksType.GetProperty("CurrentProcessUtcStartTicks")!.SetValue(hooks, 638999999999999999L);
                    hooksType.GetProperty("GetEnvironmentVariable")!.SetValue(
                        hooks,
                        (Func<string, string?>)(_ => null));
                    return hooks;
                }

                // 1. Live host version takes priority over compiled metadata ("2027" wins over "2026")
                object?[] liveHostArgs = { "2027", bootstrapAssembly, fakeBootstrapPath, null, CreateHooks() };
                object livePlan = prepareHostLaunch.Invoke(null, liveHostArgs)!;
                Assert.AreEqual("2027", liveHostArgs[3]);
                Assert.AreEqual("ShadowCopy", launchPlanType.GetProperty("Mode")!.GetValue(livePlan)!.ToString());
                StringAssert.EndsWith("Synthetic2027.dll", (string)launchPlanType.GetProperty("TargetAssemblyPath")!.GetValue(livePlan)!);

                // 2. Missing host version falls back cleanly to assembly metadata ("2026")
                object?[] fallbackArgs = { null, bootstrapAssembly, fakeBootstrapPath, null, CreateHooks() };
                object fallbackPlan = prepareHostLaunch.Invoke(null, fallbackArgs)!;
                Assert.AreEqual("2026", fallbackArgs[3]);
                Assert.AreEqual("ShadowCopy", launchPlanType.GetProperty("Mode")!.GetValue(fallbackPlan)!.ToString());
                StringAssert.EndsWith("Synthetic2026.dll", (string)launchPlanType.GetProperty("TargetAssemblyPath")!.GetValue(fallbackPlan)!);

                // 3. Missing or unparseable metadata returns Failed LaunchPlan with diagnostics (no silent 2026 fallback)
                Assembly missingMetaAsm = CreateDynamicAssembly(("Other", "2026"));
                Assembly badMetaAsm = CreateDynamicAssembly(("RevitVersion", "bad-year"));

                foreach (Assembly brokenAsm in new[] { missingMetaAsm, badMetaAsm })
                {
                    object?[] failArgs = { "   ", brokenAsm, fakeBootstrapPath, null, CreateHooks() };
                    object failPlan = prepareHostLaunch.Invoke(null, failArgs)!;
                    Assert.AreEqual(string.Empty, failArgs[3]);
                    Assert.AreEqual("Failed", launchPlanType.GetProperty("Mode")!.GetValue(failPlan)!.ToString());
                    string errorMessage = (string)launchPlanType.GetProperty("ErrorMessage")!.GetValue(failPlan)!;
                    StringAssert.Contains("RevitVersion", errorMessage);
                    var diagnostics = (IReadOnlyList<string>)launchPlanType.GetProperty("Diagnostics")!.GetValue(failPlan)!;
                    Assert.IsNotEmpty(diagnostics);
                }

                // 4. OnStartupCore returns Result.Failed and records journal + log diagnostics when metadata is missing
                object bootstrapInstance = Activator.CreateInstance(bootstrapAppType)!;
                var uiApp = new UIControlledApplication();
                uiApp.ControlledApplication.VersionNumber = string.Empty;

                // Synthetic.Bootstrap2026 compiles against real RevitAPIUI.dll where Result.Failed == -1
                // (whereas RevitAPIUIMock defines Succeeded = -1, Failed = 0).
                int startupResultCode = Convert.ToInt32(onStartupCore.Invoke(
                    bootstrapInstance,
                    new object?[] { uiApp, missingMetaAsm, CreateHooks() })!);
                Assert.AreEqual(-1, startupResultCode, "Expected real RevitAPIUI Result.Failed (-1) on missing metadata.");
                Assert.IsTrue(
                    uiApp.ControlledApplication.JournalComments.Any(c => c.StartsWith("SYNTHETIC_BOOTSTRAP_ERROR:", StringComparison.Ordinal)),
                    "Expected SYNTHETIC_BOOTSTRAP_ERROR journal comment on missing metadata failure.");

                string logsDir = Path.Combine(localAppDataDir, "Synthetic", "Logs");
                Assert.IsTrue(Directory.Exists(logsDir), "Expected diagnostic logs directory to be created.");
                string[] logFiles = Directory.GetFiles(logsDir, "Synthetic_Bootstrap_unknown_*.log");
                Assert.IsNotEmpty(logFiles, "Expected bootstrap diagnostic log file on metadata failure.");
                string logText = File.ReadAllText(logFiles[0]);
                StringAssert.Contains("RevitVersion: unknown", logText);
                StringAssert.Contains("RevitVersion", logText);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup
                }
            }
        }

        private static Assembly LoadBootstrapAssembly(string bootstrapDllPath)
        {
            foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, "Synthetic.Bootstrap2026", StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(bootstrapDllPath);
        }

        private static string FindBootstrap2026AssemblyPath()
        {
            string testDir = Path.GetDirectoryName(typeof(SettingsSyncRegistryTests).Assembly.Location)!;
            string dirName = Path.GetFileName(testDir);
            if (dirName.StartsWith("Synthetic.LegacyMockTests_", StringComparison.OrdinalIgnoreCase))
            {
                string runSuffix = dirName.Substring("Synthetic.LegacyMockTests_".Length);
                string isolatedCandidate = Path.GetFullPath(Path.Combine(
                    testDir,
                    "..",
                    $"Synthetic.Bootstrap2026_{runSuffix}",
                    "Synthetic.Bootstrap2026.dll"));
                if (File.Exists(isolatedCandidate))
                {
                    return isolatedCandidate;
                }
            }

            string repoRoot = TestPathHelper.GetProjectRoot();
            string standardCandidate = Path.Combine(
                repoRoot,
                "output",
                "Synthetic",
                "Bootstrap",
                "v1",
                "Synthetic.Bootstrap2026.dll");
            if (File.Exists(standardCandidate))
            {
                return standardCandidate;
            }

            throw new FileNotFoundException(
                $"Could not locate compiled Synthetic.Bootstrap2026.dll near '{testDir}' or '{standardCandidate}'.");
        }

        private static Assembly CreateDynamicAssembly(params (string Key, string Value)[] metadataEntries)
        {
            AssemblyName name = new AssemblyName("DynamicHostMetadataTest_" + Guid.NewGuid().ToString("N"));
            AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            if (metadataEntries != null && metadataEntries.Length > 0)
            {
                ConstructorInfo ctor = typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!;
                foreach ((string key, string value) in metadataEntries)
                {
                    builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, new object[] { key, value }));
                }
            }

            return builder;
        }
    }
}
