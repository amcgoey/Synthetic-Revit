using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Synthetic.Features.MergeDuplicates.Utilities;

namespace SyntheticTests.Features.MergeDuplicates
{
    [TestFixture]
    [UnitTest]
    public class MergeDiagnosticPathsTests
    {
        [Test]
        public void GetDeletionErrorLogPath_WithExplicitProcessIds_ProducesDistinctScopedPaths()
        {
            string pathA = MergeDiagnosticPaths.GetDeletionErrorLogPath(12345);
            string pathB = MergeDiagnosticPaths.GetDeletionErrorLogPath(67890);

            Assert.AreNotEqual(pathA, pathB);
            Assert.AreEqual(
                Path.Combine(Path.GetTempPath(), "synthetic_merge_deletion_error_12345.txt"),
                pathA);
            Assert.AreEqual(
                Path.Combine(Path.GetTempPath(), "synthetic_merge_deletion_error_67890.txt"),
                pathB);
            StringAssert.StartsWith("synthetic_merge_deletion_error_", Path.GetFileName(pathA));
            StringAssert.EndsWith(".txt", Path.GetFileName(pathA));
        }

        [Test]
        public void GetDeletionErrorLogPath_WithoutOverride_UsesCurrentProcessId()
        {
            int currentPid;
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                currentPid = currentProcess.Id;
            }

            string resolvedPath = MergeDiagnosticPaths.GetDeletionErrorLogPath();

            Assert.AreEqual(
                Path.Combine(Path.GetTempPath(), $"synthetic_merge_deletion_error_{currentPid}.txt"),
                resolvedPath);
        }
    }
}
