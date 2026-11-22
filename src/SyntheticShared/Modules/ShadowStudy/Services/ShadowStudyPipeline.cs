using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.Services
{
    /// <summary>
    /// Headless Shadow Study batch orchestrator driving raw export via <see cref="IShadowStudyAdapter"/>,
    /// streaming compositing via <see cref="ShadowImageCompositor"/>, and guaranteed temp cleanup.
    /// </summary>
    public class ShadowStudyPipeline
    {
        private readonly IShadowStudyAdapter _adapter;
        private readonly ShadowImageCompositor _compositor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyPipeline"/> class.
        /// </summary>
        public ShadowStudyPipeline(IShadowStudyAdapter adapter, ShadowImageCompositor? compositor = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _compositor = compositor ?? new ShadowImageCompositor();
        }

        /// <summary>
        /// Executes the Shadow Study batch pipeline headlessly.
        /// </summary>
        public ShadowStudyRunSummary Execute(
            ShadowStudyRunRequest request,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.OutputFolder))
                throw new ArgumentException("Output folder must not be blank.", nameof(request));
            if (request.Views == null || request.Views.Count == 0)
                throw new ArgumentException("At least one target view is required.", nameof(request));

            var views = request.Views.ToList();
            ShadowStudyPathHelper.AssignViewLabels(views);

            var validSchedules = new List<DateSolarSchedule>();
            var skippedDateLabels = new List<string>();
            var warnings = new List<string>();

            foreach (var schedule in request.DateSchedules ?? Array.Empty<DateSolarSchedule>())
            {
                if (schedule == null) continue;
                if (schedule.Frames == null || schedule.Frames.Count == 0)
                {
                    string skippedLabel = schedule.DateEntry.DateLabel;
                    skippedDateLabels.Add(skippedLabel);
                    warnings.Add($"Skipped date {skippedLabel}: 0 valid daylight frames (Peak solar altitude: {schedule.PeakSolarAltitudeDegrees:F1}°).");
                }
                else
                {
                    validSchedules.Add(schedule);
                }
            }

            if (validSchedules.Count == 0)
            {
                return new ShadowStudyRunSummary
                {
                    OutputFolder = request.OutputFolder,
                    Warnings = warnings.AsReadOnly(),
                    SkippedDateLabels = skippedDateLabels.AsReadOnly(),
                    WasCancelled = cancellationToken.IsCancellationRequested
                };
            }

            foreach (var view in views)
            {
                foreach (var schedule in validSchedules)
                {
                    string dateLabel = schedule.DateEntry.DateLabel;
                    string compositeName = ShadowStudyPathHelper.BuildCompositeFileName(
                        view.ViewLabel,
                        dateLabel);
                    string compositePath = Path.Combine(request.OutputFolder, compositeName);
                    ShadowStudyPathHelper.ValidateOutputPathLength(compositePath);

                    if (request.KeepIndividualFrames)
                    {
                        string framesFolder = Path.Combine(
                            request.OutputFolder,
                            ShadowStudyPathHelper.BuildFrameFolderName(view.ViewLabel, dateLabel));
                        foreach (var slot in schedule.Frames)
                        {
                            string frameFileName = ShadowStudyPathHelper.BuildFrameFileName(
                                view.ViewLabel,
                                dateLabel,
                                slot.ProjectLocalTime);
                            ShadowStudyPathHelper.ValidateOutputPathLength(Path.Combine(framesFolder, frameFileName));
                        }
                    }
                }
            }

            Directory.CreateDirectory(request.OutputFolder);
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "SyntheticShadowStudy_" + Guid.NewGuid().ToString("N"));

            var compositePaths = new List<string>();
            var savedFramePaths = new List<string>();

            try
            {
                var exportRequest = new RawFrameExportRequest
                {
                    Views = views.AsReadOnly(),
                    DateSchedules = validSchedules.AsReadOnly(),
                    TempRootDirectory = tempRoot,
                    PixelWidth = request.PixelWidth,
                    ForceHiddenLine = request.ForceHiddenLine,
                    ForceWhiteBackground = request.ForceWhiteBackground,
                    ExportPerFrameReference = !request.ForceHiddenLine
                };

                RawFrameExportResult exportResult = _adapter.ExportRawFrames(exportRequest, progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (exportResult.Warnings != null && exportResult.Warnings.Count > 0)
                {
                    warnings.AddRange(exportResult.Warnings);
                }

                int totalComposites = exportResult.DateFrameGroups.Count;
                int totalRawFrames = validSchedules.Sum(s => s.Frames.Count) * views.Count;
                int totalBounds = Math.Max(1, totalRawFrames + totalComposites);

                for (int g = 0; g < totalComposites; g++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var group = exportResult.DateFrameGroups[g];
                    if (group.Frames == null || group.Frames.Count == 0)
                    {
                        continue;
                    }

                    string dateLabel = group.DateSchedule.DateEntry.DateLabel;
                    string compositeFileName = ShadowStudyPathHelper.BuildCompositeFileName(group.View.ViewLabel, dateLabel);
                    string compositePath = Path.Combine(request.OutputFolder, compositeFileName);
                    ShadowStudyPathHelper.ValidateOutputPathLength(compositePath);

                    progress?.Report(new ProgressState
                    {
                        TaskDescription = "Compositing Shadow Study",
                        CurrentItemName = compositeFileName,
                        ProgressIndex = totalRawFrames + g,
                        MaximumBounds = totalBounds
                    });

                    bool usePerFrameRef = group.Frames.Any(f => !string.IsNullOrWhiteSpace(f.PerFrameReferencePngPath));
                    if (usePerFrameRef)
                    {
                        var pairs = group.Frames
                            .Select(f => new KeyValuePair<string, string>(
                                f.FramePngPath,
                                string.IsNullOrWhiteSpace(f.PerFrameReferencePngPath)
                                    ? group.SharedReferencePngPath
                                    : f.PerFrameReferencePngPath!))
                            .ToList();
                        int peakIndex = SelectPeakSolarAltitudeFrameIndex(group.Frames);
                        _compositor.CompositePerFrameReference(pairs, compositePath, peakIndex);
                    }
                    else
                    {
                        var framePaths = group.Frames.Select(f => f.FramePngPath).ToList();
                        _compositor.Composite(group.SharedReferencePngPath, framePaths, compositePath);
                    }

                    compositePaths.Add(compositePath);

                    if (request.KeepIndividualFrames)
                    {
                        string framesFolder = Path.Combine(
                            request.OutputFolder,
                            ShadowStudyPathHelper.BuildFrameFolderName(group.View.ViewLabel, dateLabel));
                        Directory.CreateDirectory(framesFolder);

                        foreach (var frame in group.Frames)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string frameFileName = ShadowStudyPathHelper.BuildFrameFileName(
                                group.View.ViewLabel,
                                dateLabel,
                                frame.Slot.ProjectLocalTime);
                            string destFramePath = Path.Combine(framesFolder, frameFileName);
                            ShadowStudyPathHelper.ValidateOutputPathLength(destFramePath);
                            File.Copy(frame.FramePngPath, destFramePath, overwrite: true);
                            savedFramePaths.Add(destFramePath);
                        }
                    }

                    progress?.Report(new ProgressState
                    {
                        TaskDescription = "Compositing Shadow Study",
                        CurrentItemName = compositeFileName,
                        ProgressIndex = totalRawFrames + g + 1,
                        MaximumBounds = totalBounds
                    });
                }

                return new ShadowStudyRunSummary
                {
                    OutputFolder = request.OutputFolder,
                    CompositePaths = compositePaths.AsReadOnly(),
                    SavedFramePaths = savedFramePaths.AsReadOnly(),
                    Warnings = warnings.AsReadOnly(),
                    SkippedDateLabels = skippedDateLabels.AsReadOnly(),
                    WasCancelled = false
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ShadowStudyRunSummary
                {
                    OutputFolder = request.OutputFolder,
                    CompositePaths = Array.Empty<string>(),
                    SavedFramePaths = Array.Empty<string>(),
                    Warnings = warnings.AsReadOnly(),
                    SkippedDateLabels = skippedDateLabels.AsReadOnly(),
                    WasCancelled = true
                };
            }
            finally
            {
                SafeDeleteDirectory(tempRoot);
            }
        }

        internal static int SelectPeakSolarAltitudeFrameIndex(IReadOnlyList<RawExportedFrame> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                return 0;
            }

            int midpoint = frames.Count / 2;
            int bestIndex = 0;
            double bestAltitude = double.NegativeInfinity;

            for (int i = 0; i < frames.Count; i++)
            {
                double altitude = frames[i].Slot != null
                    ? frames[i].Slot.SolarAltitudeDegrees
                    : double.NegativeInfinity;

                if (altitude > bestAltitude ||
                    (Math.Abs(altitude - bestAltitude) <= 1e-9 &&
                     Math.Abs(i - midpoint) < Math.Abs(bestIndex - midpoint)))
                {
                    bestAltitude = altitude;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of temp directory
            }
        }
    }
}
