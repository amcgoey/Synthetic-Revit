### File: AutoTagger/Commands/CmdBatchTag.cs
```csharp
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

using Synthetic.Modules.RevitDOM;
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
```

### File: AutoTagger/Commands/CmdManageTemplates.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Commands
{
    /// <summary>
    /// Revit external command to manage tag templates.
    /// Provides a user interface to view, create, edit, or delete tag templates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdManageTemplates : IExternalCommand
    {
        /// <summary>
        /// Executes the template manager command.
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
            bool keepOpen = true;
            
            // State Machine Loop: Allows transitioning between WPF Dialog (Thread Blocked) 
            // and Revit API Canvas Interaction without losing command context.
            while (keepOpen)
            {
                var vm = new ManageTemplatesViewModel(doc);
                var view = new ManageTemplatesView(uiapp.MainWindowHandle) { DataContext = vm };
                vm.CloseAction = new Action(view.Close);
                
                view.ShowDialog();

                if (vm.RequestedAction == ManageTemplatesAction.NewTemplate)
                {
                    try
                    {
                        Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), 
                            "Select a Host Element (FamilyInstance) for the new template.");
                        FamilyInstance? host = doc.GetElement(hostRef) as FamilyInstance;
                        Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), 
                            "Select the associated Independent Tag.");
                        IndependentTag? tag = doc.GetElement(tagRef) as IndependentTag;
                        if (host != null && tag != null)
                        {
                            XYZ? localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);
                            if (localOffset != null)
                            {
                                SetTemplateViewModel setVm = new SetTemplateViewModel(doc, host, localOffset!, tag.TagOrientation);
                                SetTemplateView setView = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = setVm };
                                setVm.CloseAction = new Action(setView.Close);
                                setView.ShowDialog();
                            }
                            else
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Transformation Error", "Could not calculate the coordinate transform.");
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // User pressed ESC during PickObject. Swallow exception to allow the while loop to re-open the manager.
                    }
                    catch (Exception ex)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Error", ex.Message);
                    }
                }
                else if (vm.RequestedAction == ManageTemplatesAction.EditTemplate && vm.TemplateToEdit != null)
                {
                    try
                    {
                        Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), 
                            $"Select new Host Element for template: {vm.TemplateToEdit.TemplateName}");
                        FamilyInstance? host = doc.GetElement(hostRef) as FamilyInstance;
                        Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), 
                            "Select the associated Independent Tag.");
                        IndependentTag? tag = doc.GetElement(tagRef) as IndependentTag;
                        if (host != null && tag != null)
                        {
                            XYZ? localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);
                            if (localOffset != null)
                            {
                                SetTemplateViewModel setVm = new SetTemplateViewModel(doc, host, localOffset!, tag.TagOrientation, vm.TemplateToEdit);
                                SetTemplateView setView = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = setVm };
                                setVm.CloseAction = new Action(setView.Close);
                                setView.ShowDialog();
                            }
                            else
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Transformation Error", "Could not calculate the coordinate transform.");
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // User pressed ESC during PickObject. Swallow exception to allow the while loop to re-open the manager.
                    }
                    catch (Exception ex)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Error", ex.Message);
                    }
                }
                else
                {
                    // User closed the window or clicked the X. Terminate the state loop.
                    keepOpen = false;
                }
            }
            return Result.Succeeded;
        }

        private class FamilyInstanceFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private class IndependentTagFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is IndependentTag;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
```

### File: AutoTagger/Commands/CmdSetTemplate.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Commands
{
    /// <summary>
    /// Revit external command to set a tag template.
    /// Prompts the user to select a host element and its associated tag, then registers the configuration.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdSetTemplate : IExternalCommand
    {
        /// <summary>
        /// Executes the set template command.
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
            try
            {
                Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), "Select a Host Element (FamilyInstance).");
                FamilyInstance? host = doc.GetElement(hostRef) as FamilyInstance;
                Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), "Select the associated Independent Tag.");
                IndependentTag? tag = doc.GetElement(tagRef) as IndependentTag;
                if (host == null || tag == null) return Result.Cancelled;
                XYZ? localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);
                                
                if (localOffset == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Transformation Error", "Could not calculate the coordinate transform. The host element geometry may not be conformal.");
                    return Result.Failed;
                }
                SetTemplateViewModel vm = new SetTemplateViewModel(doc, host, localOffset!, tag.TagOrientation);
                SetTemplateView view = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = vm };
                vm.CloseAction = new Action(view.Close);
                                
                view.ShowDialog();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private class FamilyInstanceFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private class IndependentTagFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is IndependentTag;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
```

### File: AutoTagger/Models/TagTemplate.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.AutoTagger.Models
{
    /// <summary>
    /// Data Transfer Object (DTO) representing a user-defined tag offset template.
    /// </summary>
    public class TagTemplate
    {
        /// <summary>
        /// Gets or sets the unique identifier of the tag template.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Gets or sets the name of the template.
        /// </summary>
        public string TemplateName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target category name.
        /// </summary>
        public string TargetCategory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target family name.
        /// </summary>
        public string? TargetFamily { get; set; }

        /// <summary>
        /// Gets or sets the target type name.
        /// </summary>
        public string? TargetType { get; set; }
        
        /// <summary>
        /// Gets or sets the relative X offset in decimal feet.
        /// </summary>
        public double OffsetX { get; set; }

        /// <summary>
        /// Gets or sets the relative Y offset in decimal feet.
        /// </summary>
        public double OffsetY { get; set; }

        /// <summary>
        /// Gets or sets the relative Z offset in decimal feet.
        /// </summary>
        public double OffsetZ { get; set; }

        /// <summary>
        /// Gets or sets the orientation of the tag.
        /// </summary>
        public TagOrientation Orientation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether orientation change is allowed.
        /// </summary>
        public bool AllowOrientationChange { get; set; }

        /// <summary>
        /// Gets or sets the X component of the host's hand orientation vector.
        /// </summary>
        public double HostHandX { get; set; }

        /// <summary>
        /// Gets or sets the Y component of the host's hand orientation vector.
        /// </summary>
        public double HostHandY { get; set; }

        /// <summary>
        /// Gets or sets the Z component of the host's hand orientation vector.
        /// </summary>
        public double HostHandZ { get; set; }
    }
}
```

### File: AutoTagger/Repositories/TemplateStorageRepository.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.AutoTagger.Repositories
{
    /// <summary>
    /// Handles all CRUD operations for TagTemplates, abstracting Revit Extensible Storage and local JSON file I/O.
    /// </summary>
    public class TemplateStorageRepository
    {
        private readonly Guid _schemaGuid = new Guid("4A9119B9-7221-4A59-AC10-7B32F4D9C8A1");
        private const string SchemaName = "SyntheticAutoTagger_Templates";
        private const string FieldName = "TemplateJsonData";

        private Schema GetSchema()
        {
            Schema schema = Schema.Lookup(_schemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(_schemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName(SchemaName);
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }

        private DataStorage? GetDataStorage(Document doc, Schema schema)
        {
            var collector = new FilteredElementCollector(doc).OfClass(typeof(DataStorage));
            foreach (DataStorage ds in collector)
            {
                Entity entity = ds.GetEntity(schema);
                if (entity.IsValid()) return ds;
            }
            return null;
        }

        /// <summary>
        /// Retrieves all templates saved within the active Revit Document.
        /// </summary>
        public List<TagTemplate> GetTemplates(Document doc)
        {
            Schema schema = GetSchema();
            DataStorage? ds = GetDataStorage(doc, schema);
            if (ds == null) return new List<TagTemplate>();
            Entity entity = ds.GetEntity(schema);
            string jsonData = entity.Get<string>(FieldName);
            if (string.IsNullOrWhiteSpace(jsonData)) return new List<TagTemplate>();
            try
            {
                return JsonConvert.DeserializeObject<List<TagTemplate>>(jsonData) ?? new List<TagTemplate>();
            }
            catch
            {
                return new List<TagTemplate>();
            }
        }

        /// <summary>
        /// Saves a template to the Revit Document. Creates the DataStorage element if it does not exist.
        /// </summary>
        public void SaveTemplate(Document doc, TagTemplate template)
        {
            List<TagTemplate> templates = GetTemplates(doc);
            
            // Update existing or add new
            var existing = templates.FirstOrDefault(t => t.Id == template.Id);
            if (existing != null) templates.Remove(existing);
            templates.Add(template);
            string jsonData = JsonConvert.SerializeObject(templates);
            Schema schema = GetSchema();
            using (Transaction t = new Transaction(doc, "Save Tag Template"))
            {
                t.Start();
                DataStorage? ds = GetDataStorage(doc, schema);
                if (ds == null)
                {
                    ds = DataStorage.Create(doc);
                }
                Entity entity = new Entity(schema);
                entity.Set(FieldName, jsonData);
                ds.SetEntity(entity);
                t.Commit();
            }
        }

        /// <summary>
        /// Exports a list of templates to a local JSON file.
        /// </summary>
        public void ExportToJson(List<TagTemplate> templates, string filePath)
        {
            string jsonData = JsonConvert.SerializeObject(templates, Formatting.Indented);
            File.WriteAllText(filePath, jsonData);
        }

        /// <summary>
        /// Imports a list of templates from a local JSON file.
        /// </summary>
        /// <param name="filePath">The file path to the JSON file containing tag templates.</param>
        /// <returns>A list of deserialized TagTemplate objects.</returns>
        public List<TagTemplate> ImportFromJson(string filePath)
        {
            if (!File.Exists(filePath)) return new List<TagTemplate>();
            string jsonData = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<TagTemplate>>(jsonData) ?? new List<TagTemplate>();
        }

        /// <summary>
        /// Deletes a specific template from the Extensible Storage.
        /// </summary>
        public void DeleteTemplate(Document doc, Guid templateId)
        {
            List<TagTemplate> templates = GetTemplates(doc);
            var existing = templates.FirstOrDefault(t => t.Id == templateId);
            if (existing != null)
            {
                templates.Remove(existing);
                string jsonData = JsonConvert.SerializeObject(templates);
                Schema schema = GetSchema();

                using (Transaction t = new Transaction(doc, "Delete Tag Template"))
                {
                    t.Start();
                    DataStorage? ds = GetDataStorage(doc, schema);
                    if (ds != null)
                    {
                        Entity entity = new Entity(schema);
                        entity.Set(FieldName, jsonData);
                        ds.SetEntity(entity);
                    }
                    t.Commit();
                }
            }
        }

        /// <summary>
        /// Bulk saves templates, merging them with existing templates by ID.
        /// </summary>
        public void SaveAllTemplates(Document doc, List<TagTemplate> newTemplates)
        {
            List<TagTemplate> templates = GetTemplates(doc);
            
            foreach (var nt in newTemplates)
            {
                templates.RemoveAll(t => t.Id == nt.Id);
                templates.Add(nt);
            }

            string jsonData = JsonConvert.SerializeObject(templates);
            Schema schema = GetSchema();

            using (Transaction t = new Transaction(doc, "Import Tag Templates"))
            {
                t.Start();
                DataStorage? ds = GetDataStorage(doc, schema);
                if (ds == null) ds = DataStorage.Create(doc);

                Entity entity = new Entity(schema);
                entity.Set(FieldName, jsonData);
                ds.SetEntity(entity);
                t.Commit();
            }
        }
    }
}
```

### File: AutoTagger/ViewModels/ManageTemplatesViewModel.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Microsoft.Win32;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.AutoTagger.ViewModels
{
    /// <summary>
    /// Specifies the actions that can be requested when managing templates.
    /// </summary>
    public enum ManageTemplatesAction
    {
        /// <summary>
        /// No action requested.
        /// </summary>
        None,

        /// <summary>
        /// Request to create a new template.
        /// </summary>
        NewTemplate,

        /// <summary>
        /// Request to edit an existing template.
        /// </summary>
        EditTemplate
    }

    /// <summary>
    /// ViewModel controlling the CRUD interface and state loop for managing templates.
    /// </summary>
    public class ManageTemplatesViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly TemplateStorageRepository _repository;
        private readonly IFileDialogService _fileDialogService;
        private readonly IUserPromptService _userPromptService;
        /// <summary>
        /// Gets or sets the collection of tag templates.
        /// </summary>
        public ObservableCollection<TagTemplate> Templates { get; set; } = new ObservableCollection<TagTemplate>();

        private TagTemplate? _selectedTemplate;
        /// <summary>
        /// Gets or sets the currently selected template.
        /// </summary>
        public TagTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets the action requested by the view model.
        /// </summary>
        public ManageTemplatesAction RequestedAction { get; private set; } = ManageTemplatesAction.None;

        /// <summary>
        /// Gets the template selected for editing.
        /// </summary>
        public TagTemplate? TemplateToEdit { get; private set; }

        /// <summary>
        /// Gets the delete template command.
        /// </summary>
        public System.Windows.Input.ICommand DeleteCommand { get; }

        /// <summary>
        /// Gets the export templates to JSON command.
        /// </summary>
        public System.Windows.Input.ICommand ExportJsonCommand { get; }

        /// <summary>
        /// Gets the import templates from JSON command.
        /// </summary>
        public System.Windows.Input.ICommand ImportJsonCommand { get; }

        /// <summary>
        /// Gets the edit selected template command.
        /// </summary>
        public System.Windows.Input.ICommand EditCommand { get; }

        /// <summary>
        /// Gets the command to create a new template.
        /// </summary>
        public System.Windows.Input.ICommand NewTemplateCommand { get; }

        /// <summary>
        /// Gets or sets the action to close the window.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManageTemplatesViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        public ManageTemplatesViewModel(Document doc, IFileDialogService? fileDialogService = null, IUserPromptService? userPromptService = null)
        {
            _doc = doc;
            _repository = new TemplateStorageRepository();
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();
            _userPromptService = userPromptService ?? new WindowsUserPromptService();
            
            LoadTemplates();

            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteSelectionBased);
            ExportJsonCommand = new RelayCommand(ExecuteExport, CanExecuteExport);
            ImportJsonCommand = new RelayCommand(ExecuteImport);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteSelectionBased);
            NewTemplateCommand = new RelayCommand(ExecuteNewTemplate);
        }

        private void LoadTemplates()
        {
            var dbTemplates = _repository.GetTemplates(_doc);
            Templates = new ObservableCollection<TagTemplate>(dbTemplates);
            OnPropertyChanged(nameof(Templates));
        }

        private bool CanExecuteSelectionBased(object obj) => SelectedTemplate != null;
        private bool CanExecuteExport(object obj) => Templates != null && Templates.Any();

        private void ExecuteDelete(object obj)
        {
            if (SelectedTemplate != null)
            {
                _repository.DeleteTemplate(_doc, SelectedTemplate.Id);
                Templates.Remove(SelectedTemplate);
            }
        }

        private void ExecuteExport(object obj)
        {
            var filePath = _fileDialogService.SaveFileDialog("JSON Files (*.json)|*.json", "Export Templates", "AutoTagger_Templates.json");
            if (!string.IsNullOrEmpty(filePath))
            {
                _repository.ExportToJson(Templates.ToList(), filePath);
                _userPromptService.ShowMessage($"Templates exported to:\n{filePath}", "Export Successful");
            }
        }

        private void ExecuteImport(object obj)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json", "Import Templates", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var imported = _repository.ImportFromJson(filePath);
                    if (imported != null && imported.Any())
                    {
                        _repository.SaveAllTemplates(_doc, imported);
                        LoadTemplates();
                        _userPromptService.ShowMessage($"Imported {imported.Count} templates.", "Import Successful");
                    }
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Failed to parse JSON file:\n{ex.Message}", "Import Error");
                }
            }
        }

        private void ExecuteEdit(object obj)
        {
            RequestedAction = ManageTemplatesAction.EditTemplate;
            TemplateToEdit = SelectedTemplate;
            CloseAction?.Invoke();
        }

        private void ExecuteNewTemplate(object obj)
        {
            RequestedAction = ManageTemplatesAction.NewTemplate;
            CloseAction?.Invoke();
        }
    }
}
```

### File: AutoTagger/ViewModels/ResolveConflictsViewModel.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.AutoTagger.ViewModels
{
    /// <summary>
    /// Represents an item with a naming conflict and its resolution options.
    /// </summary>
    public class ConflictItem : ViewModelBase
    {
        /// <summary>
        /// Gets or sets the display name of the conflicted item.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the unique key of the conflicted item.
        /// </summary>
        public string UniqueKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of candidates to resolve the conflict.
        /// </summary>
        public List<TagTemplate> Candidates { get; set; } = new List<TagTemplate>();

        private TagTemplate? _selectedCandidate;
        /// <summary>
        /// Gets or sets the selected candidate chosen to resolve the conflict.
        /// </summary>
        public TagTemplate? SelectedCandidate
        {
            get => _selectedCandidate;
            set
            {
                SetProperty(ref _selectedCandidate, value);
            }
        }
    }

    /// <summary>
    /// ViewModel for resolving template conflicts.
    /// </summary>
    public class ResolveConflictsViewModel : ViewModelBase
    {
        /// <summary>
        /// Gets the collection of conflict items.
        /// </summary>
        public ObservableCollection<ConflictItem> Conflicts { get; }

        /// <summary>
        /// Gets a value indicating whether the user proceeded with resolving the conflicts.
        /// </summary>
        public bool Proceeded { get; private set; } = false;

        /// <summary>
        /// Gets the proceed command.
        /// </summary>
        public ICommand ProceedCommand { get; }

        /// <summary>
        /// Gets the cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets or sets the close action for the window.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResolveConflictsViewModel"/> class.
        /// </summary>
        /// <param name="conflicts">The list of naming conflicts to resolve.</param>
        public ResolveConflictsViewModel(List<ConflictItem> conflicts)
        {
            Conflicts = new ObservableCollection<ConflictItem>(conflicts);
            
            // Default select the first candidate for each conflict item
            foreach (var item in Conflicts)
            {
                item.SelectedCandidate = item.Candidates.FirstOrDefault();
            }

            ProceedCommand = new RelayCommand(ExecuteProceed, CanExecuteProceed);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        private bool CanExecuteProceed(object obj)
        {
            return Conflicts.All(c => c.SelectedCandidate != null);
        }

        private void ExecuteProceed(object obj)
        {
            Proceeded = true;
            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object obj)
        {
            Proceeded = false;
            CloseAction?.Invoke();
        }
    }
}
```

### File: AutoTagger/ViewModels/SetTemplateViewModel.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.DB;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.AutoTagger.ViewModels
{
    /// <summary>
    /// Specifies the matching scope for a tag template (Category-level, Family-level, or Type-level).
    /// </summary>
    public enum TemplateScope
    {
        /// <summary>
        /// Applies to all elements in the category.
        /// </summary>
        Category,

        /// <summary>
        /// Applies to all types under a specific family.
        /// </summary>
        Family,

        /// <summary>
        /// Applies only to a specific element type.
        /// </summary>
        Type
    }

    /// <summary>
    /// ViewModel controlling the logic for capturing and saving a new TagTemplate.
    /// </summary>
    public class SetTemplateViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly FamilyInstance _host;
        private readonly XYZ _localOffset;
        private readonly TagOrientation _tagOrientation;
        private readonly TagTemplate? _editingTemplate;

        private string _templateName = string.Empty;
        /// <summary>
        /// Gets or sets the name of the template.
        /// </summary>
        public string TemplateName 
        { 
            get => _templateName; 
            set 
            { 
                SetProperty(ref _templateName, value); 
                System.Windows.Input.CommandManager.InvalidateRequerySuggested(); 
            }
        }

        private bool _allowOrientationChange;
        /// <summary>
        /// Gets or sets a value indicating whether orientation change is allowed.
        /// </summary>
        public bool AllowOrientationChange
        {
            get => _allowOrientationChange;
            set
            {
                SetProperty(ref _allowOrientationChange, value);
            }
        }

        /// <summary>
        /// Gets the category name of the host element.
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        /// Gets the family name of the host element.
        /// </summary>
        public string FamilyName { get; }

        /// <summary>
        /// Gets the type name of the host element.
        /// </summary>
        public string TypeName { get; }

        private TemplateScope _selectedScope = TemplateScope.Type;
        /// <summary>
        /// Gets or sets the selected template matching scope.
        /// </summary>
        public TemplateScope SelectedScope 
        { 
            get => _selectedScope; 
            set 
            { 
                if (SetProperty(ref _selectedScope, value))
                {
                    OnPropertyChanged(nameof(IsCategoryScope));
                    OnPropertyChanged(nameof(IsFamilyScope));
                    OnPropertyChanged(nameof(IsTypeScope));
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the scope is set to Category.
        /// </summary>
        public bool IsCategoryScope { get => SelectedScope == TemplateScope.Category; set { if(value) SelectedScope = TemplateScope.Category; } }

        /// <summary>
        /// Gets or sets a value indicating whether the scope is set to Family.
        /// </summary>
        public bool IsFamilyScope { get => SelectedScope == TemplateScope.Family; set { if(value) SelectedScope = TemplateScope.Family; } }

        /// <summary>
        /// Gets or sets a value indicating whether the scope is set to Type.
        /// </summary>
        public bool IsTypeScope { get => SelectedScope == TemplateScope.Type; set { if(value) SelectedScope = TemplateScope.Type; } }

        /// <summary>
        /// Gets the Save template command.
        /// </summary>
        public System.Windows.Input.ICommand SaveCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public System.Windows.Input.ICommand CancelCommand { get; }
        
        /// <summary>
        /// Delegate assigned by the View to allow the ViewModel to request closure without violating MVVM.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetTemplateViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="host">The host family instance.</param>
        /// <param name="localOffset">The tag offset coordinates.</param>
        /// <param name="tagOrientation">The tag orientation.</param>
        /// <param name="editingTemplate">Optional existing template being edited.</param>
        public SetTemplateViewModel(Document doc, FamilyInstance host, XYZ localOffset, TagOrientation tagOrientation, TagTemplate? editingTemplate = null)
        {
            _doc = doc;
            _host = host;
            _localOffset = localOffset;
            _tagOrientation = tagOrientation;
            _editingTemplate = editingTemplate;

            CategoryName = host.Category?.Name ?? "Unknown Category";
            FamilyName = host.Symbol?.FamilyName ?? "Unknown Family";
            TypeName = host.Name;
            
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);

            if (_editingTemplate != null)
            {
                TemplateName = _editingTemplate.TemplateName;
                AllowOrientationChange = _editingTemplate.AllowOrientationChange;
                if (!string.IsNullOrEmpty(_editingTemplate.TargetType))
                    SelectedScope = TemplateScope.Type;
                else if (!string.IsNullOrEmpty(_editingTemplate.TargetFamily))
                    SelectedScope = TemplateScope.Family;
                else
                    SelectedScope = TemplateScope.Category;
            }
            else
            {
                TemplateName = $"{FamilyName} - Auto Tag";
                AllowOrientationChange = false;
                SelectedScope = TemplateScope.Type;
            }
        }

        private bool CanExecuteSave(object obj) => !string.IsNullOrWhiteSpace(TemplateName);

        private void ExecuteSave(object obj)
        {
            var template = _editingTemplate ?? new TagTemplate();
            
            template.TemplateName = this.TemplateName;
            template.TargetCategory = this.CategoryName;
            template.TargetFamily = this.SelectedScope >= TemplateScope.Family ? this.FamilyName : null;
            template.TargetType = this.SelectedScope == TemplateScope.Type ? this.TypeName : null;
            template.OffsetX = _localOffset.X;
            template.OffsetY = _localOffset.Y;
            template.OffsetZ = _localOffset.Z;
            template.Orientation = _tagOrientation;
            template.AllowOrientationChange = this.AllowOrientationChange;
            
            if (_host != null)
            {
                template.HostHandX = _host.HandOrientation.X;
                template.HostHandY = _host.HandOrientation.Y;
                template.HostHandZ = _host.HandOrientation.Z;
            }

            var repo = new TemplateStorageRepository();
            repo.SaveTemplate(_doc, template);

            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object obj) => CloseAction?.Invoke();
    }
}
```

### File: AutoTagger/Views/ManageTemplatesView.xaml
```xml
<Window x:Class="Synthetic.Modules.AutoTagger.Views.ManageTemplatesView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Manage AutoTag Templates"
        Height="450" Width="700" MinHeight="300" MinWidth="500"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- DataGrid inherits implicit style -->
        <DataGrid Grid.Row="0" ItemsSource="{Binding Templates}" SelectedItem="{Binding SelectedTemplate}">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Template Name" Binding="{Binding TemplateName}" Width="3*" />
                <DataGridTextColumn Header="Category" Binding="{Binding TargetCategory}" Width="2*" />
                <DataGridTextColumn Header="Family" Binding="{Binding TargetFamily}" Width="2*" />
                <DataGridTextColumn Header="Type" Binding="{Binding TargetType}" Width="2*" />
            </DataGrid.Columns>
        </DataGrid>

        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,15,0,0">
            <Button Content="New Template" Command="{Binding NewTemplateCommand}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Edit" Command="{Binding EditCommand}"
                    Margin="0,0,10,0"/>
            <Button Content="Import JSON" Command="{Binding ImportJsonCommand}"
                    Margin="0,0,10,0"/>
            <Button Content="Export JSON" Command="{Binding ExportJsonCommand}"
                    Margin="0,0,10,0"/>
            <Button Content="Delete" Command="{Binding DeleteCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: AutoTagger/Views/ManageTemplatesView.xaml.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Views
{
    /// <summary>
    /// Interaction logic for ManageTemplatesView.xaml.
    /// </summary>
    public partial class ManageTemplatesView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManageTemplatesView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ManageTemplatesView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: AutoTagger/Views/ResolveConflictsView.xaml
```xml
<Window x:Class="Synthetic.Modules.AutoTagger.Views.ResolveConflictsView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Resolve Tagging Conflicts"
        Height="400" Width="600" MinHeight="300" MinWidth="500"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <StackPanel Grid.Row="0" Margin="0,0,0,15">
            <TextBlock Text="Resolve Template Conflicts" FontSize="16" FontWeight="Bold"
                       Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,5"/>
            <TextBlock Text="Multiple templates match the active elements at the same highest scope priority. Please select which template to apply for each target:"
                       TextWrapping="Wrap" Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"/>
        </StackPanel>

        <!-- DataGrid inherits implicit styles -->
        <DataGrid Grid.Row="1" ItemsSource="{Binding Conflicts}">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Scope Target" Binding="{Binding DisplayName}" IsReadOnly="True" Width="3*" />
                <DataGridTemplateColumn Header="Template to Apply" Width="2*">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <!-- ComboBox inherits implicit style -->
                            <ComboBox ItemsSource="{Binding Candidates}"
                                      SelectedItem="{Binding SelectedCandidate, UpdateSourceTrigger=PropertyChanged}"
                                      DisplayMemberPath="TemplateName" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,15,0,0">
            <Button Content="Proceed AutoTag" Command="{Binding ProceedCommand}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: AutoTagger/Views/ResolveConflictsView.xaml.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Views
{
    /// <summary>
    /// Interaction logic for ResolveConflictsView.xaml
    /// </summary>
    public partial class ResolveConflictsView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolveConflictsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>
        public ResolveConflictsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: AutoTagger/Views/SetTemplateView.xaml
```xml
<Window x:Class="Synthetic.Modules.AutoTagger.Views.SetTemplateView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Set Tag Template"
        SizeToContent="WidthAndHeight" MinHeight="340" MinWidth="400"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <TextBlock Grid.Row="0" Text="Template Name:" FontWeight="Bold"
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,5"/>
        
        <!-- TextBox inherits implicit style -->
        <TextBox Grid.Row="1" Text="{Binding TemplateName, UpdateSourceTrigger=PropertyChanged}"
                 Margin="0,0,0,15"/>
        
        <!-- GroupBox can use simple border/background from theme if needed or standard implicit styling -->
        <GroupBox Grid.Row="2" Header="Template Scope Applicability" Margin="0,0,0,10">
            <StackPanel Margin="10">
                <!-- RadioButton inherits implicit style (or we can style it via theme, currently standard looks good with foreground primary) -->
                <RadioButton Content="{Binding CategoryName}" IsChecked="{Binding IsCategoryScope}"
                             Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,5"/>
                <RadioButton Content="{Binding FamilyName}" IsChecked="{Binding IsFamilyScope}"
                             Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,5"/>
                <RadioButton Content="{Binding TypeName}" IsChecked="{Binding IsTypeScope}"
                             Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,5"/>
            </StackPanel>
        </GroupBox>
        
        <!-- CheckBox inherits implicit style -->
        <CheckBox Grid.Row="3" Content="Allow Tag Orientation to change" IsChecked="{Binding AllowOrientationChange}"
                  Margin="5,5,0,5"/>
        
        <StackPanel Grid.Row="5" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,15,0,0">
            <Button Content="Save Template" Command="{Binding SaveCommand}"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: AutoTagger/Views/SetTemplateView.xaml.cs
```csharp
using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.AutoTagger.Views
{
    /// <summary>
    /// Interaction logic for SetTemplateView.xaml
    /// </summary>
    public partial class SetTemplateView : Window
    {
        /// <summary>
        /// Initializes a new instance of the SetTemplateView class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent window handle.</param>
        public SetTemplateView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);
        }
    }
}
```

### File: ViewManagement/Commands/ConvertDraftingToLegend.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Converts Drafting Views to Legends
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConvertDraftingToLegend : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            string transactionGroupName = "Convert Drafting Views to Legends";

            List<View> draftingViews = (List<View>)new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .OfCategory(BuiltInCategory.OST_Views)
                .OfType<View>()
                .Where(v => v.ViewType == ViewType.DraftingView)
                .ToList();

            IList<View> views = SelectViews(draftingViews, uiapp.MainWindowHandle);

            if (views == null || views.Count == 0)
            {
                return Result.Cancelled;
            }

            IList<View> legends = new List<View>();
            bool warningTripped = false;
            bool isCanceled = false;

            // Initialize Progress Bar Window
            ProgressCoordinator.Initialize("Convert Drafting to Legend", "Starting conversion...", views.Count);

            using (TransactionGroup transGroup = new TransactionGroup(doc, transactionGroupName))
            {
                try
                {
                    transGroup.Start();

                    foreach (View view in views)
                    {
                        if (ProgressCoordinator.IsCancelled())
                        {
                            isCanceled = true;
                            break;
                        }

                        View? legend = LegendsUtil.ConvertFromDrafting(view, ref warningTripped);
                        if (legend == null)
                        {
                            if (ProgressCoordinator.IsCancelled())
                            {
                                isCanceled = true;
                            }
                            else
                            {
                                throw new InvalidOperationException($"Conversion failed for view: {view.Name}");
                            }
                            break;
                        }

                        legends.Add(legend);
                        ProgressCoordinator.UpdateProgress(view.Name);
                    }

                    if (isCanceled)
                    {
                        transGroup.RollBack();
                        ProgressCoordinator.Close();
                        Autodesk.Revit.UI.TaskDialog.Show("Cancelled", "The conversion process was cancelled. All modifications have been rolled back.");
                        return Result.Cancelled;
                    }

                    transGroup.Assimilate();
                }
                catch (Exception ex)
                {
                    if (transGroup.GetStatus() == TransactionStatus.Started)
                    {
                        transGroup.RollBack();
                    }
                    ProgressCoordinator.Close();
                    Autodesk.Revit.UI.TaskDialog.Show("Error", $"An error occurred during conversion:\n{ex.Message}\n\nAll changes have been rolled back.");
                    return Result.Failed;
                }
            }

            ProgressCoordinator.Close();

            string successMsg = $"Successfully converted {legends.Count} Drafting Views to Legends.";
            if (warningTripped)
            {
                successMsg += "\n\nNote: Dimension/Constraint deletion warnings were encountered and suppressed when grouping reference planes.";
            }

            Autodesk.Revit.UI.TaskDialog.Show("Success", successMsg);
            return Result.Succeeded;
        }

        /// <summary>
        /// Displays a checkbox selection dialog allowing the user to choose views for batch conversion.
        /// </summary>
        /// <param name="views">The list of available views for selection.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        /// <returns>A list of user-selected views.</returns>
        internal IList<View> SelectViews(IList<View> views, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<View> selectedViews = new List<View>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Drafting Views";
            viewModel.Instruction = "The selected Drafting Views will be converted to Legends";
            viewModel.IsSingleSelection = false;

            foreach (View view in views)
            {
                itemList.Add(view.Name);
            }
            viewModel.SetItems(itemList, false);

            ListByCheckboxView viewWindow = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dialogResult = viewWindow.ShowDialog();

            if (dialogResult == true)
            {
                List<string> selectedList = viewModel.CheckedItems;
                foreach (string selectedItem in selectedList)
                {
                    View v = views.First(s => s.Name == selectedItem);
                    selectedViews.Add(v);
                }
            }
            return selectedViews;
        }
    }
}
```

### File: ViewManagement/Commands/ConvertLegendToDrafting.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Converts Legend Views to Drafting Views
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConvertLegendToDrafting : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            string transactionName = "Convert Legend Views to Drafting";

            List<View> legendViews = (List<View>)new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .OfCategory(BuiltInCategory.OST_Views)
                .OfType<View>()
                .Where(v => v.ViewType == ViewType.Legend)
                .ToList();

            IList<View> views = SelectViews(legendViews, uiapp.MainWindowHandle);

            IList<View> drafting = new List<View>();

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
            {
                trans.Start(transactionName);
                foreach (View view in views)
                {
                    View? draftingView = LegendsUtil.ConvertToDrafting(view);
                    if (draftingView != null)
                    {
                        drafting.Add(draftingView);
                    }
                }
                trans.Commit();
            }
            
            return Result.Succeeded;
        }

        internal IList<View> SelectViews(IList<View> views, IntPtr mainWindowHandle)
        {
            List<string> itemList = new List<string>();
            List<View> selectedViews = new List<View>();

            ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();
            viewModel.Title = "Select Legend Views";
            viewModel.Instruction = "The selected Legend Views will be converted to Drafting Views";
            viewModel.IsSingleSelection = false;

            foreach (View view in views)
            {
                itemList.Add(view.Name);
            }
            viewModel.SetItems(itemList, false);

            ListByCheckboxView viewWindow = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };

            bool? dialogResult = viewWindow.ShowDialog();

            if (dialogResult == true)
            {
                List<string> selectedList = viewModel.CheckedItems;
                foreach (string selectedItem in selectedList)
                {
                    View v = views.First(s => s.Name == selectedItem);
                    selectedViews.Add(v);
                }
            }
            return selectedViews;
        }
    }
}
```

### File: ViewManagement/Commands/ViewAutoNumberConfig.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

using Synthetic.Core;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Revit Command to configure the View Autonumber command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ViewAutoNumberConfig : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            Config? config = null;
            if (App.Configurations.ContainsProjectConfig(doc))
            {
                config = App.Configurations.GetProjectConfig(doc);
            }
            else
            {
                config = new Config();
                App.Configurations.AddProjectConfig(doc, config);
            }

            ViewAutoNumSettings settings = new ViewAutoNumSettings();

            List<string> itemList = new List<string>();
            Dictionary<string, FamilySymbol> choices = new Dictionary<string, FamilySymbol>();
            IList<Element> familyList = FamilySymbolUtil.GetFamiliesOfCategory(doc, BuiltInCategory.OST_GenericAnnotation);

            if (familyList != null)
            {
                foreach (FamilySymbol familySymbol in familyList)
                {
                    string itemName = familySymbol.Family.Name + " | " + familySymbol.Name;
                    choices.Add(itemName, familySymbol);
                    itemList.Add(itemName);
                }

                var r = SelectFamily(itemList, uiapp.MainWindowHandle);
                bool dResult = r.result;
                string? selection = r.selection;

                if (dResult && selection != null)
                {
                    FamilySymbol familySymbol = choices[selection];

                    if (familySymbol != null)
                    {
                        List<string> itemList2 = new List<string>();

                        ParameterSet xGridParameters = familySymbol.Parameters;
                        foreach (Parameter parameter in xGridParameters)
                        {
                            itemList2.Add(parameter.Definition.Name);
                        }

                        r = SelectGrid(itemList2, true, uiapp.MainWindowHandle);
                        bool dResult2 = r.result;
                        string? viewAutoNumXGridName = r.selection;

                        if (dResult2 && viewAutoNumXGridName != null)
                        {
                            List<string> itemList3 = new List<string>();

                            ParameterSet yGridParameters = familySymbol.Parameters;
                            foreach (Parameter parameter in yGridParameters)
                            {
                                itemList3.Add(parameter.Definition.Name);
                            }

                            r = SelectGrid(itemList2, false, uiapp.MainWindowHandle);
                            bool dResult3 = r.result;
                            string? viewAutoNumYGridName = r.selection;

                            if (dResult3 && viewAutoNumYGridName != null)
                            {
                                settings.ViewAutoNumFamily = familySymbol.Family.Name;
                                settings.ViewAutoNumFamilyType = familySymbol.Name;
                                settings.ViewAutoNumXGridName = viewAutoNumXGridName;
                                settings.ViewAutoNumYGridName = viewAutoNumYGridName;

                                config?.SetSettings(ViewAutoNumSettings.Name, settings);
                            }
                        }
                    }
                }
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Opens dialog to select the family to use for the origin and parameters for the view renumbering
        /// </summary>
        internal (bool result, string? selection) SelectFamily(List<string> itemList, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Autonumber View Configuration";
            viewModel.Instruction = "Select a Generic Annotation Family";
            viewModel.ItemLabel = "Family & Type";
            viewModel.Items = itemList;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();
            string? selection = viewModel.SelectedItem;

            return (dResult == true, selection);
        }

        /// <summary>
        /// Opens dialog box to select the Grid Parameters with options for X Grid or Y Grid
        /// </summary>
        internal (bool result, string? selection) SelectGrid(List<string> itemList, bool IsXGrid, IntPtr mainWindowHandle)
        {
            DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();
            viewModel.Title = "Autonumber View Configuration";
            viewModel.Instruction = "Select a parameter that determines the " + ((IsXGrid) ? "X" : "Y") + " Grid Spacing";
            viewModel.ItemLabel = "Parameter";
            viewModel.Items = itemList;
            viewModel.IsSorted = true;

            DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };
            bool? dResult = dialog.ShowDialog();
            string? selection = viewModel.SelectedItem;

            return (dResult == true, selection);
        }
    }
}
```

### File: ViewManagement/Commands/ViewsAutoNumber.cs
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Commands
{
    /// <summary>
    /// Revit Command to automatically number views on the active sheet based on a family that determines the origin point and the grid spacing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ViewsAutoNumber : IExternalCommand
    {
        /// <summary>
        /// Execute a Revit Command
        /// </summary>
        /// <param name="commandData">commandData</param>
        /// <param name="message">message</param>
        /// <param name="elements">Currently selected elements</param>
        /// <returns>A Autodesk.Revit.UI.Result</returns>
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            string transactionName = "Autonumber views on sheet";

            // Access View Renumber Settings
            ViewAutoNumSettings settings = SettingsManager.Get<ViewAutoNumSettings>(doc);

            if (
                settings == null ||
                !settings.IsValid(doc)
                )
            {
                Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic View Autonumber Settings");
                taskDialog.MainInstruction = "View Autonumber Settins not set";
                taskDialog.MainContent = "Run the Set View Autonumber Settings command";
                taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
                taskDialog.DefaultButton = TaskDialogResult.Close;
                TaskDialogResult tResult = taskDialog.Show();
                taskDialog.Dispose();
            }
            else
            {
                ViewAutoNumModel viewAutoNum = new ViewAutoNumModel(
                    doc,
                    settings.ViewAutoNumFamily,
                    settings.ViewAutoNumFamilyType,
                    settings.ViewAutoNumXGridName,
                    settings.ViewAutoNumYGridName
                    );

                

                if (viewAutoNum.Family != null)
                {
                    ViewSheet sheet = (ViewSheet)doc.ActiveView;
                    using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(doc))
                    {
                        trans.Start(transactionName);
                        viewAutoNum.AutoNumberOnSheet(sheet);
                        trans.Commit();
                    }
                }
                else
                {
                    Autodesk.Revit.UI.TaskDialog taskDialog = new Autodesk.Revit.UI.TaskDialog("Synthetic View Autonumber Settings");
                    taskDialog.MainInstruction = "View Autonumber Family cannot be found";
                    taskDialog.MainContent = "Please load the family and place on the sheet or set a different family.";
                    taskDialog.CommonButtons = TaskDialogCommonButtons.Close;
                    taskDialog.DefaultButton = TaskDialogResult.Close;
                    TaskDialogResult tResult = taskDialog.Show();
                    taskDialog.Dispose();
                }
            }

                return Result.Succeeded;
        }
    }
}
```

### File: ViewManagement/Models/ViewAutoNumModel.cs
```csharp
using Autodesk.Revit.DB;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
using Synthetic.Shared.UI;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.ViewManagement.Models
{
    /// <summary>
    /// Class used to automatically renumber views based on a grid.  Uses a family on the sheet to determine the origin point and the spacing of the grid.
    /// </summary>
    public class ViewAutoNumModel
    {
        #region Properties

        /// <summary>
        /// A Revit Document
        /// </summary>
        public Document Document { get; set; }

        /// <summary>
        /// A Revit Family used as the Origin Point
        /// </summary>
        public FamilySymbol? Family { get; set; }

        /// <summary>
        /// Name of the parameter in the Revit Family that specifies the X grid spacing
        /// </summary>
        public string XSpacingParameter { get; set; }

        /// <summary>
        /// Name of the parameter in the Revit Family that specifies the X grid spacing
        /// </summary>
        public string YSpacingParameter { get; set; }

        /// <summary>
        /// The X grid spacing used to number views on relative the origin point.
        /// </summary>
        public double XSpacing { get; set; }

        /// <summary>
        /// The Y grid spacing used to number views on relative the origin point.
        /// </summary>
        public double YSpacing { get; set; }
        
        #endregion


        #region Constructors
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="doc">A Revit Document</param>
        /// <param name="familyName">Name of the Family used as the origin point</param>
        /// <param name="symbolName">Name of the Family Type used as the origin</param>
        /// <param name="xSpacingParameter">Name of the Parameter in the Family that specifies the X grid spacing</param>
        /// <param name="ySpacingParameter">Name of the Parameter in the Family that specifies the Y grid spacing</param>
        public ViewAutoNumModel(Document doc, string familyName, string symbolName, string xSpacingParameter, string ySpacingParameter)
        {
            this.Document = doc;
            this.Family = FamilySymbolUtil.GetByName( this.Document, familyName, symbolName );
            this.XSpacingParameter = xSpacingParameter;
            this.YSpacingParameter = ySpacingParameter;

            if (this.Family != null)
            {
                //this.Origin = FamilySymbolUtil.GetOrigin(this.Family);
                this.XSpacing = this.Family.LookupParameter(this.XSpacingParameter).AsDouble();
                this.YSpacing = this.Family.LookupParameter(this.YSpacingParameter).AsDouble();
            }
            else 
            {
                //this.Origin = XYZ.Zero;
                this.XSpacing = 0.0833333333;
                this.YSpacing = 0.0833333333;
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Renumbers the views on the given sheet based on the views location in a grid
        /// </summary>
        /// <param name="sheet">Sheet to renumber views on</param>
        /// <returns>Viewports on the sheet.</returns>
        public List<Viewport>? AutoNumberOnSheet (ViewSheet sheet)
        {
            if (this.Family == null) return null;
            List<Viewport>? viewports = null;
            FamilyInstance instance = (FamilyInstance)FamilySymbolUtil.GetInstancesInView(this.Document, this.Family, sheet).FirstElement();

            if (instance != null)
            {
                LocationPoint location = (LocationPoint)instance.Location;
                XYZ originPoint = location.Point;

                List<ElementId> viewportIds = (List<ElementId>)sheet.GetAllViewports();

                Synthetic.Shared.UI.ProgressCoordinator.Initialize("Auto-Numbering Views", "Renumbering views on sheet...", viewportIds.Count);

                try
                {
                    viewports = _tempRenumberViewports(viewportIds, this.Document);
                    if (!Synthetic.Shared.UI.ProgressCoordinator.IsCancelled())
                    {
                        viewports = _renumberViewports(viewports, this.XSpacing, this.YSpacing, originPoint.X, originPoint.Y);
                    }
                }
                finally
                {
                    Synthetic.Shared.UI.ProgressCoordinator.Close();
                }
            }
            return viewports;
        }
        #endregion

        #region Internal Functions

        /// <summary>
        /// Given a list of viewport element IDs, the function will get the viewport from the document and give each viewport a temporary sheet number.  Function will ignore legends.
        /// </summary>
        /// <param name="viewPortIds">Revit ElementId of the viewports.</param>
        /// <param name="doc">The Revit Document the viewports are in.</param>
        /// <returns name="viewports">Returns the Revit viewports.</returns>
        internal static List<Viewport> _tempRenumberViewports(List<ElementId> viewPortIds, Document doc)
        {
            List<Viewport> viewPorts = new List<Viewport>();
            int i = 1;

            foreach (ElementId id in viewPortIds)
            {
                if (Synthetic.Shared.UI.ProgressCoordinator.IsCancelled())
                {
                    break;
                }

                Viewport vp = (Viewport)doc.GetElement(id);
                View v = (View)doc.GetElement(vp.ViewId);

                if (
                    v.ViewType == ViewType.FloorPlan
                    || v.ViewType == ViewType.CeilingPlan
                    || v.ViewType == ViewType.Elevation
                    || v.ViewType == ViewType.ThreeD
                    || v.ViewType == ViewType.DraftingView
                    || v.ViewType == ViewType.AreaPlan
                    || v.ViewType == ViewType.Section
                    || v.ViewType == ViewType.Detail
                    || v.ViewType == ViewType.Rendering
                    )
                {
                    viewPorts.Add(vp);

                    Parameter param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (param != null) param.Set("!!" + i);
                    i++;
                }
            }
            return viewPorts;
        }

        /// <summary>
        /// Given a list of viewports, grid spacing and an origin point, function will renumber the viewports based on grid location.
        /// </summary>
        /// <param name="viewports">Revit ViewPorts</param>
        /// <param name="gridX">Grid spacing in the X direction</param>
        /// <param name="gridY">Grid spacing in the Y direction</param>
        /// <param name="originX">X coordinate of the grid origin</param>
        /// <param name="originY">Y coordinate of the grid origin</param>
        /// <returns name="viewports">The renumbered Revit ViewPorts</returns>
        internal static List<Viewport> _renumberViewports(List<Viewport> viewports, double gridX, double gridY, double originX, double originY)
        {
            foreach (Viewport vp in viewports)
            {
                if (Synthetic.Shared.UI.ProgressCoordinator.IsCancelled())
                {
                    break;
                }

                Outline labelOutline = vp.GetLabelOutline();
                XYZ minPt = labelOutline.MinimumPoint;

                string viewNumber = _calculateViewNumber(minPt.X, minPt.Y, gridX, gridY, originX, originY);

                Parameter param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                if (param != null) param.Set(viewNumber);

                View v = (View)vp.Document.GetElement(vp.ViewId);
                Synthetic.Shared.UI.ProgressCoordinator.UpdateProgress(v.Name ?? $"Detail: {viewNumber}");
            }

            return viewports;
        }

        internal static string _calculateViewNumber(double viewX, double viewY, double gridX, double gridY, double originX, double originY)
        {
            // const double viewportOffset = 0.0114;
            const double viewportOffset = 0.0;

            double x = Math.Floor(((viewX - originX) + viewportOffset) / gridX + 1);
            double y = Math.Floor(((viewY - originY) + viewportOffset) / gridY + 1);

            string stringX = x.ToString();
            string stringY = _IntToLetters((int)Math.Abs(y));

            if (y < 0)
            {
                stringY = "-" + stringY;
            }
            else if (y == 0)
            {
                stringY = "!" + stringY;
            }

            return stringY + stringX;
        }

        /// <summary>
        /// Given an Int, returns an equivalent letter from the alphabet.
        /// </summary>
        /// <param name="value">An integer</param>
        /// <returns>A letter from the alphabet</returns>
        internal static string _IntToLetters(int value)
        {
            string result = string.Empty;
            while (--value >= 0)
            {
                result = (char)('A' + value % 26) + result;
                value /= 26;
            }
            return result;
        }

        #endregion
    }
}
```

### File: ViewManagement/Utilities/LegendsUtil.cs
```csharp
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

```

### File: ViewManagement/Utilities/ViewUtil.cs
```csharp
using Autodesk.Revit.DB;
using Synthetic.Modules.ViewManagement.Commands;
using Synthetic.Modules.ViewManagement.Models;
using Synthetic.Modules.ViewManagement.Utilities;

using System;
using System.Collections.Generic;
using System.Text;

using revitDB = Autodesk.Revit.DB;
using revitView = Autodesk.Revit.DB.View;
using revitView3D = Autodesk.Revit.DB.View3D;
using RevitDoc = Autodesk.Revit.DB.Document;
using revitElem = Autodesk.Revit.DB.Element;
using revitElemId = Autodesk.Revit.DB.ElementId;
using revitViewOrientation = Autodesk.Revit.DB.ViewOrientation3D;
using revitXYZ = Autodesk.Revit.DB.XYZ;
using revitBBxyz = Autodesk.Revit.DB.BoundingBoxXYZ;
using revitBBuv = Autodesk.Revit.DB.BoundingBoxUV;
using revitParam = Autodesk.Revit.DB.Parameter;
using revitSheet = Autodesk.Revit.DB.ViewSheet;
using revitViewport = Autodesk.Revit.DB.Viewport;
using revitCollector = Autodesk.Revit.DB.FilteredElementCollector;
using revitElementFilter = Autodesk.Revit.DB.ElementFilter;
using revitFamilySymbol = Autodesk.Revit.DB.FamilySymbol;
using revitOutline = Autodesk.Revit.DB.Outline;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.ViewManagement.Utilities
{
    /// <summary>
    /// Utility methods for managing and auto-numbering Revit Views.
    /// </summary>
    public class ViewUtil
    {
        /// <summary>
        /// Renumbers the views on the Active Sheet
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyType">Revit Family Symbol that represents the origin element</param>
        /// <param name="xGridName">Name of the parameter that represents the X grid spacing</param>
        /// <param name="yGridName">Name of the parameter that represents the Y grid spacing</param>
        /// <returns name="Viewports">Revit viewport objects on the sheet.</returns>
        public static List<revitViewport>? AutoNumber(RevitDoc doc, revitFamilySymbol familyType, string xGridName, string yGridName)
        {
            revitSheet rSheet = (revitSheet)doc.ActiveView;

            return _renumberViewsOnSheet(familyType, xGridName, yGridName, rSheet, doc);
        }

        #region Utility Functions

        /// <summary>
        /// Sets a workset to visible in a specific view.
        /// </summary>
        /// <param name="view">The Revit View</param>
        /// <param name="workset">The Workset to show</param>
        /// <returns>True if successful, False if skipped due to template</returns>
        public static bool SetWorksetVisibilityInView(revitView view, revitDB.Workset workset)
        {
            if (view.ViewTemplateId != revitElemId.InvalidElementId)
            {
                return false;
            }

            view.SetWorksetVisibility(workset.Id, revitDB.WorksetVisibility.Visible);
            return true;
        }

        internal static List<revitViewport>? _renumberViewsOnSheet(revitFamilySymbol familyType, string xGridName, string yGridName, revitSheet rSheet, RevitDoc document)
        {
            string transactionName = "Renumber views on sheet";

            //  Initialize variables
            revitFamilySymbol rFamilySymbol = (revitFamilySymbol)familyType;

            //  Get all viewport ID's on the sheet.
            List<revitElemId> viewportIds = (List<revitElemId>)rSheet.GetAllViewports();
            List<revitViewport>? viewports = null;

            //  Get the family Instances in view
            revitElemId symbolId = familyType.Id;

            revitCollector collector = new revitCollector(document, rSheet.Id);
            revitElementFilter filterInstance = new revitDB.FamilyInstanceFilter(document, symbolId);

            collector.OfClass(typeof(revitDB.FamilyInstance)).WherePasses(filterInstance);

            revitDB.FamilyInstance originFamily = (revitDB.FamilyInstance)collector.FirstElement();

            //  If family instance is found in the view
            //  Then renumber views.
            if (originFamily != null)
            {
                revitDB.LocationPoint location = (revitDB.LocationPoint)originFamily.Location;
                revitXYZ originPoint = location.Point;

                double gridX = rFamilySymbol.LookupParameter(xGridName).AsDouble();
                double gridY = rFamilySymbol.LookupParameter(yGridName).AsDouble();

                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
                {
                    trans.Start(transactionName);
                    viewports = _tempRenumberViewports(viewportIds, document);
                    viewports = _renumberViewports(viewports, gridX, gridY, originPoint.X, originPoint.Y);
                    trans.Commit();
                }
            }

            return viewports;
        }

        /// <summary>
        /// Given a list of viewport element IDs, the function will get the viewport from the document and give each viewport a temporary sheet number.  Function will ignore legends.
        /// </summary>
        /// <param name="viewPortIds">Revit ElementId of the viewports.</param>
        /// <param name="doc">The Revit Document the viewports are in.</param>
        /// <returns name="viewports">Returns the Revit viewports.</returns>
        internal static List<revitViewport> _tempRenumberViewports(List<revitElemId> viewPortIds, RevitDoc doc)
        {
            List<revitViewport> viewPorts = new List<revitViewport>();
            int i = 1;

            foreach (revitElemId id in viewPortIds)
            {
                revitViewport vp = (revitViewport)doc.GetElement(id);
                revitView v = (revitView)doc.GetElement(vp.ViewId);

                if (
                    v.ViewType == revitDB.ViewType.FloorPlan
                    || v.ViewType == revitDB.ViewType.CeilingPlan
                    || v.ViewType == revitDB.ViewType.Elevation
                    || v.ViewType == revitDB.ViewType.ThreeD
                    || v.ViewType == revitDB.ViewType.DraftingView
                    || v.ViewType == revitDB.ViewType.AreaPlan
                    || v.ViewType == revitDB.ViewType.Section
                    || v.ViewType == revitDB.ViewType.Detail
                    || v.ViewType == revitDB.ViewType.Rendering
                    )
                {
                    viewPorts.Add(vp);

                    revitDB.Parameter param = vp.get_Parameter(revitDB.BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (param != null) param.Set("!!" + i);
                    i++;
                }
            }
            return viewPorts;
        }

        /// <summary>
        /// Given a list of viewports, grid spacing and an origin point, function will renumber the viewports based on grid location.
        /// </summary>
        /// <param name="viewports">Revit ViewPorts</param>
        /// <param name="gridX">Grid spacing in the X direction</param>
        /// <param name="gridY">Grid spacing in the Y direction</param>
        /// <param name="originX">X coordinate of the grid origin</param>
        /// <param name="originY">Y coordinate of the grid origin</param>
        /// <returns name="viewports">The renumbered Revit ViewPorts</returns>
        internal static List<revitViewport> _renumberViewports(List<revitViewport> viewports, double gridX, double gridY, double originX, double originY)
        {
            //const double viewportOffset = 0.0114;

            //int i = 1;

            foreach (revitViewport vp in viewports)
            {
                revitOutline labelOutline = vp.GetLabelOutline();
                revitXYZ minPt = labelOutline.MinimumPoint;

                string viewNumber = _calculateViewNumber(minPt.X, minPt.Y, gridX, gridY, originX, originY);

                revitDB.Parameter param = vp.get_Parameter(revitDB.BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                if (param != null) param.Set(viewNumber);
            }

            return viewports;
        }

        internal static string _calculateViewNumber(double viewX, double viewY, double gridX, double gridY, double originX, double originY)
        {
            // const double viewportOffset = 0.0114;
            const double viewportOffset = 0.0;

            double x = Math.Floor(((viewX - originX) + viewportOffset) / gridX + 1);
            double y = Math.Floor(((viewY - originY) + viewportOffset) / gridY + 1);

            string stringX = x.ToString();
            string stringY = _IntToLetters((int)Math.Abs(y));

            if (y < 0)
            {
                stringY = "-" + stringY;
            }
            else if (y == 0)
            {
                stringY = "!" + stringY;
            }

            return stringY + stringX;
        }

        internal static string _IntToLetters(int value)
        {
            string result = string.Empty;
            while (--value >= 0)
            {
                result = (char)('A' + value % 26) + result;
                value /= 26;
            }
            return result;
        }

        #endregion
    }
}
```

### File: DetailItemFactory/Commands/CmdDetailItemFactory.cs
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using View = Autodesk.Revit.DB.View;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

using Synthetic.Shared.UI;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.DetailItemFactory.Commands
{
    /// <summary>
    /// Production external command to batch-process selected 3D model elements
    /// into 2D Detail Item family documents via temporary DWG projection and tracing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdDetailItemFactory : IExternalCommand
    {
        private static DetailItemFactoryEventHandler? _eventHandler;
        private static ExternalEvent? _externalEvent;

        /// <summary>
        /// Executes the Detail Item Factory command, showing the WPF UI options and running the batch conversion.
        /// </summary>
        /// <param name="commandData">Revit external command data.</param>
        /// <param name="message">A message returning errors if any.</param>
        /// <param name="elements">Revit elements set.</param>
        /// <returns>Result code of the execution.</returns>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Verify active view is 2D projection view
            View activeView = doc.ActiveView;
            if (activeView is View3D)
            {
                TaskDialog.Show("Detail Item Factory", "Please run this command from a 2D view (Plan, Section, or Elevation).");
                return Result.Failed;
            }

            // Retrieve selection
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show("Detail Item Factory", "Please select at least one 3D model element to convert.");
                return Result.Failed;
            }

            // Show UI to gather user preferences
            DetailItemFactoryViewModel vm = new DetailItemFactoryViewModel(doc, activeView, selectedIds, uiapp.MainWindowHandle);
            DetailItemFactoryView view = new DetailItemFactoryView(uiapp.MainWindowHandle) { DataContext = vm };

            if (view.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            // Setup asynchronous modeless progress dialog and event handler
            Synthetic.Shared.UI.ProgressCoordinator.Initialize("Detail Item Factory Progress", "Starting batch processing...", vm.Elements.Count);

            _eventHandler = new DetailItemFactoryEventHandler
            {
                ConfiguredElements = new List<SelectedElementItemViewModel>(vm.Elements),
                OutputFolder = vm.OutputPath,
                TargetSubcategory = vm.SelectedSubcategory,
                OverwriteExisting = vm.OverwriteExisting
            };

            // Register and raise the external event (persisted in static reference to prevent garbage collection)
            _externalEvent = ExternalEvent.Create(_eventHandler);
            if (_externalEvent == null)
            {
                Synthetic.Shared.UI.ProgressCoordinator.Close();
                TaskDialog.Show("Detail Item Factory", "Failed to create the external event handler.");
                return Result.Failed;
            }

            // Raise the event to start processing on the main thread
            _externalEvent.Raise();

            return Result.Succeeded;
        }
    }
}
```

### File: DetailItemFactory/Handlers/DetailItemFactoryEventHandler.cs
```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using View = Autodesk.Revit.DB.View;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

using Synthetic.Infrastructure.Diagnostics;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.DetailItemFactory.Models;

namespace Synthetic.Modules.DetailItemFactory.Handlers
{
    /// <summary>
    /// External event handler to process selected elements into 2D Detail Items asynchronously on the Revit main thread.
    /// </summary>
    public class DetailItemFactoryEventHandler : IExternalEventHandler
    {
        /// <summary>
        /// Gets or sets the collection of configured elements to process.
        /// </summary>
        public List<SelectedElementItemViewModel> ConfiguredElements { get; set; } = new List<SelectedElementItemViewModel>();

        /// <summary>
        /// Gets or sets the target output folder path.
        /// </summary>
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target subcategory name.
        /// </summary>
        public string TargetSubcategory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether existing family files should be overwritten on disk.
        /// </summary>
        public bool OverwriteExisting { get; set; }

        /// <summary>
        /// Executes the batch processing loop asynchronously on the Revit main thread.
        /// </summary>
        /// <param name="app">The Revit UIApplication context.</param>
        public void Execute(UIApplication app)
        {
            if (app == null) return;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            if (string.IsNullOrWhiteSpace(TargetSubcategory))
            {
                TargetSubcategory = "Detail Items";
            }

            // Resolve family template path dynamically
            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? assemblyDir = Path.GetDirectoryName(assemblyPath);
            string templatePath = Path.Combine(assemblyDir ?? string.Empty, "Assets", "Templates", "Detail Item.rft");

            if (!File.Exists(templatePath))
            {
                ProgressCoordinator.Close();
                TaskDialog.Show("Detail Item Factory", $"Template file not found at:\n{templatePath}");
                return;
            }

            // Results collection for the dashboard UI
            List<DetailItemResultItem> resultItems = new List<DetailItemResultItem>();

            // Initialize progress UI
            int totalCount = ConfiguredElements.Count;
            ProgressCoordinator.UpdateStatus("Starting batch processing...");

            // Extract project-level OST_DetailComponents subcategory settings
            Category detailComponentsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            Category? projectSubcategory = null;
            Autodesk.Revit.DB.Color projectLineColor = new Autodesk.Revit.DB.Color(0, 0, 0);
            int projectLineWeight = 1;
            ElementId projectLinePatternId = ElementId.InvalidElementId;
            string? projectLinePatternName = null;

            if (detailComponentsCategory != null && detailComponentsCategory.SubCategories.Contains(TargetSubcategory))
            {
                projectSubcategory = detailComponentsCategory.SubCategories.get_Item(TargetSubcategory);
                if (projectSubcategory != null)
                {
                    projectLineColor = projectSubcategory.LineColor;
                    int? weight = projectSubcategory.GetLineWeight(GraphicsStyleType.Projection);
                    if (weight.HasValue)
                    {
                        projectLineWeight = weight.Value;
                    }
                    projectLinePatternId = projectSubcategory.GetLinePatternId(GraphicsStyleType.Projection);
                    if (projectLinePatternId != ElementId.InvalidElementId)
                    {
                        if (doc.GetElement(projectLinePatternId) is LinePatternElement patternElem)
                        {
                            projectLinePatternName = patternElem.Name;
                        }
                    }
                }
            }

            // DWG Export Setup
            DWGExportOptions dwgOptions = new DWGExportOptions
            {
                HideScopeBox = true,
                HideReferencePlane = true,
                HideUnreferenceViewTags = true
            };

            // Loop selected elements
            for (int i = 0; i < totalCount; i++)
            {
                // Check for user cancellation
                if (ProgressCoordinator.IsCancelled())
                {
                    for (int j = i; j < totalCount; j++)
                    {
                        SelectedElementItemViewModel cancelledItem = ConfiguredElements[j];
                        ElementId cancelledId = cancelledItem.ElementId;
                        Element cancelledElement = doc.GetElement(cancelledId);
                        string name = cancelledItem.DisplayName ?? cancelledElement?.Name ?? $"ID: {cancelledId}";
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = name,
                            ElementId = cancelledId.ToString(),
                            Status = "Skipped",
                            Message = "Cancelled by user."
                        });
                    }
                    break;
                }

                SelectedElementItemViewModel configItem = ConfiguredElements[i];
                ElementId selectedId = configItem.ElementId;
                string viewOrientation = configItem.SelectedOrientation;
                Element element = doc.GetElement(selectedId);
                if (element == null) continue;

                int skippedCurvesCount = 0;

                string cleanFamilyName = "Unknown";
                string cleanTypeName = "Unknown";

                try
                {
                    // Update progress window
                    string statusMsg = $"Processing element {i + 1} of {totalCount}: {element.Name ?? "ID: " + element.Id}...";
                    ProgressCoordinator.UpdateStatus(statusMsg);

                    // Filter out annotations & non-model categories
                    if (element.Category == null || element.Category.CategoryType != CategoryType.Model)
                    {
                        string elemName = element.Name ?? $"ID: {element.Id}";
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = elemName,
                            ElementId = element.Id.ToString(),
                            Status = "Skipped",
                            Message = "Annotation or non-Model element skipped."
                        });
                        continue;
                    }

                    // Resolve names
                    string familyName = "";
                    string typeName = "";

                    if (element is FamilyInstance fi)
                    {
                        familyName = fi.Symbol.Family.Name;
                        typeName = fi.Symbol.Name;
                    }
                    else
                    {
                        ElementId typeId = element.GetTypeId();
                        if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType et)
                        {
                            familyName = et.FamilyName;
                            typeName = et.Name;
                        }
                        else
                        {
                            familyName = element.Category?.Name ?? "UnknownCategory";
                            typeName = element.Name ?? "UnknownType";
                        }
                    }

                    cleanFamilyName = MakeValidFileName(familyName);
                    cleanTypeName = MakeValidFileName(typeName);
                    string fileName = $"2D - {cleanFamilyName} - {cleanTypeName} - {viewOrientation}.rfa";
                    string targetPath = Path.Combine(OutputFolder, fileName);

                    // Overwrite check
                    if (File.Exists(targetPath) && !OverwriteExisting)
                    {
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                            ElementId = element.Id.ToString(),
                            Status = "Skipped",
                            Message = "Family file already exists and overwrite flag is disabled."
                        });
                        continue;
                    }

                    // Calculate Desired Origin (Project Side)
                    XYZ viewRelativeOffset = XYZ.Zero;
                    BoundingBoxXYZ bbox = element.get_BoundingBox(activeView);
                    if (bbox != null)
                    {
                        XYZ center = (bbox.Max + bbox.Min) / 2.0;
                        XYZ desiredOrigin = bbox.Min;
                        if (element.Location is LocationPoint locPoint)
                        {
                            desiredOrigin = locPoint.Point;
                        }

                        XYZ projectOffsetVector = desiredOrigin - center;
                        double offsetX = projectOffsetVector.DotProduct(activeView.RightDirection);
                        double offsetY = projectOffsetVector.DotProduct(activeView.UpDirection);
                        viewRelativeOffset = new XYZ(offsetX, offsetY, 0);
                    }

                    string dwgName = "temp_export_" + Path.GetFileNameWithoutExtension(fileName);

                    // 1. Temporary hide/isolate for export
                    EventHandler<Autodesk.Revit.UI.Events.DialogBoxShowingEventArgs> dialogHandler = 
                        (sender, args) =>
                        {
                            if (args is Autodesk.Revit.UI.Events.TaskDialogShowingEventArgs taskArgs)
                            {
                                if (taskArgs.DialogId == "TaskDialog_Really_Print_Or_Export_Temp_View_Modes")
                                {
                                    taskArgs.OverrideResult(1002);
                                }
                            }
                        };

                    app.DialogBoxShowing += dialogHandler;

                    try
                    {
                        using (Transaction t = new Transaction(doc, "Temporary Isolate for Export"))
                        {
                            t.Start();
                            activeView.CropBoxVisible = false;

                            List<ElementId> idsToIsolate = new List<ElementId> { element.Id };
                            if (element is FamilyInstance familyInstance)
                            {
                                ICollection<ElementId> subComponentIds = familyInstance.GetSubComponentIds();
                                if (subComponentIds != null && subComponentIds.Count > 0)
                                {
                                    idsToIsolate.AddRange(subComponentIds);
                                }
                            }

                            activeView.IsolateElementsTemporary(idsToIsolate);
                            doc.Regenerate();

                            doc.Export(OutputFolder, dwgName, new List<ElementId> { activeView.Id }, dwgOptions);

                            t.RollBack();
                        }
                    }
                    finally
                    {
                        app.DialogBoxShowing -= dialogHandler;
                    }

                    string[] matchingFiles = Directory.GetFiles(OutputFolder, dwgName + "*.dwg");
                    if (matchingFiles.Length == 0)
                    {
                        throw new Exception("DWG Export failed to generate a file on disk.");
                    }
                    string dwgPath = matchingFiles[0];

                    // 2. Create Family Document
                    Document familyDoc = doc.Application.NewFamilyDocument(templatePath);
                    if (familyDoc == null)
                    {
                        throw new Exception("Failed to generate a new family document from the template.");
                    }

                    // 3. Subcategory Serialization
                    GraphicsStyle? subcategoryStyle = null;

                    using (Transaction t = new Transaction(familyDoc, "Serialize Subcategory"))
                    {
                        t.Start();

                        Category familyDetailComponentsCategory = familyDoc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
                        Category? familySubcategory = null;

                        if (familyDetailComponentsCategory.SubCategories.Contains(TargetSubcategory))
                        {
                            familySubcategory = familyDetailComponentsCategory.SubCategories.get_Item(TargetSubcategory);
                        }
                        else
                        {
                            familySubcategory = familyDoc.Settings.Categories.NewSubcategory(familyDetailComponentsCategory, TargetSubcategory);
                        }

                        if (familySubcategory != null)
                        {
                            familySubcategory.LineColor = projectLineColor;
                            familySubcategory.SetLineWeight(projectLineWeight, GraphicsStyleType.Projection);

                            if (projectLinePatternName != null)
                            {
                                LinePatternElement? familyPattern = new FilteredElementCollector(familyDoc)
                                    .OfClass(typeof(LinePatternElement))
                                    .Cast<LinePatternElement>()
                                    .FirstOrDefault(p => p.Name == projectLinePatternName);

                                if (familyPattern == null)
                                {
                                    if (doc.GetElement(projectLinePatternId) is LinePatternElement patternElem)
                                    {
                                        try
                                        {
                                            familyPattern = LinePatternElement.Create(familyDoc, patternElem.GetLinePattern());
                                        }
                                        catch { }
                                    }
                                }

                                if (familyPattern != null)
                                {
                                    familySubcategory.SetLinePatternId(familyPattern.Id, GraphicsStyleType.Projection);
                                }
                            }

                            subcategoryStyle = familySubcategory.GetGraphicsStyle(GraphicsStyleType.Projection);
                        }

                        t.Commit();
                    }

                    // 4. Import & Trace DWG
                    using (Transaction t = new Transaction(familyDoc, "Import and Trace DWG"))
                    {
                        t.Start();

                        View? planView = familyDoc.ActiveView;
                        if (planView == null)
                        {
                            planView = new FilteredElementCollector(familyDoc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.EngineeringPlan);
                        }

                        if (planView == null)
                        {
                            throw new Exception("Could not find a valid Floor Plan view inside the Family template.");
                        }

                        DWGImportOptions importOptions = new DWGImportOptions { ThisViewOnly = true };
                        ElementId importedElementId;
                        bool importSuccess = familyDoc.Import(dwgPath, importOptions, planView, out importedElementId);

                        if (!importSuccess || importedElementId == ElementId.InvalidElementId)
                        {
                            throw new Exception("Failed to import the temporary DWG payload into the Family Document.");
                        }

                        ImportInstance? importInstance = familyDoc.GetElement(importedElementId) as ImportInstance;
                        if (importInstance != null)
                        {
                            // Calibrate import origin to align with original project placement
                            BoundingBoxXYZ importBBox = importInstance.get_BoundingBox(familyDoc.ActiveView);
                            if (importBBox != null)
                            {
                                XYZ importCenter = (importBBox.Max + importBBox.Min) / 2.0;
                                XYZ currentDesiredOrigin = importCenter + viewRelativeOffset;
                                XYZ moveVector = XYZ.Zero - currentDesiredOrigin;
                                moveVector = new XYZ(moveVector.X, moveVector.Y, 0);

                                importInstance.Pinned = false;
                                ElementTransformUtils.MoveElement(familyDoc, importInstance.Id, moveVector);
                            }

                            Options geomOptions = new Options { ComputeReferences = true };
                            GeometryElement geomElement = importInstance.get_Geometry(geomOptions);

                            double shortCurveTolerance = familyDoc.Application.ShortCurveTolerance;

                            ProcessGeometry(geomElement, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);

                            importInstance.Pinned = false;
                            familyDoc.Delete(importInstance.Id);
                        }

                        t.Commit();
                    }

                    // 5. Save and Load
                    SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                    familyDoc.SaveAs(targetPath, saveOptions);

                    Family? loadedFamily = null;
                    using (Transaction projectTx = new Transaction(doc, "Load Family: " + cleanFamilyName))
                    {
                        projectTx.Start();
                        doc.LoadFamily(targetPath, new SimpleFamilyLoadOptions(), out loadedFamily);
                        projectTx.Commit();
                    }

                    familyDoc.Close(false);

                    if (skippedCurvesCount == 0)
                    {
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                            ElementId = element.Id.ToString(),
                            Status = "Success",
                            Message = "Successfully generated and loaded family."
                        });
                    }
                    else
                    {
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                            ElementId = element.Id.ToString(),
                            Status = "Partial Success",
                            Message = $"Successfully generated, but skipped {skippedCurvesCount} microscopic or invalid curve{(skippedCurvesCount == 1 ? "" : "s")} during tracing."
                        });
                    }
                    // Last line of loop
                    ProgressCoordinator.UpdateProgress(element.Name ?? "ID: " + element.Id);
                }
                catch (Exception ex)
                {
                    resultItems.Add(new DetailItemResultItem
                    {
                        ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                        ElementId = element.Id.ToString(),
                        Status = "Failed",
                        Message = ex.Message
                    });
                }
            }

            // Close the progress window
            ProgressCoordinator.UpdateStatus("Complete!");
            ProgressCoordinator.Close();

            // Initialize and show the results dashboard modally
            DetailItemFactoryResultsViewModel resultsVm = new DetailItemFactoryResultsViewModel(OutputFolder, resultItems);
            DetailItemFactoryResultsView resultsView = new DetailItemFactoryResultsView(app.MainWindowHandle) { DataContext = resultsVm };
            resultsView.ShowDialog();
        }

        private void ProcessGeometry(
            GeometryElement geomElement, 
            Document familyDoc, 
            View planView, 
            double shortCurveTolerance,
            GraphicsStyle? subcategoryStyle,
            ref int skippedCurvesCount)
        {
            if (geomElement == null) return;

            foreach (GeometryObject geomObj in geomElement)
            {
                if (geomObj is GeometryInstance geomInst)
                {
                    ProcessGeometry(geomInst.GetInstanceGeometry(), familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
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
                                        DetailCurve dc1 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat1);
                                        if (dc1 != null && subcategoryStyle != null) dc1.LineStyle = subcategoryStyle;
                                    }
                                    if (flat2 != null)
                                    {
                                        DetailCurve dc2 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat2);
                                        if (dc2 != null && subcategoryStyle != null) dc2.LineStyle = subcategoryStyle;
                                    }
                                }
                                catch (Exception)
                                {
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
                                        DetailCurve dc1 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat1);
                                        if (dc1 != null && subcategoryStyle != null) dc1.LineStyle = subcategoryStyle;
                                    }
                                    if (flat2 != null)
                                    {
                                        DetailCurve dc2 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat2);
                                        if (dc2 != null && subcategoryStyle != null) dc2.LineStyle = subcategoryStyle;
                                    }
                                }
                                catch (Exception)
                                {
                                    skippedCurvesCount++;
                                }
                                continue;
                            }

                            skippedCurvesCount++;
                            continue;
                        }

                        if (curve.Length < shortCurveTolerance)
                        {
                            DrawHealedCurve(curve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
                            continue;
                        }

                        if (curve is NurbSpline || curve is HermiteSpline)
                        {
                            DrawHealedCurve(curve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
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
                            DrawHealedCurve(flatCurve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
                            continue;
                        }

                        DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, flatCurve);
                        if (detailCurve != null && subcategoryStyle != null)
                        {
                            detailCurve.LineStyle = subcategoryStyle;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        skippedCurvesCount++;
                    }
                    catch (Exception)
                    {
                        skippedCurvesCount++;
                    }
                }
                else if (geomObj is PolyLine polyLine)
                {
                    IList<XYZ> coords = polyLine.GetCoordinates();
                    for (int i = 0; i < coords.Count - 1; i++)
                    {
                        XYZ p1Flat = new XYZ(coords[i].X, coords[i].Y, 0.0);
                        XYZ p2Flat = new XYZ(coords[i+1].X, coords[i+1].Y, 0.0);

                        double segmentLength = p1Flat.DistanceTo(p2Flat);
                        if (segmentLength < shortCurveTolerance)
                        {
                            skippedCurvesCount++;
                            continue;
                        }

                        try
                        {
                            Line segmentLine = Line.CreateBound(p1Flat, p2Flat);
                            DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, segmentLine);
                            if (detailCurve != null && subcategoryStyle != null)
                            {
                                detailCurve.LineStyle = subcategoryStyle;
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            skippedCurvesCount++;
                        }
                        catch (Exception)
                        {
                            skippedCurvesCount++;
                        }
                    }
                }
            }
        }

        private void DrawHealedCurve(
            Curve curve, 
            Document familyDoc, 
            View planView, 
            double shortCurveTolerance, 
            GraphicsStyle? subcategoryStyle, 
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

                List<XYZ> simplifiedPoints = new List<XYZ>();
                simplifiedPoints.Add(points[0]);

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
                    XYZ p1 = simplifiedPoints[i+1];

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
                        DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, segmentLine);
                        if (detailCurve != null && subcategoryStyle != null)
                        {
                            detailCurve.LineStyle = subcategoryStyle;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        skippedCurvesCount++;
                    }
                    catch (Exception)
                    {
                        skippedCurvesCount++;
                    }
                }
            }
            catch (Exception)
            {
                skippedCurvesCount++;
            }
        }

        private Curve? FlattenCurve(Curve curve)
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
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private static string MakeValidFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        /// <summary>
        /// Gets the handler name.
        /// </summary>
        /// <returns>The string handler name.</returns>
        public string GetName()
        {
            return "Detail Item Factory Event Handler";
        }
    }

    /// <summary>
    /// Standard implementation of IFamilyLoadOptions to automatically overwrite family parameters.
    /// </summary>
    public class SimpleFamilyLoadOptions : IFamilyLoadOptions
    {
        /// <summary>
        /// Callback when a family is found during loading.
        /// </summary>
        /// <param name="familyInUse">Indicates if the family is currently in use.</param>
        /// <param name="overwriteParameterValues">Output flag to overwrite parameter values.</param>
        /// <returns>True to overwrite the family.</returns>
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        /// <summary>
        /// Callback when a shared family is found during loading.
        /// </summary>
        /// <param name="sharedFamily">The shared family element.</param>
        /// <param name="familyInUse">Indicates if the family is currently in use.</param>
        /// <param name="source">Output family source.</param>
        /// <param name="overwriteParameterValues">Output flag to overwrite parameter values.</param>
        /// <returns>True to overwrite the family.</returns>
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
```

### File: DetailItemFactory/Models/DetailItemResultItem.cs
```csharp
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Modules.DetailItemFactory.Models
{
    /// <summary>
    /// Represents the results of processing a single model element to a 2D Detail Item family.
    /// </summary>
    public class DetailItemResultItem
    {
        /// <summary>
        /// Gets or sets the name of the processed model element.
        /// </summary>
        public string ElementName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Revit Element ID of the processed element.
        /// </summary>
        public string ElementId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the execution status (e.g., Success, Skipped, Failed).
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a description or details (such as exception messages or skip reasons).
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
```

### File: DetailItemFactory/Settings/DetailItemFactorySettings.cs
```csharp
using Newtonsoft.Json;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.DetailItemFactory.Settings
{
    /// <summary>
    /// Configuration settings for the Detail Item Factory module.
    /// This module is serialized and saved inside the Revit Document's Extensible Storage schema.
    /// </summary>
    public class DetailItemFactorySettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "DetailItemFactorySettings";

        /// <summary>
        /// Gets or sets the default output folder path.
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// Gets or sets the default target subcategory name.
        /// </summary>
        public string TargetSubcategory { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactorySettings"/> class.
        /// </summary>
        public DetailItemFactorySettings()
        {
            OutputPath = string.Empty;
            TargetSubcategory = "Detail Items";
        }

        /// <summary>
        /// App default settings.
        /// </summary>
        /// <returns>DetailItemFactorySettings object with default configurations.</returns>
        public DetailItemFactorySettings Defaults()
        {
            OutputPath = string.Empty;
            TargetSubcategory = "Detail Items";
            return this;
        }

        /// <summary>
        /// Validates settings module.
        /// </summary>
        public bool IsValid(Document doc)
        {
            if (!string.IsNullOrEmpty(OutputPath) && !Directory.Exists(OutputPath))
            {
                return false;
            }
            return true;
        }
    }
}
```

### File: DetailItemFactory/ViewModels/DetailItemFactoryResultsViewModel.cs
```csharp
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.DetailItemFactory.Models;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel for the Detail Item Factory batch results dashboard.
    /// Provides collections for list views and garbage collection commands for temporary DWG files.
    /// </summary>
    public class DetailItemFactoryResultsViewModel : ViewModelBase
    {
        private readonly string _outputFolder;

        /// <summary>
        /// Gets the collection of elements conversion results.
        /// </summary>
        public ObservableCollection<DetailItemResultItem> Results { get; }

        /// <summary>
        /// Gets the command to delete temporary DWG files from the output folder.
        /// </summary>
        public ICommand DeleteTempDwgsCommand { get; }

        /// <summary>
        /// Gets the command to close the window.
        /// </summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// Delegate assigned by the View to handle window closure.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryResultsViewModel"/> class.
        /// </summary>
        /// <param name="outputFolder">The output folder path where files were saved.</param>
        /// <param name="results">The list of execution result items.</param>
        public DetailItemFactoryResultsViewModel(string outputFolder, IEnumerable<DetailItemResultItem> results)
        {
            _outputFolder = outputFolder ?? throw new ArgumentNullException(nameof(outputFolder));
            Results = new ObservableCollection<DetailItemResultItem>(results ?? new List<DetailItemResultItem>());

            DeleteTempDwgsCommand = new RelayCommand(ExecuteDeleteTempDwgs);
            CloseCommand = new RelayCommand(ExecuteClose);
        }

        private void ExecuteDeleteTempDwgs(object obj)
        {
            if (!Directory.Exists(_outputFolder))
            {
                TaskDialog.Show("Detail Item Factory", "The output folder does not exist.");
                return;
            }

            int deletedCount = 0;
            int failedCount = 0;

             try
             {
                 string[] dwgFiles = Directory.GetFiles(_outputFolder, "*.dwg");
                 string[] pcpFiles = Directory.GetFiles(_outputFolder, "*.pcp");
                 List<string> files = new List<string>();
                 files.AddRange(dwgFiles);
                 files.AddRange(pcpFiles);

                 foreach (string file in files)
                 {
                     string filename = Path.GetFileName(file);
                     // Strictly match the temporary export naming pattern (temp_export_*)
                     if (filename.StartsWith("temp_export_", StringComparison.OrdinalIgnoreCase))
                     {
                         try
                         {
                             File.Delete(file);
                             deletedCount++;
                         }
                         catch
                         {
                             failedCount++;
                         }
                     }
                 }

                 string msg = $"Garbage Collection Complete.\n\nSuccessfully deleted {deletedCount} temporary CAD file{(deletedCount == 1 ? "" : "s")} (.dwg/.pcp).";
                if (failedCount > 0)
                {
                    msg += $"\nFailed to delete {failedCount} file{(failedCount == 1 ? "" : "s")} (possibly locked or in use).";
                }
                TaskDialog.Show("Detail Item Factory - Cleanup", msg);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Detail Item Factory - Error", $"An error occurred during DWG cleanup:\n{ex.Message}");
            }
        }

        private void ExecuteClose(object obj)
        {
            CloseAction?.Invoke();
        }
    }
}
```

### File: DetailItemFactory/ViewModels/DetailItemFactorySettingsViewModel.cs
```csharp
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel for managing Detail Item Factory configurations in the Settings Dashboard.
    /// </summary>
    public class DetailItemFactorySettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private DetailItemFactorySettings _settings;
        private bool _isOverridden;
        private bool _isDirty;
        private string _outputPath = string.Empty;
        private string _selectedSubcategoryName = string.Empty;

        /// <inheritdoc/>
        public string ModuleName => "Detail Item Factory";

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <summary>
        /// Gets or sets the default output folder path.
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetProperty(ref _outputPath, value))
                {
                    _settings.OutputPath = value;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <summary>
        /// Gets the collection of available subcategory names from BuiltInCategory.OST_DetailComponents.
        /// </summary>
        public ObservableCollection<string> AvailableSubcategories { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets or sets the selected subcategory name.
        /// </summary>
        public string SelectedSubcategoryName
        {
            get => _selectedSubcategoryName;
            set
            {
                if (SetProperty(ref _selectedSubcategoryName, value))
                {
                    _settings.TargetSubcategory = value;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                string prefix;
                string path = string.Empty;
                string subcategory = "Detail Items";

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    var displaySettings = new DetailItemFactorySettings();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(DetailItemFactorySettings.Name))
                            {
                                displaySettings = defaultConfig.GetSettings<DetailItemFactorySettings>(DetailItemFactorySettings.Name);
                            }
                        }
                    }
                    catch { }
                    path = displaySettings?.OutputPath ?? string.Empty;
                    subcategory = displaySettings?.TargetSubcategory ?? "Detail Items";
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    path = OutputPath;
                    subcategory = SelectedSubcategoryName;
                }

                if (string.IsNullOrWhiteSpace(subcategory))
                {
                    subcategory = "Detail Items";
                }

                string displayPath = string.IsNullOrEmpty(path) ? "(Default Dynamic Document Path)" : path;
                return prefix + $"Output Folder Path:\n{displayPath}\n\nTarget Subcategory:\n{subcategory}";
            }
        }

        /// <inheritdoc/>
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the command that allows browsing for an output folder.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactorySettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public DetailItemFactorySettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            _settings = SettingsManager.Get<DetailItemFactorySettings>(_doc);
            _outputPath = _settings.OutputPath;

            // Harvest subcategories of BuiltInCategory.OST_DetailComponents from the active document
            Category detailComponentsCategory = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrEmpty(subCat.Name))
                    {
                        AvailableSubcategories.Add(subCat.Name);
                    }
                }
            }

            // Ensure "Detail Items" is in the list
            if (!AvailableSubcategories.Contains("Detail Items"))
            {
                AvailableSubcategories.Add("Detail Items");
            }

            // Set default SelectedSubcategoryName with validation
            string targetSubcat = _settings.TargetSubcategory;
            if (string.IsNullOrWhiteSpace(targetSubcat) || !AvailableSubcategories.Contains(targetSubcat))
            {
                targetSubcat = "Detail Items";
            }
            _selectedSubcategoryName = targetSubcat;

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + DetailItemFactorySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            BrowseCommand = new RelayCommand(OnBrowse, _ => IsOverridden);
        }

        private void OnBrowse(object parameter)
        {
            string initialPath = string.IsNullOrEmpty(OutputPath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : OutputPath;
            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Default Output Folder", initialPath);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                OutputPath = selectedPath;
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, DetailItemFactorySettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<DetailItemFactorySettings>(_doc);
            _outputPath = _settings.OutputPath;

            // Reload available subcategories just in case they changed
            AvailableSubcategories.Clear();
            Category detailComponentsCategory = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrEmpty(subCat.Name))
                    {
                        AvailableSubcategories.Add(subCat.Name);
                    }
                }
            }
            if (!AvailableSubcategories.Contains("Detail Items"))
            {
                AvailableSubcategories.Add("Detail Items");
            }

            string targetSubcat = _settings.TargetSubcategory;
            if (string.IsNullOrWhiteSpace(targetSubcat) || !AvailableSubcategories.Contains(targetSubcat))
            {
                targetSubcat = "Detail Items";
            }
            _selectedSubcategoryName = targetSubcat;

            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(OutputPath));
            OnPropertyChanged(nameof(SelectedSubcategoryName));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
```

### File: DetailItemFactory/ViewModels/DetailItemFactoryViewModel.cs
```csharp
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel for the Detail Item Factory WPF dialog.
    /// Provides bindings and validation logic for batch processing Revit model geometry to 2D Detail Items.
    /// Supports per-element configuration of view orientations.
    /// </summary>
    public class DetailItemFactoryViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly IntPtr _mainWindowHandle;

        private string _outputPath = string.Empty;
        private string _selectedSubcategory = "Detail Items";
        private bool _overwriteExisting = true;

        /// <summary>
        /// Gets the collection of elements configured for conversion.
        /// </summary>
        public ObservableCollection<SelectedElementItemViewModel> Elements { get; } = new ObservableCollection<SelectedElementItemViewModel>();

        /// <summary>
        /// Gets or sets the target folder path where family files are saved.
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetProperty(ref _outputPath, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets the collection of OST_DetailComponents subcategory names available in the document.
        /// </summary>
        public ObservableCollection<string> Subcategories { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets or sets the selected subcategory name.
        /// </summary>
        public string SelectedSubcategory
        {
            get => _selectedSubcategory;
            set
            {
                if (SetProperty(ref _selectedSubcategory, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether existing family files should be overwritten on disk.
        /// </summary>
        public bool OverwriteExisting
        {
            get => _overwriteExisting;
            set => SetProperty(ref _overwriteExisting, value);
        }

        /// <summary>
        /// Command to browse for an output folder.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Command to remove an element from the queue.
        /// </summary>
        public ICommand RemoveElementCommand { get; }

        /// <summary>
        /// Command to trigger processing (runs validation and closes window with a success result).
        /// </summary>
        public ICommand RunCommand { get; }

        /// <summary>
        /// Command to abort processing and close the window with a cancelled result.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Delegate assigned by the View to request window closure, passing the dialog result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="activeView">The active Revit view.</param>
        /// <param name="selectedIds">The selected element IDs.</param>
        /// <param name="mainWindowHandle">The parent Revit window handle.</param>
        public DetailItemFactoryViewModel(Document doc, View activeView, IEnumerable<ElementId> selectedIds, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
            _mainWindowHandle = mainWindowHandle;

            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            // Resolve dynamic default output path based on document status
            string resolvedPath = string.Empty;
            if (string.IsNullOrEmpty(_doc.PathName))
            {
                resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (_doc.IsModelInCloud)
            {
                resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (_doc.IsWorkshared)
            {
                ModelPath centralPath = _doc.GetWorksharingCentralModelPath();
                if (centralPath != null)
                {
                    string userVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                    if (!string.IsNullOrEmpty(userVisiblePath))
                    {
                        try
                        {
                            resolvedPath = Path.GetDirectoryName(userVisiblePath) ?? string.Empty;
                        }
                        catch
                        {
                            resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        }
                    }
                    else
                    {
                        resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }
                }
                else
                {
                    resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }
            else
            {
                try
                {
                    resolvedPath = Path.GetDirectoryName(_doc.PathName) ?? string.Empty;
                }
                catch
                {
                    resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }

            var settings = SettingsManager.Get<DetailItemFactorySettings>(_doc);
            if (settings != null && !string.IsNullOrEmpty(settings.OutputPath))
            {
                string storedPath = settings.OutputPath;
                if (!storedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    storedPath += Path.DirectorySeparatorChar;
                }
                OutputPath = storedPath;
            }
            else if (!string.IsNullOrEmpty(resolvedPath))
            {
                if (!resolvedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    resolvedPath += Path.DirectorySeparatorChar;
                }
                OutputPath = resolvedPath;
            }

            // Wire commands
            BrowseCommand = new RelayCommand(ExecuteBrowse);
            RemoveElementCommand = new RelayCommand(ExecuteRemoveElement);
            RunCommand = new RelayCommand(ExecuteRun, CanExecuteRun);
            CancelCommand = new RelayCommand(ExecuteCancel);

            // Populate Elements collection (filtering for Model elements only)
            foreach (ElementId id in selectedIds)
            {
                Element element = _doc.GetElement(id);
                if (element != null && element.Category != null && element.Category.CategoryType == CategoryType.Model)
                {
                    Elements.Add(new SelectedElementItemViewModel(element, _activeView));
                }
            }

            // Populate OST_DetailComponents subcategories
            Category detailComponentsCategory = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrEmpty(subCat.Name))
                    {
                        Subcategories.Add(subCat.Name);
                    }
                }
            }

            // Ensure "Detail Items" is in the list
            if (!Subcategories.Contains("Detail Items"))
            {
                Subcategories.Add("Detail Items");
            }

            // Set default SelectedSubcategory with fallback routing
            string targetSubcat = settings?.TargetSubcategory ?? "Detail Items";
            if (string.IsNullOrWhiteSpace(targetSubcat) || !Subcategories.Contains(targetSubcat))
            {
                targetSubcat = "Detail Items";
            }
            SelectedSubcategory = targetSubcat;
        }

        private void ExecuteBrowse(object obj)
        {
            string initialPath = OutputPath;
            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Output Folder", initialPath);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (!selectedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    selectedPath += Path.DirectorySeparatorChar;
                }
                OutputPath = selectedPath;
            }
        }

        private void ExecuteRemoveElement(object parameter)
        {
            if (parameter is SelectedElementItemViewModel item)
            {
                Elements.Remove(item);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool CanExecuteRun(object obj)
        {
            return Elements.Count > 0 && !string.IsNullOrWhiteSpace(OutputPath) && !string.IsNullOrWhiteSpace(SelectedSubcategory);
        }

        private void ExecuteRun(object obj)
        {
            CloseAction?.Invoke(true);
        }

        private void ExecuteCancel(object obj)
        {
            CloseAction?.Invoke(false);
        }
    }
}
```

### File: DetailItemFactory/ViewModels/SelectedElementItemViewModel.cs
```csharp
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel representing a single selected element for per-element configuration.
    /// </summary>
    public class SelectedElementItemViewModel : ViewModelBase
    {
        private string _selectedOrientation = string.Empty;

        /// <summary>
        /// Gets the Revit Element ID.
        /// </summary>
        public ElementId ElementId { get; }

        /// <summary>
        /// Gets the user-friendly display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the list of available orientation options.
        /// </summary>
        public List<string> AvailableOrientations { get; } = new List<string>
        {
            "Plan",
            "Elev Front",
            "Elev Back",
            "Elev Side",
            "Elev Top",
            "Elev Bottom",
            "Elev Section"
        };

        /// <summary>
        /// Gets or sets the selected orientation for this element.
        /// </summary>
        public string SelectedOrientation
        {
            get => _selectedOrientation;
            set => SetProperty(ref _selectedOrientation, value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectedElementItemViewModel"/> class.
        /// </summary>
        /// <param name="element">The Revit element.</param>
        /// <param name="activeView">The active view context.</param>
        public SelectedElementItemViewModel(Element element, View activeView)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (activeView == null) throw new ArgumentNullException(nameof(activeView));

            ElementId = element.Id;

            // Compute DisplayName: "[Category] - [Family Name] - [Type]"
            string categoryName = element.Category?.Name ?? "UnknownCategory";
            string familyName = "UnknownFamily";
            string typeName = "UnknownType";

            if (element is FamilyInstance fi)
            {
                familyName = fi.Symbol.Family.Name;
                typeName = fi.Symbol.Name;
            }
            else
            {
                ElementId typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId && element.Document.GetElement(typeId) is ElementType et)
                {
                    familyName = et.FamilyName;
                    typeName = et.Name;
                }
                else
                {
                    familyName = element.Category?.Name ?? "Model";
                    typeName = element.Name ?? "Element";
                }
            }

            DisplayName = $"{categoryName} - {familyName} - {typeName}";

            // Smart default logic
            bool isLegendView = activeView.ViewType == ViewType.Legend;
            bool isLegendComponent = false;
#if REVIT2024 || REVIT2025 || REVIT2026
            isLegendComponent = element.Category != null && element.Category.Id.Value == (long)BuiltInCategory.OST_LegendComponents;
#else
            isLegendComponent = element.Category != null && element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_LegendComponents;
#endif

            if (isLegendView && isLegendComponent)
            {
                SelectedOrientation = GetLegendComponentDefaultOrientation(element);
            }
            else
            {
                if (activeView.ViewType == ViewType.FloorPlan ||
                    activeView.ViewType == ViewType.CeilingPlan ||
                    activeView.ViewType == ViewType.EngineeringPlan ||
                    activeView.ViewType == ViewType.AreaPlan)
                {
                    SelectedOrientation = "Plan";
                }
                else if (activeView.ViewType == ViewType.Elevation ||
                         activeView.ViewType == ViewType.Section)
                {
                    SelectedOrientation = "Elev Front";
                }
                else
                {
                    SelectedOrientation = "Plan";
                }
            }
        }

        private string GetLegendComponentDefaultOrientation(Element element)
        {
            Parameter param = element.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW);
            if (param != null)
            {
                string valStr = param.AsValueString();
                if (!string.IsNullOrEmpty(valStr))
                {
                    if (valStr.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Plan";
                    if (valStr.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Front";
                    if (valStr.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Back";
                    if (valStr.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        valStr.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 || 
                        valStr.IndexOf("side", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Side";
                    if (valStr.IndexOf("top", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Top";
                    if (valStr.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Bottom";
                    if (valStr.IndexOf("section", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Elev Section";
                }
            }
            return "Plan";
        }
    }
}
```

### File: DetailItemFactory/Views/DetailItemFactoryResultsView.xaml
```xml
<Window x:Class="Synthetic.Modules.DetailItemFactory.Views.DetailItemFactoryResultsView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Detail Item Factory Results"
        Height="450" Width="650" MinHeight="400" MinWidth="550"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title/Header -->
            <RowDefinition Height="*"/>    <!-- DataGrid of Results -->
            <RowDefinition Height="Auto"/> <!-- Action Footer -->
        </Grid.RowDefinitions>

        <!-- Header -->
        <TextBlock Grid.Row="0" Text="Batch Conversion Results Summary"
                   FontSize="14" FontWeight="SemiBold"
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}" Margin="0,0,0,12"/>

        <!-- Results Grid — inherits implicit style -->
        <DataGrid Grid.Row="1" ItemsSource="{Binding Results}" Margin="0,0,0,15">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Element Name" Binding="{Binding ElementName}" Width="2*" IsReadOnly="True">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Margin" Value="8,0"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>

                <DataGridTextColumn Header="Element ID" Binding="{Binding ElementId}" Width="1*" IsReadOnly="True">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Margin" Value="8,0"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>

                <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="1*" IsReadOnly="True">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Margin" Value="8,0"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                            <Style.Triggers>
                                <Trigger Property="Text" Value="Success">
                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Success}"/>
                                </Trigger>
                                <Trigger Property="Text" Value="Partial Success">
                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Warning}"/>
                                </Trigger>
                                <Trigger Property="Text" Value="Skipped">
                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Warning}"/>
                                </Trigger>
                                <Trigger Property="Text" Value="Failed">
                                    <Setter Property="Foreground" Value="{DynamicResource Synthetic.Brushes.Error}"/>
                                </Trigger>
                            </Style.Triggers>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>

                <DataGridTextColumn Header="Details / Message" Binding="{Binding Message}" Width="3*" IsReadOnly="True">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="VerticalAlignment" Value="Center"/>
                            <Setter Property="Margin" Value="8,0"/>
                            <Setter Property="TextWrapping" Value="Wrap"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Footer -->
        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <Button Grid.Column="0" Content="Delete Temp DWGs" Command="{Binding DeleteTempDwgsCommand}"/>

            <Button Grid.Column="2" Content="Close" Command="{Binding CloseCommand}" IsCancel="True"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"/>
        </Grid>
    </Grid>
</Window>
```

### File: DetailItemFactory/Views/DetailItemFactoryResultsView.xaml.cs
```csharp
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.DetailItemFactory.Views
{
    /// <summary>
    /// Interaction logic for DetailItemFactoryResultsView.xaml
    /// </summary>
    public partial class DetailItemFactoryResultsView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryResultsView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public DetailItemFactoryResultsView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is DetailItemFactoryResultsViewModel vm)
                {
                    vm.CloseAction = () => this.Close();
                }
            };
        }
    }
}
```

### File: DetailItemFactory/Views/DetailItemFactorySettingsView.xaml
```xml
<UserControl x:Class="Synthetic.Modules.DetailItemFactory.Views.DetailItemFactorySettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent" Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Title -->
            <RowDefinition Height="Auto"/> <!-- Override Checkbox -->
            <RowDefinition Height="*"/>    <!-- Settings Section -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBlock Grid.Row="0"
                   Text="Detail Item Factory Settings"
                   FontSize="18" FontWeight="Bold"
                   Foreground="{DynamicResource Synthetic.Brushes.TextPrimary}"
                   Margin="0,0,0,15"/>

        <!-- Override Checkbox -->
        <CheckBox Grid.Row="1"
                  Content="Override Firm Settings for this Project"
                  IsChecked="{Binding IsOverridden, Mode=TwoWay}"
                  FontSize="13"
                  Margin="0,0,0,15"/>

        <!-- Settings Group -->
        <GroupBox Grid.Row="2"
                  Header="Configuration"
                  BorderBrush="{DynamicResource Synthetic.Brushes.BorderNormal}"
                  BorderThickness="1"
                  Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                  Padding="12"
                  IsEnabled="{Binding IsOverridden}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="15"/> <!-- Spacer -->
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <!-- Output Folder Path -->
                <TextBlock Grid.Row="0"
                           Text="Default Output Folder Path (leave empty to use default document path):"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                           FontSize="11"
                           Margin="0,0,0,5"/>

                <Grid Grid.Row="1">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- TextBox for Folder Path — inherits theme TextBox style -->
                    <TextBox Grid.Column="0"
                             Text="{Binding OutputPath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                             FontFamily="Consolas"
                             VerticalAlignment="Center"
                             Margin="0,0,10,0"/>

                    <Button Grid.Column="1"
                            Content="Browse..."
                            Command="{Binding BrowseCommand}"/>
                </Grid>

                <!-- Target Subcategory -->
                <TextBlock Grid.Row="3"
                           Text="Default Target Subcategory Name:"
                           Foreground="{DynamicResource Synthetic.Brushes.TextSecondary}"
                           FontSize="11"
                           Margin="0,0,0,5"/>

                <!-- ComboBox — inherits theme ComboBox style -->
                <ComboBox Grid.Row="4"
                          ItemsSource="{Binding AvailableSubcategories}"
                          SelectedItem="{Binding SelectedSubcategoryName, Mode=TwoWay}"
                          VerticalAlignment="Center"/>
            </Grid>
        </GroupBox>
    </Grid>
</UserControl>
```

### File: DetailItemFactory/Views/DetailItemFactorySettingsView.xaml.cs
```csharp

using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;
using System.Windows.Controls;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.DetailItemFactory.Views
{
    /// <summary>
    /// Interaction logic for DetailItemFactorySettingsView.xaml.
    /// </summary>
    public partial class DetailItemFactorySettingsView : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactorySettingsView"/> class.
        /// </summary>
        public DetailItemFactorySettingsView()
        {
            InitializeComponent();
        }
    }
}
```

### File: DetailItemFactory/Views/DetailItemFactoryView.xaml
```xml
<Window x:Class="Synthetic.Modules.DetailItemFactory.Views.DetailItemFactoryView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Synthetic.Shared.UI"
        Title="Detail Item Factory"
        Height="600" Width="650" MinHeight="500" MinWidth="550"
        WindowStartupLocation="CenterOwner"
        Style="{DynamicResource SyntheticWindowStyle}"
        local:WindowChromeBehavior.EnableWindowCommands="True">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="../../../Shared/UI/SyntheticTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="15">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Folder Path GroupBox -->
            <RowDefinition Height="*"/>    <!-- Elements DataGrid GroupBox -->
            <RowDefinition Height="Auto"/> <!-- Subcategory GroupBox -->
            <RowDefinition Height="Auto"/> <!-- Options CheckBox -->
            <RowDefinition Height="Auto"/> <!-- Action Buttons -->
        </Grid.RowDefinitions>

        <!-- Output Folder Path Selection -->
        <GroupBox Grid.Row="0" Header="Output Folder Path" Margin="0,0,0,10" Padding="8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBox Grid.Column="0" Text="{Binding OutputPath, UpdateSourceTrigger=PropertyChanged}" 
                         VerticalContentAlignment="Center" Margin="0,0,8,0"/>
                <Button Grid.Column="1" Content="Browse..." Command="{Binding BrowseCommand}"/>
            </Grid>
        </GroupBox>

        <!-- Selected Elements Per-Element Configuration -->
        <GroupBox Grid.Row="1" Header="Configure View Orientations" Margin="0,0,0,10" Padding="8">
            <!-- DataGrid inherits implicit style -->
            <DataGrid ItemsSource="{Binding Elements}" RowHeight="32">
                <DataGrid.Columns>
                    <!-- Display Name -->
                    <DataGridTextColumn Header="Selected Element" Binding="{Binding DisplayName}" Width="3*" IsReadOnly="True">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="VerticalAlignment" Value="Center"/>
                                <Setter Property="Margin" Value="8,0"/>
                                <Setter Property="TextTrimming" Value="CharacterEllipsis"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <!-- View Orientation Dropdown -->
                    <DataGridTemplateColumn Header="Orientation" Width="1.5*">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <ComboBox ItemsSource="{Binding AvailableOrientations}" SelectedItem="{Binding SelectedOrientation, UpdateSourceTrigger=PropertyChanged}"
                                          VerticalAlignment="Center" Margin="5,0"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>

                    <!-- Remove Action -->
                    <DataGridTemplateColumn Header="Action" Width="Auto">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <Button Content="Remove" Command="{Binding DataContext.RemoveElementCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                        CommandParameter="{Binding}" VerticalAlignment="Center" Margin="5,0"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>
        </GroupBox>

        <!-- Target Subcategory Selection -->
        <GroupBox Grid.Row="2" Header="Target Subcategory" Margin="0,0,0,10" Padding="8">
            <ComboBox ItemsSource="{Binding Subcategories}" SelectedItem="{Binding SelectedSubcategory}" 
                      VerticalContentAlignment="Center"/>
        </GroupBox>

        <!-- Additional Options -->
        <StackPanel Grid.Row="3" Margin="5,5,0,10">
            <CheckBox Content="Overwrite Existing Family Files" IsChecked="{Binding OverwriteExisting}"/>
        </StackPanel>

        <!-- Action Buttons -->
        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Run" Command="{Binding RunCommand}" IsDefault="True"
                    Style="{DynamicResource Synthetic.Styles.PrimaryButton}"
                    Margin="0,0,10,0"/>
            <Button Content="Cancel" Command="{Binding CancelCommand}" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

### File: DetailItemFactory/Views/DetailItemFactoryView.xaml.cs
```csharp
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Windows;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.DetailItemFactory.Views
{
    /// <summary>
    /// Interaction logic for DetailItemFactoryView.xaml
    /// </summary>
    public partial class DetailItemFactoryView : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryView"/> class.
        /// </summary>
        /// <param name="mainWindowHandle">The parent Revit main window handle.</param>
        public DetailItemFactoryView(IntPtr mainWindowHandle)
        {
            InitializeComponent();
            RevitWindowHelper.SetOwner(this, mainWindowHandle);

            DataContextChanged += (sender, args) =>
            {
                if (DataContext is DetailItemFactoryViewModel vm)
                {
                    vm.CloseAction = (dialogResult) =>
                    {
                        try
                        {
                            this.DialogResult = dialogResult;
                        }
                        catch (InvalidOperationException)
                        {
                            // Window is closing or closed
                        }
                        this.Close();
                    };
                }
            };
        }
    }
}
```

