using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.Features.MultiModelPdfExport.Adapters
{
    /// <summary>
    /// Concrete Revit API adapter implementing <see cref="IMultiModelPdfExportAdapter"/> using
    /// native <see cref="Document.Export(string, IList{ElementId}, PDFExportOptions)"/> and
    /// <see cref="ExportPDFSettings"/> without third-party printer drivers or Windows Registry writes.
    /// </summary>
    public sealed class RevitMultiModelPdfExportAdapter : IMultiModelPdfExportAdapter
    {
        private const string TempViewModesTaskDialogId = "TaskDialog_Really_Print_Or_Export_Temp_View_Modes";
        private const int SuppressTempViewModesOverrideResult = 1002;

        private readonly Document _doc;
        private readonly UIApplication? _uiapp;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevitMultiModelPdfExportAdapter"/> class.
        /// </summary>
        public RevitMultiModelPdfExportAdapter(Document doc, UIApplication? uiapp = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _uiapp = uiapp;
        }

        /// <inheritdoc />
        public MultiModelPdfSessionSnapshot CaptureSessionSnapshot()
        {
            if (!_doc.IsValidObject)
            {
                throw new InvalidOperationException("Active Revit document is no longer valid.");
            }

            if (_doc.IsFamilyDocument)
            {
                return new MultiModelPdfSessionSnapshot(
                    _doc.Title,
                    Array.Empty<MultiModelPdfDocumentSnapshot>(),
                    new[] { MultiModelPdfSetupOption.CreateDefault() });
            }

            string activeTitle = ResolveDocumentTitle(_doc);
            string activeKey = ComputeDocumentKey(_doc);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal) { activeKey };

            var documents = new List<MultiModelPdfDocumentSnapshot>
            {
                BuildDocumentSnapshot(_doc, activeKey, activeTitle, MultiModelPdfDocumentOrigin.Active)
            };

            Autodesk.Revit.ApplicationServices.Application app = _uiapp?.Application ?? _doc.Application;
            var openTabCandidates = new List<(Document Doc, string Key, string Title)>();

            foreach (Document candidate in app.Documents)
            {
                if (candidate == null || !candidate.IsValidObject)
                {
                    continue;
                }

                if (candidate.IsLinked || candidate.IsFamilyDocument || IsSameDocument(candidate, _doc))
                {
                    continue;
                }

                openTabCandidates.Add((candidate, ComputeDocumentKey(candidate), ResolveDocumentTitle(candidate)));
            }

            foreach ((Document Doc, string Key, string Title) openTab in openTabCandidates
                .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Key, StringComparer.Ordinal))
            {
                if (!seenKeys.Add(openTab.Key))
                {
                    continue;
                }

                documents.Add(BuildDocumentSnapshot(
                    openTab.Doc,
                    openTab.Key,
                    openTab.Title,
                    MultiModelPdfDocumentOrigin.OpenStandalone));
            }

            IReadOnlyList<MultiModelPdfSetupOption> pdfSetups = HarvestPdfSetups(_doc);

            return new MultiModelPdfSessionSnapshot(
                activeDocumentTitle: activeTitle,
                documents: documents,
                pdfSetups: pdfSetups);
        }

        /// <inheritdoc />
        public MultiModelPdfDocumentOutcome ExportDocument(MultiModelPdfDocumentJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            if (!_doc.IsValidObject || _doc.IsFamilyDocument)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    job.DocumentTitle,
                    $"Document '{job.DocumentTitle}' is unavailable or is a family document.");
            }

            Document? targetDoc = ResolveOpenProjectDocument(job.DocumentKey);
            if (targetDoc == null || !targetDoc.IsValidObject || targetDoc.IsFamilyDocument || targetDoc.IsLinked)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    job.DocumentTitle,
                    $"Open project document '{job.DocumentTitle}' is no longer available in this Revit session.");
            }

            string docTitle = ResolveDocumentTitle(targetDoc);
            if (targetDoc.IsModifiable)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Document '{docTitle}' has an open transaction or uncommitted edits and cannot be exported.");
            }

            if (!MultiModelPdfPathHelper.TryValidateOutputFolder(job.OutputFolder, out string folderError))
            {
                return MultiModelPdfDocumentOutcome.Failure(job.DocumentKey, docTitle, folderError);
            }

            bool isCombined = job.FileMode == MultiModelPdfFileMode.CombinedPerDocument;
            string normalizedFolder = MultiModelPdfPathHelper.NormalizeFolderPath(job.OutputFolder);
            string destinationTargetPath = isCombined
                ? MultiModelPdfPathHelper.BuildCombinedPdfPath(normalizedFolder, job.OutputFileToken)
                : MultiModelPdfPathHelper.BuildSeparateDocumentFolderPath(normalizedFolder, job.OutputFileToken);

            if (!MultiModelPdfPathHelper.IsPathWithinSafeLimit(destinationTargetPath))
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Output path exceeds {MultiModelPdfPathHelper.SafeMaxPathLength - 1} characters: {destinationTargetPath}");
            }

            ViewSheetSet? printSet = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(ViewSheetSet))
                .WhereElementIsNotElementType()
                .Cast<ViewSheetSet>()
                .FirstOrDefault(vs => string.Equals(vs.Name, job.PrintSetName, StringComparison.OrdinalIgnoreCase));

            if (printSet == null)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Print Set '{job.PrintSetName}' was not found in '{docTitle}'.");
            }

            ExtractedPrintSetViews extracted = ExtractViewsFromPrintSet(printSet);
            var warnings = new List<string>();
            if (extracted.SkippedViews.Count > 0)
            {
                warnings.Add(MultiModelPdfExportPipeline.FormatSkippedViewsWarning(
                    docTitle,
                    printSet.Name,
                    extracted.SkippedViews));
            }

            if (extracted.PrintableViewIds.Count == 0)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Print Set '{printSet.Name}' in '{docTitle}' has 0 printable views.",
                    warnings);
            }

            PDFExportOptions options = BuildPdfExportOptions(
                _doc,
                targetDoc,
                docTitle,
                job.PdfSetupId,
                job.FileMode,
                job.OutputFileToken,
                extracted.AllSheets,
                warnings);

            string stagingDir = Path.Combine(
                Path.GetTempPath(),
                "SyntheticPdfStage_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            EventHandler<FailuresProcessingEventArgs> failuresHandler = OnFailuresProcessing;
            EventHandler<DialogBoxShowingEventArgs> dialogHandler = OnDialogBoxShowing;

            targetDoc.Application.FailuresProcessing += failuresHandler;
            if (_uiapp != null)
            {
                _uiapp.DialogBoxShowing += dialogHandler;
            }

            string currentErrorTarget = destinationTargetPath;
            try
            {
                Directory.CreateDirectory(stagingDir);
                bool exported = targetDoc.Export(stagingDir, extracted.PrintableViewIds, options);
                if (!exported)
                {
                    return MultiModelPdfDocumentOutcome.Failure(
                        job.DocumentKey,
                        docTitle,
                        $"Revit PDF export failed for '{docTitle}' (Print Set '{printSet.Name}').",
                        warnings);
                }

                IReadOnlyList<string> promotedPaths = PromoteStagedExports(
                    stagingDir,
                    isCombined,
                    destinationTargetPath,
                    normalizedFolder,
                    job.OutputFileToken,
                    docTitle,
                    ref currentErrorTarget,
                    out string? validationFailure);

                if (validationFailure != null)
                {
                    return MultiModelPdfDocumentOutcome.Failure(
                        job.DocumentKey,
                        docTitle,
                        validationFailure,
                        warnings);
                }

                return MultiModelPdfDocumentOutcome.Success(
                    job.DocumentKey,
                    docTitle,
                    promotedPaths,
                    warnings);
            }
            catch (Autodesk.Revit.Exceptions.FileAccessException ex)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Cannot write '{currentErrorTarget}': {ex.Message}",
                    warnings);
            }
            catch (IOException ex)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Cannot write '{currentErrorTarget}': {ex.Message}",
                    warnings);
            }
            catch (UnauthorizedAccessException ex)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Access denied writing '{currentErrorTarget}': {ex.Message}",
                    warnings);
            }
            catch (Exception ex)
            {
                return MultiModelPdfDocumentOutcome.Failure(
                    job.DocumentKey,
                    docTitle,
                    $"Unexpected PDF export error for '{docTitle}': {ex.Message}",
                    warnings);
            }
            finally
            {
                targetDoc.Application.FailuresProcessing -= failuresHandler;
                if (_uiapp != null)
                {
                    _uiapp.DialogBoxShowing -= dialogHandler;
                }

                TryDeleteDirectory(stagingDir);
            }
        }

        private static MultiModelPdfDocumentSnapshot BuildDocumentSnapshot(
            Document doc,
            string docKey,
            string docTitle,
            MultiModelPdfDocumentOrigin origin)
        {
            return new MultiModelPdfDocumentSnapshot(
                documentKey: docKey,
                documentTitle: docTitle,
                displayPath: ResolveDisplayPath(doc),
                origin: origin,
                storage: ClassifyStorageKind(doc),
                isUnloadedLink: false,
                printSets: HarvestPrintSets(doc));
        }

        private Document? ResolveOpenProjectDocument(string documentKey)
        {
            if (_doc.IsValidObject &&
                !_doc.IsFamilyDocument &&
                !_doc.IsLinked &&
                string.Equals(ComputeDocumentKey(_doc), documentKey, StringComparison.Ordinal))
            {
                return _doc;
            }

            Autodesk.Revit.ApplicationServices.Application app = _uiapp?.Application ?? _doc.Application;
            foreach (Document candidate in app.Documents)
            {
                if (candidate == null || !candidate.IsValidObject || candidate.IsLinked || candidate.IsFamilyDocument)
                {
                    continue;
                }

                if (string.Equals(ComputeDocumentKey(candidate), documentKey, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsSameDocument(Document? left, Document? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || !left.IsValidObject || !right.IsValidObject)
            {
                return false;
            }

            return left.Equals(right);
        }

        private static string ResolveDocumentTitle(Document doc)
        {
            return string.IsNullOrWhiteSpace(doc.Title)
                ? MultiModelPdfPathHelper.FallbackDocumentToken
                : doc.Title;
        }

        private static IReadOnlyList<MultiModelPdfSetSnapshot> HarvestPrintSets(Document doc)
        {
            var rawSets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .WhereElementIsNotElementType()
                .Cast<ViewSheetSet>()
                .Where(vs => !string.IsNullOrWhiteSpace(vs.Name))
                .OrderBy(vs => vs.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(vs => vs.UniqueId, StringComparer.Ordinal)
                .ToList();

            var snapshots = new List<MultiModelPdfSetSnapshot>(rawSets.Count);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ViewSheetSet printSet in rawSets)
            {
                string name = printSet.Name!.Trim();
                if (!seenNames.Add(name))
                {
                    continue;
                }

                ExtractedPrintSetViews extracted = ExtractViewsFromPrintSet(printSet);
                snapshots.Add(new MultiModelPdfSetSnapshot(
                    name: name,
                    printableViewCount: extracted.PrintableViewIds.Count,
                    skippedViews: extracted.SkippedViews));
            }

            return snapshots.AsReadOnly();
        }

        private static IReadOnlyList<MultiModelPdfSetupOption> HarvestPdfSetups(Document doc)
        {
            var options = new List<MultiModelPdfSetupOption>
            {
                MultiModelPdfSetupOption.CreateDefault()
            };

            IList<string>? settingNames = null;
            try
            {
                settingNames = ExportPDFSettings.ListNames(doc);
            }
            catch
            {
                settingNames = Array.Empty<string>();
            }

            if (settingNames != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawName in settingNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        continue;
                    }

                    string trimmed = rawName.Trim();
                    if (seen.Add(trimmed))
                    {
                        options.Add(new MultiModelPdfSetupOption(trimmed, trimmed, isDefault: false));
                    }
                }
            }

            return options.AsReadOnly();
        }

        private static PDFExportOptions BuildPdfExportOptions(
            Document activeDoc,
            Document targetDoc,
            string targetDocTitle,
            string pdfSetupId,
            MultiModelPdfFileMode fileMode,
            string outputFileToken,
            bool allSheets,
            List<string> warnings)
        {
            PDFExportOptions? options = null;
            bool loadedSavedSetup = false;
            if (!string.IsNullOrWhiteSpace(pdfSetupId) &&
                !string.Equals(pdfSetupId, MultiModelPdfSetupOption.DefaultSetupId, StringComparison.Ordinal))
            {
                ExportPDFSettings? savedSettings = ExportPDFSettings.FindByName(activeDoc, pdfSetupId);
                if (savedSettings != null)
                {
                    options = savedSettings.GetOptions();
                    loadedSavedSetup = options != null;
                }
                else
                {
                    warnings.Add($"{targetDocTitle}: PDF Export Setup '{pdfSetupId}' was not found; used '{MultiModelPdfSetupOption.DefaultSetupDisplayName}'.");
                }
            }

            options ??= CreateDefaultPdfExportOptions(allSheets);

            if (loadedSavedSetup && !IsSameDocument(activeDoc, targetDoc))
            {
                options = RemapNamingRuleForTargetDocument(
                    activeDoc,
                    targetDoc,
                    targetDocTitle,
                    pdfSetupId,
                    fileMode,
                    options,
                    warnings);
            }

            if (fileMode == MultiModelPdfFileMode.CombinedPerDocument)
            {
                options.Combine = true;
                options.FileName = outputFileToken;
            }
            else
            {
                options.Combine = false;
            }

            options.StopOnError = true;

#if REVIT2023 || REVIT2024
            // Synchronous export is the only mode in Revit 2023-2024.
#else
            options.SetExportInBackground(false);
#endif

            return options;
        }

        private static PDFExportOptions RemapNamingRuleForTargetDocument(
            Document activeDoc,
            Document targetDoc,
            string targetDocTitle,
            string pdfSetupId,
            MultiModelPdfFileMode fileMode,
            PDFExportOptions options,
            List<string> warnings)
        {
            IList<TableCellCombinedParameterData>? sourceRule = options.GetNamingRule();
            if (sourceRule == null || sourceRule.Count == 0)
            {
                return options;
            }

            bool hasNonBuiltInParameter = false;
            foreach (TableCellCombinedParameterData entry in sourceRule)
            {
                if (entry != null && GetElementIdValue(entry.ParamId) > 0)
                {
                    hasNonBuiltInParameter = true;
                    break;
                }
            }

            if (!hasNonBuiltInParameter)
            {
                return options;
            }

            var remappedRule = new List<TableCellCombinedParameterData>(sourceRule.Count);
            var missingParamNames = new List<string>();

            foreach (TableCellCombinedParameterData entry in sourceRule)
            {
                if (entry == null)
                {
                    continue;
                }

                long paramIdValue = GetElementIdValue(entry.ParamId);
                if (paramIdValue < 0)
                {
                    remappedRule.Add(CloneCombinedParameterData(entry, entry.ParamId));
                    continue;
                }

                Element? sourceElement = activeDoc.GetElement(entry.ParamId);
                if (sourceElement is ParameterElement sourceParam)
                {
                    string? paramName = sourceParam.GetDefinition()?.Name;
                    Guid? sharedGuid = sourceParam is SharedParameterElement shared ? shared.GuidValue : (Guid?)null;
                    ParameterElement? targetParam = FindMatchingParameter(targetDoc, sharedGuid, paramName);

                    if (targetParam != null && targetParam.IsValidObject)
                    {
                        remappedRule.Add(CloneCombinedParameterData(entry, targetParam.Id));
                    }
                    else
                    {
                        string label = string.IsNullOrWhiteSpace(paramName) ? $"ParameterId({paramIdValue})" : paramName!;
                        if (!missingParamNames.Contains(label, StringComparer.OrdinalIgnoreCase))
                        {
                            missingParamNames.Add(label);
                        }
                    }
                }
                else
                {
                    missingParamNames.Add($"ParameterId({paramIdValue})");
                }
            }

            if (missingParamNames.Count == 0 &&
                remappedRule.Count > 0 &&
                PDFExportOptions.IsValidNamingRule(remappedRule))
            {
                options.SetNamingRule(remappedRule);
                return options;
            }

            PDFExportOptions resetOptions = ResetToDefaultNamingRule(options);
            if (fileMode == MultiModelPdfFileMode.SeparatePerViewSheet)
            {
                string missingList = missingParamNames.Count > 0
                    ? string.Join(", ", missingParamNames)
                    : "naming rule";
                warnings.Add(
                    $"{targetDocTitle}: PDF Export Setup '{pdfSetupId}' references missing parameter(s) ({missingList}); fell back to Revit's default sheet naming rule.");
            }

            return resetOptions;
        }

        private static ParameterElement? FindMatchingParameter(
            Document targetDoc,
            Guid? sharedGuid,
            string? paramName)
        {
            if (sharedGuid.HasValue && sharedGuid.Value != Guid.Empty)
            {
                SharedParameterElement? sharedMatch = SharedParameterElement.Lookup(targetDoc, sharedGuid.Value);
                if (sharedMatch != null && sharedMatch.IsValidObject)
                {
                    return sharedMatch;
                }
            }

            if (string.IsNullOrWhiteSpace(paramName))
            {
                return null;
            }

            string targetName = paramName!.Trim();
            ParameterElement? caseInsensitiveMatch = null;
            foreach (ParameterElement candidate in new FilteredElementCollector(targetDoc)
                .OfClass(typeof(ParameterElement))
                .WhereElementIsNotElementType()
                .Cast<ParameterElement>())
            {
                if (!candidate.IsValidObject)
                {
                    continue;
                }

                string? candidateName = candidate.GetDefinition()?.Name;
                if (string.IsNullOrWhiteSpace(candidateName))
                {
                    continue;
                }

                if (string.Equals(candidateName, targetName, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (caseInsensitiveMatch == null &&
                    string.Equals(candidateName!.Trim(), targetName, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = candidate;
                }
            }

            return caseInsensitiveMatch;
        }

        private static TableCellCombinedParameterData CloneCombinedParameterData(
            TableCellCombinedParameterData source,
            ElementId targetParamId)
        {
            TableCellCombinedParameterData clone = TableCellCombinedParameterData.Create();
            clone.ParamId = targetParamId;
            if (source.CategoryId != null)
            {
                clone.CategoryId = source.CategoryId;
            }

            clone.Prefix = source.Prefix ?? string.Empty;
            clone.Suffix = source.Suffix ?? string.Empty;
            clone.Separator = source.Separator ?? string.Empty;
            clone.SampleValue = source.SampleValue ?? string.Empty;
            return clone;
        }

        private static PDFExportOptions ResetToDefaultNamingRule(PDFExportOptions sourceOptions)
        {
            var fresh = new PDFExportOptions();
            IList<TableCellCombinedParameterData>? defaultRule = fresh.GetNamingRule();
            if (defaultRule != null && defaultRule.Count > 0 && PDFExportOptions.IsValidNamingRule(defaultRule))
            {
                sourceOptions.SetNamingRule(defaultRule);
                return sourceOptions;
            }

            // If a fresh PDFExportOptions exposes an empty naming rule (meaning implicit Revit default),
            // copy all non-naming-rule settings onto it because SetNamingRule rejects empty lists.
            fresh.PaperFormat = sourceOptions.PaperFormat;
            fresh.PaperPlacement = sourceOptions.PaperPlacement;
            fresh.PaperOrientation = sourceOptions.PaperOrientation;
            fresh.ColorDepth = sourceOptions.ColorDepth;
            fresh.ExportQuality = sourceOptions.ExportQuality;
            fresh.RasterQuality = sourceOptions.RasterQuality;
            fresh.AlwaysUseRaster = sourceOptions.AlwaysUseRaster;
            fresh.HideCropBoundaries = sourceOptions.HideCropBoundaries;
            fresh.HideReferencePlane = sourceOptions.HideReferencePlane;
            fresh.HideScopeBoxes = sourceOptions.HideScopeBoxes;
            fresh.HideUnreferencedViewTags = sourceOptions.HideUnreferencedViewTags;
            fresh.MaskCoincidentLines = sourceOptions.MaskCoincidentLines;
            fresh.ReplaceHalftoneWithThinLines = sourceOptions.ReplaceHalftoneWithThinLines;
            fresh.ViewLinksInBlue = sourceOptions.ViewLinksInBlue;
            fresh.ZoomType = sourceOptions.ZoomType;
            fresh.ZoomPercentage = sourceOptions.ZoomPercentage;
            fresh.OriginOffsetX = sourceOptions.OriginOffsetX;
            fresh.OriginOffsetY = sourceOptions.OriginOffsetY;
            return fresh;
        }

        private static PDFExportOptions CreateDefaultPdfExportOptions(bool allSheets)
        {
            return new PDFExportOptions
            {
                PaperFormat = ExportPaperFormat.Default,
                ColorDepth = ColorDepthType.Color,
                ExportQuality = PDFExportQualityType.DPI600,
                RasterQuality = RasterQualityType.High,
                AlwaysUseRaster = false,
                HideCropBoundaries = true,
                HideReferencePlane = true,
                HideScopeBoxes = true,
                HideUnreferencedViewTags = true,
                ReplaceHalftoneWithThinLines = false,
                ZoomType = allSheets ? ZoomType.Zoom : ZoomType.FitToPage,
                ZoomPercentage = 100
            };
        }

        private static ExtractedPrintSetViews ExtractViewsFromPrintSet(ViewSheetSet printSet)
        {
            var printableIds = new List<ElementId>();
            var skipped = new List<MultiModelPdfSkippedViewInfo>();
            var seenIds = new HashSet<long>();
            bool allSheets = true;

            IEnumerable<View> candidateViews = EnumerateOrderedOrFallbackViews(printSet);
            foreach (View view in candidateViews)
            {
                if (view == null || !view.IsValidObject)
                {
                    continue;
                }

                long numericId = GetElementIdValue(view.Id);
                if (!seenIds.Add(numericId))
                {
                    continue;
                }

                if (view.IsTemplate)
                {
                    skipped.Add(new MultiModelPdfSkippedViewInfo(
                        view.ViewType.ToString(),
                        view.Name,
                        "View template"));
                    continue;
                }

                if (view is ViewSheet sheet && sheet.IsPlaceholder)
                {
                    skipped.Add(new MultiModelPdfSkippedViewInfo(
                        sheet.SheetNumber,
                        sheet.Name,
                        "Placeholder sheet"));
                    continue;
                }

                if (!view.CanBePrinted)
                {
                    string displayCode = view is ViewSheet nonPrintableSheet && !string.IsNullOrWhiteSpace(nonPrintableSheet.SheetNumber)
                        ? nonPrintableSheet.SheetNumber
                        : view.ViewType.ToString();
                    string reason = view.ViewType == ViewType.Schedule
                        ? "Schedule — not printable"
                        : "Not printable";
                    skipped.Add(new MultiModelPdfSkippedViewInfo(displayCode, view.Name, reason));
                    continue;
                }

                if (!(view is ViewSheet))
                {
                    allSheets = false;
                }

                printableIds.Add(view.Id);
            }

            return new ExtractedPrintSetViews(printableIds, skipped, allSheets && printableIds.Count > 0);
        }

        private static IEnumerable<View> EnumerateOrderedOrFallbackViews(ViewSheetSet printSet)
        {
            IReadOnlyList<View>? ordered = null;
            try
            {
                ordered = printSet.OrderedViewList;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                ordered = null;
            }

            if (ordered != null && ordered.Count > 0)
            {
                return ordered;
            }

            if (printSet.Views == null)
            {
                return Array.Empty<View>();
            }

            var fallback = new List<View>();
            foreach (object item in printSet.Views)
            {
                if (item is View view)
                {
                    fallback.Add(view);
                }
            }

            return fallback;
        }

        private static string ComputeDocumentKey(Document doc)
        {
            if (doc.IsModelInCloud)
            {
                try
                {
                    ModelPath? cloudPath = doc.GetCloudModelPath();
                    if (cloudPath != null)
                    {
                        Guid projectGuid = cloudPath.GetProjectGUID();
                        Guid modelGuid = cloudPath.GetModelGUID();
                        if (projectGuid != Guid.Empty && modelGuid != Guid.Empty)
                        {
                            return $"cloud:{projectGuid:D}:{modelGuid:D}";
                        }
                    }
                }
                catch
                {
                    // Fall through to path / creation fallback
                }
            }

            if (doc.IsWorkshared && !doc.IsDetached)
            {
                try
                {
                    ModelPath? centralPath = doc.GetWorksharingCentralModelPath();
                    if (centralPath != null)
                    {
                        string visible = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                        if (!string.IsNullOrWhiteSpace(visible))
                        {
                            return "central:" + NormalizePathForKey(visible);
                        }
                    }
                }
                catch
                {
                    // Fall through to local PathName / unsaved fallback
                }
            }

            if (!doc.IsDetached && !string.IsNullOrWhiteSpace(doc.PathName))
            {
                return "path:" + NormalizePathForKey(doc.PathName);
            }

            string stateTag = doc.IsDetached ? "detached" : "unsaved";
            int sessionInstanceToken = doc.GetHashCode();
#if REVIT2023
            string fallbackUid = doc.ProjectInformation?.UniqueId ?? string.Empty;
            return $"{stateTag}:{fallbackUid}:{sessionInstanceToken}:{doc.Title}";
#else
            return $"{stateTag}:{doc.CreationGUID:D}:{sessionInstanceToken}:{doc.Title}";
#endif
        }

        private static MultiModelPdfStorageKind ClassifyStorageKind(Document doc)
        {
            if (doc.IsModelInCloud)
            {
                return MultiModelPdfStorageKind.CloudAcc;
            }

            if (doc.IsWorkshared)
            {
                return MultiModelPdfStorageKind.Workshared;
            }

            if (!string.IsNullOrWhiteSpace(doc.PathName))
            {
                return MultiModelPdfStorageKind.Local;
            }

            return MultiModelPdfStorageKind.Unsaved;
        }

        private static string ResolveDisplayPath(Document doc)
        {
            if (!string.IsNullOrWhiteSpace(doc.PathName))
            {
                return doc.PathName;
            }

            if (doc.IsModelInCloud)
            {
                try
                {
                    ModelPath? cloud = doc.GetCloudModelPath();
                    if (cloud != null)
                    {
                        string visible = ModelPathUtils.ConvertModelPathToUserVisiblePath(cloud);
                        if (!string.IsNullOrWhiteSpace(visible))
                        {
                            return visible;
                        }
                    }
                }
                catch
                {
                    // Ignore lookup failure on unsaved/detached cloud docs
                }
            }

            if (doc.IsWorkshared && !doc.IsDetached)
            {
                try
                {
                    ModelPath? central = doc.GetWorksharingCentralModelPath();
                    if (central != null)
                    {
                        string visible = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                        if (!string.IsNullOrWhiteSpace(visible))
                        {
                            return visible;
                        }
                    }
                }
                catch
                {
                    // Ignore lookup failure
                }
            }

            return ResolveDocumentTitle(doc);
        }

        private static string NormalizePathForKey(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim()).TrimEnd('\\', '/').ToUpperInvariant();
            }
            catch
            {
                return path.Trim().TrimEnd('\\', '/').ToUpperInvariant();
            }
        }

        private static long GetElementIdValue(ElementId id)
        {
#if REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        private static IReadOnlyList<string> PromoteStagedExports(
            string stagingDir,
            bool isCombined,
            string destinationTargetPath,
            string normalizedOutputFolder,
            string outputFileToken,
            string docTitle,
            ref string currentErrorTarget,
            out string? validationFailure)
        {
            validationFailure = null;
            if (isCombined)
            {
                string? stagedPdfPath = ResolveStagedPdfPath(stagingDir, outputFileToken);
                if (string.IsNullOrEmpty(stagedPdfPath) || !File.Exists(stagedPdfPath) || new FileInfo(stagedPdfPath!).Length == 0)
                {
                    validationFailure = $"Revit PDF export produced no PDF file for '{docTitle}'.";
                    return Array.Empty<string>();
                }

                currentErrorTarget = destinationTargetPath;
                Directory.CreateDirectory(normalizedOutputFolder);
                AtomicallyPromotePdf(stagedPdfPath!, destinationTargetPath);
                return new[] { destinationTargetPath };
            }

            string[] stagedPdfPaths = Directory.GetFiles(stagingDir, "*.pdf");
            Array.Sort(stagedPdfPaths, StringComparer.OrdinalIgnoreCase);
            if (stagedPdfPaths.Length == 0)
            {
                validationFailure = $"Revit Separate PDF export produced no PDF files for '{docTitle}'.";
                return Array.Empty<string>();
            }

            var promotionPairs = new List<(string StagedPath, string DestPath)>(stagedPdfPaths.Length);
            foreach (string stagedPdf in stagedPdfPaths)
            {
                if (!File.Exists(stagedPdf) || new FileInfo(stagedPdf).Length == 0)
                {
                    validationFailure = $"Revit Separate PDF export produced an empty PDF file '{Path.GetFileName(stagedPdf)}' for '{docTitle}'.";
                    return Array.Empty<string>();
                }

                string destPdf = Path.Combine(destinationTargetPath, Path.GetFileName(stagedPdf));
                if (!MultiModelPdfPathHelper.IsPathWithinSafeLimit(destPdf))
                {
                    validationFailure = $"Output path exceeds {MultiModelPdfPathHelper.SafeMaxPathLength - 1} characters: {destPdf}";
                    return Array.Empty<string>();
                }

                promotionPairs.Add((stagedPdf, destPdf));
            }

            // Pre-flight existing target writability before overwriting any sibling PDFs in the document subfolder.
            foreach ((string _, string destPdf) in promotionPairs)
            {
                if (File.Exists(destPdf))
                {
                    currentErrorTarget = destPdf;
                    using (new FileStream(destPdf, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
            }

            Directory.CreateDirectory(destinationTargetPath);
            var promotedPaths = new List<string>(promotionPairs.Count);
            foreach ((string stagedPdf, string destPdf) in promotionPairs)
            {
                currentErrorTarget = destPdf;
                AtomicallyPromotePdf(stagedPdf, destPdf);
                promotedPaths.Add(destPdf);
            }

            return promotedPaths.AsReadOnly();
        }

        private static string? ResolveStagedPdfPath(string stagingDir, string expectedToken)
        {
            string expectedPath = Path.Combine(stagingDir, expectedToken + ".pdf");
            if (File.Exists(expectedPath))
            {
                return expectedPath;
            }

            string[] pdfs = Directory.GetFiles(stagingDir, "*.pdf");
            return pdfs.Length > 0 ? pdfs[0] : null;
        }

        private static void AtomicallyPromotePdf(string stagedPdfPath, string destinationPdfPath)
        {
            string? destDir = Path.GetDirectoryName(destinationPdfPath);
            if (string.IsNullOrEmpty(destDir))
            {
                File.Copy(stagedPdfPath, destinationPdfPath, overwrite: true);
                return;
            }

            string siblingTemp = Path.Combine(destDir!, $".syn_{Guid.NewGuid():N}.tmp");
            if (siblingTemp.Length >= MultiModelPdfPathHelper.SafeMaxPathLength)
            {
                File.Copy(stagedPdfPath, destinationPdfPath, overwrite: true);
                return;
            }

            try
            {
                File.Copy(stagedPdfPath, siblingTemp, overwrite: true);
                if (File.Exists(destinationPdfPath))
                {
                    File.Replace(siblingTemp, destinationPdfPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(siblingTemp, destinationPdfPath);
                }
            }
            finally
            {
                if (File.Exists(siblingTemp))
                {
                    try
                    {
                        File.Delete(siblingTemp);
                    }
                    catch
                    {
                        // Best-effort cleanup of temp sibling
                    }
                }
            }
        }

        private static void TryDeleteDirectory(string path)
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
                // Best-effort cleanup of temp staging folder
            }
        }

        private static void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs args)
        {
            if (args is TaskDialogShowingEventArgs taskArgs &&
                string.Equals(taskArgs.DialogId, TempViewModesTaskDialogId, StringComparison.Ordinal))
            {
                taskArgs.OverrideResult(SuppressTempViewModesOverrideResult);
            }
        }

        private static void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failures = fa.GetFailureMessages();
            if (failures == null || failures.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            bool deletedAny = false;
            foreach (FailureMessageAccessor failure in failures)
            {
                FailureDefinitionId id = failure.GetFailureDefinitionId();
                if (IsSuppressibleExportFailure(id))
                {
                    fa.DeleteWarning(failure);
                    deletedAny = true;
                }
            }

            e.SetProcessingResult(deletedAny ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue);
        }

        private static bool IsSuppressibleExportFailure(FailureDefinitionId id)
        {
            return id == BuiltInFailures.RoomFailures.RoomNotEnclosed ||
                   id == BuiltInFailures.RoomFailures.RoomNotEnclosedAreas ||
                   id == BuiltInFailures.RoomFailures.RoomNotEnclosedLoadAreas ||
                   id == BuiltInFailures.RoomFailures.RoomNotEnclosedRooms ||
                   id == BuiltInFailures.RoomFailures.RoomNotEnclosedSpaces ||
                   id == BuiltInFailures.ExportFailures.SwitchToRasterProcessingInsteadOfVectorWarning;
        }

        private sealed class ExtractedPrintSetViews
        {
            public ExtractedPrintSetViews(
                IList<ElementId> printableViewIds,
                IReadOnlyList<MultiModelPdfSkippedViewInfo> skippedViews,
                bool allSheets)
            {
                PrintableViewIds = printableViewIds;
                SkippedViews = skippedViews;
                AllSheets = allSheets;
            }

            public IList<ElementId> PrintableViewIds { get; }
            public IReadOnlyList<MultiModelPdfSkippedViewInfo> SkippedViews { get; }
            public bool AllSheets { get; }
        }
    }
}
