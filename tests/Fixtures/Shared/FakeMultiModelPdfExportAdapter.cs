using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// In-memory test double for <see cref="IMultiModelPdfExportAdapter"/> supporting deterministic
    /// session snapshots, fault injection, and cancellation simulation in headless Logic Tests.
    /// </summary>
    public class FakeMultiModelPdfExportAdapter : IMultiModelPdfExportAdapter
    {
        public MultiModelPdfSessionSnapshot Snapshot { get; set; }

        public List<MultiModelPdfDocumentJob> ExportedJobs { get; } = new List<MultiModelPdfDocumentJob>();

        public Dictionary<string, MultiModelPdfDocumentOutcome> OutcomesByDocumentKey { get; } =
            new Dictionary<string, MultiModelPdfDocumentOutcome>(StringComparer.Ordinal);

        public Dictionary<string, Exception> ExceptionsByDocumentKey { get; } =
            new Dictionary<string, Exception>(StringComparer.Ordinal);

        public int CancelAfterExportCount { get; set; } = -1;

        public CancellationTokenSource? CancelSourceToTrigger { get; set; }

        public bool WriteStubPdfToDisk { get; set; } = true;

        public FakeMultiModelPdfExportAdapter(MultiModelPdfSessionSnapshot? snapshot = null)
        {
            Snapshot = snapshot ?? CreateDefaultActiveSnapshot();
        }

        public static MultiModelPdfSessionSnapshot CreateDefaultActiveSnapshot(
            string activeTitle = "HostModel.rvt",
            bool includePrintSets = true)
        {
            var printSets = includePrintSets
                ? new[]
                {
                    new MultiModelPdfSetSnapshot("CD Set", 3),
                    new MultiModelPdfSetSnapshot(
                        "Permit Set",
                        2,
                        new[]
                        {
                            new MultiModelPdfSkippedViewInfo("A900", "UNPLACED SIGNAGE", "Placeholder sheet")
                        })
                }
                : Array.Empty<MultiModelPdfSetSnapshot>();

            var activeDoc = new MultiModelPdfDocumentSnapshot(
                documentKey: "active-host",
                documentTitle: activeTitle,
                displayPath: @"C:\BIM\" + activeTitle,
                origin: MultiModelPdfDocumentOrigin.Active,
                storage: MultiModelPdfStorageKind.Workshared,
                isUnloadedLink: false,
                printSets: printSets);

            var setups = new[]
            {
                MultiModelPdfSetupOption.CreateDefault(),
                new MultiModelPdfSetupOption("INC_FullSize", "INC_FullSize", isDefault: false)
            };

            return new MultiModelPdfSessionSnapshot(activeTitle, new[] { activeDoc }, setups);
        }

        public MultiModelPdfSessionSnapshot CaptureSessionSnapshot() => Snapshot;

        public MultiModelPdfDocumentOutcome ExportDocument(MultiModelPdfDocumentJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            ExportedJobs.Add(job);

            if (CancelAfterExportCount >= 0 &&
                ExportedJobs.Count >= CancelAfterExportCount &&
                CancelSourceToTrigger != null)
            {
                CancelSourceToTrigger.Cancel();
            }

            if (ExceptionsByDocumentKey.TryGetValue(job.DocumentKey, out Exception? ex))
            {
                throw ex;
            }

            if (OutcomesByDocumentKey.TryGetValue(job.DocumentKey, out MultiModelPdfDocumentOutcome? custom))
            {
                return custom;
            }

            string pdfPath = Path.Combine(job.OutputFolder, job.OutputFileToken + ".pdf");
            if (WriteStubPdfToDisk)
            {
                Directory.CreateDirectory(job.OutputFolder);
                File.WriteAllText(pdfPath, "%PDF-1.4\n%SyntheticTestStub\n%%EOF\n", Encoding.ASCII);
            }

            return MultiModelPdfDocumentOutcome.Success(job.DocumentKey, job.DocumentTitle, new[] { pdfPath });
        }
    }
}
