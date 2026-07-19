using Synthetic.Modules.Worksets.Commands;
using Synthetic.Modules.Worksets.Models;
using Synthetic.Modules.Worksets.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using RevitDB = Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitElem = Autodesk.Revit.DB.Element;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Modules.Worksets.Utilities
{
    /// <summary>
    /// Utility methods for querying and modifying Revit Scope Box elements.
    /// </summary>
    public static class ScopeBoxUtil
    {
        /// <summary>
        /// Retrieves all Scope Boxes from the document.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>List of Scope Box elements</returns>
        public static IList<RevitElem> GetAllScopeBoxes(RevitDoc doc)
        {
            RevitDB.FilteredElementCollector collector = new RevitDB.FilteredElementCollector(doc);
            return collector.OfCategory(RevitDB.BuiltInCategory.OST_VolumeOfInterest).ToElements();
        }

        /// <summary>
        /// Moves a list of Scope Boxes to a specific workset.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <param name="scopeBoxes">List of Scope Boxes</param>
        /// <param name="workset">Destination Workset</param>
        public static void MoveToWorkset(RevitDoc doc, IList<RevitElem> scopeBoxes, RevitDB.Workset workset)
        {
            if (workset == null) return;

            using (RevitDB.Transaction trans = new RevitDB.Transaction(doc))
            {
                trans.Start("Move Scope Boxes to Workset " + workset.Name);
                foreach (RevitElem elem in scopeBoxes)
                {
                    RevitDB.Parameter worksetParam = elem.get_Parameter(RevitDB.BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (worksetParam != null && !worksetParam.IsReadOnly)
                    {
                        worksetParam.Set(workset.Id.IntegerValue);
                    }
                }
                trans.Commit();
            }
        }

        /// <summary>
        /// Finds all views with "Scope Boxes" in their name.
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>List of views</returns>
        public static IList<RevitDB.View> GetScopeBoxViews(RevitDoc doc)
        {
            RevitDB.FilteredElementCollector collector = new RevitDB.FilteredElementCollector(doc);
            return collector.OfClass(typeof(RevitDB.View))
                            .Cast<RevitDB.View>()
                            .Where(v => v.Name.IndexOf("Scope Boxes", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToList();
        }
    }
}
