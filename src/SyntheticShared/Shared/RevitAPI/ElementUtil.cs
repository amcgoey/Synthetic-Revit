using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using RevitDB = Autodesk.Revit.DB;
using RevitElem = Autodesk.Revit.DB.Element;
using RevitElemId = Autodesk.Revit.DB.ElementId;
using RevitDoc = Autodesk.Revit.DB.Document;
using RevitFaceArray = Autodesk.Revit.DB.FaceArray;
using RevitFace = Autodesk.Revit.DB.Face;
using RevitGeo = Autodesk.Revit.DB.GeometryObject;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;


using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Manipulation and modification of Dynamo wrapped Revit elements.
    /// </summary>
    public class ElementUtil
    {
        /// <summary>
        /// Dummy constructor for the class.  Not used.
        /// </summary>
        internal ElementUtil() { }

        /// <summary>
        /// Gets an elements document
        /// </summary>
        /// <param name="Element">A dynamo wrapped element</param>
        /// <returns name="Document">A Autodesk.Revit.DB.Document</returns>
        public static RevitDoc Document(RevitElem Element)
        {
            return Element.Document;
        }

        /// <summary>
        /// Sets an element's parameters based on a Dictionary object with the Key being the parameter name and the Value being the parameter value.
        /// </summary>
        /// <param name="element">A Dynamo wrapped element.</param>
        /// <param name="dictionary">A Synthetic Dictionary</param>
        /// <returns></returns>
        public static RevitElem SetParamterByDictionary(RevitElem element, Dictionary<string, RevitDB.Parameter> dictionary)
        {
            RevitDoc doc = element.Document;

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
        /// <param name="element">A Dynamo wrapped element.</param>
        /// <param name="parameterNames">A list of parameter names.</param>
        /// <returns></returns>
        public static Dictionary<string, RevitDB.Parameter> GetParamterToDictionary(RevitElem element, List<string> parameterNames)
        {
            Dictionary<string, RevitDB.Parameter> dict = new Dictionary<string, RevitDB.Parameter>();
            RevitDoc doc = element.Document;

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
        /// Overwrite an elements parameters with the parameter values from a source element.
        /// </summary>
        /// <param name="Element">Destination element</param>
        /// <param name="SourceElement">Source element for the parameter values</param>
        /// <returns name="Element">The destination element</returns>
        public static RevitElem TransferParameters(RevitElem Element, RevitElem SourceElement)
        {
            Action<RevitElem, RevitElem> transfer = (sElem, dElem) =>
            {
                _transferParameters(sElem, dElem);
            };

            RevitDoc document = Element.Document;

            transfer(SourceElement, Element);

            return Element;
        }

        /// <summary>
        /// Copy elements to the same location between documents.  Can be used to copy system types or view templates between documents.  Model elements are copied in the same location.  If the elements already exist, Revit will give you an option to either duplicate the types or cancel the operation.  Please note that documents are to be a Autodesk.Revit.DB.Document objects, not a Dynamo wrapped Revit Document.
        /// </summary>
        /// <param name="sourceDoc">The source document to copy items from.</param>
        /// <param name="elementIds">List of Element Ids of elements to be copied.</param>
        /// <param name="destinationDoc">The destination document.</param>
        /// <returns></returns>
        public static List<RevitElemId> CopyElements(RevitDoc sourceDoc, List<int> elementIds, RevitDoc destinationDoc)
        {
            string transactionName = "Copy Elements from document " + sourceDoc.Title;
            List<RevitElemId> copiedElemsIds;
            List<RevitElemId> revitElemIds = new List<RevitElemId>();

            Func<RevitDoc, List<RevitElemId>, RevitDoc, List<RevitElemId>> copy = (sDoc, elemIds, dDoc) =>
            {
                Autodesk.Revit.DB.CopyPasteOptions cpo = new Autodesk.Revit.DB.CopyPasteOptions();
                return (List<RevitElemId>)Autodesk.Revit.DB.ElementTransformUtils.CopyElements(sDoc, elemIds, dDoc, null, cpo);
            };

            foreach (int id in elementIds)
            {
#if REVIT2022 || REVIT2023
                revitElemIds.Add(new RevitElemId(id));
#else
                revitElemIds.Add(new RevitElemId((long)id));
#endif
            }
            copiedElemsIds = copy(sourceDoc, revitElemIds, destinationDoc);

            return copiedElemsIds;
        }

        /// <summary>
        /// Overwrites the parameters of an element with the parameters of an element from a different document.  Associated elements such as materials may be duplicated in the document.
        /// </summary>
        /// <param name="Element"></param>
        /// <param name="SourceElement"></param>
        /// <returns></returns>
        public static RevitElem TransferElements(
            RevitElem Element,
            RevitElem SourceElement)
        {
            RevitDoc destinationDoc = Element.Document;
            RevitDoc sourceDoc = SourceElement.Document;

            string transactionName = "Element overwritten from " + sourceDoc.Title;

            RevitElem returnElem;

            Func<RevitElem, RevitDoc, RevitElem, RevitDoc, RevitElem> transfer = (dElem, dDoc, sElem, sDoc) =>
            {
                List<RevitElemId> revitElemIds = new List<RevitElemId>();
                revitElemIds.Add(sElem.Id);

                Autodesk.Revit.DB.CopyPasteOptions cpo = new Autodesk.Revit.DB.CopyPasteOptions();
                List<RevitElemId> ids = (List<RevitElemId>)Autodesk.Revit.DB.ElementTransformUtils.CopyElements(sDoc, revitElemIds, dDoc, null, cpo);

                RevitElem tempElem = dDoc.GetElement(ids[0]);
                _transferParameters(tempElem, dElem);
                destinationDoc.Delete(tempElem.Id);

                return dElem;
            };

            returnElem = transfer(Element, destinationDoc, SourceElement, sourceDoc);

            return returnElem;
        }

        /// <summary>
        /// Changes an element's subcategory.  Only works inside of Family documents
        /// </summary>
        /// <param name="element">Element to change</param>
        /// <param name="category">Subcategory to set the element too.</param>
        /// <returns name="Suceeded">If the element's category was changed.</returns>
        /// <returns name="Failed">If the change in category failed.</returns>
        public static IDictionary SetCategory(RevitElem element, RevitDB.Category category)
        {
            //  Name of Transaction
            string transactionName = "Set Element Category to" + category.Name;

            RevitDoc document = element.Document;

            // Intialize list for elements that are successfully merged and failed to merge.
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

            // Define Function to change element's category.
            Action<RevitElem, RevitDB.Category> _SetCategory = (elem, cat) =>
            {
                // If Element is in a group, put the element in the failed list
#if REVIT2022 || REVIT2023
                long groupId = elem.GroupId.IntegerValue;
#else
                long groupId = elem.GroupId.Value;
#endif
                if (groupId == -1)
                {
                    //elem.TextNoteType = rToType;
                    RevitDB.Parameter parameter = elem.get_Parameter(RevitDB.BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                    parameter.Set(cat.Id);
                    elementsMerged.Add(elem);
                }
                else
                {
                    elementsFailed.Add(elem);
                }
            };
            _SetCategory(element, category);

            return new Dictionary<string, object>
            {
                {"Succeeded", elementsMerged},
                {"Failed", elementsFailed}
            };
        }

        /// <summary>
        /// Given a list of elements, sets their worksets to the given workset.
        /// </summary>
        /// <param name="elements">A list of elements to change</param>
        /// <param name="workset">A Revit Workset object</param>
        /// <param name="document">A Revit Document</param>
        /// <returns>The list of elements for chaining.</returns>
        public static List<RevitElem> SetWorkset(List<RevitElem> elements, Workset workset, RevitDoc document)
        {
            //  Name of Transaction
            string transactionName = "Move " + elements.Count + " elements to workset " + workset.Name;

            using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
            {
                trans.Start(transactionName);
                foreach (RevitElem element in elements)
                {
#if !REVIT2022
                    if (element.IsModifiable)
                    {
#endif
                    Parameter worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (worksetParam != null 
                        && worksetParam.AsInteger() != workset.Id.IntegerValue 
                        && !worksetParam.IsReadOnly 
                        /*&& worksetParam.UserModifiable*/)
                        {
                            worksetParam.Set(workset.Id.IntegerValue);
                        }
#if !REVIT2022
                    }
#endif
                }
                trans.Commit();
            }
            return elements;
        }

        /// <summary>
        /// Merges ElementType FromType into ToType.  FromType will be deleted if all instances of the Type are successfully changed.  Elements in groups will not be changed.
        /// </summary>
        /// <param name="FromType">All instances of this ElementType will be merged into the ToType and the Type will be deleted.</param>
        /// <param name="ToType">ElementType to merge into.</param>
        /// <returns name="Merged">A list of instances that were successfully changed to ToType</returns>
        /// <returns name="Failed">A list of instances that failed to changed to ToType</returns>
        public static IDictionary MergeElementTypes(RevitElem FromType, RevitElem ToType)
        {
            // Get the Revit elements from the Dynamo Elements
            RevitDB.ElementType rFromType = (RevitDB.ElementType)FromType;
            RevitDB.ElementType rToType = (RevitDB.ElementType)ToType;

            RevitDoc document = rToType.Document;

            // Collect all instances of FromType
            IEnumerable<RevitDB.Element> instances = Select.GetInstancesFromElemType(rFromType, document);

            // Intialize list for elements that are successfully merged and failed to merge.
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

            // Define Function to change instances types.
            Action<IEnumerable<RevitDB.Element>> _SetType = (elements) =>
            {
                foreach (RevitDB.Element elem in elements)
                {
                    // If Element is in a group, put the element in the failed list
#if REVIT2022 || REVIT2023
                    long groupId = elem.GroupId.IntegerValue;
#else
                    long groupId = elem.GroupId.Value;
#endif
                    if (groupId == -1)
                    {
                        //elem.TextNoteType = rToType;
                        RevitDB.Parameter param = elem.get_Parameter(RevitDB.BuiltInParameter.ELEM_TYPE_PARAM);
                        param.Set(rToType.Id);
                        RevitElem dElem = elem;
                        elementsMerged.Add(dElem);
                    }
                    else
                    {
                        RevitElem dElem = elem;
                        elementsFailed.Add(dElem);
                    }
                }

                // Check if there are any instances of FromType left (either failed to merge, or none existed)
                if (elementsFailed.Count == 0)
                {
                    document.Delete(rFromType.Id);
                }
            };

            _SetType(instances);

            return new Dictionary<string, object>
            {
                {"Merged", elementsMerged},
                {"Failed", elementsFailed}
            };
        }

        /// <summary>
        /// Merges ElementType FromType into ToType.  FromType will be deleted if all instances of the Type are successfully changed.  Elements in groups will not be changed.
        /// </summary>
        /// <param name="FromType">All instances of this ElementType will be merged into the ToType and the Type will be deleted.</param>
        /// <param name="ToType">ElementType to merge into.</param>
        /// <returns name="Merged">A list of instances that were successfully changed to ToType</returns>
        /// <returns name="Failed">A list of instances that failed to changed to ToType</returns>
        public static IDictionary MergeElementTypesRevit(RevitElem FromType, RevitElem ToType)
        {
            //  Name of Transaction
            string transactionName = "Merge Element Type";

            RevitDoc document = ToType.Document;

            // Collect all instances of FromType
            IEnumerable<RevitDB.Element> instances = Select.GetInstancesFromElemType(FromType, document);

            // Intialize list for elements that are successfully merged and failed to merge.
            List<RevitElem> elementsMerged = new List<RevitElem>();
            List<RevitElem> elementsFailed = new List<RevitElem>();

            // Define Function to change instances types.
            Action<IEnumerable<RevitDB.Element>> _SetType = (elements) =>
            {
                foreach (RevitDB.Element elem in elements)
                {
                    // If Element is in a group, put the element in the failed list
#if REVIT2022 || REVIT2023
                    long groupId = elem.GroupId.IntegerValue;
#else
                    long groupId = elem.GroupId.Value;
#endif
                    if (groupId == -1)
                    {
                        //elem.TextNoteType = rToType;
                        RevitDB.Parameter param = elem.get_Parameter(RevitDB.BuiltInParameter.ELEM_TYPE_PARAM);
                        param.Set(ToType.Id);
                        elementsMerged.Add(elem);
                    }
                    else
                    {
                        elementsFailed.Add(elem);
                    }
                }

                // Check if there are any instances of FromType left (either failed to merge, or none existed)
                if (elementsFailed.Count == 0)
                {
                            document.Delete(FromType.Id);           
                }
            };

            try
            {
                using (Autodesk.Revit.DB.Transaction trans = new Autodesk.Revit.DB.Transaction(document))
                {
                    trans.Start(transactionName);
                    _SetType(instances);
                    trans.Commit();
                }
            }
            catch (Exception) { }

            return new Dictionary<string, object>
            {
                {"Merged", elementsMerged},
                {"Failed", elementsFailed}
            };
        }

        /// <summary>
        /// Tests whether the element has a parameter of a given value.  Returns true if the parameter has an equal value and false otherwise.  A list of parameters names can be given to test against values in elements within parameters.  For example, one can test for a type description from a instance by creating a list of each parameter.  Please note that the comparision is done using the string representation of each parameter.
        /// </summary>
        /// <param name="element">A dynamo wrapped Revit element.</param>
        /// <param name="parameterNames">A list of parameter names.  The parameters in the list will each be retrieved iteratively.  So the first name is on the input element, the next name on the element returned from the first parameter and so on.</param>
        /// <param name="value">A parameter value as a string.</param>
        /// <returns name="Filter">True if the element's parameter equals the input value, otherwise false.</returns>
        //[MultiReturn(new[] { "Filter", "Debug" })]
        //public static Dictionary<string,object> FilterByParameterValue (dynamoElem element, List<string> parameterNames, string value )
        public static bool FilterByParameterValue(RevitElem element, List<string> parameterNames, string value)
        {
            bool filter;
            object valueParam = element;

            //List<string> debug = new List<string>();

            foreach (string param in parameterNames)
            {
                Type vType = valueParam.GetType();

                if (vType != typeof(string) && vType != typeof(int) && vType != typeof(double))
                {
                    RevitElem e = (RevitElem)valueParam;
                    valueParam = e.LookupParameter(param);
                }
            }

            if (value.ToString() == valueParam.ToString())
            {
                filter = true;
            }
            else
            {
                filter = false;
            }

            return filter;
        }

        /// <summary>
        /// Gets an element given the ElementId
        /// </summary>
        /// <param name="elementId">A Autodesk.Revit.DB.ElementId</param>
        /// <param name="document">Document that the element is in.</param>
        /// <returns name="Element">Returns a unwrapped Autodesk.Revit.DB.Element</returns>
        public static RevitElem GetByElementId(RevitElemId elementId, RevitDoc document)
        {
            return document.GetElement(elementId);
        }

        /// <summary>
        /// Gets an element given its UniqueId
        /// </summary>
        /// <param name="UniqueId">A UniqueId as a string</param>
        /// <param name="document">Document that the element is in.</param>
        /// <returns name="Element">Returns a unwrapped Autodesk.Revit.DB.Element</returns>
        public static RevitElem GetByUniqueId(string UniqueId, RevitDoc document)
        {
            return document.GetElement(UniqueId);
        }

        /// <summary>
        /// Gets a Element's ElementId
        /// </summary>
        /// <param name="Element">A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element</param>
        /// <returns name="ElementId">The Autodesk.Revit.DB.ElementId</returns>
        public static RevitElemId Id(System.Object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.Id;
        }

        /// <summary>
        /// Gets a Element's name
        /// </summary>
        /// <param name="Element">A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element</param>
        /// <returns name="Name">The name of the element</returns>
        public static string Name(System.Object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.Name;
        }

        /// <summary>
        /// Gets a Element's UniqueId
        /// </summary>
        /// <param name="Element">A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element</param>
        /// <returns name="UniqueId">The UniqueId of the element</returns>
        public static string UniqueId(System.Object Element)
        {
            RevitElem elem = (RevitElem)Element;
            return elem.UniqueId;
        }

        /// <summary>
        /// If the object 
        /// </summary>
        /// <param name="Object"></param>
        /// <returns></returns>
        public static RevitElem CastRevitElement(System.Object Object)
        {
            return (RevitElem)Object;
        }

        /// <summary>
        /// Paints every face in an element with a material
        /// </summary>
        /// <param name="Element">The element to paint</param>
        /// <param name="MaterialId">The material to paint</param>
        /// <returns name="Element">The modified element</returns>
        public static RevitElem PaintElement(RevitElem Element, RevitElemId MaterialId)
        {
            RevitDoc document = Element.Document;
            RevitElemId elementId = Element.Id;

            RevitDB.Options op = new RevitDB.Options();
            RevitDB.GeometryElement geoElem = Element.get_Geometry(op);

            IList<RevitDB.Face> faces = new List<RevitDB.Face>();

            foreach (RevitDB.GeometryObject geo in geoElem)
            {
                if (geo is RevitDB.Solid solid)
                {
                    foreach (RevitDB.Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }

                }
                else if (geo is RevitDB.Face face)
                {
                    faces.Add(face);
                }

                Action<RevitElemId, IList<RevitDB.Face>, RevitElemId> paintFaces = (RevitElemId elemId, IList<RevitDB.Face> faceList, RevitElemId matId) =>
                {
                    foreach (RevitDB.Face face in faceList)
                    {
                        document.Paint(elemId, face, matId);
                    }
                };

                paintFaces(elementId, faces, MaterialId);
            }

            return Element;
        }

        /// <summary>
        /// Removes all painted faces on an elementl
        /// </summary>
        /// <param name="Element">The element to removve painted faces</param>
        /// <returns name="Element">The modified element</returns>
        public static RevitElem RemovePaintElement(RevitElem Element)
        {
            RevitDoc document = Element.Document;
            RevitElemId elementId = Element.Id;

            RevitDB.Options op = new RevitDB.Options();
            RevitDB.GeometryElement geoElem = Element.get_Geometry(op);

            IList<RevitDB.Face> faces = new List<RevitDB.Face>();

            foreach (RevitDB.GeometryObject geo in geoElem)
            {
                if (geo is RevitDB.Solid solid)
                {
                    foreach (RevitDB.Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }

                }
                else if (geo is RevitDB.Face face)
                {
                    faces.Add(face);
                }

                Action<RevitElemId, IList<RevitDB.Face>> RemovePaintedFaces = (RevitElemId elemId, IList<RevitDB.Face> faceList) =>
                {
                    foreach (RevitDB.Face face in faceList)
                    {
                        document.RemovePaint(elemId, face);
                    }
                };

                RemovePaintedFaces(elementId, faces);
            }

            return Element;
        }

        #region Helper Functions

        private static void _transferParameters(RevitElem SourceElement, RevitElem DestinationElement)
        {
            RevitDB.ParameterSet sourceParameters = SourceElement.Parameters;

            foreach (RevitDB.Parameter sourceParam in sourceParameters)
            {
                if (sourceParam.IsReadOnly == false)
                {
                    RevitDB.Definition def = sourceParam.Definition;
                    RevitDB.Parameter destinationParam = DestinationElement.get_Parameter(def);

                    RevitDB.StorageType st = sourceParam.StorageType;
                    switch (st)
                    {
                        case RevitDB.StorageType.Double:
                            destinationParam.Set(sourceParam.AsDouble());
                            break;
                        case RevitDB.StorageType.ElementId:
                            destinationParam.Set(sourceParam.AsElementId());
                            break;
                        case RevitDB.StorageType.Integer:
                            destinationParam.Set(sourceParam.AsInteger());
                            break;
                        case RevitDB.StorageType.String:
                            destinationParam.Set(sourceParam.AsString());
                            break;
                    }
                }
            }
        }
        #endregion

    }
}
