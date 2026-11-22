using System;
using System.Collections.Generic;
using System.Linq;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.RevitDOM.Operations.Standards
{
    public static class StandardsMergeUtility
    {
        /// <summary>
        /// Merges a list of existing standard elements with new standard elements, identifying duplicates strictly by Class + Name.
        /// </summary>
        public static List<ElementModel> Merge(List<ElementModel> existingElements, List<ElementModel> newElements, bool overwriteDuplicates)
        {
            var resultDict = new Dictionary<string, ElementModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in existingElements)
            {
                if (el != null && !string.IsNullOrEmpty(el.Class) && !string.IsNullOrEmpty(el.Name))
                {
                    string key = $"{el.Class}:{el.Name}";
                    resultDict[key] = el;
                }
            }

            foreach (var el in newElements)
            {
                if (el != null && !string.IsNullOrEmpty(el.Class) && !string.IsNullOrEmpty(el.Name))
                {
                    string key = $"{el.Class}:{el.Name}";
                    if (resultDict.ContainsKey(key))
                    {
                        if (overwriteDuplicates)
                        {
                            resultDict[key] = el;
                        }
                    }
                    else
                    {
                        resultDict[key] = el;
                    }
                }
            }

            return resultDict.Values.ToList();
        }
    }
}
