using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Platform.UI;

namespace Synthetic.Features.MultiModelPdfExport.Services
{
    /// <summary>
    /// Headless Multi-Model PDF Export batch orchestrator coordinating per-document
    /// <see cref="IMultiModelPdfExportAdapter.ExportDocument"/> calls, progress reporting,
    /// between-document cancellation, and fault isolation.
    /// </summary>
    public sealed class MultiModelPdfExportPipeline
    {
        private readonly IMultiModelPdfExportAdapter _adapter;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfExportPipeline"/> class.
        /// </summary>
        public MultiModelPdfExportPipeline(IMultiModelPdfExportAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        /// <summary>
        /// Executes the batch PDF export request headlessly across all scheduled document jobs.
        /// </summary>
        public MultiModelPdfRunSummary Execute(
            MultiModelPdfRunRequest request,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Jobs == null || request.Jobs.Count == 0)
                throw new ArgumentException("At least one document export job is required.", nameof(request));

            if (!MultiModelPdfPathHelper.TryValidateOutputFolder(request.OutputFolder, out string folderError))
            {
                throw new ArgumentException(folderError, nameof(request));
            }

            string normalizedFolder = MultiModelPdfPathHelper.NormalizeFolderPath(request.OutputFolder);

            foreach (MultiModelPdfDocumentJob job in request.Jobs)
            {
                if (job.FileMode == MultiModelPdfFileMode.CombinedPerDocument)
                {
                    string targetPdf = MultiModelPdfPathHelper.BuildCombinedPdfPath(job.OutputFolder, job.OutputFileToken);
                    MultiModelPdfPathHelper.ValidateOutputPathLength(targetPdf);
                }
            }

            var generatedFiles = new List<string>();
            var outcomes = new List<MultiModelPdfDocumentOutcome>();
            var warnings = new List<string>();
            var errors = new List<string>();
            int completedCount = 0;
            int failedCount = 0;
            bool wasCancelled = false;

            if (cancellationToken.IsCancellationRequested)
            {
                return new MultiModelPdfRunSummary(
                    normalizedFolder,
                    generatedFiles,
                    outcomes,
                    warnings,
                    errors,
                    completedCount,
                    failedCount,
                    wasCancelled: true);
            }

            Directory.CreateDirectory(normalizedFolder);

            int totalJobs = request.Jobs.Count;
            for (int i = 0; i < totalJobs; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    break;
                }

                MultiModelPdfDocumentJob job = request.Jobs[i];
                string statusText = $"{job.DocumentTitle} ({i + 1} of {totalJobs})";

                progress?.Report(new ProgressState
                {
                    TaskDescription = "Exporting PDFs",
                    CurrentItemName = statusText,
                    ProgressIndex = i,
                    MaximumBounds = totalJobs
                });

                MultiModelPdfDocumentOutcome outcome;
                try
                {
                    outcome = _adapter.ExportDocument(job)
                              ?? MultiModelPdfDocumentOutcome.Failure(
                                  job.DocumentKey,
                                  job.DocumentTitle,
                                  $"Adapter returned no outcome for '{job.DocumentTitle}'.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    outcome = MultiModelPdfDocumentOutcome.Failure(
                        job.DocumentKey,
                        job.DocumentTitle,
                        $"{job.DocumentTitle}: {ex.Message}");
                }

                outcomes.Add(outcome);
                if (outcome.Warnings.Count > 0)
                {
                    warnings.AddRange(outcome.Warnings);
                }

                if (outcome.Succeeded)
                {
                    completedCount++;
                    generatedFiles.AddRange(outcome.GeneratedFilePaths);
                }
                else
                {
                    failedCount++;
                    string errMsg = string.IsNullOrWhiteSpace(outcome.ErrorMessage)
                        ? $"{job.DocumentTitle}: Export failed."
                        : outcome.ErrorMessage!;
                    errors.Add(errMsg);
                }

                progress?.Report(new ProgressState
                {
                    TaskDescription = "Exporting PDFs",
                    CurrentItemName = statusText,
                    ProgressIndex = i + 1,
                    MaximumBounds = totalJobs
                });

                if (cancellationToken.IsCancellationRequested && i + 1 < totalJobs)
                {
                    wasCancelled = true;
                    break;
                }
            }

            if (wasCancelled && completedCount > 0)
            {
                warnings.Add($"Export cancelled after completing {completedCount} document(s).");
            }

            return new MultiModelPdfRunSummary(
                normalizedFolder,
                generatedFiles,
                outcomes,
                warnings,
                errors,
                completedCount,
                failedCount,
                wasCancelled);
        }
    }
}
