using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;

using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a container of various Revit element models to be serialized to/from JSON.
    /// </summary>
    public class ModelsToSerialize
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the dictionary of fill pattern elements.
        /// </summary>
        [JsonProperty(Order = 1)]
        public Dictionary<string, FillPatternElementModel> FillPatternElements { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of line pattern elements.
        /// </summary>
        [JsonProperty(Order = 2)]
        public Dictionary<string, LinePatternElementModel> LinePatternElements { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of property set elements (material assets).
        /// </summary>
        [JsonProperty(Order = 3)]
        public Dictionary<string, PropertySetElementModel> PropertySetElements { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of materials.
        /// </summary>
        [JsonProperty(Order = 4)]
        public Dictionary<string, MaterialModel> Materials { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of element types.
        /// </summary>
        [JsonProperty(Order = 5)]
        public Dictionary<string, ElementTypeModel> ElementTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of category override models.
        /// </summary>
        [JsonProperty(Order = 6)]
        public Dictionary<string, CategoryModel> Categories { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of host object types.
        /// </summary>
        [JsonProperty(Order = 7)]
        public Dictionary<string, HostObjTypeModel> HostObjTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of filled region types.
        /// </summary>
        [JsonProperty(Order = 8)]
        public Dictionary<string, FilledRegionTypeModel> FillRegionTypes {  get; set; }

        /// <summary>
        /// Gets or sets the dictionary of dimension types.
        /// </summary>
        [JsonProperty(Order = 9)]
        public Dictionary<string, DimensionTypeModel> DimensionTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of text note types.
        /// </summary>
        [JsonProperty(Order = 10)]
        public Dictionary<string, ElementTypeModel> TextNoteTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of text element types (family-level labels).
        /// </summary>
        [JsonProperty(Order = 23)]
        public Dictionary<string, ElementTypeModel> TextElementTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of model text types.
        /// </summary>
        [JsonProperty(Order = 24)]
        public Dictionary<string, ElementTypeModel> ModelTextTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of spot dimension types.
        /// </summary>
        [JsonProperty(Order = 25)]
        public Dictionary<string, ElementTypeModel> SpotDimensionTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of toposolid types.
        /// </summary>
        [JsonProperty(Order = 26)]
        public Dictionary<string, HostObjTypeModel> ToposolidTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of view templates.
        /// </summary>
        [JsonProperty(Order = 11)]
        public Dictionary<string, ViewModel> ViewTemplates { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of views.
        /// </summary>
        [JsonProperty(Order = 12)]
        public Dictionary<string, ViewModel> Views { get; set; }

        /// <summary>
        /// Gets or sets the list of generic element models.
        /// </summary>
        [JsonProperty(Order = 13)]
        public List<ElementModel> Elements { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of grid types.
        /// </summary>
        [JsonProperty(Order = 14)]
        public Dictionary<string, GridTypeModel> GridTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of level types.
        /// </summary>
        [JsonProperty(Order = 15)]
        public Dictionary<string, LevelTypeModel> LevelTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of curtain system types.
        /// </summary>
        [JsonProperty(Order = 16)]
        public Dictionary<string, CurtainSystemTypeModel> CurtainSystemTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of mullion types.
        /// </summary>
        [JsonProperty(Order = 17)]
        public Dictionary<string, MullionTypeModel> MullionTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of fascia types.
        /// </summary>
        [JsonProperty(Order = 18)]
        public Dictionary<string, FasciaTypeModel> FasciaTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of gutter types.
        /// </summary>
        [JsonProperty(Order = 19)]
        public Dictionary<string, GutterTypeModel> GutterTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of view family types.
        /// </summary>
        [JsonProperty(Order = 20)]
        public Dictionary<string, ViewFamilyTypeModel> ViewFamilyTypes { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of browser organizations.
        /// </summary>
        [JsonProperty(Order = 21)]
        public Dictionary<string, BrowserOrganizationModel> BrowserOrganizations { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of parameter elements.
        /// </summary>
        [JsonProperty(Order = 22)]
        public Dictionary<string, ParameterElementModel> ParameterElements { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of parameter filter elements (view filters).
        /// </summary>
        [JsonProperty(Order = 24)]
        public Dictionary<string, ParameterFilterElementModel> ParameterFilters { get; set; }

        #endregion
        #region Internal Constructors

        internal ModelsToSerialize ()
        {
            FillRegionTypes = new Dictionary<string, FilledRegionTypeModel> ();
            Materials = new Dictionary<string, MaterialModel>();
            ElementTypes = new Dictionary<string, ElementTypeModel>();
            DimensionTypes = new Dictionary<string, DimensionTypeModel>();
            HostObjTypes = new Dictionary<string, HostObjTypeModel>();
            Views = new Dictionary<string, ViewModel>();
            Categories = new Dictionary<string, CategoryModel>();
            Elements = new List<ElementModel>();
            GridTypes = new Dictionary<string, GridTypeModel>();
            LevelTypes = new Dictionary<string, LevelTypeModel>();
            FillPatternElements = new Dictionary<string, FillPatternElementModel>();
            LinePatternElements = new Dictionary<string, LinePatternElementModel>();
            PropertySetElements = new Dictionary<string, PropertySetElementModel>();
            CurtainSystemTypes = new Dictionary<string, CurtainSystemTypeModel>();
            MullionTypes = new Dictionary<string, MullionTypeModel>();
            FasciaTypes = new Dictionary<string, FasciaTypeModel>();
            GutterTypes = new Dictionary<string, GutterTypeModel>();
            ViewFamilyTypes = new Dictionary<string, ViewFamilyTypeModel>();
            BrowserOrganizations = new Dictionary<string, BrowserOrganizationModel>();
            ParameterElements = new Dictionary<string, ParameterElementModel>();
            TextNoteTypes = new Dictionary<string, ElementTypeModel>();
            TextElementTypes = new Dictionary<string, ElementTypeModel>();
            ModelTextTypes = new Dictionary<string, ElementTypeModel>();
            SpotDimensionTypes = new Dictionary<string, ElementTypeModel>();
            ToposolidTypes = new Dictionary<string, HostObjTypeModel>();
            ViewTemplates = new Dictionary<string, ViewModel>();
            ParameterFilters = new Dictionary<string, ParameterFilterElementModel>();
        }

        #endregion

        #region Serialize and Deserialize Methods

        /// <summary>
        /// Deserializes a JSON string into a flat list of ElementModels.
        /// </summary>
        /// <param name="Json">The JSON representation of ModelsToSerialize.</param>
        /// <returns>An enumerable collection of ElementModels.</returns>
        public static IEnumerable<ElementModel> DeserializeByJson (string Json)
        {
            ModelsToSerialize? serializeJSON = JsonConvert.DeserializeObject<ModelsToSerialize>(Json);
            if (serializeJSON == null)
            {
                return Enumerable.Empty<ElementModel>();
            }

            serializeJSON.MigrateLegacyData();

            List<ElementModel> list = new List<ElementModel>();
            
            // 1. Patterns
            if (serializeJSON.LinePatternElements != null) list.AddRange(serializeJSON.LinePatternElements.Values);
            if (serializeJSON.FillPatternElements != null) list.AddRange(serializeJSON.FillPatternElements.Values);

            // 2. Materials
            if (serializeJSON.Materials != null) list.AddRange(serializeJSON.Materials.Values);

            // 3. Families
            if (serializeJSON.ElementTypes != null) list.AddRange(serializeJSON.ElementTypes.Values);

            // 4. Categories
            if (serializeJSON.Categories != null) list.AddRange(serializeJSON.Categories.Values);

            // 5. System Types
            if (serializeJSON.PropertySetElements != null) list.AddRange(serializeJSON.PropertySetElements.Values);
            if (serializeJSON.ParameterElements != null) list.AddRange(serializeJSON.ParameterElements.Values);
            if (serializeJSON.HostObjTypes != null) list.AddRange(serializeJSON.HostObjTypes.Values);
            if (serializeJSON.FillRegionTypes != null) list.AddRange(serializeJSON.FillRegionTypes.Values);
            if (serializeJSON.DimensionTypes != null) list.AddRange(serializeJSON.DimensionTypes.Values);
            if (serializeJSON.GridTypes != null) list.AddRange(serializeJSON.GridTypes.Values);
            if (serializeJSON.LevelTypes != null) list.AddRange(serializeJSON.LevelTypes.Values);
            if (serializeJSON.CurtainSystemTypes != null) list.AddRange(serializeJSON.CurtainSystemTypes.Values);
            if (serializeJSON.MullionTypes != null) list.AddRange(serializeJSON.MullionTypes.Values);
            if (serializeJSON.FasciaTypes != null) list.AddRange(serializeJSON.FasciaTypes.Values);
            if (serializeJSON.GutterTypes != null) list.AddRange(serializeJSON.GutterTypes.Values);
            if (serializeJSON.ViewFamilyTypes != null) list.AddRange(serializeJSON.ViewFamilyTypes.Values);
            if (serializeJSON.TextNoteTypes != null) list.AddRange(serializeJSON.TextNoteTypes.Values);
            if (serializeJSON.TextElementTypes != null) list.AddRange(serializeJSON.TextElementTypes.Values);
            if (serializeJSON.ModelTextTypes != null) list.AddRange(serializeJSON.ModelTextTypes.Values);
            if (serializeJSON.SpotDimensionTypes != null) list.AddRange(serializeJSON.SpotDimensionTypes.Values);
            if (serializeJSON.ToposolidTypes != null) list.AddRange(serializeJSON.ToposolidTypes.Values);
            if (serializeJSON.BrowserOrganizations != null) list.AddRange(serializeJSON.BrowserOrganizations.Values);
            if (serializeJSON.ParameterFilters != null) list.AddRange(serializeJSON.ParameterFilters.Values);

            // 6. Views
            if (serializeJSON.ViewTemplates != null) list.AddRange(serializeJSON.ViewTemplates.Values);
            if (serializeJSON.Views != null) list.AddRange(serializeJSON.Views.Values);

            // 7. Instances
            if (serializeJSON.Elements != null) list.AddRange(serializeJSON.Elements);

            return list;
        }
        
        private static Dictionary<string, T> _sortDict<T>(Dictionary<string, T>? dict) where T : ElementModel
        {
            if (dict == null) return new Dictionary<string, T>();
            var sorted = dict.Values
                .OrderBy(x => x.Class ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var newDict = new Dictionary<string, T>();
            foreach (var item in sorted)
            {
                if (item.Name != null && !newDict.ContainsKey(item.Name))
                {
                    newDict.Add(item.Name, item);
                }
            }
            return newDict;
        }

        /// <summary>
        /// Serializes a list of object models into a formatted JSON string.
        /// </summary>
        /// <param name="serialList">The list of object models to serialize.</param>
        /// <returns>A formatted JSON string representation.</returns>
        public static string SerializeToJson (List<ObjectModel> serialList)
        {
            ModelsToSerialize serializeJSON = new ModelsToSerialize();
            
            foreach (ObjectModel se in serialList)
            {
                serializeJSON._sortObjectModel(se);
            }

            serializeJSON.FillRegionTypes = _sortDict(serializeJSON.FillRegionTypes);
            serializeJSON.Materials = _sortDict(serializeJSON.Materials);
            serializeJSON.ElementTypes = _sortDict(serializeJSON.ElementTypes);
            serializeJSON.DimensionTypes = _sortDict(serializeJSON.DimensionTypes);
            serializeJSON.HostObjTypes = _sortDict(serializeJSON.HostObjTypes);
            serializeJSON.Views = _sortDict(serializeJSON.Views);

            if (serializeJSON.Categories != null)
            {
                var sorted = serializeJSON.Categories.Values
                    .OrderBy(x => x.Class ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                serializeJSON.Categories = new Dictionary<string, CategoryModel>();
                foreach (var item in sorted)
                {
                    if (item.Name != null)
                    {
                        string key = $"[{item.ParentCategoryName ?? ""}]_{item.Name}";
                        if (!serializeJSON.Categories.ContainsKey(key))
                        {
                            serializeJSON.Categories.Add(key, item);
                        }
                    }
                }
            }

            serializeJSON.GridTypes = _sortDict(serializeJSON.GridTypes);
            serializeJSON.LevelTypes = _sortDict(serializeJSON.LevelTypes);
            serializeJSON.FillPatternElements = _sortDict(serializeJSON.FillPatternElements);
            serializeJSON.LinePatternElements = _sortDict(serializeJSON.LinePatternElements);
            serializeJSON.PropertySetElements = _sortDict(serializeJSON.PropertySetElements);
            serializeJSON.CurtainSystemTypes = _sortDict(serializeJSON.CurtainSystemTypes);
            serializeJSON.MullionTypes = _sortDict(serializeJSON.MullionTypes);
            serializeJSON.FasciaTypes = _sortDict(serializeJSON.FasciaTypes);
            serializeJSON.GutterTypes = _sortDict(serializeJSON.GutterTypes);
            serializeJSON.ViewFamilyTypes = _sortDict(serializeJSON.ViewFamilyTypes);
            serializeJSON.BrowserOrganizations = _sortDict(serializeJSON.BrowserOrganizations);
            serializeJSON.ParameterElements = _sortDict(serializeJSON.ParameterElements);
            serializeJSON.ParameterFilters = _sortDict(serializeJSON.ParameterFilters);
            serializeJSON.TextNoteTypes = _sortDict(serializeJSON.TextNoteTypes);
            serializeJSON.TextElementTypes = _sortDict(serializeJSON.TextElementTypes);
            serializeJSON.ModelTextTypes = _sortDict(serializeJSON.ModelTextTypes);
            serializeJSON.SpotDimensionTypes = _sortDict(serializeJSON.SpotDimensionTypes);
            serializeJSON.ToposolidTypes = _sortDict(serializeJSON.ToposolidTypes);
            serializeJSON.ViewTemplates = _sortDict(serializeJSON.ViewTemplates);

            if (serializeJSON.Elements != null)
            {
                serializeJSON.Elements = serializeJSON.Elements
                    .OrderBy(x => x.Class ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return Newtonsoft.Json.JsonConvert.SerializeObject(serializeJSON, Formatting.Indented);
        }

        /// <summary>
        /// Serializes a list of ElementTypeModel objects into a formatted JSON string.
        /// </summary>
        /// <param name="serialList">The list of element type models.</param>
        /// <returns>A JSON string representation.</returns>
        public static string SerializeToJsonBySerialElementType (List<ElementTypeModel> serialList)
        {
            List<ObjectModel> serialListObjects = serialList.Cast<ObjectModel>().ToList();
            return SerializeToJson(serialListObjects);
        }

        /// <summary>
        /// Migrates legacy JSON data. Moves any TextElementType objects found in
        /// the generic ElementTypes dictionary to the new TextElementTypes dictionary.
        /// </summary>
        public void MigrateLegacyData()
        {
            try
            {
                if (this.ElementTypes != null)
                {
                    if (this.TextElementTypes == null)
                    {
                        this.TextElementTypes = new Dictionary<string, ElementTypeModel>();
                    }

                    var keysToMigrate = new List<string>();
                    foreach (var kvp in this.ElementTypes)
                    {
                        var elemType = kvp.Value;
                        if (elemType != null && string.Equals(elemType.Class, "Autodesk.Revit.DB.TextElementType", StringComparison.OrdinalIgnoreCase))
                        {
                            keysToMigrate.Add(kvp.Key);
                        }
                    }

                    foreach (var key in keysToMigrate)
                    {
                        var elemType = this.ElementTypes[key];
                        this.ElementTypes.Remove(key);
                        if (elemType.Name != null && !this.TextElementTypes.ContainsKey(elemType.Name))
                        {
                            this.TextElementTypes.Add(elemType.Name, elemType);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Strict try/catch logic per Robustness Directive.
                // Keep it silent as requested.
            }
        }

        //public static SerialElement DeserializeByJson (string Json)
        //{
        //    SerialElement serialElem = JsonConvert.DeserializeObject<SerialElement>(Json);

        //    switch (serialElem.Class)
        //    {
        //        case "Autodesk.Revit.DB.Material":
        //            serialElem = JsonConvert.DeserializeObject<SerialMaterial>(Json);
        //            break;
        //        case "Autodesk.Revit.DB.ElementType":
        //        case "Autodesk.Revit.DB.TextNoteType":
        //        case "Autodesk.Revit.DB DimensionType":
        //            serialElem = JsonConvert.DeserializeObject<SerialElementType>(Json);
        //            break;
        //        default:
        //            break;
        //    }

        //    return serialElem;
        //}

        //public static string SerializeToJson (SerialElement serialElement)
        //{
        //    SerializeJSON serializeJSON = new SerializeJSON();
        //    serializeJSON._sortSerialElement(serialElement);
        //    return Newtonsoft.Json.JsonConvert.SerializeObject(serializeJSON, Formatting.Indented);
        //}

        #endregion


        private void _sortObjectModel(ObjectModel serialElement)
        {
            if (serialElement is FilledRegionTypeModel fillRegionType)
            {
                string name = fillRegionType.Name;
                if (!this.FillRegionTypes.ContainsKey(name))
                {
                    this.FillRegionTypes.Add(name, fillRegionType);
                }
            }
            else if (serialElement is MaterialModel material)
            {
                string name = material.Name;
                if (!this.Materials.ContainsKey(name))
                {
                    this.Materials.Add(name, material);
                }
            }
            else if (serialElement is DimensionTypeModel dimType)
            {
                string name = dimType.Name;
                if (name != DimensionTypeModel.InternalDimStyleName)
                {
                    if (this.DimensionTypes.ContainsKey(name))
                    {
                        for (int i = 1; this.DimensionTypes.ContainsKey(name) && i < this.DimensionTypes.Count() + 2; i++)
                        {
                            name += i.ToString();
                        }
                    }
                    if (!this.DimensionTypes.ContainsKey(name))
                    {
                        this.DimensionTypes.Add(name, dimType);
                    }
                }
            }
            else if (serialElement is CurtainSystemTypeModel curtainSystemType)
            {
                string name = curtainSystemType.Name;
                if (!this.CurtainSystemTypes.ContainsKey(name))
                {
                    this.CurtainSystemTypes.Add(name, curtainSystemType);
                }
            }
            else if (serialElement is HostObjTypeModel hostObjType)
            {
                string name = hostObjType.Name;
                if (string.Equals(hostObjType.Class, "Autodesk.Revit.DB.ToposolidType", StringComparison.OrdinalIgnoreCase))
                {
                    if (!this.ToposolidTypes.ContainsKey(name))
                    {
                        this.ToposolidTypes.Add(name, hostObjType);
                    }
                }
                else
                {
                    if (!this.HostObjTypes.ContainsKey(name))
                    {
                        this.HostObjTypes.Add(name, hostObjType);
                    }
                }
            }
            else if (serialElement is ViewModel view)
            {
                string name = view.Name;
                if (view.IsTemplate)
                {
                    if (!this.ViewTemplates.ContainsKey(name))
                    {
                        this.ViewTemplates.Add(name, view);
                    }
                }
                else
                {
                    if (!this.Views.ContainsKey(name))
                    {
                        this.Views.Add(name, view);
                    }
                }
            }
            else if (serialElement is CategoryModel category)
            {
                string key = $"[{category.ParentCategoryName ?? ""}]_{category.Name}";
                if (!this.Categories.ContainsKey(key))
                {
                    this.Categories.Add(key, category);
                }
            }
            else if (serialElement is GridTypeModel gridType)
            {
                string name = gridType.Name;
                if (!this.GridTypes.ContainsKey(name))
                {
                    this.GridTypes.Add(name, gridType);
                }
            }
            else if (serialElement is LevelTypeModel levelType)
            {
                string name = levelType.Name;
                if (!this.LevelTypes.ContainsKey(name))
                {
                    this.LevelTypes.Add(name, levelType);
                }
            }
            else if (serialElement is MullionTypeModel mullionType)
            {
                string name = mullionType.Name;
                if (!this.MullionTypes.ContainsKey(name))
                {
                    this.MullionTypes.Add(name, mullionType);
                }
            }
            else if (serialElement is FasciaTypeModel fasciaType)
            {
                string name = fasciaType.Name;
                if (!this.FasciaTypes.ContainsKey(name))
                {
                    this.FasciaTypes.Add(name, fasciaType);
                }
            }
            else if (serialElement is GutterTypeModel gutterType)
            {
                string name = gutterType.Name;
                if (!this.GutterTypes.ContainsKey(name))
                {
                    this.GutterTypes.Add(name, gutterType);
                }
            }
            else if (serialElement is ViewFamilyTypeModel viewFamilyType)
            {
                string name = viewFamilyType.Name;
                if (!this.ViewFamilyTypes.ContainsKey(name))
                {
                    this.ViewFamilyTypes.Add(name, viewFamilyType);
                }
            }
            else if (serialElement is ElementTypeModel elemType)
            {
                string name = elemType.Name;
                if (string.Equals(elemType.Class, "Autodesk.Revit.DB.TextNoteType", StringComparison.OrdinalIgnoreCase))
                {
                    if (!this.TextNoteTypes.ContainsKey(name))
                    {
                        this.TextNoteTypes.Add(name, elemType);
                    }
                }
                else if (string.Equals(elemType.Class, "Autodesk.Revit.DB.TextElementType", StringComparison.OrdinalIgnoreCase))
                {
                    if (!this.TextElementTypes.ContainsKey(name))
                    {
                        this.TextElementTypes.Add(name, elemType);
                    }
                }
                else if (string.Equals(elemType.Class, "Autodesk.Revit.DB.ModelTextType", StringComparison.OrdinalIgnoreCase))
                {
                    if (!this.ModelTextTypes.ContainsKey(name))
                    {
                        this.ModelTextTypes.Add(name, elemType);
                    }
                }
                else if (string.Equals(elemType.Class, "Autodesk.Revit.DB.SpotDimensionType", StringComparison.OrdinalIgnoreCase))
                {
                    if (!this.SpotDimensionTypes.ContainsKey(name))
                    {
                        this.SpotDimensionTypes.Add(name, elemType);
                    }
                }
                else
                {
                    if (!this.ElementTypes.ContainsKey(name))
                    {
                        this.ElementTypes.Add(name, elemType);
                    }
                }
            }
            else if (serialElement is FillPatternElementModel fillPattern)
            {
                string name = fillPattern.Name;
                if (!this.FillPatternElements.ContainsKey(name))
                {
                    this.FillPatternElements.Add(name, fillPattern);
                }
            }
            else if (serialElement is LinePatternElementModel linePattern)
            {
                string name = linePattern.Name;
                if (!this.LinePatternElements.ContainsKey(name))
                {
                    this.LinePatternElements.Add(name, linePattern);
                }
            }
            else if (serialElement is PropertySetElementModel propSet)
            {
                string name = propSet.Name;
                if (!this.PropertySetElements.ContainsKey(name))
                {
                    this.PropertySetElements.Add(name, propSet);
                }
            }
            else if (serialElement is BrowserOrganizationModel browserOrganization)
            {
                string name = browserOrganization.Name;
                if (!this.BrowserOrganizations.ContainsKey(name))
                {
                    this.BrowserOrganizations.Add(name, browserOrganization);
                }
            }
            else if (serialElement is ParameterElementModel parameterElement)
            {
                string name = parameterElement.Name;
                if (!this.ParameterElements.ContainsKey(name))
                {
                    this.ParameterElements.Add(name, parameterElement);
                }
            }
            else if (serialElement is ParameterFilterElementModel parameterFilter)
            {
                string name = parameterFilter.Name;
                if (!this.ParameterFilters.ContainsKey(name))
                {
                    this.ParameterFilters.Add(name, parameterFilter);
                }
            }
            else if (serialElement is ElementModel elem)
            {
                this.Elements.Add(elem);
            }
        }



    }
}
