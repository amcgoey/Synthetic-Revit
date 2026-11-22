using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.RevitDOM.Operations.Standards
{
    /// <summary>
    /// Service responsible for managing the recursive extraction and dependency harvesting of Revit standards.
    /// Uses a Directed Acyclic Graph (DAG) pattern with a visited registry to avoid infinite recursion.
    /// </summary>
    public class StandardsExtractionOrchestrator : IStandardsExtractionOrchestrator
    {
        private readonly IIdentityService _identityService;
        private readonly IStandardSerializationEngine _engine;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsExtractionOrchestrator"/> class.
        /// </summary>
        /// <param name="identityService">The identity service to use for element resolution and mapping.</param>
        /// <param name="engine">The standard serialization engine to use.</param>
        public StandardsExtractionOrchestrator(IIdentityService identityService, IStandardSerializationEngine? engine = null)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
            _engine = engine ?? new StandardSerializationEngine(identityService);
        }

        /// <inheritdoc />
        public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
        {
            return Extract(doc, rootElements, progress, false);
        }

        /// <inheritdoc />
        public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rootElements == null) throw new ArgumentNullException(nameof(rootElements));

            var visitedIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dependencyOrigins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var finalPocos = new List<ObjectModel>();

            var currentBatch = new List<Element>(rootElements);

            while (currentBatch.Count > 0)
            {
                // Extract current batch of elements to POCOs
                var pocos = _engine.ByRevit(currentBatch, doc, isTemplate, progress).ToList();

                // Process extracted POCOs
                foreach (var poco in pocos)
                {
                    if (poco == null) continue;

                    string key = GetIdentityKey(poco);

                    // Mark as visited
                    visitedIdentities.Add(key);

                    // Tag Dependency Origin if this POCO was pulled in as a dependency
                    if (poco is ElementModel elem)
                    {
                        if (dependencyOrigins.TryGetValue(key, out var originName))
                        {
                            elem.DependencyOrigin = originName;
                        }
                    }

                    finalPocos.Add(poco);
                }

                // Scan for the next level of dependencies
                var missingDependencies = new List<ElementIdModel>();

                foreach (var poco in pocos)
                {
                    if (poco == null) continue;

                    // Scan POCO for nested ElementIdModel references
                    var dependencies = RevitDomDependencyScanner.Scan(poco);

                    foreach (var dep in dependencies)
                    {
                        if (dep == null) continue;

                        string depKey = GetIdentityKey(dep);

                        // If we haven't extracted it and haven't already queued it
                        if (!visitedIdentities.Contains(depKey))
                        {
                            if (!dependencyOrigins.ContainsKey(depKey))
                            {
                                missingDependencies.Add(dep);

                                string parentName = string.Empty;
                                if (poco is ElementModel elem)
                                {
                                    parentName = elem.Name;
                                }
                                dependencyOrigins[depKey] = parentName;
                            }
                        }
                    }
                }

                // Fetch native Revit elements for the missing dependencies to process in the next loop iteration
                if (missingDependencies.Count > 0)
                {
                    var nextBatch = _identityService.GetElementsByElementIdModels(doc, missingDependencies);
                    currentBatch = nextBatch.ToList();
                }
                else
                {
                    currentBatch.Clear();
                }
            }

            return finalPocos;
        }

        private string GetIdentityKey(ObjectModel model)
        {
            if (model is ElementModel elem)
            {
                if (!string.IsNullOrEmpty(elem.UniqueId))
                {
                    return elem.UniqueId;
                }
                return $"{elem.Class}_{elem.Name}";
            }
            return model.GetType().FullName ?? string.Empty;
        }

        private string GetIdentityKey(ElementIdModel model)
        {
            if (!string.IsNullOrEmpty(model.UniqueId))
            {
                return model.UniqueId;
            }
            return $"{model.Class}_{model.Name}";
        }
    }
}
