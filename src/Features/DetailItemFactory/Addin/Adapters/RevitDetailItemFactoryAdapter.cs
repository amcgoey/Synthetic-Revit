using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Services;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Platform.Revit;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.Features.DetailItemFactory.Adapters
{
    /// <summary>
    /// Single-seam Revit adapter for Detail Item Factory session harvesting, DWG projection,
    /// Detail Item family generation, and host loading.
    /// </summary>
    public sealed class RevitDetailItemFactoryAdapter : IDetailItemFactoryAdapter
    {
        private readonly Document _doc;
        private readonly UIDocument? _uidoc;
        private readonly UIApplication? _uiapp;
        private readonly View? _activeViewOverride;
        private readonly IReadOnlyCollection<ElementId>? _selectedIdsOverride;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevitDetailItemFactoryAdapter"/> class.
        /// </summary>
        public RevitDetailItemFactoryAdapter(Document doc, UIDocument? uidoc = null, UIApplication? uiapp = null)
            : this(doc, uidoc, uiapp, activeViewOverride: null, selectedIdsOverride: null)
        {
        }

        internal RevitDetailItemFactoryAdapter(
            Document doc,
            UIDocument? uidoc,
            UIApplication? uiapp,
            View? activeViewOverride,
            IReadOnlyCollection<ElementId>? selectedIdsOverride)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _uidoc = uidoc;
            _uiapp = uiapp;
            _activeViewOverride = activeViewOverride;
            _selectedIdsOverride = selectedIdsOverride;
        }

        /// <summary>
        /// Creates a <see cref="DetailItemFactorySettingsViewModel"/> wired to Revit Extensible Storage.
        /// </summary>
        public static DetailItemFactorySettingsViewModel CreateSettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            return DetailItemFactorySettingsService.CreateSettingsViewModel(doc, mainWindowHandle);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="view"/> is a non-template 2D view supported by Detail Item Factory.
        /// </summary>
        public static bool IsSupported2DView(View? view)
        {
            if (view == null || view.IsTemplate)
            {
                return false;
            }

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Elevation:
                case ViewType.Section:
                case ViewType.Detail:
                case ViewType.Legend:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Classifies a Revit <see cref="View"/> into a pure <see cref="DetailItemSourceViewKind"/>.
        /// </summary>
        public static DetailItemSourceViewKind ClassifyViewKind(View? view)
        {
            if (view == null)
            {
                return DetailItemSourceViewKind.Other;
            }

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                    return DetailItemSourceViewKind.Plan;
                case ViewType.Elevation:
                case ViewType.Section:
                    return DetailItemSourceViewKind.ElevationOrSection;
                case ViewType.Legend:
                    return DetailItemSourceViewKind.Legend;
                default:
                    return DetailItemSourceViewKind.Other;
            }
        }

        /// <summary>
        /// Resolves the default output directory for <paramref name="doc"/>, falling back to My Documents
        /// for unsaved, cloud, detached, or non-filesystem central models.
        /// </summary>
        public static string ResolveDefaultOutputDirectory(Document doc, string? myDocumentsOverride = null)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            string fallbackDir = !string.IsNullOrWhiteSpace(myDocumentsOverride)
                ? myDocumentsOverride!
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (string.IsNullOrWhiteSpace(doc.PathName) || doc.IsModelInCloud || doc.IsDetached)
            {
                return fallbackDir;
            }

            try
            {
                if (doc.IsWorkshared)
                {
                    ModelPath? centralPath = doc.GetWorksharingCentralModelPath();
                    if (centralPath == null || centralPath.ServerPath)
                    {
                        return fallbackDir;
                    }
                }

                string? folderPath = DocumentUtil.GetFolderPath(doc);
                if (!string.IsNullOrWhiteSpace(folderPath) && DetailItemFactorySettings.IsValidOutputPathSyntax(folderPath))
                {
                    return folderPath!;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RevitDetailItemFactoryAdapter: default output directory lookup fell back to My Documents: {ex.Message}");
            }

            return fallbackDir;
        }

        /// <summary>
        /// Resolves the Detail Item family template path, checking the add-in assembly directory first
        /// and falling back to Revit's configured <c>Application.FamilyTemplatePath</c>.
        /// </summary>
        internal static string? ResolveTemplatePath(string? assemblyLocation, string? familyTemplatePath)
        {
            if (assemblyLocation != null && !string.IsNullOrWhiteSpace(assemblyLocation))
            {
                string? assemblyDir = Path.GetDirectoryName(assemblyLocation);
                if (assemblyDir != null && !string.IsNullOrWhiteSpace(assemblyDir))
                {
                    string bundledAssetPath = Path.Combine(assemblyDir, "Assets", "Templates", "Detail Item.rft");
                    if (File.Exists(bundledAssetPath))
                    {
                        return bundledAssetPath;
                    }

                    string siblingPath = Path.Combine(assemblyDir, "Detail Item.rft");
                    if (File.Exists(siblingPath))
                    {
                        return siblingPath;
                    }
                }
            }

            if (familyTemplatePath != null && !string.IsNullOrWhiteSpace(familyTemplatePath))
            {
                string fallbackPath = Path.Combine(familyTemplatePath, "Detail Item.rft");
                if (File.Exists(fallbackPath))
                {
                    return fallbackPath;
                }
            }

            return null;
        }

        /// <inheritdoc />
        public DetailItemSessionSnapshot CaptureSessionSnapshot()
        {
            View? activeView = _activeViewOverride ?? _doc.ActiveView;
            bool isSupported = IsSupported2DView(activeView);
            DetailItemSourceViewKind viewKind = ClassifyViewKind(activeView);
            string defaultOutputFolder = ResolveDefaultOutputDirectory(_doc);
            DetailItemFactorySettings settings = SettingsManager.Get<DetailItemFactorySettings>(_doc).DeepClone();
            IReadOnlyList<string> subcategories = DetailItemFactorySettingsService.HarvestDetailComponentSubcategories(_doc);

            var harvestedElements = new List<DetailItemSessionElement>();
            ICollection<ElementId>? selectedIds = _selectedIdsOverride != null
                ? (ICollection<ElementId>)_selectedIdsOverride.ToList()
                : _uidoc?.Selection?.GetElementIds();

            if (selectedIds != null && activeView != null)
            {
                foreach (ElementId id in selectedIds)
                {
                    if (id == null || id == ElementId.InvalidElementId)
                    {
                        continue;
                    }

                    Element? element = _doc.GetElement(id);
                    if (element == null || !IsEligibleElement(element, activeView))
                    {
                        continue;
                    }

                    bool isLegendComponent = IsLegendComponent(element);
                    (string familyName, string typeName) = ResolveElementNames(_doc, element, isLegendComponent);
                    string categoryName = element.Category?.Name ?? "UnknownCategory";
                    string? legendViewVal = isLegendComponent
                        ? element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW)?.AsValueString()
                        : null;

                    harvestedElements.Add(new DetailItemSessionElement(
                        elementUniqueId: element.UniqueId,
                        elementId: element.Id.ToString(),
                        categoryName: categoryName,
                        familyName: familyName,
                        typeName: typeName,
                        isLegendComponent: isLegendComponent,
                        legendComponentViewValue: legendViewVal));
                }
            }

            return new DetailItemSessionSnapshot(
                activeViewUniqueId: activeView?.UniqueId ?? string.Empty,
                activeViewName: activeView?.Name ?? string.Empty,
                activeViewKind: viewKind,
                isActiveViewSupported: isSupported,
                defaultOutputFolder: defaultOutputFolder,
                settings: settings,
                availableSubcategories: subcategories,
                elements: harvestedElements);
        }

        /// <inheritdoc />
        public DetailItemBatchOutcome ExecuteBatch(
            DetailItemBatchPlan plan,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            string outputFolder = plan.OutputFolder;
            int totalCount = plan.Items.Count;

            if (_doc.IsReadOnly || _doc.IsModifiable)
            {
                return DetailItemBatchOutcome.Failure(
                    outputFolder,
                    "Cannot execute Detail Item Factory while the document is read-only or has an active transaction.");
            }

            if (plan.ExecutableCount > 0 && !DetailItemFactorySettings.IsValidOutputPathSyntax(outputFolder))
            {
                return DetailItemBatchOutcome.Failure(
                    outputFolder,
                    $"The output folder path '{outputFolder}' is not a valid directory path.");
            }

            View? activeView = null;
            string? templatePath = null;
            if (plan.ExecutableCount > 0)
            {
                activeView = _activeViewOverride ?? ResolveViewByUniqueId(_doc, plan.ActiveViewUniqueId) ?? _doc.ActiveView;
                if (!IsSupported2DView(activeView))
                {
                    return DetailItemBatchOutcome.Failure(
                        outputFolder,
                        "Active view is not a supported non-template 2D view.");
                }

                string assemblyLocation = typeof(RevitDetailItemFactoryAdapter).Assembly.Location;
                string? familyTemplatePath = _doc.Application?.FamilyTemplatePath;
                templatePath = ResolveTemplatePath(assemblyLocation, familyTemplatePath);

                if (string.IsNullOrEmpty(templatePath))
                {
                    string? assemblyDir = string.IsNullOrWhiteSpace(assemblyLocation)
                        ? null
                        : Path.GetDirectoryName(assemblyLocation);
                    string bundledPath = Path.Combine(assemblyDir ?? string.Empty, "Assets", "Templates", "Detail Item.rft");
                    return DetailItemBatchOutcome.Failure(
                        outputFolder,
                        $"Template file not found at:\n{bundledPath}\n\nFallback FamilyTemplatePath:\n{familyTemplatePath ?? "(not set)"}");
                }

                try
                {
                    if (!Directory.Exists(outputFolder))
                    {
                        Directory.CreateDirectory(outputFolder);
                    }
                }
                catch (Exception ex)
                {
                    return DetailItemBatchOutcome.Failure(
                        outputFolder,
                        $"Could not create output folder '{outputFolder}': {ex.Message}");
                }
            }

            string targetSubcategory = string.IsNullOrWhiteSpace(plan.TargetSubcategory)
                ? DetailItemFactorySettings.DefaultTargetSubcategory
                : plan.TargetSubcategory.Trim();

            bool createCustomSubcategory = !string.Equals(
                targetSubcategory,
                DetailItemFactorySettings.DefaultTargetSubcategory,
                StringComparison.OrdinalIgnoreCase);

            SubcategoryStyleSpec projectSubcategoryStyle = createCustomSubcategory
                ? CaptureProjectSubcategoryStyle(_doc, targetSubcategory)
                : SubcategoryStyleSpec.Default;

            var dwgOptions = new DWGExportOptions
            {
                HideScopeBox = true,
                HideReferencePlane = true,
                HideUnreferenceViewTags = true
            };

            var resultItems = new List<DetailItemResultItem>();
            var executedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string runToken = DetailItemBatchPlanner.CreateTempRunToken();
            bool wasCancelled = false;
            int committedCount = 0;

            _doc.ExecuteCommittedBatchGroup(
                "Batch Convert to 2D Detail Items",
                () =>
                {
                    for (int i = 0; i < totalCount; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            wasCancelled = true;
                            for (int j = i; j < totalCount; j++)
                            {
                                DetailItemPlannedElement cancelledItem = plan.Items[j];
                                int cancelledProgressIndex = j + 1;
                                resultItems.Add(new DetailItemResultItem
                                {
                                    ElementName = cancelledItem.DisplayName,
                                    ElementId = cancelledItem.ElementId,
                                    Status = DetailItemResultStatus.Skipped,
                                    Message = "Cancelled by user."
                                });
                                ReportProgress(
                                    progress,
                                    "Cancelled by user...",
                                    cancelledItem.DisplayName,
                                    cancelledProgressIndex,
                                    totalCount);
                            }
                            break;
                        }

                        DetailItemPlannedElement planned = plan.Items[i];
                        int progressIndex = i + 1;
                        string displayLabel = $"{planned.CleanFamilyName} - {planned.CleanTypeName}";

                        if (planned.Disposition != DetailItemPlanDisposition.Execute)
                        {
                            resultItems.Add(new DetailItemResultItem
                            {
                                ElementName = displayLabel,
                                ElementId = planned.ElementId,
                                Status = DetailItemResultStatus.Skipped,
                                Message = planned.SkipReason ?? "Skipped."
                            });
                            ReportProgress(
                                progress,
                                $"Skipped element {progressIndex} of {totalCount}",
                                displayLabel,
                                progressIndex,
                                totalCount);
                            continue;
                        }

                        if (!plan.OverwriteExisting && File.Exists(planned.TargetPath))
                        {
                            resultItems.Add(new DetailItemResultItem
                            {
                                ElementName = displayLabel,
                                ElementId = planned.ElementId,
                                Status = DetailItemResultStatus.Skipped,
                                Message = "Family file already exists and overwrite flag is disabled."
                            });
                            ReportProgress(
                                progress,
                                $"Skipped element {progressIndex} of {totalCount}",
                                displayLabel,
                                progressIndex,
                                totalCount);
                            continue;
                        }

                        if (!executedTargets.Add(planned.TargetPath))
                        {
                            resultItems.Add(new DetailItemResultItem
                            {
                                ElementName = displayLabel,
                                ElementId = planned.ElementId,
                                Status = DetailItemResultStatus.Skipped,
                                Message = $"Duplicate target family '{planned.TargetFileName}' skipped (already included in batch)."
                            });
                            ReportProgress(
                                progress,
                                $"Skipped element {progressIndex} of {totalCount}",
                                displayLabel,
                                progressIndex,
                                totalCount);
                            continue;
                        }

                        Element? element = _doc.GetElement(planned.ElementUniqueId);
                        if (element == null || activeView == null || !IsEligibleElement(element, activeView))
                        {
                            resultItems.Add(new DetailItemResultItem
                            {
                                ElementName = displayLabel,
                                ElementId = planned.ElementId,
                                Status = DetailItemResultStatus.Skipped,
                                Message = "Element is no longer available or is not a convertible 3D model element."
                            });
                            ReportProgress(
                                progress,
                                $"Skipped element {progressIndex} of {totalCount}",
                                displayLabel,
                                progressIndex,
                                totalCount);
                            continue;
                        }

                        string itemStatusLabel = element.Name ?? $"ID: {element.Id}";
                        ReportProgress(
                            progress,
                            $"Processing element {progressIndex} of {totalCount}: {itemStatusLabel}...",
                            itemStatusLabel,
                            i,
                            totalCount);

                        try
                        {
                            int skippedCurvesCount = ProcessSingleElement(
                                _doc,
                                activeView,
                                element,
                                planned,
                                outputFolder,
                                templatePath!,
                                targetSubcategory,
                                createCustomSubcategory,
                                projectSubcategoryStyle,
                                dwgOptions,
                                runToken,
                                plan.CleanupTempFiles);

                            committedCount++;

                            if (skippedCurvesCount == 0)
                            {
                                resultItems.Add(new DetailItemResultItem
                                {
                                    ElementName = displayLabel,
                                    ElementId = planned.ElementId,
                                    Status = DetailItemResultStatus.Success,
                                    Message = "Successfully generated and loaded family."
                                });
                            }
                            else
                            {
                                resultItems.Add(new DetailItemResultItem
                                {
                                    ElementName = displayLabel,
                                    ElementId = planned.ElementId,
                                    Status = DetailItemResultStatus.PartialSuccess,
                                    Message = $"Successfully generated, but skipped {skippedCurvesCount} microscopic or invalid curve{(skippedCurvesCount == 1 ? "" : "s")} during tracing."
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            resultItems.Add(new DetailItemResultItem
                            {
                                ElementName = displayLabel,
                                ElementId = planned.ElementId,
                                Status = DetailItemResultStatus.Failed,
                                Message = ex.Message
                            });
                        }
                        finally
                        {
                            ReportProgress(
                                progress,
                                $"Processed element {progressIndex} of {totalCount}",
                                itemStatusLabel,
                                progressIndex,
                                totalCount);
                        }
                    }

                    return committedCount > 0;
                });

            return DetailItemBatchOutcome.Completed(
                outputFolder: outputFolder,
                results: resultItems,
                wasCancelled: wasCancelled);
        }

        private int ProcessSingleElement(
            Document doc,
            View activeView,
            Element element,
            DetailItemPlannedElement planned,
            string outputFolder,
            string templatePath,
            string targetSubcategory,
            bool createCustomSubcategory,
            SubcategoryStyleSpec projectSubcategoryStyle,
            DWGExportOptions dwgOptions,
            string runToken,
            bool cleanupTempFiles)
        {
            string dwgPrefix = DetailItemBatchPlanner.BuildTempExportPrefix(runToken, planned.SequenceIndex);

            try
            {
                XYZ viewRelativeOffset = ComputeViewRelativeOffset(element, activeView);

                ExportIsolatedElementToDwg(doc, activeView, element, outputFolder, dwgPrefix, dwgOptions);

                string[] matchingFiles = Directory
                    .GetFiles(outputFolder, dwgPrefix + "*.dwg", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetFileName(f).StartsWith(dwgPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matchingFiles.Length == 0)
                {
                    throw new InvalidOperationException("DWG Export failed to generate a file on disk.");
                }
                string dwgPath = matchingFiles[0];

                int skippedCurvesCount = 0;
                Document? familyDoc = null;
                try
                {
                    familyDoc = doc.Application.NewFamilyDocument(templatePath);
                    if (familyDoc == null)
                    {
                        throw new InvalidOperationException("Failed to generate a new family document from the template.");
                    }

                    GraphicsStyle? subcategoryStyle = null;
                    if (createCustomSubcategory)
                    {
                        subcategoryStyle = EnsureFamilySubcategory(
                            doc,
                            familyDoc,
                            targetSubcategory,
                            projectSubcategoryStyle);
                    }

                    int createdCurvesCount = 0;
                    ExecuteVerifiedTransaction(
                        familyDoc,
                        "Import and Trace DWG",
                        () =>
                        {
                            View? planView = ResolveFamilyPlanView(familyDoc);
                            if (planView == null)
                            {
                                throw new InvalidOperationException("Could not find a valid Floor Plan view inside the Family template.");
                            }

                            var importOptions = new DWGImportOptions { ThisViewOnly = true };
                            ElementId importedElementId;
                            bool importSuccess = familyDoc.Import(dwgPath, importOptions, planView, out importedElementId);

                            if (!importSuccess || importedElementId == ElementId.InvalidElementId)
                            {
                                throw new InvalidOperationException("Failed to import the temporary DWG payload into the Family Document.");
                            }

                            if (!(familyDoc.GetElement(importedElementId) is ImportInstance importInstance))
                            {
                                throw new InvalidOperationException("Imported DWG element could not be resolved as an ImportInstance.");
                            }

                            BoundingBoxXYZ? importBBox = importInstance.get_BoundingBox(planView);
                            if (importBBox != null)
                            {
                                XYZ importCenter = (importBBox.Max + importBBox.Min) / 2.0;
                                XYZ currentDesiredOrigin = importCenter + viewRelativeOffset;
                                XYZ moveVector = XYZ.Zero - currentDesiredOrigin;
                                moveVector = new XYZ(moveVector.X, moveVector.Y, 0);

                                importInstance.Pinned = false;
                                ElementTransformUtils.MoveElement(familyDoc, importInstance.Id, moveVector);
                            }

                            var geomOptions = new Options { ComputeReferences = true };
                            GeometryElement geomElement = importInstance.get_Geometry(geomOptions);
                            double shortCurveTolerance = familyDoc.Application.ShortCurveTolerance;

                            ProcessGeometry(
                                geomElement,
                                familyDoc,
                                planView,
                                shortCurveTolerance,
                                subcategoryStyle,
                                ref createdCurvesCount,
                                ref skippedCurvesCount);

                            importInstance.Pinned = false;
                            familyDoc.Delete(importInstance.Id);

                            if (createdCurvesCount == 0)
                            {
                                throw new InvalidOperationException(
                                    $"No 2D detail curves could be traced for '{planned.CleanFamilyName} - {planned.CleanTypeName}'.");
                            }
                        });

                    var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                    familyDoc.SaveAs(planned.TargetPath, saveOptions);
                }
                finally
                {
                    CloseAndDisposeDocument(familyDoc);
                }

                ExecuteVerifiedTransaction(
                    doc,
                    "Load Family: " + planned.CleanFamilyName,
                    () =>
                    {
                        bool loaded = doc.LoadFamily(planned.TargetPath, new SimpleFamilyLoadOptions(), out Family? loadedFamily);
                        if (!loaded || loadedFamily == null)
                        {
                            throw new InvalidOperationException($"Revit failed to load family '{planned.TargetFileName}' into the project.");
                        }
                    });

                return skippedCurvesCount;
            }
            finally
            {
                if (cleanupTempFiles)
                {
                    DetailItemTempCleanupSummary cleanup = DetailItemBatchPlanner.CleanupTempExportFiles(outputFolder, dwgPrefix);
                    foreach (string error in cleanup.Errors)
                    {
                        Debug.WriteLine($"RevitDetailItemFactoryAdapter: {error}");
                    }
                }
            }
        }

        private void ExportIsolatedElementToDwg(
            Document doc,
            View activeView,
            Element element,
            string outputFolder,
            string dwgName,
            DWGExportOptions dwgOptions)
        {
            EventHandler<DialogBoxShowingEventArgs>? dialogHandler = null;
            if (_uiapp != null)
            {
                dialogHandler = (_, args) =>
                {
                    if (args is TaskDialogShowingEventArgs taskArgs &&
                        string.Equals(taskArgs.DialogId, "TaskDialog_Really_Print_Or_Export_Temp_View_Modes", StringComparison.Ordinal))
                    {
                        taskArgs.OverrideResult((int)TaskDialogResult.CommandLink2);
                    }
                };
                _uiapp.DialogBoxShowing += dialogHandler;
            }

            try
            {
                doc.ExecuteRolledBackGroup(
                    "Temporary Isolate for Export",
                    () =>
                    {
                        ExecuteVerifiedTransaction(
                            doc,
                            "Temporary Isolate for Export",
                            () =>
                            {
                                DisableCropBoxIfModifiable(activeView);

                                var idsToIsolate = new List<ElementId> { element.Id };
                                if (element is FamilyInstance familyInstance)
                                {
                                    ICollection<ElementId>? subComponentIds = familyInstance.GetSubComponentIds();
                                    if (subComponentIds != null && subComponentIds.Count > 0)
                                    {
                                        idsToIsolate.AddRange(subComponentIds);
                                    }
                                }

                                activeView.IsolateElementsTemporary(idsToIsolate);
                                doc.Regenerate();

                                bool exported = doc.Export(outputFolder, dwgName, new List<ElementId> { activeView.Id }, dwgOptions);
                                if (!exported)
                                {
                                    throw new InvalidOperationException($"DWG export returned false for element '{element.Id}'.");
                                }
                            });
                    });
            }
            finally
            {
                if (_uiapp != null && dialogHandler != null)
                {
                    _uiapp.DialogBoxShowing -= dialogHandler;
                }
            }
        }

        private static void DisableCropBoxIfModifiable(View activeView)
        {
            if (activeView.ViewType == ViewType.Legend || !activeView.CropBoxVisible)
            {
                return;
            }

            Parameter? cropVisibleParam = activeView.get_Parameter(BuiltInParameter.VIEWER_CROP_REGION_VISIBLE);
            if (cropVisibleParam != null && cropVisibleParam.IsReadOnly)
            {
                return;
            }

            try
            {
                activeView.CropBoxVisible = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RevitDetailItemFactoryAdapter: could not hide crop box on view '{activeView.Name}': {ex.Message}");
            }
        }

        private static GraphicsStyle? EnsureFamilySubcategory(
            Document projectDoc,
            Document familyDoc,
            string targetSubcategory,
            SubcategoryStyleSpec projectStyle)
        {
            GraphicsStyle? subcategoryStyle = null;
            ExecuteVerifiedTransaction(
                familyDoc,
                "Serialize Subcategory",
                () =>
                {
                    Category? familyDetailComponentsCategory = familyDoc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
                    if (familyDetailComponentsCategory == null)
                    {
                        return;
                    }

                    Category? familySubcategory = familyDetailComponentsCategory.SubCategories.Contains(targetSubcategory)
                        ? familyDetailComponentsCategory.SubCategories.get_Item(targetSubcategory)
                        : familyDoc.Settings.Categories.NewSubcategory(familyDetailComponentsCategory, targetSubcategory);

                    if (familySubcategory == null)
                    {
                        return;
                    }

                    if (projectStyle.LineColor != null && projectStyle.LineColor.IsValid)
                    {
                        familySubcategory.LineColor = projectStyle.LineColor;
                    }

                    if (projectStyle.LineWeight >= 1 && projectStyle.LineWeight <= 16)
                    {
                        familySubcategory.SetLineWeight(projectStyle.LineWeight, GraphicsStyleType.Projection);
                    }

                    if (!string.IsNullOrEmpty(projectStyle.LinePatternName))
                    {
                        LinePatternElement? familyPattern = new FilteredElementCollector(familyDoc)
                            .OfClass(typeof(LinePatternElement))
                            .Cast<LinePatternElement>()
                            .FirstOrDefault(p => string.Equals(p.Name, projectStyle.LinePatternName, StringComparison.Ordinal));

                        if (familyPattern == null &&
                            projectStyle.LinePatternId != ElementId.InvalidElementId &&
                            projectDoc.GetElement(projectStyle.LinePatternId) is LinePatternElement patternElem)
                        {
                            try
                            {
                                familyPattern = LinePatternElement.Create(familyDoc, patternElem.GetLinePattern());
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"RevitDetailItemFactoryAdapter: failed to copy line pattern '{projectStyle.LinePatternName}': {ex.Message}");
                            }
                        }

                        if (familyPattern != null)
                        {
                            familySubcategory.SetLinePatternId(familyPattern.Id, GraphicsStyleType.Projection);
                        }
                    }

                    subcategoryStyle = familySubcategory.GetGraphicsStyle(GraphicsStyleType.Projection);
                });

            return subcategoryStyle;
        }

        private static void ExecuteVerifiedTransaction(Document doc, string transactionName, Action action)
        {
            using (Transaction tx = doc.CreateTransaction(
                transactionName,
                FailurePipelines.ObserveOnly(),
                clearAfterRollback: true))
            {
                tx.Start();
                try
                {
                    action();

                    TransactionStatus status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        throw new InvalidOperationException($"Failed to commit '{transactionName}' transaction (status: {status}).");
                    }
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

        private static SubcategoryStyleSpec CaptureProjectSubcategoryStyle(Document doc, string targetSubcategory)
        {
            Category? detailComponentsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory == null || !detailComponentsCategory.SubCategories.Contains(targetSubcategory))
            {
                return SubcategoryStyleSpec.Default;
            }

            Category? projectSubcategory = detailComponentsCategory.SubCategories.get_Item(targetSubcategory);
            if (projectSubcategory == null)
            {
                return SubcategoryStyleSpec.Default;
            }

            Autodesk.Revit.DB.Color lineColor = projectSubcategory.LineColor != null && projectSubcategory.LineColor.IsValid
                ? projectSubcategory.LineColor
                : new Autodesk.Revit.DB.Color(0, 0, 0);
            int rawLineWeight = projectSubcategory.GetLineWeight(GraphicsStyleType.Projection) ?? 1;
            int lineWeight = Math.Max(1, Math.Min(16, rawLineWeight));
            ElementId patternId = projectSubcategory.GetLinePatternId(GraphicsStyleType.Projection);
            string? patternName = null;

            if (patternId != ElementId.InvalidElementId && doc.GetElement(patternId) is LinePatternElement patternElem)
            {
                patternName = patternElem.Name;
            }

            return new SubcategoryStyleSpec(lineColor, lineWeight, patternId, patternName);
        }

        private static View? ResolveFamilyPlanView(Document familyDoc)
        {
            View? active = familyDoc.ActiveView;
            if (active != null && !active.IsTemplate &&
                (active.ViewType == ViewType.FloorPlan || active.ViewType == ViewType.EngineeringPlan))
            {
                return active;
            }

            return new FilteredElementCollector(familyDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => !v.IsTemplate && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.EngineeringPlan));
        }

        private static View? ResolveViewByUniqueId(Document doc, string viewUniqueId)
        {
            if (string.IsNullOrWhiteSpace(viewUniqueId))
            {
                return null;
            }

            return doc.GetElement(viewUniqueId) as View;
        }

        private static XYZ ComputeViewRelativeOffset(Element element, View activeView)
        {
            BoundingBoxXYZ? bbox = element.get_BoundingBox(activeView);
            if (bbox == null)
            {
                return XYZ.Zero;
            }

            XYZ center = (bbox.Max + bbox.Min) / 2.0;
            XYZ desiredOrigin = bbox.Min;
            if (element.Location is LocationPoint locPoint)
            {
                desiredOrigin = locPoint.Point;
            }

            XYZ projectOffsetVector = desiredOrigin - center;
            double offsetX = projectOffsetVector.DotProduct(activeView.RightDirection);
            double offsetY = projectOffsetVector.DotProduct(activeView.UpDirection);
            return new XYZ(offsetX, offsetY, 0);
        }

        private static bool IsEligibleElement(Element element, View activeView)
        {
            if (element.Category == null)
            {
                return false;
            }

            if (element.Category.CategoryType == CategoryType.Model)
            {
                return true;
            }

            return activeView.ViewType == ViewType.Legend && IsLegendComponent(element);
        }

        private static bool IsLegendComponent(Element element)
        {
            if (element.Category == null)
            {
                return false;
            }

#if REVIT2023
            return element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_LegendComponents;
#else
            return element.Category.Id.Value == (long)BuiltInCategory.OST_LegendComponents;
#endif
        }

        private static (string FamilyName, string TypeName) ResolveElementNames(Document doc, Element element, bool isLegendComponent)
        {
            if (element is FamilyInstance fi)
            {
                return (
                    fi.Symbol?.Family?.Name ?? "UnknownFamily",
                    fi.Symbol?.Name ?? "UnknownType");
            }

            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType et)
            {
                return (
                    et.FamilyName ?? "UnknownFamily",
                    et.Name ?? "UnknownType");
            }

            if (isLegendComponent)
            {
                Parameter? componentParam = element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                if (componentParam != null && componentParam.StorageType == StorageType.ElementId)
                {
                    ElementId symbolId = componentParam.AsElementId();
                    if (symbolId != ElementId.InvalidElementId && doc.GetElement(symbolId) is ElementType legendType)
                    {
                        return (
                            legendType.FamilyName ?? "UnknownFamily",
                            legendType.Name ?? "UnknownType");
                    }
                }
            }

            return (
                element.Category?.Name ?? "UnknownCategory",
                element.Name ?? "UnknownType");
        }

        private static void CloseAndDisposeDocument(Document? document)
        {
            if (document == null)
            {
                return;
            }

            try
            {
                if (document.IsValidObject)
                {
                    document.Close(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RevitDetailItemFactoryAdapter: failed to close temporary family document: {ex.Message}");
            }
            finally
            {
                try
                {
                    document.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RevitDetailItemFactoryAdapter: failed to dispose temporary family document: {ex.Message}");
                }
            }
        }

        private static void ReportProgress(
            IProgress<ProgressState>? progress,
            string taskDescription,
            string currentItemName,
            int progressIndex,
            int maximumBounds)
        {
            progress?.Report(new ProgressState
            {
                TaskDescription = taskDescription,
                CurrentItemName = currentItemName,
                ProgressIndex = progressIndex,
                MaximumBounds = Math.Max(1, maximumBounds)
            });
        }

        private static void ProcessGeometry(
            GeometryElement? geomElement,
            Document familyDoc,
            View planView,
            double shortCurveTolerance,
            GraphicsStyle? subcategoryStyle,
            ref int createdCurvesCount,
            ref int skippedCurvesCount)
        {
            if (geomElement == null)
            {
                return;
            }

            foreach (GeometryObject geomObj in geomElement)
            {
                if (geomObj is GeometryInstance geomInst)
                {
                    ProcessGeometry(geomInst.GetInstanceGeometry(), familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref createdCurvesCount, ref skippedCurvesCount);
                }
                else if (geomObj is Curve curve)
                {
                    try
                    {
                        if (!curve.IsBound)
                        {
                            if (curve is Arc arc)
                            {
                                try
                                {
                                    XYZ center = arc.Center;
                                    double radius = arc.Radius;
                                    XYZ xAxis = arc.XDirection.Normalize();
                                    XYZ yAxis = arc.YDirection.Normalize();

                                    Arc half1 = Arc.Create(center, radius, 0.0, Math.PI, xAxis, yAxis);
                                    Arc half2 = Arc.Create(center, radius, Math.PI, 2.0 * Math.PI, xAxis, yAxis);

                                    Curve? flat1 = FlattenCurve(half1);
                                    Curve? flat2 = FlattenCurve(half2);

                                    if (flat1 != null)
                                    {
                                        TryCreateDetailCurve(familyDoc, planView, flat1, subcategoryStyle, ref createdCurvesCount);
                                    }
                                    if (flat2 != null)
                                    {
                                        TryCreateDetailCurve(familyDoc, planView, flat2, subcategoryStyle, ref createdCurvesCount);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"RevitDetailItemFactoryAdapter: ProcessGeometry unbounded arc failed: {ex.Message}");
                                    skippedCurvesCount++;
                                }
                                continue;
                            }
                            else if (curve is Ellipse ellipse)
                            {
                                try
                                {
                                    XYZ center = ellipse.Center;
                                    double rx = ellipse.RadiusX;
                                    double ry = ellipse.RadiusY;
                                    XYZ xAxis = ellipse.XDirection.Normalize();
                                    XYZ yAxis = ellipse.YDirection.Normalize();

                                    Curve half1 = Ellipse.CreateCurve(center, rx, ry, xAxis, yAxis, 0.0, Math.PI);
                                    Curve half2 = Ellipse.CreateCurve(center, rx, ry, xAxis, yAxis, Math.PI, 2.0 * Math.PI);

                                    Curve? flat1 = FlattenCurve(half1);
                                    Curve? flat2 = FlattenCurve(half2);

                                    if (flat1 != null)
                                    {
                                        TryCreateDetailCurve(familyDoc, planView, flat1, subcategoryStyle, ref createdCurvesCount);
                                    }
                                    if (flat2 != null)
                                    {
                                        TryCreateDetailCurve(familyDoc, planView, flat2, subcategoryStyle, ref createdCurvesCount);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"RevitDetailItemFactoryAdapter: ProcessGeometry unbounded ellipse failed: {ex.Message}");
                                    skippedCurvesCount++;
                                }
                                continue;
                            }

                            skippedCurvesCount++;
                            continue;
                        }

                        if (curve.Length < shortCurveTolerance)
                        {
                            DrawHealedCurve(curve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref createdCurvesCount, ref skippedCurvesCount);
                            continue;
                        }

                        if (curve is NurbSpline || curve is HermiteSpline)
                        {
                            DrawHealedCurve(curve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref createdCurvesCount, ref skippedCurvesCount);
                            continue;
                        }

                        Curve? flatCurve = FlattenCurve(curve);
                        if (flatCurve == null)
                        {
                            skippedCurvesCount++;
                            continue;
                        }

                        if (flatCurve.Length < shortCurveTolerance)
                        {
                            DrawHealedCurve(flatCurve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref createdCurvesCount, ref skippedCurvesCount);
                            continue;
                        }

                        TryCreateDetailCurve(familyDoc, planView, flatCurve, subcategoryStyle, ref createdCurvesCount);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"RevitDetailItemFactoryAdapter: ProcessGeometry curve failed: {ex.Message}");
                        skippedCurvesCount++;
                    }
                }
                else if (geomObj is PolyLine polyLine)
                {
                    IList<XYZ> coords = polyLine.GetCoordinates();
                    for (int i = 0; i < coords.Count - 1; i++)
                    {
                        XYZ p1Flat = new XYZ(coords[i].X, coords[i].Y, 0.0);
                        XYZ p2Flat = new XYZ(coords[i + 1].X, coords[i + 1].Y, 0.0);

                        double segmentLength = p1Flat.DistanceTo(p2Flat);
                        if (segmentLength < shortCurveTolerance)
                        {
                            skippedCurvesCount++;
                            continue;
                        }

                        try
                        {
                            Line segmentLine = Line.CreateBound(p1Flat, p2Flat);
                            TryCreateDetailCurve(familyDoc, planView, segmentLine, subcategoryStyle, ref createdCurvesCount);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RevitDetailItemFactoryAdapter: ProcessGeometry PolyLine segment failed: {ex.Message}");
                            skippedCurvesCount++;
                        }
                    }
                }
            }
        }

        private static void DrawHealedCurve(
            Curve curve,
            Document familyDoc,
            View planView,
            double shortCurveTolerance,
            GraphicsStyle? subcategoryStyle,
            ref int createdCurvesCount,
            ref int skippedCurvesCount)
        {
            try
            {
                IList<XYZ> points = curve.Tessellate();
                if (points == null || points.Count < 2)
                {
                    skippedCurvesCount++;
                    return;
                }

                var simplifiedPoints = new List<XYZ> { points[0] };
                double threshold = shortCurveTolerance + 0.003;

                for (int i = 1; i < points.Count - 1; i++)
                {
                    XYZ current = points[i];
                    XYZ lastAdded = simplifiedPoints[simplifiedPoints.Count - 1];

                    XYZ currentFlat = new XYZ(current.X, current.Y, 0.0);
                    XYZ lastAddedFlat = new XYZ(lastAdded.X, lastAdded.Y, 0.0);

                    if (currentFlat.DistanceTo(lastAddedFlat) > threshold)
                    {
                        simplifiedPoints.Add(current);
                    }
                }

                XYZ lastPoint = points[points.Count - 1];
                XYZ lastPointFlat = new XYZ(lastPoint.X, lastPoint.Y, 0.0);
                XYZ currentLastAddedFlat = new XYZ(simplifiedPoints[simplifiedPoints.Count - 1].X, simplifiedPoints[simplifiedPoints.Count - 1].Y, 0.0);

                if (simplifiedPoints.Count == 1 || lastPointFlat.DistanceTo(currentLastAddedFlat) > 0.0001)
                {
                    simplifiedPoints.Add(lastPoint);
                }

                for (int i = 0; i < simplifiedPoints.Count - 1; i++)
                {
                    XYZ p0 = simplifiedPoints[i];
                    XYZ p1 = simplifiedPoints[i + 1];

                    XYZ p0Flat = new XYZ(p0.X, p0.Y, 0.0);
                    XYZ p1Flat = new XYZ(p1.X, p1.Y, 0.0);

                    if (p0Flat.DistanceTo(p1Flat) < shortCurveTolerance)
                    {
                        skippedCurvesCount++;
                        continue;
                    }

                    try
                    {
                        Line segmentLine = Line.CreateBound(p0Flat, p1Flat);
                        TryCreateDetailCurve(familyDoc, planView, segmentLine, subcategoryStyle, ref createdCurvesCount);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"RevitDetailItemFactoryAdapter: DrawHealedCurve segment failed: {ex.Message}");
                        skippedCurvesCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RevitDetailItemFactoryAdapter: DrawHealedCurve failed: {ex.Message}");
                skippedCurvesCount++;
            }
        }

        private static void TryCreateDetailCurve(
            Document familyDoc,
            View planView,
            Curve curve,
            GraphicsStyle? subcategoryStyle,
            ref int createdCurvesCount)
        {
            DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, curve);
            if (detailCurve != null)
            {
                createdCurvesCount++;
                if (subcategoryStyle != null)
                {
                    detailCurve.LineStyle = subcategoryStyle;
                }
            }
        }

        private static Curve? FlattenCurve(Curve curve)
        {
            if (curve is Line line)
            {
                XYZ p0 = line.GetEndPoint(0);
                XYZ p1 = line.GetEndPoint(1);
                XYZ p0Flat = new XYZ(p0.X, p0.Y, 0);
                XYZ p1Flat = new XYZ(p1.X, p1.Y, 0);
                if (p0Flat.DistanceTo(p1Flat) > 0.0001)
                {
                    return Line.CreateBound(p0Flat, p1Flat);
                }
            }
            else if (curve is Arc arc)
            {
                XYZ p0 = arc.GetEndPoint(0);
                XYZ p1 = arc.Evaluate(0.5, true);
                XYZ p2 = arc.GetEndPoint(1);
                XYZ p0Flat = new XYZ(p0.X, p0.Y, 0);
                XYZ p1Flat = new XYZ(p1.X, p1.Y, 0);
                XYZ p2Flat = new XYZ(p2.X, p2.Y, 0);

                XYZ v1 = (p1Flat - p0Flat).Normalize();
                XYZ v2 = (p2Flat - p1Flat).Normalize();
                if (Math.Abs(v1.DotProduct(v2)) < 0.9999)
                {
                    return Arc.Create(p0Flat, p2Flat, p1Flat);
                }
                else
                {
                    if (p0Flat.DistanceTo(p2Flat) > 0.0001)
                    {
                        return Line.CreateBound(p0Flat, p2Flat);
                    }
                }
            }
            else if (curve is Ellipse ellipse)
            {
                try
                {
                    XYZ center = ellipse.Center;
                    XYZ centerFlat = new XYZ(center.X, center.Y, 0.0);
                    XYZ xDir = ellipse.XDirection;
                    XYZ yDir = ellipse.YDirection;
                    XYZ xDirFlat = new XYZ(xDir.X, xDir.Y, 0.0).Normalize();
                    XYZ yDirFlat = new XYZ(yDir.X, yDir.Y, 0.0).Normalize();
                    double rx = ellipse.RadiusX;
                    double ry = ellipse.RadiusY;

                    double p0 = ellipse.GetEndParameter(0);
                    double p1 = ellipse.GetEndParameter(1);

                    return Ellipse.CreateCurve(centerFlat, rx, ry, xDirFlat, yDirFlat, p0, p1);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RevitDetailItemFactoryAdapter: FlattenCurve failed: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private sealed class SubcategoryStyleSpec
        {
            public static SubcategoryStyleSpec Default { get; } =
                new SubcategoryStyleSpec(new Autodesk.Revit.DB.Color(0, 0, 0), 1, ElementId.InvalidElementId, null);

            public Autodesk.Revit.DB.Color LineColor { get; }
            public int LineWeight { get; }
            public ElementId LinePatternId { get; }
            public string? LinePatternName { get; }

            public SubcategoryStyleSpec(Autodesk.Revit.DB.Color lineColor, int lineWeight, ElementId linePatternId, string? linePatternName)
            {
                LineColor = lineColor;
                LineWeight = lineWeight;
                LinePatternId = linePatternId;
                LinePatternName = linePatternName;
            }
        }

        private sealed class SimpleFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
