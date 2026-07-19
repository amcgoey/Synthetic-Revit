using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Modules.StandardsManagement.Utilities;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Defines the contract for the parent Project Standards Dashboard ViewModel.
    /// Breaks tight coupling and reduces Feature Envy/Inappropriate Intimacy between sub-ViewModels.
    /// </summary>
    public interface IProjectStandardsDashboard
    {
        /// <summary>
        /// Gets or sets the currently active source standard tab.
        /// </summary>
        ProjectStandardsSourceViewModel? SelectedSource { get; set; }

        /// <summary>
        /// Gets or sets the active panel mode in the right column sub-workspace.
        /// </summary>
        WorkspaceMode ActiveWorkspace { get; set; }

        /// <summary>
        /// Gets the active Revit document.
        /// </summary>
        Document? Document { get; }

        /// <summary>
        /// Gets the find and replace utility service.
        /// </summary>
        IFindReplaceService FindReplaceService { get; }

        /// <summary>
        /// Gets or sets the mock open documents list for headless testing.
        /// </summary>
        List<Document>? MockOpenDocuments { get; set; }

        /// <summary>
        /// Shows the document selection dialog callback.
        /// </summary>
        Func<SelectRevitDocumentViewModel, bool?>? ShowDocumentSelectionDialog { get; set; }

        /// <summary>
        /// Shows the consolidation merge dialog callback.
        /// </summary>
        Func<Synthetic.Shared.UI.SingleItemSelectionViewModel<QueueItemModel>, bool?>? ShowMergeDialog { get; set; }

        /// <summary>
        /// Traverses a source tree node hierarchy recursively and populates a flat list of ElementModels.
        /// </summary>
        void GetElementModelsFromHierarchy(SourceTreeItemViewModel node, List<ElementModel> list);

        /// <summary>
        /// Gathers the flat list of all checked element nodes in the active source tree.
        /// </summary>
        List<StandardElementModel> GetCheckedElements();

        /// <summary>
        /// Updates parameter value name references targeting an old name to the new name.
        /// </summary>
        void ReplaceReferences(ObjectModel oldElement, string oldName, string newName);

        /// <summary>
        /// Replaces name references targeting a list of consolidated old queue items to the new survivor name.
        /// </summary>
        void ReplaceQueueReferences(List<QueueItemModel> oldElements, string newName);
    }
}
