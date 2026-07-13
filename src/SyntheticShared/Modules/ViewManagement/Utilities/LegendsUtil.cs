using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.ViewManagement.Utilities
{
    /// <summary>
    /// Utility functions for dealing with legends
    /// </summary>
    public class LegendsUtil
    {
        /// <summary>
        /// Convert a Drafting view to a Legend
        /// </summary>
        /// <param name="drafting">Drafting View</param>
        /// <param name="warningTripped">Flag set to true if any constraint warnings were suppressed</param>
        /// <returns>A Legend view</returns>
        public static View? ConvertFromDrafting(View drafting, ref bool warningTripped)
        {
            if (drafting == null || drafting.ViewType != ViewType.DraftingView)
            {
                return null;
            }

            Document doc = drafting.Document;

            // --- Transaction 1: Group Ungrouped Reference Planes ---
            if (ProgressCoordinator.IsCancelled()) return null;
            ProgressCoordinator.UpdateStatus($"[{drafting.Name}] Grouping loose reference planes...");

            Group? planesGroup = null;
            using (Transaction t1 = new Transaction(doc, "Group Reference Planes"))
            {
                t1.Start();

                FailureHandlingOptions options = t1.GetFailureHandlingOptions();
                SuppressConstraintsPreprocessor preprocessor = new SuppressConstraintsPreprocessor();
                options.SetFailuresPreprocessor(preprocessor);
                t1.SetFailureHandlingOptions(options);

                List<ReferencePlane> refPlanes = new FilteredElementCollector(doc, drafting.Id)
                    .OfClass(typeof(ReferencePlane))
                    .Cast<ReferencePlane>()
                    .ToList();

                List<ReferencePlane> ungroupedPlanes = refPlanes
                    .Where(rp => rp.GroupId == ElementId.InvalidElementId)
                    .ToList();

                if (ungroupedPlanes.Any())
                {
                    ICollection<ElementId> planeIds = ungroupedPlanes.Select(rp => rp.Id).ToList();
                    planesGroup = doc.Create.NewGroup(planeIds);

                    string uniqueGroupName = "RefPlanes_" + drafting.Name + "_" + Guid.NewGuid().ToString().Substring(0, 8);
                    planesGroup.GroupType.Name = uniqueGroupName;
                }

                t1.Commit();
                if (preprocessor.WarningTripped)
                {
                    warningTripped = true;
                }
            }

            // --- Transaction 2: Temp Group Creation & Pre-Swap mapping ---
            if (ProgressCoordinator.IsCancelled()) return null;
            ProgressCoordinator.UpdateStatus($"[{drafting.Name}] Generating temporary swap groups...");

            Dictionary<ElementId, ElementId> tempToOriginalTypeMap = new Dictionary<ElementId, ElementId>();
            List<ElementId> groupsToRestoreInDrafting = new List<ElementId>();

            using (Transaction t2 = new Transaction(doc, "Pre-Swap Groups"))
            {
                t2.Start();

                List<Group> draftingGroups = new FilteredElementCollector(doc, drafting.Id)
                    .OfClass(typeof(Group))
                    .Cast<Group>()
                    .ToList();

                List<GroupType> distinctGroupTypes = draftingGroups
                    .Select(g => g.GroupType)
                    .GroupBy(gt => gt.Id)
                    .Select(g => g.First())
                    .ToList();

                foreach (GroupType origType in distinctGroupTypes)
                {
                    Line line = Line.CreateBound(XYZ.Zero, new XYZ(0.1, 0, 0));
                    DetailCurve detailLine = doc.Create.NewDetailCurve(drafting, line);

                    Group tempGroupInstance = doc.Create.NewGroup(new List<ElementId> { detailLine.Id });
                    GroupType tempGroupType = tempGroupInstance.GroupType;
                    tempGroupType.Name = "TEMP_SWAP_" + Guid.NewGuid().ToString();

                    tempToOriginalTypeMap.Add(tempGroupType.Id, origType.Id);

                    foreach (Group g in draftingGroups.Where(g => g.GroupType.Id == origType.Id))
                    {
                        g.GroupType = tempGroupType;
                        groupsToRestoreInDrafting.Add(g.Id);
                    }

                    doc.Delete(tempGroupInstance.Id);
                }

                t2.Commit();
            }

            // --- Transaction 3: Copy elements to Legend ---
            if (ProgressCoordinator.IsCancelled()) return null;
            ProgressCoordinator.UpdateStatus($"[{drafting.Name}] Copying elements to Legend view...");

            View? newLegend = null;
            List<ElementId>? copiedElementIds = null;

            using (Transaction t3 = new Transaction(doc, "Copy Elements to Legend"))
            {
                t3.Start();

                // Get base legend
                View? baseLegend = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .OfCategory(BuiltInCategory.OST_Views)
                    .OfType<View>()
                    .FirstOrDefault(v => v.ViewType == ViewType.Legend);

                if (baseLegend == null)
                {
                    t3.RollBack();
                    throw new InvalidOperationException("No base Legend view found in the document.");
                }

                // Duplicate base legend
                newLegend = (View)doc.GetElement(baseLegend.Duplicate(ViewDuplicateOption.Duplicate));
                newLegend.Scale = drafting.Scale;

                // Collect elements to copy
                IList<Element> draftingElements = new FilteredElementCollector(doc, drafting.Id).ToElements();
                List<ElementId> elementsToCopy = new List<ElementId>();

                foreach (Element element in draftingElements)
                {
                    if (element != null && element.Category != null)
                    {
                        if (element.Id != drafting.Id && !(element is View))
                        {
                            elementsToCopy.Add(element.Id);
                        }
                    }
                }

                CopyPasteOptions copyPasteOptions = new CopyPasteOptions();
                copyPasteOptions.SetDuplicateTypeNamesHandler(new CopyUseDestination());

                // Execute copy
                copiedElementIds = (List<ElementId>)ElementTransformUtils.CopyElements(
                    drafting,
                    elementsToCopy,
                    newLegend,
                    null,
                    copyPasteOptions);

                // Set overrides
                List<Tuple<ElementId, ElementId>> zipped = copiedElementIds
                    .Zip(elementsToCopy, (a, b) => new Tuple<ElementId, ElementId>(a, b))
                    .ToList();

                foreach (Tuple<ElementId, ElementId> tuple in zipped)
                {
                    newLegend.SetElementOverrides(tuple.Item1, drafting.GetElementOverrides(tuple.Item2));
                }

                // Unique naming collision checking with numerical suffix iteration
                IList<string> allLegends = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .OfCategory(BuiltInCategory.OST_Views)
                    .OfType<View>()
                    .Where(v => v.ViewType == ViewType.Legend)
                    .Select(v => v.Name)
                    .ToList();

                string newName = drafting.Name;
                int suffix = 1;
                while (allLegends.Contains(newName))
                {
                    newName = $"{drafting.Name} (Converted from Drafting)_{suffix++}";
                }
                newLegend.Name = newName;

                t3.Commit();
            }

            // --- Transaction 4: Post-Swap back to Original GroupTypes & Delete Temp GroupTypes ---
            if (ProgressCoordinator.IsCancelled()) return null;
            ProgressCoordinator.UpdateStatus($"[{drafting.Name}] Reverting group types and cleaning up...");

            using (Transaction t4 = new Transaction(doc, "Restore Group Types and Cleanup"))
            {
                t4.Start();

                // Swap Legend groups back
                List<Group> legendGroups = copiedElementIds
                    .Select(id => doc.GetElement(id))
                    .OfType<Group>()
                    .ToList();

                foreach (Group lg in legendGroups)
                {
                    if (tempToOriginalTypeMap.TryGetValue(lg.GroupType.Id, out ElementId? originalTypeId) && originalTypeId != null)
                    {
                        lg.GroupType = (GroupType)doc.GetElement(originalTypeId);
                    }
                }

                // Swap Drafting groups back
                foreach (ElementId dgId in groupsToRestoreInDrafting)
                {
                    Group? dg = doc.GetElement(dgId) as Group;
                    if (dg != null && tempToOriginalTypeMap.TryGetValue(dg.GroupType.Id, out ElementId? originalTypeId) && originalTypeId != null)
                    {
                        dg.GroupType = (GroupType)doc.GetElement(originalTypeId);
                    }
                }

                // Delete temporary group types
                foreach (ElementId tempTypeId in tempToOriginalTypeMap.Keys)
                {
                    doc.Delete(tempTypeId);
                }

                t4.Commit();
            }

            // --- Transaction 5: Viewport Replacement ---
            if (ProgressCoordinator.IsCancelled()) return null;
            ProgressCoordinator.UpdateStatus($"[{drafting.Name}] Swapping view on sheet...");

            try
            {
                using (Transaction t5 = new Transaction(doc, "Replace Viewport on Sheet"))
                {
                    t5.Start();

                    Viewport? oldViewport = new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .FirstOrDefault(vp => vp.ViewId == drafting.Id);

                    if (oldViewport != null && newLegend != null)
                    {
                        ElementId sheetId = oldViewport.SheetId;
                        XYZ boxCenter = oldViewport.GetBoxCenter();
                        ElementId typeId = oldViewport.GetTypeId();

                        // Delete the old viewport
                        doc.Delete(oldViewport.Id);

                        // Create the new viewport for the legend view
                        Viewport newViewport = Viewport.Create(doc, sheetId, newLegend.Id, boxCenter);
                        if (newViewport != null && typeId != ElementId.InvalidElementId)
                        {
                            newViewport.ChangeTypeId(typeId);
                        }
                    }

                    t5.Commit();
                }
            }
            catch (Exception)
            {
                // Gracefully skip and let conversion succeed normally if viewport replacement fails
            }

            if (ProgressCoordinator.IsCancelled()) return null;

            return newLegend;
        }

        /// <summary>
        /// Convert a Legend view to a Drafting view
        /// </summary>
        /// <param name="legend">Legend View</param>
        /// <returns>A Drafting view</returns>
        public static View? ConvertToDrafting(View legend)
        {
            View? newDrafting = null;

            if (legend != null && legend.ViewType == ViewType.Legend)
            {
                Document doc = legend.Document;
                IList<Element> legendElements = new FilteredElementCollector(doc, legend.Id).ToElements();

                List<ElementId> elementsToCopy = new List<ElementId>();
                foreach (Element element in legendElements)
                {
                    if (element is Element && element.Category != null && element.Category.Name != "Legend Components")
                    {
                        elementsToCopy.Add(element.Id);
                    }
                }

                IList<string> allDrafting = (IList<string>)new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .OfCategory(BuiltInCategory.OST_Views)
                        .OfType<View>()
                        .Where(v => v.ViewType == ViewType.DraftingView)
                        .Select<View, string>(v => v.Name)
                        .ToList();

                ViewFamilyType? viewType = null;
                try
                {
                    viewType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .OfType<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                }
                catch (ArgumentNullException)
                {

                }

                if (viewType == null)
                {
                    return null;
                }

                newDrafting = ViewDrafting.Create(doc, viewType.Id);
                CopyPasteOptions copyPasteOptions = new CopyPasteOptions();
                copyPasteOptions.SetDuplicateTypeNamesHandler(new CopyUseDestination());

                List<ElementId> newElements = (List<ElementId>)ElementTransformUtils.CopyElements(
                    legend,
                    elementsToCopy,
                    newDrafting,
                    null,
                    copyPasteOptions);

                List<Tuple<ElementId, ElementId>> zipped = newElements
                    .Zip(elementsToCopy, (a, b) => new Tuple<ElementId, ElementId>(a, b))
                    .ToList();

                foreach (Tuple<ElementId, ElementId> t in zipped)
                {
                    newDrafting.SetElementOverrides(t.Item1, legend.GetElementOverrides(t.Item2));
                }

                string newName = legend.Name;
                if (allDrafting.Contains(newName))
                {
                    newName += " (Converted from Legend)";
                }

                newDrafting.Name = newName;
                newDrafting.Scale = legend.Scale;
            }

            return newDrafting;
        }
    }

    /// <summary>
    /// Overrides IDuplicateTypeNamsHandler for use in converting Drafting Views to Legends.
    /// </summary>
    public class CopyUseDestination : IDuplicateTypeNamesHandler
    {
        DuplicateTypeAction IDuplicateTypeNamesHandler.OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }

    /// <summary>
    /// Suppresses constraint deletion warnings when grouping reference planes.
    /// </summary>
    public class SuppressConstraintsPreprocessor : IFailuresPreprocessor
    {
        /// <summary>
        /// Gets a value indicating whether a warning was tripped and suppressed.
        /// </summary>
        public bool WarningTripped { get; private set; } = false;

        /// <summary>
        /// Preprocesses failures to delete warnings.
        /// </summary>
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failureMessages)
            {
                FailureSeverity severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                    WarningTripped = true;
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}

