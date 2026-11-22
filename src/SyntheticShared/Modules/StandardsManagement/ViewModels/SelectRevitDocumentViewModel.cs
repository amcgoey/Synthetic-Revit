using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Synthetic.Shared.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.RevitDOM.Operations.Standards;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    public class RevitDocumentItem : ViewModelBase
    {
        private bool _isSelected;
        public string Title { get; set; } = string.Empty;
        public Document Document { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class SelectRevitDocumentViewModel : ViewModelBase
    {
        private bool _scanFamilies = false;
        private bool _includeNestedFamilies = false;

        public ObservableCollection<RevitDocumentItem> OpenDocuments { get; } = new ObservableCollection<RevitDocumentItem>();
        
        /// <summary>
        /// Gets the hierarchical checkable standards filter tree.
        /// </summary>
        public ObservableCollection<StandardGroupModel> FilterHierarchy { get; }

        /// <summary>
        /// Gets or sets a value indicating whether families should be scanned.
        /// </summary>
        public bool ScanFamilies
        {
            get => _scanFamilies;
            set
            {
                if (SetProperty(ref _scanFamilies, value))
                {
                    if (!value)
                    {
                        IncludeNestedFamilies = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether nested families should be included in family scanning.
        /// </summary>
        public bool IncludeNestedFamilies
        {
            get => _includeNestedFamilies;
            set => SetProperty(ref _includeNestedFamilies, value);
        }

        public SelectRevitDocumentViewModel(UIApplication? uiapp, List<Document>? mockDocs = null)
        {
            FilterHierarchy = StandardsHierarchyUtility.GenerateTemplateHierarchy();
            
            // Check all nodes by default on load
            foreach (var group in FilterHierarchy)
            {
                group.IsChecked = true;
            }

            if (mockDocs != null)
            {
                foreach (var doc in mockDocs)
                {
                    OpenDocuments.Add(new RevitDocumentItem { Title = doc.Title, Document = doc, IsSelected = false });
                }
            }
            else if (uiapp != null)
            {
                foreach (Document doc in uiapp.Application.Documents)
                {
                    OpenDocuments.Add(new RevitDocumentItem { Title = doc.Title, Document = doc, IsSelected = false });
                }
            }
        }

        public List<Document> SelectedDocuments => OpenDocuments
            .Where(d => d.IsSelected)
            .Select(d => d.Document)
            .ToList();

        /// <summary>
        /// Gets the list of names of all checked classes in the filter hierarchy.
        /// </summary>
        public List<string> SelectedFamilyGroupings => FilterHierarchy
            .SelectMany(g => g.Children.OfType<StandardClassModel>())
            .Where(c => c.IsChecked == true)
            .Select(c => c.Name)
            .ToList();
    }
}
