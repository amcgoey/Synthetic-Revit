using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.AutoTagger.Commands
{
    /// <summary>
    /// Revit external command to batch tag elements in the active view or a selection.
    /// Matches elements to saved tag templates and places tags accordingly.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdBatchTag : IExternalCommand
    {
        /// <summary>
        /// Executes the batch tagging command.
        /// </summary>
        /// <param name="commandData">Revit external command data.</param>
        /// <param name="message">A message returning errors if any.</param>
        /// <param name="elements">Revit elements set.</param>
        /// <returns>Result code of the execution.</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1. Retrieve Saved Templates
            var repo = new TemplateStorageRepository();
            List<TagTemplate> templates = repo.GetTemplates(doc);

            if (templates == null || !templates.Any())
            {
                Autodesk.Revit.UI.TaskDialog.Show("No Templates", "There are no saved tag templates. Please use 'Set Tag Template' or 'Manage Templates' to create some first.");
                return Result.Cancelled;
            }

            // 2. Identify Already-Tagged Elements in Active View
            HashSet<ElementId> taggedElementIds = new HashSet<ElementId>();
            var existingTags = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in existingTags)
            {
                // GetTaggedLocalElementIds() is the required method for Revit 2022+ API
                foreach (var id in tag.GetTaggedLocalElementIds())
                {
                    taggedElementIds.Add(id);
                }
            }

            // 3. Gather Target Elements (Selection vs. View Collection)
            IEnumerable<FamilyInstance> targetElements;
            var selectedIds = uidoc.Selection.GetElementIds();

            if (selectedIds.Count > 0)
            {
                targetElements = selectedIds
                    .Select(id => doc.GetElement(id))
                    .OfType<FamilyInstance>();
            }
            else
            {
                targetElements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();
            }

            // 4. Filter Targets (Exclude nested families and already tagged elements)
            var validTargets = targetElements
                .Where(e => e.SuperComponent == null && !taggedElementIds.Contains(e.Id))
                .ToList();

            if (!validTargets.Any())
            {
                Autodesk.Revit.UI.TaskDialog.Show("Batch Tag Complete", "No untagged elements found matching the current selection or view.");
                return Result.Succeeded;
            }

            // Pre-flight check: scan for template scope conflicts
            var conflictsMap = new Dictionary<string, (string DisplayName, List<TagTemplate> Candidates)>();
            var nonConflictingMap = new Dictionary<string, TagTemplate>();
            var elementScopeMap = new Dictionary<FamilyInstance, string>();

            foreach (var host in validTargets)
            {
                string? catName = host.Category?.Name;
                string? famName = host.Symbol?.FamilyName;
                string typeName = host.Name;

                if (string.IsNullOrEmpty(catName))
                    continue;

                List<TagTemplate> matches = new List<TagTemplate>();
                string scopeKey = string.Empty;
                string scopeDisplayName = string.Empty;

                var typeTemplates = templates.Where(t => t.TargetCategory == catName && t.TargetFamily == famName && t.TargetType == typeName).ToList();
                if (typeTemplates.Any())
                {
                    matches = typeTemplates;
                    scopeKey = $"Type:{catName}:{famName}:{typeName}";
                    scopeDisplayName = $"[Type] {famName} - {typeName}";
                }
                else
                {
                    var familyTemplates = templates.Where(t => t.TargetCategory == catName && t.TargetFamily == famName && string.IsNullOrEmpty(t.TargetType)).ToList();
                    if (familyTemplates.Any())
                    {
                        matches = familyTemplates;
                        scopeKey = $"Family:{catName}:{famName}";
                        scopeDisplayName = $"[Family] {famName}";
                    }
                    else
                    {
                        var categoryTemplates = templates.Where(t => t.TargetCategory == catName && string.IsNullOrEmpty(t.TargetFamily) && string.IsNullOrEmpty(t.TargetType)).ToList();
                        if (categoryTemplates.Any())
                        {
                            matches = categoryTemplates;
                            scopeKey = $"Category:{catName}";
                            scopeDisplayName = $"[Category] {catName}";
                        }
                    }
                }

                if (matches.Any())
                {
                    elementScopeMap[host] = scopeKey;
                    if (matches.Count == 1)
                    {
                        nonConflictingMap[scopeKey] = matches[0];
                    }
                    else
                    {
                        if (!conflictsMap.ContainsKey(scopeKey))
                        {
                            conflictsMap[scopeKey] = (scopeDisplayName, matches);
                        }
                    }
                }
            }

            var resolvedScopeTemplates = new Dictionary<string, TagTemplate>();
            foreach (var kvp in nonConflictingMap)
            {
                resolvedScopeTemplates[kvp.Key] = kvp.Value;
            }

            if (conflictsMap.Any())
            {
                var conflictItems = conflictsMap.Select(kvp => new ConflictItem
                {
                    UniqueKey = kvp.Key,
                    DisplayName = kvp.Value.DisplayName,
                    Candidates = kvp.Value.Candidates
                }).ToList();

                var vm = new ResolveConflictsViewModel(conflictItems);
                var view = new ResolveConflictsView(uiapp.MainWindowHandle) { DataContext = vm };
                vm.CloseAction = new Action(view.Close);

                view.ShowDialog();

                if (!vm.Proceeded)
                {
                    return Result.Cancelled;
                }

                foreach (var item in vm.Conflicts)
                {
                    if (item.SelectedCandidate != null)
                    {
                        resolvedScopeTemplates[item.UniqueKey] = item.SelectedCandidate;
                    }
                }
            }

            int successCount = 0;
            int skipCount = 0;
            int errorCount = 0;

            // 5. Execution Loop
            using (Transaction t = new Transaction(doc, "Batch Auto-Tag Elements"))
            {
                t.Start();

                foreach (var host in validTargets)
                {
                    try
                    {
                        if (!elementScopeMap.TryGetValue(host, out string? scopeKey) ||
                            !resolvedScopeTemplates.TryGetValue(scopeKey, out TagTemplate? match) ||
                            match == null)
                        {
                            skipCount++;
                            continue;
                        }

                        // Calculate Transform
                        XYZ localOffset = new XYZ(match.OffsetX, match.OffsetY, match.OffsetZ);
                        XYZ? worldPoint = CoordinateUtility.GetWorldPoint(host, localOffset);
                        if (worldPoint != null)
                        {
                            TagOrientation orientation = CoordinateUtility.CalculateTagOrientation(host, match);
                            IndependentTag.Create(doc, doc.ActiveView.Id, new Reference(host), false, TagMode.TM_ADDBY_CATEGORY, orientation, worldPoint!);
                            successCount++;
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                    catch (Exception)
                    {
                        // Safely swallow exceptions (e.g. tag family not loaded) to prevent failing the entire batch
                        errorCount++;
                    }
                }
                t.Commit();
            }

            // 6. Summary Report
            Autodesk.Revit.UI.TaskDialog.Show("Batch Tag Execution", 
                $"Batch Tagging Complete.\n\n" +
                $"Successfully Tagged: {successCount}\n" +
                $"Skipped (No Template/Already Tagged): {skipCount}\n" +
                $"Errors (Missing Tag Families/Bad Geometry): {errorCount}");
            return Result.Succeeded;
        }
    }
}
