using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RevitArch = Autodesk.Revit.DB.Architecture;
using RevitDB = Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitElem = Autodesk.Revit.DB.Element;
using RevitFECollector = Autodesk.Revit.DB.FilteredElementCollector;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Pre-configured <see cref="RevitFECollector"/> queries and Revit type resolution helpers.
    /// </summary>
    public class Select
    {
        internal Select() { }

        /// <summary>
        /// Retrieves all materials in the document.
        /// </summary>
        /// <param name="document">An Autodesk.Revit.DB.Document object.</param>
        /// <returns>A sequence of Autodesk.Revit.DB.Material objects.</returns>
        public static IEnumerable<RevitDB.Material> AllMaterials(RevitDoc document)
        {
            RevitFECollector collector = new RevitFECollector(document);

            return collector
                .OfClass(typeof(RevitDB.Material))
                .OfType<RevitDB.Material>();
        }

        /// <summary>
        /// Given a list of materials, returns the material that matches the given name.
        /// </summary>
        /// <param name="materials">A list of Autodesk.Revit.DB.Materials.</param>
        /// <param name="materialName">The name of the material.</param>
        /// <returns>An Autodesk.Revit.DB.Material that matches the given name.</returns>
        public static RevitDB.Material? GetMaterialByName(IEnumerable<RevitDB.Material> materials, string materialName)
        {
            return materials
                .OfType<RevitDB.Material>()
                .FirstOrDefault(m => m.Name.Equals(materialName));
        }

        /// <summary>
        /// Retrieves a Revit element of the specified class/type that matches the given name.
        /// </summary>
        /// <param name="Name">The name of the element to retrieve.</param>
        /// <param name="Class">The class type of the element (e.g. typeof(Material)).</param>
        /// <param name="document">The Revit document to search.</param>
        /// <returns>The matching element, or null if not found.</returns>
        public static RevitElem? ElementByNameClass(string Name, Type Class, RevitDoc document)
        {
            RevitFECollector collector = new RevitFECollector(document);

            RevitElem? elem = collector
                .OfClass(Class)
                .FirstOrDefault(e => e.Name.Equals(Name));

            return elem;
        }

        /// <summary>
        /// Yields Revit namespace alias candidates for a class name (Architecture &lt;-&gt; DB, and bare short-name qualification).
        /// </summary>
        internal static IEnumerable<string> GetNamespaceAliasCandidates(string? typeName)
        {
            if (string.IsNullOrEmpty(typeName)) yield break;

            if (typeName.StartsWith("Autodesk.Revit.DB.Architecture.", StringComparison.OrdinalIgnoreCase))
            {
                string shortName = typeName.Substring("Autodesk.Revit.DB.Architecture.".Length);
                yield return "Autodesk.Revit.DB." + shortName;
            }
            else if (typeName.StartsWith("Autodesk.Revit.DB.", StringComparison.OrdinalIgnoreCase))
            {
                string shortName = typeName.Substring("Autodesk.Revit.DB.".Length);
                yield return "Autodesk.Revit.DB.Architecture." + shortName;
            }
            else if (!typeName.Contains("."))
            {
                yield return "Autodesk.Revit.DB." + typeName;
                yield return "Autodesk.Revit.DB.Architecture." + typeName;
            }
        }

        /// <summary>
        /// Gets the Type of a Revit Class from RevitAPI.dll given its name.
        /// </summary>
        /// <param name="typeName">Name of the Autodesk.Revit.DB Class.</param>
        /// <returns>The Type of a Revit Class, or null if not found.</returns>
        public static Type? RevitClassByString(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            Assembly assembly = typeof(RevitElem).Assembly;
            Type? elemClass = assembly.GetType(typeName);
            if (elemClass != null) return elemClass;

            foreach (string candidate in GetNamespaceAliasCandidates(typeName))
            {
                elemClass = assembly.GetType(candidate);
                if (elemClass != null) return elemClass;
            }

            return null;
        }

        /// <summary>
        /// Given an ElementType Type, retrieves the corresponding Instance Type.
        /// </summary>
        /// <param name="elementType">A Type of ElementType.</param>
        /// <returns>The Type of Instance, or null if unmapped.</returns>
        public static Type? InstanceClassFromTypeClass(Type elementType)
        {
            Type? instanceType = null;

            var types = new Dictionary<Type, Type>
            {
                { typeof(RevitDB.FamilySymbol), typeof(RevitDB.FamilyInstance) },
                { typeof(RevitDB.TextNoteType), typeof(RevitDB.TextElement) },
                { typeof(RevitDB.DimensionType), typeof(RevitDB.Dimension) },
                { typeof(RevitDB.WallType), typeof(RevitDB.Wall) },
                { typeof(RevitDB.FloorType), typeof(RevitDB.Floor) },
                { typeof(RevitDB.CeilingType), typeof(RevitDB.Ceiling) },
                { typeof(RevitDB.RoofType), typeof(RevitDB.RoofBase) },
                { typeof(RevitDB.BuildingPadType), typeof(RevitArch.BuildingPad) },
                { typeof(RevitDB.WallFoundationType), typeof(RevitDB.WallFoundation) },
                { typeof(RevitDB.BeamSystemType), typeof(RevitDB.BeamSystem) },
                { typeof(RevitDB.GroupType), typeof(RevitDB.Group) },
                { typeof(RevitDB.ViewFamilyType), typeof(RevitDB.View) },
                { typeof(RevitDB.FilledRegionType), typeof(RevitDB.FilledRegion) },
                { typeof(RevitDB.GridType), typeof(RevitDB.Grid) },
                { typeof(RevitDB.LevelType), typeof(RevitDB.Level) },
                { typeof(RevitDB.TextElementType), typeof(RevitDB.TextElement) },
                { typeof(RevitDB.RevitLinkType), typeof(RevitDB.RevitLinkInstance) },
                { typeof(RevitDB.AssemblyType), typeof(RevitDB.AssemblyInstance) }
            };

            if (types.ContainsKey(elementType))
            {
                instanceType = types[elementType];
            }

            return instanceType;
        }

        /// <summary>
        /// Retrieves all instances in the document that use the specified element type.
        /// </summary>
        /// <param name="ElemType">The element type whose instances are to be retrieved.</param>
        /// <param name="document">The Revit document.</param>
        /// <returns>A collection of matching instance elements.</returns>
        public static IEnumerable<RevitElem> GetInstancesFromElemType(RevitElem ElemType, RevitDoc document)
        {
            RevitFECollector collector = new RevitFECollector(document);
            RevitDB.BuiltInParameter parameterId = RevitDB.BuiltInParameter.ELEM_TYPE_PARAM;
            RevitDB.FilterNumericEquals filterNumberRule = new RevitDB.FilterNumericEquals();
            RevitDB.ParameterValueProvider provider = new RevitDB.ParameterValueProvider(new RevitDB.ElementId(parameterId));
            RevitDB.FilterRule filterRule = new RevitDB.FilterElementIdRule(provider, filterNumberRule, ElemType.Id);
            RevitDB.ElementFilter filterParameter = new RevitDB.ElementParameterFilter(filterRule, false);

            Type? instanceType = Select.InstanceClassFromTypeClass(ElemType.GetType());
            if (instanceType != null)
            {
                collector.OfClass(instanceType);
            }

            IEnumerable<RevitElem> instances = collector
                .WhereElementIsNotElementType()
                .WherePasses(filterParameter)
                .ToElements();

            return instances;
        }

        /// <summary>
        /// Retrieves an ElementType by its name.
        /// </summary>
        /// <param name="Name">The name of the ElementType.</param>
        /// <param name="document">The Revit document.</param>
        /// <returns>The matching ElementType, or null if not found.</returns>
        public static RevitElem? GetElementTypeByName(string Name, RevitDoc document)
        {
            RevitFECollector collector = new RevitFECollector(document);

            return collector
                .WhereElementIsElementType()
                .ToElements()
                .FirstOrDefault(e => e.Name.Equals(Name));
        }
    }
}
