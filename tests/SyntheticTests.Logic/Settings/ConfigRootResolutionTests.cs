using System;
using System.IO;
using NUnit.Framework;
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
    }
}
