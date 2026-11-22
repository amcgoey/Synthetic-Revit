using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Utility methods for managing and query Revit FamilySymbol elements.
    /// </summary>
    public class FamilySymbolUtil
    {
        /// <summary>
        /// Retrieves a FamilySymbol by its family name and symbol/type name.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyName">The name of the Family.</param>
        /// <param name="symbolName">The name of the FamilySymbol/Type.</param>
        /// <returns>The matching FamilySymbol, or null if not found.</returns>
        public static FamilySymbol? GetByName (RevitDoc doc, string familyName, string symbolName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);

            return collector
                .OfClass(typeof(Family))
                .OfType<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))?
                .GetFamilySymbolIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilySymbol>()
                .FirstOrDefault(symbol => symbol.Name.Equals(symbolName));
        }

        /// <summary>
        /// Gets all family symbols/types in the document belonging to the specified BuiltInCategory.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="category">The BuiltInCategory to query.</param>
        /// <returns>A list of family symbol elements matching the category.</returns>
        public static IList<Element> GetFamiliesOfCategory (RevitDoc doc, BuiltInCategory category)
        {
            FilteredElementCollector collector = new FilteredElementCollector (doc);
            collector.OfClass(typeof(FamilySymbol)).OfCategory(category).WhereElementIsElementType();

            return collector.ToElements();
        }

        /// <summary>
        /// Checks if a FamilySymbol with the specified family name and symbol name is loaded in the document.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="familyName">The name of the Family.</param>
        /// <param name="symbolName">The name of the FamilySymbol/Type.</param>
        /// <returns>True if loaded; otherwise, false.</returns>
        public static bool IsLoaded (RevitDoc doc, string familyName, string symbolName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);

            FamilySymbol? familySymbol = collector
                .OfClass(typeof(Family))
                .OfType<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName))?
                .GetFamilySymbolIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilySymbol>()
                .FirstOrDefault(symbol => symbol.Name.Equals(symbolName));

            return familySymbol != null;
        }

        /// <summary>
        /// Gets the location point origin of a FamilySymbol.
        /// </summary>
        /// <param name="family">The FamilySymbol to query.</param>
        /// <returns>The XYZ origin point, or null if not found.</returns>
        public static XYZ? GetOrigin(FamilySymbol? family)
        {
            if (family != null)
            {
                LocationPoint location = (LocationPoint)family.Location;
                return location.Point;
            }
            return null;
        }

        /// <summary>
        /// Gets all instances of a FamilySymbol that exist within a specific View.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="family">The FamilySymbol whose instances to query.</param>
        /// <param name="view">The Revit View to search in.</param>
        /// <returns>A FilteredElementCollector containing the family instances.</returns>
        public static FilteredElementCollector GetInstancesInView (Document doc, FamilySymbol family, View view)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id);
            ElementFilter filterInstance = new FamilyInstanceFilter(doc, family.Id);

            collector.OfClass(typeof(FamilyInstance)).WherePasses(filterInstance);

            return collector;
        }
    }
}
