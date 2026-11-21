using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    public class FakeShadowStudyAdapter : IShadowStudyAdapter
    {
        public ShadowStudySiteInfo SiteInfo { get; set; } = new ShadowStudySiteInfo
        {
            PlaceName = "Boston, MA",
            LatitudeDegrees = 42.3601,
            LongitudeDegrees = -71.0589,
            TimeZoneHours = -5.0,
            UsesDst = false
        };

        public ShadowStudyViewItem? ActiveView { get; set; } = new ShadowStudyViewItem
        {
            Id = "view-1",
            Name = "Level 1 - Floor Plan",
            ViewType = "FloorPlan",
            ViewLabel = "Level 1 - Floor Plan",
            IsCropped = true,
            DisplayStyleName = "HLR",
            IsBackgroundSupported = false
        };

        public List<ShadowStudyViewItem> EligibleViews { get; } = new List<ShadowStudyViewItem>();
        public List<ShadowStudyPrintSetItem> PrintSets { get; } = new List<ShadowStudyPrintSetItem>();
        public List<(string Name, IReadOnlyList<string> ShadowViewIds, bool SaveAsNew, string? SourcePrintSetId)> SavePrintSetCalls { get; } =
            new List<(string, IReadOnlyList<string>, bool, string?)>();
        public List<RawFrameExportRequest> ExportRequests { get; } = new List<RawFrameExportRequest>();
        public int CancelAfterFrameCount { get; set; } = -1;
        public CancellationTokenSource? CancelSourceToTrigger { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public int ImageWidth { get; set; } = 32;
        public int ImageHeight { get; set; } = 32;

        public ShadowStudySiteInfo GetSiteInfo() => SiteInfo;

        public ShadowStudyViewItem? GetActiveViewInfo() => ActiveView;

        public IReadOnlyList<ShadowStudyViewItem> GetEligibleViews()
        {
            if (EligibleViews.Count > 0) return EligibleViews;
            return ActiveView != null ? new List<ShadowStudyViewItem> { ActiveView } : new List<ShadowStudyViewItem>();
        }

        public IReadOnlyList<ShadowStudyPrintSetItem> GetPrintSets()
        {
            var sorted = new List<ShadowStudyPrintSetItem>(PrintSets.Count);
            foreach (var set in PrintSets)
            {
                sorted.Add(ClonePrintSet(set));
            }
            sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return sorted;
        }

        public ShadowStudyPrintSetItem SavePrintSet(
            string printSetName,
            IReadOnlyList<string> selectedShadowViewIds,
            bool saveAsNew,
            string? sourcePrintSetId = null)
        {
            string trimmed = (printSetName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new ArgumentException("Print Set name cannot be blank.", nameof(printSetName));
            }

            if (string.Equals(trimmed, ShadowStudyPrintSetItem.InSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("'<In-Session>' is reserved.", nameof(printSetName));
            }

            SavePrintSetCalls.Add((trimmed, new List<string>(selectedShadowViewIds ?? Array.Empty<string>()), saveAsNew, sourcePrintSetId));

            var shadowList = new List<string>(selectedShadowViewIds ?? Array.Empty<string>());
            if (saveAsNew)
            {
                foreach (var existing in PrintSets)
                {
                    if (string.Equals(existing.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"A Print Set named '{trimmed}' already exists.");
                    }
                }

                ShadowStudyPrintSetItem? sourceSet = !string.IsNullOrEmpty(sourcePrintSetId)
                    ? PrintSets.Find(p => string.Equals(p.Id, sourcePrintSetId, StringComparison.Ordinal))
                    : null;

                var nonShadow = sourceSet != null
                    ? new List<string>(sourceSet.NonShadowMemberIds)
                    : new List<string>();

                var created = new ShadowStudyPrintSetItem
                {
                    Id = "ps-" + (PrintSets.Count + 1),
                    Name = trimmed,
                    ShadowViewIds = shadowList.AsReadOnly(),
                    NonShadowMemberIds = nonShadow.AsReadOnly(),
                    IsInSession = false
                };
                PrintSets.Add(created);
                return ClonePrintSet(created);
            }
            else
            {
                ShadowStudyPrintSetItem? target = !string.IsNullOrEmpty(sourcePrintSetId)
                    ? PrintSets.Find(p => string.Equals(p.Id, sourcePrintSetId, StringComparison.Ordinal))
                    : PrintSets.Find(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    throw new InvalidOperationException($"Saved Print Set '{trimmed}' was not found.");
                }

                target.ShadowViewIds = shadowList.AsReadOnly();
                return ClonePrintSet(target);
            }
        }

        private static ShadowStudyPrintSetItem ClonePrintSet(ShadowStudyPrintSetItem source)
        {
            return new ShadowStudyPrintSetItem
            {
                Id = source.Id,
                Name = source.Name,
                ShadowViewIds = new List<string>(source.ShadowViewIds ?? Array.Empty<string>()).AsReadOnly(),
                NonShadowMemberIds = new List<string>(source.NonShadowMemberIds ?? Array.Empty<string>()).AsReadOnly(),
                IgnoredNonShadowMemberCount = source.IgnoredNonShadowMemberCount,
                IsInSession = source.IsInSession
            };
        }

        public RawFrameExportResult ExportRawFrames(
            RawFrameExportRequest request,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ExportRequests.Add(request);

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            var groups = new List<RawExportedDateFrames>();
            int totalFrames = 0;
            foreach (var s in request.DateSchedules)
            {
                totalFrames += s.Frames.Count;
            }
            totalFrames *= Math.Max(1, request.Views.Count);

            int exportedSoFar = 0;
            foreach (var view in request.Views)
            {
                string refPath = Path.Combine(request.TempRootDirectory, view.Id, "ref.png");
                WriteSolidTestPng(refPath, ImageWidth, ImageHeight, 255, 255, 255, 255);

                foreach (var schedule in request.DateSchedules)
                {
                    if (schedule.Frames.Count == 0) continue;

                    var frames = new List<RawExportedFrame>();
                    for (int i = 0; i < schedule.Frames.Count; i++)
                    {
                        if (CancelAfterFrameCount >= 0 && exportedSoFar >= CancelAfterFrameCount && CancelSourceToTrigger != null)
                        {
                            CancelSourceToTrigger.Cancel();
                        }
                        cancellationToken.ThrowIfCancellationRequested();

                        var slot = schedule.Frames[i];
                        string framePath = Path.Combine(
                            request.TempRootDirectory,
                            view.Id,
                            schedule.DateEntry.DateLabel,
                            $"f_{i:D3}_{slot.FrameFileStem}.png");

                        byte shadowByte = (byte)Math.Max(0, Math.Min(255, 255 - (int)Math.Round(255.0 * schedule.ShadowIntensity / 100.0)));
                        WriteSolidTestPng(framePath, ImageWidth, ImageHeight, shadowByte, shadowByte, shadowByte, 255);

                        frames.Add(new RawExportedFrame
                        {
                            Slot = slot,
                            FramePngPath = framePath
                        });

                        exportedSoFar++;
                        progress?.Report(new ProgressState
                        {
                            TaskDescription = $"Exporting {view.ViewLabel}",
                            CurrentItemName = $"{schedule.DateEntry.DateLabel} {slot.FrameFileStem}",
                            ProgressIndex = exportedSoFar,
                            MaximumBounds = Math.Max(1, totalFrames)
                        });
                    }

                    groups.Add(new RawExportedDateFrames
                    {
                        View = view,
                        DateSchedule = schedule,
                        SharedReferencePngPath = refPath,
                        Frames = frames
                    });
                }
            }

            return new RawFrameExportResult { DateFrameGroups = groups };
        }

        public static void WriteSolidTestPng(string path, int width, int height, byte b, byte g, byte r, byte a)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i + 0] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = a;
            }

            var bmp = BitmapSource.Create(width, height, 96.0, 96.0, PixelFormats.Bgra32, null, pixels, width * 4);
            bmp.Freeze();
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(fs);
        }
    }
}
