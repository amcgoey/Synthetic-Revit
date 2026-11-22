using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Pure C# reflection utility to recursively scan an ObjectModel (POCO)
    /// and yield all nested ElementIdModel references.
    /// </summary>
    public static class RevitDomDependencyScanner
    {
        /// <summary>
        /// Scans an ObjectModel for all ElementIdModel references.
        /// </summary>
        /// <param name="rootModel">The root ObjectModel to scan.</param>
        /// <returns>A collection of all ElementIdModel references found.</returns>
        public static IEnumerable<ElementIdModel> Scan(ObjectModel rootModel)
        {
            var visited = new HashSet<object>();
            var results = new List<ElementIdModel>();
            ScanInternal(rootModel, visited, results);
            return results;
        }

        private static void ScanInternal(object? obj, HashSet<object> visited, List<ElementIdModel> results)
        {
            if (obj == null) return;
            if (obj is string || obj.GetType().IsValueType) return;
            if (!visited.Add(obj)) return;

            if (obj is ElementIdModel idModel)
            {
                results.Add(idModel);
            }

            if (obj is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    ScanInternal(item, visited, results);
                }
            }
            else
            {
                var ns = obj.GetType().Namespace;
                if (ns != null && ns.StartsWith("Synthetic", StringComparison.OrdinalIgnoreCase))
                {
                    var type = obj.GetType();
                    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var prop in properties)
                    {
                        if (prop.GetIndexParameters().Length > 0) continue;

                        if (prop.PropertyType == typeof(string) || prop.PropertyType.IsValueType)
                        {
                            continue;
                        }

                        try
                        {
                            var val = prop.GetValue(obj);
                            if (val != null)
                            {
                                ScanInternal(val, visited, results);
                            }
                        }
                        catch
                        {
                            // Ignore properties that throw
                        }
                    }
                }
            }
        }
    }
}
