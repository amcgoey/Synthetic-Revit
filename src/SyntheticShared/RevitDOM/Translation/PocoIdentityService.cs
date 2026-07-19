using System;
using System.Collections.Generic;
using System.Linq;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Implements IPocoIdentityService by executing a 5-step fallback identity resolution strategy
    /// over pure in-memory ObjectModel collections.
    /// </summary>
    public class PocoIdentityService : IPocoIdentityService
    {
        /// <inheritdoc />
        public ElementModel? ResolveElement(ElementIdModel model, IEnumerable<ElementModel> pool)
        {
            if (model == null || pool == null) return null;

            // Step 1: UniqueId search
            if (!string.IsNullOrEmpty(model.UniqueId))
            {
                var match = pool.FirstOrDefault(p => string.Equals(p.UniqueId, model.UniqueId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    // Step 3: Type Guard verification
                    if (VerifyType(match, model.Class))
                    {
                        return match;
                    }
                }
            }

            // Step 2: Id (integer) search
            if (model.Id != 0)
            {
                var match = pool.FirstOrDefault(p => p.Id == model.Id);
                if (match != null)
                {
                    // Step 3: Type Guard verification
                    if (VerifyType(match, model.Class))
                    {
                        return match;
                    }
                }
            }

            // Step 4: Name match (within expected Revit Class)
            if (!string.IsNullOrEmpty(model.Name) && !string.IsNullOrEmpty(model.Class))
            {
                var match = pool.FirstOrDefault(p => 
                    string.Equals(p.Name, model.Name, StringComparison.OrdinalIgnoreCase) && 
                    VerifyType(p, model.Class));
                if (match != null) return match;
            }

            // Step 5: Alias match (within expected Revit Class)
            if (model.Aliases != null && model.Aliases.Count > 0 && !string.IsNullOrEmpty(model.Class))
            {
                foreach (var alias in model.Aliases)
                {
                    if (string.IsNullOrEmpty(alias)) continue;

                    // 5a. Check if candidate's Name matches the alias
                    var match = pool.FirstOrDefault(p => 
                        string.Equals(p.Name, alias, StringComparison.OrdinalIgnoreCase) && 
                        VerifyType(p, model.Class));
                    if (match != null) return match;

                    // 5b. Check if candidate's Aliases contain the alias
                    var aliasMatch = pool.FirstOrDefault(p => 
                        p.Aliases != null && 
                        p.Aliases.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)) && 
                        VerifyType(p, model.Class));
                    if (aliasMatch != null) return aliasMatch;
                }
            }

            // Fallback alias match: check if the candidate POCO's aliases contain the target model's Name
            if (!string.IsNullOrEmpty(model.Name) && !string.IsNullOrEmpty(model.Class))
            {
                var match = pool.FirstOrDefault(p => 
                    p.Aliases != null && 
                    p.Aliases.Any(a => string.Equals(a, model.Name, StringComparison.OrdinalIgnoreCase)) && 
                    VerifyType(p, model.Class));
                if (match != null) return match;
            }

            return null;
        }

        /// <inheritdoc />
        public IEnumerable<ElementModel> ResolveElements(IEnumerable<ElementIdModel> models, IEnumerable<ElementModel> pool)
        {
            if (models == null || pool == null) yield break;

            foreach (var model in models)
            {
                var resolved = ResolveElement(model, pool);
                if (resolved != null)
                {
                    yield return resolved;
                }
            }
        }

        private bool VerifyType(ElementModel poco, string expectedClass)
        {
            if (string.IsNullOrEmpty(expectedClass)) return true;
            if (string.IsNullOrEmpty(poco.Class)) return true;

            // Strict class matching: exact string comparison of the class name, ignoring case.
            return string.Equals(poco.Class, expectedClass, StringComparison.OrdinalIgnoreCase);
        }
    }
}
