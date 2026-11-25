using System;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Synthetic.Tools.ModelUpgrader
{
    /// <summary>
    /// Standalone NUnit host entry point executed via <c>ricaun.RevitTest</c> and <c>revit_model_upgrader.py</c>.
    /// </summary>
    [TestFixture]
    public class UpgradeRunnerTests
    {
        private UIApplication? _uiapp;
        private int _initialDocCount;
        private int _journalRunIdWritten;

        private UIApplication UiApp =>
            _uiapp ?? throw new InvalidOperationException("UIApplication was not initialized.");

        [OneTimeSetUp]
        public void OneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            _initialDocCount = uiapp.Application.Documents.Size;

            string? runId = Environment.GetEnvironmentVariable("SYNTHETIC_TEST_RUN_ID");
            if (!string.IsNullOrWhiteSpace(runId) && Interlocked.Exchange(ref _journalRunIdWritten, 1) == 0)
            {
                try
                {
                    uiapp.Application.WriteJournalComment($"SYNTHETIC_TEST_RUN:{runId!.Trim()}", true);
                }
                catch
                {
                    // Best-effort fallback marker when bootstrap is absent
                }
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_uiapp?.Application != null)
            {
                Assert.AreEqual(
                    _initialDocCount,
                    _uiapp.Application.Documents.Size,
                    $"RevitModelUpgrader leaked open Revit document(s): expected {_initialDocCount}, actual {_uiapp.Application.Documents.Size}.");
            }
        }

        [Test]
        public void RunBatchUpgrade()
        {
            string? manifestPath = Environment.GetEnvironmentVariable("SYNTHETIC_UPGRADER_MANIFEST");
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                Assert.Fail("SYNTHETIC_UPGRADER_MANIFEST environment variable is required. Run via tools/RevitModelUpgrader/revit_model_upgrader.py.");
                return;
            }

            if (!File.Exists(manifestPath))
            {
                Assert.Fail($"Upgrade manifest file not found: '{manifestPath}'.");
                return;
            }

            string rawJson = File.ReadAllText(manifestPath);
            UpgradeManifest? manifest = JsonConvert.DeserializeObject<UpgradeManifest>(rawJson);
            if (manifest == null)
            {
                Assert.Fail($"Failed to deserialize upgrade manifest from '{manifestPath}'.");
                return;
            }

            string hostVersion = (UiApp.Application.VersionNumber ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(manifest.TargetVersion) &&
                !string.Equals(hostVersion, manifest.TargetVersion.Trim(), StringComparison.Ordinal))
            {
                Assert.Fail($"Revit host version '{hostVersion}' does not match manifest TargetVersion '{manifest.TargetVersion}'.");
                return;
            }

            string effectiveVersion = string.IsNullOrWhiteSpace(manifest.TargetVersion)
                ? hostVersion
                : manifest.TargetVersion.Trim();

            var report = new UpgradeBatchReport
            {
                TargetVersion = effectiveVersion,
                ExecutedAtUtc = DateTime.UtcNow.ToString("O")
            };

            try
            {
                foreach (UpgradeJobSpec job in manifest.Jobs)
                {
                    FileUpgradeResult result = ModelUpgradeExecutor.UpgradeFile(UiApp, job, effectiveVersion);
                    report.Jobs.Add(result);
                    TestContext.WriteLine(
                        $"[UPGRADE] {(result.Success ? "PASS" : "FAIL")} | {result.SourcePath} -> {result.TargetPath} | " +
                        $"Format={result.SourceFormat}->{result.VerifiedFormat} | Workshared={result.WasWorkshared} | " +
                        $"Warnings={result.WarningCount} | Errors={result.ErrorCount} | Dialogs={result.SuppressedDialogs.Count}");
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(manifest.ReportPath))
                {
                    string? reportDir = Path.GetDirectoryName(Path.GetFullPath(manifest.ReportPath));
                    if (!string.IsNullOrEmpty(reportDir))
                    {
                        Directory.CreateDirectory(reportDir);
                    }

                    File.WriteAllText(manifest.ReportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
                }
            }

            FileUpgradeResult[] failedJobs = report.Jobs.Where(j => !j.Success).ToArray();
            if (failedJobs.Length > 0)
            {
                string failureSummary = string.Join(
                    Environment.NewLine,
                    failedJobs.Select(f => $"{f.SourcePath} -> {f.TargetPath}: {f.ErrorMessage}"));
                Assert.Fail($"One or more model upgrade jobs failed:{Environment.NewLine}{failureSummary}");
            }
        }
    }
}
