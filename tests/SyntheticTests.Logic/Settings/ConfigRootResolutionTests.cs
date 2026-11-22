using System;
using System.IO;
using NUnit.Framework;
using Synthetic.Bootstrap;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Settings;

namespace SyntheticTests.Settings
{
    [TestFixture]
    [NonParallelizable]
    public class ConfigRootResolutionTests
    {
        private string? _originalEnvRoot;
        private object? _originalAppContextRoot;

        [SetUp]
        public void SetUp()
        {
            _originalEnvRoot = Environment.GetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT");
            _originalAppContextRoot = AppContext.GetData("SYNTHETIC_ADDIN_ROOT");
            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", null);
            AppContext.SetData("SYNTHETIC_ADDIN_ROOT", null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", _originalEnvRoot);
            AppContext.SetData("SYNTHETIC_ADDIN_ROOT", _originalAppContextRoot);
        }

        [Test]
        public void AddinPath_EnvironmentVariableTakesPrecedenceOverAppContextAndAssemblyDir()
        {
            string envDir = Path.Combine(Path.GetTempPath(), "SyntheticEnvRoot");
            string ctxDir = Path.Combine(Path.GetTempPath(), "SyntheticCtxRoot");

            AppContext.SetData("SYNTHETIC_ADDIN_ROOT", ctxDir);
            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", envDir);

            Assert.AreEqual(envDir, Config.addinPath);
            Assert.AreEqual(Path.Combine(envDir, Config.defaultSettingsFile), Config.defaultSettingsPath);
            Assert.AreEqual(Path.Combine(envDir, Config.defaultSettingsFile), Config.FilePathDefaults());
        }

        [Test]
        public void AddinPath_FallsBackToAppContextWhenEnvVarIsMissingOrWhitespace()
        {
            string ctxDir = Path.Combine(Path.GetTempPath(), "SyntheticCtxRoot");

            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", "   ");
            AppContext.SetData("SYNTHETIC_ADDIN_ROOT", ctxDir);

            Assert.AreEqual(ctxDir, Config.addinPath);
        }

        [Test]
        public void AddinPath_FallsBackToAssemblyDirectoryWhenBothOverridesAreUnset()
        {
            string expectedAssemblyDir = Path.GetDirectoryName(typeof(Config).Assembly.Location) ?? string.Empty;

            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", null);
            AppContext.SetData("SYNTHETIC_ADDIN_ROOT", "  ");

            Assert.AreEqual(expectedAssemblyDir, Config.addinPath);
        }

        [Test]
        public void DiagnosticLogsDir_ResolvesToLocalAppDataIndependentOfAddinRoot()
        {
            string envDir = Path.Combine(Path.GetTempPath(), "SyntheticEnvRootLogsTest");
            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", envDir);

            string expectedLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string expectedLogsDir = Path.Combine(expectedLocalAppData, "Synthetic", "Logs");

            string resolvedLogsDir = ShadowCacheManager.ResolveDiagnosticLogsDir();
            Assert.AreEqual(expectedLogsDir, resolvedLogsDir);
            Assert.AreNotEqual(envDir, resolvedLogsDir);
            Assert.AreEqual(envDir, Config.addinPath);

            string customLocalAppData = Path.Combine(Path.GetTempPath(), "CustomLocalAppData");
            Assert.AreEqual(
                Path.Combine(customLocalAppData, "Synthetic", "Logs"),
                ShadowCacheManager.ResolveDiagnosticLogsDir(customLocalAppData));
        }

        [Test]
        public void Step6ResultsLogPath_ResolvesToLocalAppDataIndependentOfAddinRoot()
        {
            string envDir = Path.Combine(Path.GetTempPath(), "SyntheticEnvRootStep6Test");
            Environment.SetEnvironmentVariable("SYNTHETIC_ADDIN_ROOT", envDir);

            string expectedLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string expectedLogPath = Path.Combine(
                expectedLocalAppData,
                "Synthetic",
                "Logs",
                "MergeDuplicates_Step6_ExecutionResult.json");

            string resolvedLogPath = ProcessMergeEventHandler.GetStep6ResultsLogPath();
            Assert.AreEqual(expectedLogPath, resolvedLogPath);
            Assert.AreNotEqual(envDir, Path.GetDirectoryName(resolvedLogPath));
            Assert.AreEqual(envDir, Config.addinPath);

            string customLocalAppData = Path.Combine(Path.GetTempPath(), "CustomStep6LocalAppData");
            Assert.AreEqual(
                Path.Combine(customLocalAppData, "Synthetic", "Logs", "MergeDuplicates_Step6_ExecutionResult.json"),
                ProcessMergeEventHandler.GetStep6ResultsLogPath(customLocalAppData));
        }
    }
}
