using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Color = Autodesk.Revit.DB.Color;
using View = Autodesk.Revit.DB.View;
using Synthetic.Infrastructure.FailureProcessing;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.Settings;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.ShadowStudy.Adapters
{
    /// <summary>
    /// Concrete Revit API port implementing <see cref="IShadowStudyAdapter"/> with zero external <c>.rvt</c> templates
    /// and a single unconditionally rolled-back <see cref="TransactionGroup"/> during raw frame export.
    /// </summary>
    public class RevitShadowStudyAdapter : IShadowStudyAdapter
    {
        private const double RadiansToDegrees = 180.0 / Math.PI;
        private const string TempViewModesTaskDialogId = "TaskDialog_Really_Print_Or_Export_Temp_View_Modes";
        private const int SuppressTempViewModesOverrideResult = 1002;

        private readonly Document _doc;
        private readonly UIDocument? _uidoc;
        private readonly UIApplication? _uiapp;
        private readonly View? _activeViewOverride;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevitShadowStudyAdapter"/> class.
        /// </summary>
        public RevitShadowStudyAdapter(Document doc, UIDocument? uidoc = null, UIApplication? uiapp = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _uidoc = uidoc;
            _uiapp = uiapp;
        }

        internal RevitShadowStudyAdapter(Document doc, UIDocument? uidoc, UIApplication? uiapp, View? activeViewOverride)
            : this(doc, uidoc, uiapp)
        {
            _activeViewOverride = activeViewOverride;
        }

        /// <summary>
        /// Creates a <see cref="ShadowStudySettingsViewModel"/> wired to Revit document Extensible Storage
        /// and UI prompt services without exposing Revit API types to the ViewModel layer.
        /// </summary>
        public static ShadowStudySettingsViewModel CreateSettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            return new ShadowStudySettingsViewModel(
                () => SettingsManager.Get<ShadowStudySettings>(doc),
                settings => SettingsManager.Save(doc, settings),
                () => SettingsManager.Delete(doc, ShadowStudySettings.Name),
                () => HasDocumentOverride(doc),
                ProgressCoordinator.SuppressUI ? null : new WindowsTextInputPromptService(mainWindowHandle));
        }

        private static bool HasDocumentOverride(Document doc)
        {
            var dataStorages = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();

            string storageName = "Synthetic_" + ShadowStudySettings.Name;
            return dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
        }

        /// <inheritdoc />
        public ShadowStudySiteInfo GetSiteInfo()
        {
            SiteLocation site = _doc.SiteLocation;
            bool usesDst = ResolveUsesDst();

            return new ShadowStudySiteInfo
            {
                PlaceName = site?.PlaceName ?? string.Empty,
                LatitudeDegrees = site != null ? site.Latitude * RadiansToDegrees : 0.0,
                LongitudeDegrees = site != null ? site.Longitude * RadiansToDegrees : 0.0,
                TimeZoneHours = site != null ? site.TimeZone : 0.0,
                UsesDst = usesDst
            };
        }

        /// <inheritdoc />
        public ShadowStudyViewItem? GetActiveViewInfo()
        {
            View? activeView = ResolveActiveView();
            if (!IsEligibleView(activeView))
            {
                return null;
            }

            return MapViewItem(activeView!);
        }

        private View? ResolveActiveView()
        {
            return _activeViewOverride ?? _uidoc?.ActiveView ?? _doc.ActiveView;
        }

        /// <inheritdoc />
        public IReadOnlyList<ShadowStudyViewItem> GetEligibleViews()
        {
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType()
                .Cast<View>()
                .Where(IsEligibleView)
                .OrderBy(v => v.ViewType.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(MapViewItem)
                .ToList();

            ShadowStudyPathHelper.AssignViewLabels(views);
            return views.AsReadOnly();
        }

        /// <inheritdoc />
        public IReadOnlyList<ShadowStudyPrintSetItem> GetPrintSets()
        {
            // Read-only collector query: never touch PrintManager or ViewSheetSetting when inspecting Print Sets.
            var printSets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheetSet))
                .WhereElementIsNotElementType()
                .Cast<ViewSheetSet>()
                .Where(vs => !string.IsNullOrWhiteSpace(vs.Name))
                .OrderBy(vs => vs.Name, StringComparer.OrdinalIgnoreCase)
                .Select(MapPrintSetItem)
                .ToList();

            return printSets.AsReadOnly();
        }

        /// <inheritdoc />
        public ShadowStudyPrintSetItem SavePrintSet(
            string printSetName,
            IReadOnlyList<string> selectedShadowViewIds,
            bool saveAsNew,
            string? sourcePrintSetId = null)
        {
            string trimmedName = (printSetName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                throw new ArgumentException("Print Set name cannot be blank.", nameof(printSetName));
            }

            if (string.Equals(trimmedName, ShadowStudyPrintSetItem.InSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("'<In-Session>' is reserved and cannot be used as a saved Print Set name.", nameof(printSetName));
            }

            var resolvedShadowViews = new List<View>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            if (selectedShadowViewIds != null)
            {
                foreach (string viewId in selectedShadowViewIds)
                {
                    if (string.IsNullOrWhiteSpace(viewId) || !seenIds.Add(viewId))
                    {
                        continue;
                    }

                    View? view = _doc.GetElement(viewId) as View;
                    if (IsEligibleView(view))
                    {
                        resolvedShadowViews.Add(view!);
                    }
                }
            }

            var existingSets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheetSet))
                .WhereElementIsNotElementType()
                .Cast<ViewSheetSet>()
                .ToList();

            ViewSheetSet? existingByName = existingSets.FirstOrDefault(
                s => string.Equals(s.Name?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));

            ViewSheetSet? sourceSet = !string.IsNullOrWhiteSpace(sourcePrintSetId)
                ? existingSets.FirstOrDefault(s => string.Equals(s.UniqueId, sourcePrintSetId, StringComparison.Ordinal))
                : null;

            ViewSheetSet? targetSet = null;
            if (saveAsNew)
            {
                if (existingByName != null)
                {
                    throw new InvalidOperationException($"A Print Set named '{trimmedName}' already exists.");
                }
            }
            else
            {
                targetSet = sourceSet ?? existingByName;
                if (targetSet == null)
                {
                    throw new InvalidOperationException($"Saved Print Set '{trimmedName}' was not found in the document.");
                }
            }

            ViewSet combinedViewSet = BuildCombinedViewSet(resolvedShadowViews, saveAsNew ? sourceSet : targetSet);
            if (combinedViewSet.IsEmpty)
            {
                throw new InvalidOperationException("Cannot save an empty Print Set. Select at least one view.");
            }

            if (saveAsNew)
            {
                CommitPrintSetTransaction(trimmedName, setting =>
                {
                    // Revit's ViewSheetSetting can mutate a previously-active saved ViewSheetSet
                    // across CurrentViewSheetSet = InSession + SaveAs in the same session; snapshot and restore it.
                    ViewSheetSet? previousSavedSet = setting.CurrentViewSheetSet as ViewSheetSet;
                    ViewSet? previousSavedViews = null;
                    if (previousSavedSet != null && previousSavedSet.Views != null)
                    {
                        previousSavedViews = new ViewSet();
                        foreach (View member in previousSavedSet.Views)
                        {
                            if (member != null)
                            {
                                previousSavedViews.Insert(member);
                            }
                        }
                    }

                    setting.CurrentViewSheetSet = setting.InSession;
                    setting.CurrentViewSheetSet.Views = combinedViewSet;

                    if (!setting.SaveAs(trimmedName))
                    {
                        throw new InvalidOperationException($"Revit could not save Print Set '{trimmedName}'.");
                    }

                    if (previousSavedSet != null && previousSavedViews != null && !previousSavedViews.IsEmpty)
                    {
                        setting.CurrentViewSheetSet = previousSavedSet;
                        previousSavedSet.Views = previousSavedViews;
                        setting.CurrentViewSheetSet.Views = previousSavedViews;
                        setting.Save();
                    }
                });

                ViewSheetSet createdSet = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheetSet))
                    .WhereElementIsNotElementType()
                    .Cast<ViewSheetSet>()
                    .FirstOrDefault(s => string.Equals(s.Name?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Saved Print Set '{trimmedName}' could not be located after commit.");

                return MapPrintSetItem(createdSet);
            }
            else
            {
                CommitPrintSetTransaction(targetSet!.Name, setting =>
                {
                    setting.CurrentViewSheetSet = targetSet;
                    targetSet.Views = combinedViewSet;
                    setting.CurrentViewSheetSet.Views = combinedViewSet;

                    if (!setting.Save())
                    {
                        throw new InvalidOperationException($"Revit could not update Print Set '{targetSet.Name}'.");
                    }
                });

                ViewSheetSet refreshedSet = (_doc.GetElement(targetSet.UniqueId) as ViewSheetSet) ?? targetSet;
                return MapPrintSetItem(refreshedSet);
            }
        }

        private void CommitPrintSetTransaction(string contextLabel, Action<ViewSheetSetting> applyPrintSetChanges)
        {
            var preprocessor = new CompositeFailuresPreprocessor { AllowBlanketSuppression = false };
            preprocessor.SetContext($"ShadowStudy:SavePrintSet:{contextLabel}");

            using (Transaction tx = _doc.CreateTransaction("Save Print Set", preprocessor, clearAfterRollback: true))
            {
                tx.Start();
                try
                {
                    PrintManager printManager = _doc.PrintManager;
                    printManager.PrintRange = PrintRange.Select;
                    printManager.Apply();
                    applyPrintSetChanges(printManager.ViewSheetSetting);
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public ShadowStudySettings LoadShadowStudySettings()
        {
            ShadowStudySettings settings = SettingsManager.Get<ShadowStudySettings>(_doc) ?? new ShadowStudySettings();
            return settings.DeepClone();
        }

        /// <inheritdoc />
        public void SaveShadowStudySettings(ShadowStudySettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            SettingsManager.Save(_doc, settings.DeepClone());
        }

        private static ViewSet BuildCombinedViewSet(IReadOnlyList<View> shadowViews, ViewSheetSet? nonShadowSourceSet)
        {
            var combined = new ViewSet();
            var insertedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (View shadowView in shadowViews)
            {
                if (shadowView != null && insertedIds.Add(shadowView.UniqueId))
                {
                    combined.Insert(shadowView);
                }
            }

            if (nonShadowSourceSet != null && nonShadowSourceSet.Views != null)
            {
                foreach (View member in nonShadowSourceSet.Views)
                {
                    if (member != null && !IsEligibleView(member) && insertedIds.Add(member.UniqueId))
                    {
                        combined.Insert(member);
                    }
                }
            }

            return combined;
        }

        private static ShadowStudyPrintSetItem MapPrintSetItem(ViewSheetSet printSet)
        {
            var shadowIds = new List<string>();
            var nonShadowIds = new List<string>();

            if (printSet.Views != null)
            {
                foreach (View v in printSet.Views)
                {
                    if (v == null)
                    {
                        continue;
                    }

                    if (IsEligibleView(v))
                    {
                        shadowIds.Add(v.UniqueId);
                    }
                    else
                    {
                        nonShadowIds.Add(v.UniqueId);
                    }
                }
            }

            return new ShadowStudyPrintSetItem
            {
                Id = printSet.UniqueId,
                Name = printSet.Name ?? string.Empty,
                ShadowViewIds = shadowIds.AsReadOnly(),
                NonShadowMemberIds = nonShadowIds.AsReadOnly(),
                IsInSession = false
            };
        }

        /// <inheritdoc />
        public RawFrameExportResult ExportRawFrames(
            RawFrameExportRequest request,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Views == null || request.Views.Count == 0)
                throw new ArgumentException("At least one target view is required.", nameof(request));
            if (request.DateSchedules == null || request.DateSchedules.Count == 0)
                throw new ArgumentException("At least one date solar schedule is required.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.TempRootDirectory))
                throw new ArgumentException("Temporary root directory is required.", nameof(request));
            if (request.PixelWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(request), "Pixel width must be positive.");

            var resolvedViews = new List<(ShadowStudyViewItem Item, View RevitView)>(request.Views.Count);
            foreach (var viewItem in request.Views)
            {
                View? revitView = _doc.GetElement(viewItem.Id) as View;
                if (!IsEligibleView(revitView))
                {
                    throw new InvalidOperationException(
                        $"View '{viewItem.Name}' (ID: {viewItem.Id}) was not found or is not eligible for Shadow Study export.");
                }
                resolvedViews.Add((viewItem, revitView!));
            }

            var nonEmptySchedules = request.DateSchedules
                .Where(s => s != null && s.Frames != null && s.Frames.Count > 0)
                .ToList();
            if (nonEmptySchedules.Count == 0)
            {
                return new RawFrameExportResult();
            }

            int totalRawFrames = nonEmptySchedules.Sum(s => s.Frames.Count) * resolvedViews.Count;
            int totalComposites = nonEmptySchedules.Count * resolvedViews.Count;
            int totalBounds = Math.Max(1, totalRawFrames + totalComposites);
            int exportedFrameCount = 0;

            var exportedGroups = new List<RawExportedDateFrames>();
            var warnings = new List<string>();

            EventHandler<DialogBoxShowingEventArgs> dialogHandler = (sender, args) =>
            {
                if (args is TaskDialogShowingEventArgs taskArgs &&
                    string.Equals(taskArgs.DialogId, TempViewModesTaskDialogId, StringComparison.Ordinal))
                {
                    taskArgs.OverrideResult(SuppressTempViewModesOverrideResult);
                }
            };

            if (_uiapp != null)
            {
                _uiapp.DialogBoxShowing += dialogHandler;
            }

            try
            {
                using (var tg = new TransactionGroup(_doc, "Shadow Study Export"))
                {
                    tg.Start();
                    try
                    {
                        var preprocessor = new CompositeFailuresPreprocessor
                        {
                            AllowBlanketSuppression = false
                        };

                        DateTime firstSlotUtc = nonEmptySchedules[0].Frames[0].UtcTime;

                        for (int vIdx = 0; vIdx < resolvedViews.Count; vIdx++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var (viewItem, view) = resolvedViews[vIdx];
                            string viewTempDir = Path.Combine(request.TempRootDirectory, $"v_{vIdx:D3}");
                            Directory.CreateDirectory(viewTempDir);

                            preprocessor.SetContext($"ShadowStudy:{view.Name}");

                            // 1. Temporary view preparation transaction (detaches template, isolates sun, configures per-view visual style & background)
                            bool usePerFrameReferenceForView = false;
                            ExecuteInnerTransaction("Prepare Shadow Study View", preprocessor, () =>
                            {
                                if (view.ViewTemplateId != ElementId.InvalidElementId)
                                {
                                    view.ViewTemplateId = ElementId.InvalidElementId;
                                }

                                bool useHiddenLineForView = request.ForceHiddenLine ||
                                                            view.DisplayStyle == DisplayStyle.Wireframe ||
                                                            view.DisplayStyle == DisplayStyle.Rendering;

                                if (useHiddenLineForView && view.CanModifyDisplayStyle())
                                {
                                    view.DisplayStyle = DisplayStyle.HLR;
                                }

                                if (useHiddenLineForView && request.ForceWhiteBackground && IsBackgroundSupported(view.ViewType))
                                {
                                    try
                                    {
                                        var white = new Color(255, 255, 255);
                                        view.SetBackground(ViewDisplayBackground.CreateGradient(white, white, white));
                                    }
                                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                                    {
                                        warnings.Add($"Could not apply pure white background on '{view.Name}': {ex.Message}");
                                    }
                                }

                                usePerFrameReferenceForView = request.ExportPerFrameReference &&
                                                             !useHiddenLineForView &&
                                                             view.DisplayStyle != DisplayStyle.HLR;

                                SunAndShadowSettings sun = view.SunAndShadowSettings;
                                sun.SharesSettings = false;
                                sun.Visible = false;
                                sun.SunAndShadowType = SunAndShadowType.StillImage;
                                sun.SunriseToSunset = false;
                                sun.StartDateAndTime = firstSlotUtc;

                                view.ShadowIntensity = 0;
                            });

                            // 2. Shared unshadowed reference pass (ShadowIntensity = 0) for HLR / fallback views
                            string sharedReferencePngPath = string.Empty;
                            if (!usePerFrameReferenceForView)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                string refDir = Path.Combine(viewTempDir, "ref");
                                sharedReferencePngPath = ExportSingleViewPng(view, refDir, "ref", request.PixelWidth);
                            }

                            // 3. Export each study date schedule
                            for (int dIdx = 0; dIdx < nonEmptySchedules.Count; dIdx++)
                            {
                                var schedule = nonEmptySchedules[dIdx];
                                string dateDirSegment = ShadowStudyPathHelper.SanitizePathSegment(schedule.DateEntry.DateLabel);
                                var exportedFrames = new List<RawExportedFrame>(schedule.Frames.Count);

                                for (int fIdx = 0; fIdx < schedule.Frames.Count; fIdx++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();

                                    var slot = schedule.Frames[fIdx];
                                    string statusItem = $"{viewItem.ViewLabel} | {schedule.DateEntry.DateLabel} | {slot.ProjectLocalTime:HH:mm}";

                                    progress?.Report(new ProgressState
                                    {
                                        TaskDescription = "Exporting Shadow Frames",
                                        CurrentItemName = statusItem,
                                        ProgressIndex = exportedFrameCount,
                                        MaximumBounds = totalBounds
                                    });

                                    string? perFrameRefPath = null;
                                    if (usePerFrameReferenceForView)
                                    {
                                        ExecuteInnerTransaction("Set Shadow Reference Frame", preprocessor, () =>
                                        {
                                            SunAndShadowSettings sun = view.SunAndShadowSettings;
                                            sun.SunAndShadowType = SunAndShadowType.StillImage;
                                            sun.SunriseToSunset = false;
                                            sun.StartDateAndTime = slot.UtcTime;
                                            view.ShadowIntensity = 0;
                                        });

                                        cancellationToken.ThrowIfCancellationRequested();
                                        string perFrameRefDir = Path.Combine(viewTempDir, dateDirSegment, $"r_{fIdx:D3}_{slot.FrameFileStem}");
                                        perFrameRefPath = ExportSingleViewPng(view, perFrameRefDir, "ref", request.PixelWidth);
                                    }

                                    ExecuteInnerTransaction("Set Sun Frame", preprocessor, () =>
                                    {
                                        SunAndShadowSettings sun = view.SunAndShadowSettings;
                                        sun.SunAndShadowType = SunAndShadowType.StillImage;
                                        sun.SunriseToSunset = false;
                                        sun.StartDateAndTime = slot.UtcTime;
                                        view.ShadowIntensity = Math.Max(0, Math.Min(100, schedule.ShadowIntensity));
                                    });

                                    cancellationToken.ThrowIfCancellationRequested();
                                    string frameDir = Path.Combine(viewTempDir, dateDirSegment, $"f_{fIdx:D3}_{slot.FrameFileStem}");
                                    string framePngPath = ExportSingleViewPng(view, frameDir, "frame", request.PixelWidth);

                                    exportedFrames.Add(new RawExportedFrame
                                    {
                                        Slot = slot,
                                        FramePngPath = framePngPath,
                                        PerFrameReferencePngPath = perFrameRefPath
                                    });

                                    exportedFrameCount++;
                                    progress?.Report(new ProgressState
                                    {
                                        TaskDescription = "Exporting Shadow Frames",
                                        CurrentItemName = statusItem,
                                        ProgressIndex = exportedFrameCount,
                                        MaximumBounds = totalBounds
                                    });
                                }

                                exportedGroups.Add(new RawExportedDateFrames
                                {
                                    View = viewItem,
                                    DateSchedule = schedule,
                                    SharedReferencePngPath = sharedReferencePngPath,
                                    Frames = exportedFrames.AsReadOnly()
                                });
                            }
                        }
                    }
                    finally
                    {
                        if (tg.HasStarted() && !tg.HasEnded())
                        {
                            tg.RollBack();
                        }
                    }
                }
            }
            finally
            {
                if (_uiapp != null)
                {
                    _uiapp.DialogBoxShowing -= dialogHandler;
                }
            }

            return new RawFrameExportResult
            {
                DateFrameGroups = exportedGroups.AsReadOnly(),
                Warnings = warnings.AsReadOnly()
            };
        }

        private void ExecuteInnerTransaction(
            string transactionName,
            CompositeFailuresPreprocessor preprocessor,
            Action action)
        {
            using (Transaction tx = _doc.CreateTransaction(transactionName, preprocessor, clearAfterRollback: true))
            {
                tx.Start();
                try
                {
                    action();
                    _doc.Regenerate();
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }
                    throw;
                }
            }
        }

        private string ExportSingleViewPng(View view, string targetDir, string baseFileName, int pixelWidth)
        {
            Directory.CreateDirectory(targetDir);

            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(targetDir, baseFileName),
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal,
                PixelSize = pixelWidth
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });

            if (_uidoc != null && _uidoc.ActiveView != null && _uidoc.ActiveView.Id == view.Id)
            {
                _uidoc.RefreshActiveView();
            }

            _doc.ExportImage(options);

            string[] exported = Directory.GetFiles(targetDir, "*.png");
            if (exported.Length == 0)
            {
                throw new InvalidOperationException($"Revit ExportImage produced no PNG output for view '{view.Name}' in '{targetDir}'.");
            }

            return exported.Single();
        }

        private bool ResolveUsesDst()
        {
            View? active = ResolveActiveView();
            if (IsEligibleView(active) && active!.SunAndShadowSettings != null)
            {
                return active.SunAndShadowSettings.UsesDST;
            }

            View? fallbackView = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType()
                .Cast<View>()
                .FirstOrDefault(IsEligibleView);

            if (fallbackView?.SunAndShadowSettings != null)
            {
                return fallbackView.SunAndShadowSettings.UsesDST;
            }

            SunAndShadowSettings? globalSun = SunAndShadowSettings.GetActiveSunAndShadowSettings(_doc) as SunAndShadowSettings;
            return globalSun != null && globalSun.UsesDST;
        }

        internal static bool IsEligibleView(View? view)
        {
            if (view == null || view.IsTemplate)
            {
                return false;
            }

            if (view.ViewType == ViewType.Walkthrough ||
                string.Equals(view.GetType().Name, "ViewWalkthrough", StringComparison.Ordinal))
            {
                return false;
            }

            if (!view.HasDisplayStyle() || view.SunAndShadowSettings == null)
            {
                return false;
            }

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                case ViewType.ThreeD:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsBackgroundSupported(ViewType viewType)
        {
            return viewType == ViewType.ThreeD ||
                   viewType == ViewType.Section ||
                   viewType == ViewType.Elevation;
        }

        private static ShadowStudyViewItem MapViewItem(View view)
        {
            var warnings = new List<ShadowStudyViewWarningBadge>();
            if (!view.CropBoxActive)
            {
                warnings.Add(new ShadowStudyViewWarningBadge
                {
                    Code = "Uncropped",
                    Label = "Uncropped View",
                    Description = "Crop View is off. Exported framing will fit visible model extents without modifying your view."
                });
            }

            if (view.DisplayStyle == DisplayStyle.Wireframe || view.DisplayStyle == DisplayStyle.Rendering)
            {
                warnings.Add(new ShadowStudyViewWarningBadge
                {
                    Code = "VisualStyle",
                    Label = "Hidden Line Export",
                    Description = $"{view.DisplayStyle} does not render cast shadows; Hidden Line (HLR) on a pure white background will be used during export."
                });
            }

            return new ShadowStudyViewItem
            {
                Id = view.UniqueId,
                Name = view.Name ?? string.Empty,
                ViewType = view.ViewType.ToString(),
                ViewLabel = ShadowStudyPathHelper.SanitizePathSegment(view.Name),
                IsCropped = view.CropBoxActive,
                DisplayStyleName = view.DisplayStyle.ToString(),
                IsBackgroundSupported = IsBackgroundSupported(view.ViewType),
                Warnings = warnings
            };
        }
    }
}
