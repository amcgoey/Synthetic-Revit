using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitDB = Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitElem = Autodesk.Revit.DB.Element;
using RevitElemId = Autodesk.Revit.DB.ElementId;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Manipulation and modification helpers for Revit elements.
    /// </summary>
    public class ElementUtil
    {
        /// <summary>
        /// Dummy constructor for the class. Not used.
        /// </summary>
        internal ElementUtil() { }

        /// <summary>
        /// Gets an element's document.
        /// </summary>
        /// <param name="Element">A Revit element.</param>
        /// <returns>An Autodesk.Revit.DB.Document.</returns>
        public static RevitDoc Document(RevitElem Element)
        {
            return Element.Document;
        }

        /// <summary>
        /// Sets an element's parameters based on a Dictionary object with the Key being the parameter name and the Value being the parameter value.
        /// </summary>
        /// <param name="element">A Revit element.</param>
        /// <param name="dictionary">A dictionary of parameter names to parameter values.</param>
        /// <returns>The modified element.</returns>
        public static RevitElem SetParamterByDictionary(RevitElem element, Dictionary<string, RevitDB.Parameter> dictionary)
        {
            foreach (KeyValuePair<string, RevitDB.Parameter> keyValue in dictionary)
            {
                if (element.GetParameters(keyValue.Key)[0].IsReadOnly == false)
                {
                    RevitDB.Parameter param = element.LookupParameter(keyValue.Key);

                    switch (param.StorageType)
                    {
                        case RevitDB.StorageType.ElementId:
                            param.Set(keyValue.Value.AsElementId());
                            break;
                        case RevitDB.StorageType.String:
                            param.Set(keyValue.Value.AsString());
                            break;
                        case RevitDB.StorageType.Integer:
                            param.Set(keyValue.Value.AsInteger());
                            break;
                        case RevitDB.StorageType.Double:
                            param.Set(keyValue.Value.AsDouble());
                            break;
                        default:
                            throw new Exception("Parameter doesn't have a storage type");
                    }
                }
            }
            return element;
        }

        /// <summary>
        /// Gets the listed parameters of an element and returns a Dictionary with the Key being the parameter name and the Value being the parameter value.
        /// </summary>
        /// <param name="element">A Revit element.</param>
        /// <param name="parameterNames">A list of parameter names.</param>
        /// <returns>Dictionary of matching parameters.</returns>
        public static Dictionary<string, RevitDB.Parameter> GetParamterToDictionary(RevitElem element, List<string> parameterNames)
        {
            Dictionary<string, RevitDB.Parameter> dict = new Dictionary<string, RevitDB.Parameter>();

            foreach (string name in parameterNames)
            {
                RevitDB.Parameter value = element.LookupParameter(name);
                if (value != null)
                {
                    dict.Add(name, value);
                }
            }

            return dict;
        }

        /// <summary>
        /// Overwrites an element's parameters with the parameter values from a source element.
        /// </summary>
        /// <param name="Element">Destination element.</param>
        /// <param name="SourceElement">Source element for the parameter values.</param>
        /// <returns>The destination element.</returns>
        public static RevitElem TransferParameters(RevitElem Element, RevitElem SourceElement)
        {
            _transferParameters(SourceElement, Element);
            return Element;
        }

        /// <summary>
        /// Copy elements to the same location between documents.
        /// </summary>
        /// <param name="sourceDoc">The source document to copy items from.</param>
        /// <param name="elementIds">List of Element Ids of elements to be copied.</param>
        /// <param name="destinationDoc">The destination document.</param>
        /// <returns>Copied element IDs.</returns>
        public static List<RevitElemId> CopyElements(RevitDoc sourceDoc, List<int> elementIds, RevitDoc destinationDoc)
        {
            List<RevitElemId> revitElemIds = new List<RevitElemId>();

            foreach (int id in elementIds)
            {
#if REVIT2023
                revitElemIds.Add(new RevitElemId(id));
#else
                revitElemIds.Add(new RevitElemId((long)id));
#endif
            }

            CopyPasteOptions cpo = new CopyPasteOptions();
            return (List<RevitElemId>)ElementTransformUtils.CopyElements(sourceDoc, revitElemIds, destinationDoc, null, cpo);
        }

        /// <summary>
        /// Overwrites the parameters of an element with the parameters of an element from a different document.
        /// </summary>
        /// <param name="Element">Destination element.</param>
        /// <param name="SourceElement">Source element.</param>
        /// <returns>The destination element.</returns>
        public static RevitElem TransferElements(
            RevitElem Element,
            RevitElem SourceElement)
        {
            RevitDoc destinationDoc = Element.Document;
            RevitDoc sourceDoc = SourceElement.Document;

            List<RevitElemId> revitElemIds = new List<RevitElemId> { SourceElement.Id };
            CopyPasteOptions cpo = new CopyPasteOptions();
            List<RevitElemId> ids = (List<RevitElemId>)ElementTransformUtils.CopyElements(sourceDoc, revitElemIds, destinationDoc, null, cpo);

            RevitElem tempElem = destinationDoc.GetElement(ids[0]);
            _transferParameters(tempElem, Element);
            destinationDoc.Delete(tempElem.Id);

            return Element;
        }

        /// <summary>
        /// Changes an element's subcategory inside family documents.
        /// </summary>
        /// <param name="element">Element to change.</param>
        /// <param name="category">Subcategory to set the element to.</param>
        /// <returns>Dictionary with Succeeded and Failed element lists.</returns>
        public static IDictionary SetCategory(RevitElem element, RevitDB.Category category)
        {
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

#if REVIT2023
            long groupId = element.GroupId.IntegerValue;
#else
            long groupId = element.GroupId.Value;
#endif
            if (groupId == -1)
            {
                RevitDB.Parameter parameter = element.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                parameter.Set(category.Id);
                elementsMerged.Add(element);
            }
            else
            {
                elementsFailed.Add(element);
            }

            return new Dictionary<string, object>
            {
                { "Succeeded", elementsMerged },
                { "Failed", elementsFailed }
            };
        }

        /// <summary>
        /// Tests whether the element has a parameter of a given value.
        /// </summary>
        public static bool FilterByParameterValue(RevitElem element, List<string> parameterNames, string value)
        {
            object valueParam = element;

            foreach (string param in parameterNames)
            {
                Type vType = valueParam.GetType();

                if (vType != typeof(string) && vType != typeof(int) && vType != typeof(double))
                {
                    RevitElem e = (RevitElem)valueParam;
                    valueParam = e.LookupParameter(param);
                }
            }

            return value.ToString() == valueParam.ToString();
        }

        /// <summary>
        /// Gets an element given the ElementId.
        /// </summary>
        public static RevitElem GetByElementId(RevitElemId elementId, RevitDoc document)
        {
            return document.GetElement(elementId);
        }

        /// <summary>
        /// Gets an element given its UniqueId.
        /// </summary>
        public static RevitElem GetByUniqueId(string UniqueId, RevitDoc document)
        {
            return document.GetElement(UniqueId);
        }

        /// <summary>
        /// Gets an element's ElementId.
        /// </summary>
        public static RevitElemId Id(object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.Id;
        }

        /// <summary>
        /// Gets an element's name.
        /// </summary>
        public static string Name(object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.Name;
        }

        /// <summary>
        /// Gets an element's UniqueId.
        /// </summary>
        public static string UniqueId(object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.UniqueId;
        }

        /// <summary>
        /// Casts an object to a Revit Element.
        /// </summary>
        public static RevitElem CastRevitElement(object Object)
        {
            return (RevitElem)Object;
        }

        /// <summary>
        /// Paints every face in an element with a material.
        /// </summary>
        public static RevitElem PaintElement(RevitElem Element, RevitElemId MaterialId)
        {
            RevitDoc document = Element.Document;
            RevitElemId elementId = Element.Id;

            Options op = new Options();
            GeometryElement geoElem = Element.get_Geometry(op);

            IList<Face> faces = new List<Face>();

            foreach (GeometryObject geo in geoElem)
            {
                if (geo is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }
                }
                else if (geo is Face face)
                {
                    faces.Add(face);
                }

                foreach (Face faceToPaint in faces)
                {
                    document.Paint(elementId, faceToPaint, MaterialId);
                }
            }

            return Element;
        }

        /// <summary>
        /// Removes all painted faces on an element.
        /// </summary>
        public static RevitElem RemovePaintElement(RevitElem Element)
        {
            RevitDoc document = Element.Document;
            RevitElemId elementId = Element.Id;

            Options op = new Options();
            GeometryElement geoElem = Element.get_Geometry(op);

            IList<Face> faces = new List<Face>();

            foreach (GeometryObject geo in geoElem)
            {
                if (geo is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }
                }
                else if (geo is Face face)
                {
                    faces.Add(face);
                }

                foreach (Face faceToClean in faces)
                {
                    document.RemovePaint(elementId, faceToClean);
                }
            }

            return Element;
        }

        #region Helper Functions

        private static void _transferParameters(RevitElem SourceElement, RevitElem DestinationElement)
        {
            ParameterSet sourceParameters = SourceElement.Parameters;

            foreach (Parameter sourceParam in sourceParameters)
            {
                if (sourceParam.IsReadOnly == false)
                {
                    Definition def = sourceParam.Definition;
                    Parameter destinationParam = DestinationElement.get_Parameter(def);

                    StorageType st = sourceParam.StorageType;
                    switch (st)
                    {
                        case StorageType.Double:
                            destinationParam.Set(sourceParam.AsDouble());
                            break;
                        case StorageType.ElementId:
                            destinationParam.Set(sourceParam.AsElementId());
                            break;
                        case StorageType.Integer:
                            destinationParam.Set(sourceParam.AsInteger());
                            break;
                        case StorageType.String:
                            destinationParam.Set(sourceParam.AsString());
                            break;
                    }
                }
            }
        }
        #endregion
    }
}
