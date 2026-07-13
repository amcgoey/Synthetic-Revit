using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Base model representing a Revit Element, providing properties and methods for serialization, parameter management, and modification.
    /// </summary>
    public class ElementModel : ObjectModel
    {
        #region Public Properties
       
        /// <summary>
        /// Gets or sets the class name of the element.
        /// </summary>
        virtual public string Class
        {
            get => this.ElementId.Class;
            set => this.ElementId.Class = value;
        }

        /// <summary>
        /// Gets or sets the category name of the element.
        /// </summary>
        virtual public string Category
        {
            get => this.ElementId.Category;
            set => this.ElementId.Category = value;
        }

        /// <summary>
        /// Gets or sets the name of the element.
        /// </summary>
        public string Name
        {
            get => this.ElementId.Name;
            set => this.ElementId.Name = value;
        }

        /// <summary>
        /// Gets or sets the list of name aliases.
        /// </summary>
        public List<string>? Aliases
        {
            get => this.ElementId.Aliases;
            set => this.ElementId.Aliases = value;
        }

        /// <summary>
        /// Gets or sets the integer ID value of the element.
        /// </summary>
        public long Id
        {
            get => this.ElementId.Id;
            set => this.ElementId.Id = value;
        }

        /// <summary>
        /// Gets or sets the unique string ID of the element.
        /// </summary>
        public string? UniqueId
        {
            get => this.ElementId.UniqueId;
            set => this.ElementId.UniqueId = value;
        }

        /// <summary>
        /// List of the Parameters that belong to the Element.
        /// </summary>
        public List<ParameterModel> Parameters { get; set; } = new List<ParameterModel>();

        /// <summary>
        /// The Revit ElementId of the element linked to the SerialElement.
        /// </summary>
        [JsonIgnoreAttribute]
        public ElementIdModel ElementId { get; set; } = new ElementIdModel();

        /// <summary>
        /// The Revit Element that is linked to the SerialElement.
        /// </summary>
        [JsonIgnoreAttribute]
        virtual public object? Element { get; set; }

        /// <summary>
        /// The Revit Document the element belongs too.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? Document { get; set; }

        /// <summary>
        /// If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project.
        /// If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.
        /// </summary>
        [JsonIgnoreAttribute]
        public bool IsTemplate { get; set; }

        /// <summary>
        /// Gets or sets the name of the parent element that triggered the harvesting of this dependency.
        /// </summary>
        [JsonIgnoreAttribute]
        public string? DependencyOrigin { get; set; }

        #endregion
        #region Conditional Serialization Methods for Properties

        /// <summary>
        /// If IsTemplate, don't serialize the Element Id
        /// </summary>
        /// <returns>True if not a template</returns>
        public bool ShouldSerializeId()
        {
            return !IsTemplate;
        }

        /// <summary>
        /// If IsTemplate, don't serialize the Unqiue Id
        /// </summary>
        /// <returns>True if not a template</returns>
        public bool ShouldSerializeUniqueId()
        {
            return !IsTemplate;
        }

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ElementModel class.
        /// </summary>
        public ElementModel()
        {
            this.ElementId = new ElementIdModel();
            this.IsTemplate = false;
        }

        #endregion
        #region Public Methods

        /// <summary>
        /// Sets the SerialElement's aliases given a list of strings.
        /// </summary>
        /// <param name="serialElement">A SerialElement</param>
        /// <param name="aliases">A list of strings representing name aliases.</param>
        /// <returns name="serialElement">Returns the modified SerialElement</returns>
        public static ElementModel SetAliases (ElementModel serialElement, List<string> aliases)
        {
            if (aliases.Count()>0)
            {
                serialElement.Aliases = aliases;
            }
            return serialElement;
        }

        /// <summary>
        /// Returns a string representation of the ElementModel.
        /// </summary>
        /// <returns>A string detailing the model name and ID.</returns>
        public override string ToString()
        {
            return string.Format("{0}(Name=\"{1}\", ID={2})", this.GetType().Name, this.Name, this.Id);
        }

        /// <summary>
        /// Creates a shallow copy of the ElementModel.
        /// </summary>
        /// <returns>A shallow copy of the model.</returns>
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Deserializes a JSON string into an ElementModel instance.
        /// </summary>
        /// <param name="JSON">The JSON string.</param>
        /// <returns>An ElementModel instance.</returns>
        public static ElementModel ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<ElementModel>(JSON)!;
        }

        /// <summary>
        /// Serializes an ElementModel instance to a JSON string.
        /// </summary>
        /// <param name="serialElement">The instance to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string ToJSON(ElementModel serialElement)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(serialElement, Formatting.Indented);
        }

        #endregion
    }

    public static class ElementModelExtensions
    {
        public static void Populate(this ElementModel model, Element elem, bool isTemplate)
        {
            if (elem == null) return;
            model.Element = elem;
            model.Document = elem.Document;
            model.ElementId = elem.Id.ToModel(elem.Document, isTemplate);
            model.Class = elem.GetType().FullName ?? string.Empty;
            model.Name = elem.Name;

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = elem.Id.IntegerValue;
#else
            idVal = elem.Id.Value;
#endif
            model.Id = idVal;
            model.UniqueId = elem.UniqueId;
            model.IsTemplate = isTemplate;

            if (elem.Category != null)
            {
                model.Category = elem.Category.Name;
            }

            ParameterEngine.ExtractParameters(elem, model, isTemplate);
        }

        public static void Populate(this ElementTypeModel model, ElementType elemType, bool isTemplate)
        {
            model.Populate((Element)elemType, isTemplate);
            model.ElementType = elemType;
        }

        public static Element GetRevitElem(this ElementModel model, Document doc, IIdentityService identityService)
        {
            return identityService.ResolveElement(model.ElementId, doc);
        }

        public static Element GetRevitElem(this ElementModel model, Document doc)
        {
            return model.GetRevitElem(doc, new RevitIdentityService());
        }

        public static List<Element> GetAliasElements_Revit(this ElementModel model, Document doc)
        {
            List<Element> elements = new List<Element>();
            if (model.Aliases != null)
            {
                foreach (string aliasName in model.Aliases)
                {
                    Assembly assembly = typeof(Element).Assembly;
                    Type elemClass = assembly.GetType(model.Class);
                    if (elemClass != null)
                    {
                        Element elem = Select.ElementByNameClass(aliasName, elemClass, doc);
                        if (elem != null)
                        {
                            elements.Add(elem);
                        }
                    }
                }
            }
            return elements;
        }

        public static ElementModel ToModel(this Element revitElement, bool isTemplate)
        {
            if (revitElement == null) return null;

            if (revitElement is FilledRegionType fillRegionType)
            {
                return fillRegionType.ToModel(isTemplate);
            }
            if (revitElement is Material material)
            {
                return material.ToModel(revitElement.Document, isTemplate);
            }
            if (revitElement is DimensionType dimType)
            {
                if (dimType.Name != DimensionTypeModel.InternalDimStyleName)
                {
                    return dimType.ToModel(isTemplate);
                }
            }
            if (revitElement is CurtainSystemType curtainSystemType)
            {
                return curtainSystemType.ToModel(isTemplate);
            }
            if (revitElement is HostObjAttributes hostObjType)
            {
                return hostObjType.ToModel(isTemplate);
            }
            if (revitElement is View view)
            {
                if (view.IsTemplate)
                {
                    return view.ToTemplateModel(isTemplate);
                }
                else
                {
                    return view.ToModel(isTemplate);
                }
            }
            if (revitElement is GridType gridType)
            {
                return gridType.ToModel(isTemplate);
            }
            if (revitElement is LevelType levelType)
            {
                return levelType.ToModel(isTemplate);
            }
            if (revitElement is MullionType mullionType)
            {
                return mullionType.ToModel(isTemplate);
            }
            if (revitElement is FasciaType fasciaType)
            {
                return fasciaType.ToModel(isTemplate);
            }
            if (revitElement is GutterType gutterType)
            {
                return gutterType.ToModel(isTemplate);
            }
            if (revitElement is ViewFamilyType viewFamilyType)
            {
                return viewFamilyType.ToModel(isTemplate);
            }
            if (revitElement is ElementType elemType)
            {
                return elemType.ToModel(isTemplate);
            }
            if (revitElement is FillPatternElement fillPatternElement)
            {
                return fillPatternElement.ToModel(isTemplate);
            }
            if (revitElement is LinePatternElement linePatternElement)
            {
                return linePatternElement.ToModel(isTemplate);
            }
            if (revitElement is PropertySetElement propertySetElement)
            {
                return propertySetElement.ToModel(isTemplate);
            }
            if (revitElement is BrowserOrganization browserOrganization)
            {
                return browserOrganization.ToModel(isTemplate);
            }
            if (revitElement is ParameterElement parameterElement)
            {
                return parameterElement.ToModel(isTemplate);
            }
            if (revitElement is ParameterFilterElement parameterFilterElement)
            {
                return parameterFilterElement.ToModel(revitElement.Document);
            }

            var serializeElement = new ElementModel();
            serializeElement.Populate(revitElement, isTemplate);
            return serializeElement;
        }
    }
}

