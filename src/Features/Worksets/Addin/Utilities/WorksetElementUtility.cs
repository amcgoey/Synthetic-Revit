using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Platform.Revit;

namespace Synthetic.Features.Worksets.Utilities
{
    /// <summary>
    /// Workset-specific element modification utilities.
    /// </summary>
    public static class WorksetElementUtility
    {
        /// <summary>
        /// Given a list of elements, sets their worksets to the given workset.
        /// </summary>
        /// <param name="elements">A list of elements to change.</param>
        /// <param name="workset">A Revit Workset object.</param>
        /// <param name="document">A Revit Document.</param>
        /// <returns>The list of elements for chaining.</returns>
        public static List<Element> SetWorkset(List<Element> elements, Workset workset, Document document)
        {
            string transactionName = "Move " + elements.Count + " elements to workset " + workset.Name;

            document.ExecuteTransaction(transactionName, FailurePipelines.ObserveOnly(), () =>
            {
                foreach (Element element in elements)
                {
                    if (element.IsModifiable)
                    {
                        Parameter worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (worksetParam != null
                            && worksetParam.AsInteger() != workset.Id.IntegerValue
                            && !worksetParam.IsReadOnly)
                        {
                            worksetParam.Set(workset.Id.IntegerValue);
                        }
                    }
                }
            });

            return elements;
        }
    }
}
