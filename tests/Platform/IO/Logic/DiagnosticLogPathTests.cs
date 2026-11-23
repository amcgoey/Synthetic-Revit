using System;
using System.IO;
using NUnit.Framework;
using Synthetic.Platform.IO;

namespace SyntheticTests.Platform.IO
{
    [TestFixture]
    [UnitTest]
    public class DiagnosticLogPathTests
    {
        [Test]
        public void ResolveLogsDir_WithOverride_ResolvesUnderSyntheticLogs()
        {
            string customRoot = Path.Combine(Path.GetTempPath(), "SyntheticLogOverride_" + Guid.NewGuid().ToString("N"));
            string resolved = DiagnosticLogPaths.ResolveLogsDir(customRoot);

            Assert.AreEqual(Path.Combine(customRoot, "Synthetic", "Logs"), resolved);
        }

        [Test]
        public void ResolveLogsDir_WithoutOverride_UsesEnvironmentLocalApplicationData()
        {
            string expectedLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string expected = string.IsNullOrWhiteSpace(expectedLocalAppData)
                ? string.Empty
                : Path.Combine(expectedLocalAppData, "Synthetic", "Logs");

            Assert.AreEqual(expected, DiagnosticLogPaths.ResolveLogsDir());
            Assert.AreEqual(expected, DiagnosticLogPaths.ResolveLogsDir("   "));
        }

        [Test]
        public void ResolveLogPath_WithValidFileName_CombinesUnderLogsDir()
        {
            string customRoot = Path.Combine(Path.GetTempPath(), "SyntheticLogFile_" + Guid.NewGuid().ToString("N"));
            string resolved = DiagnosticLogPaths.ResolveLogPath("session.log", customRoot);

            Assert.AreEqual(Path.Combine(customRoot, "Synthetic", "Logs", "session.log"), resolved);
        }

        [Test]
        public void ResolveLogPath_WithEmptyFileName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DiagnosticLogPaths.ResolveLogPath(""));
            Assert.Throws<ArgumentException>(() => DiagnosticLogPaths.ResolveLogPath("   "));
        }

        [Test]
        public void GetStep6ResultsLogPath_ResolvesExpectedFilename()
        {
            string customRoot = Path.Combine(Path.GetTempPath(), "SyntheticStep6Log_" + Guid.NewGuid().ToString("N"));
            string resolved = DiagnosticLogPaths.GetStep6ResultsLogPath(customRoot);

            Assert.AreEqual(
                Path.Combine(customRoot, "Synthetic", "Logs", "MergeDuplicates_Step6_ExecutionResult.json"),
                resolved);
        }
    }
}
