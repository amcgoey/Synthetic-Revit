using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    /// <summary>
    /// A lightweight, in-memory mock implementation of IIdentityService for headless unit tests.
    /// </summary>
    public class FakeIdentityService : IIdentityService
    {
        private readonly Dictionary<long, ElementIdModel> _idToModel = new Dictionary<long, ElementIdModel>();
        private readonly Dictionary<string, ElementIdModel> _uniqueIdToModel = new Dictionary<string, ElementIdModel>();
        private readonly Dictionary<string, Element> _nameToElement = new Dictionary<string, Element>();
        private readonly Dictionary<string, Element> _uniqueIdToElement = new Dictionary<string, Element>();

        /// <summary>
        /// Stub a mapping for ToModel extraction.
        /// </summary>
        public void SetupMapping(ElementId id, ElementIdModel model)
        {
            long idVal;
#if REVIT2022 || REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif
            _idToModel[idVal] = model;
            if (!string.IsNullOrEmpty(model.UniqueId))
            {
                _uniqueIdToModel[model.UniqueId] = model;
            }
        }

        /// <summary>
        /// Stub an element resolution mapping.
        /// </summary>
        public void SetupElement(string nameOrUniqueId, Element element)
        {
            _nameToElement[nameOrUniqueId] = element;
            if (element != null && !string.IsNullOrEmpty(element.UniqueId))
            {
                _uniqueIdToElement[element.UniqueId] = element;
            }
        }

        public ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false)
        {
            if (id == null) return null;

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif

            if (_idToModel.TryGetValue(idVal, out var model))
            {
                return model;
            }

            return new ElementIdModel
            {
                Id = idVal,
                Name = $"FakeElement_{idVal}",
                Class = "Autodesk.Revit.DB.Element",
                UniqueId = $"fake-uid-{idVal}",
                Category = "Generic",
                IsTemplate = isTemplate
            };
        }

        public ElementId ResolveElementId(ElementIdModel model, Document doc)
        {
            if (model == null) return ElementId.InvalidElementId;

            if (_uniqueIdToModel.TryGetValue(model.UniqueId, out var mappedModel))
            {
#if REVIT2022 || REVIT2023
                return new ElementId((int)mappedModel.Id);
#else
                return new ElementId(mappedModel.Id);
#endif
            }

#if REVIT2022 || REVIT2023
            return new ElementId((int)model.Id);
#else
            return new ElementId(model.Id);
#endif
        }

        public Element ResolveElement(ElementIdModel model, Document doc)
        {
            if (model == null) return null;

            if (!string.IsNullOrEmpty(model.UniqueId) && _uniqueIdToElement.TryGetValue(model.UniqueId, out var elemByUid))
            {
                return elemByUid;
            }

            if (!string.IsNullOrEmpty(model.Name) && _nameToElement.TryGetValue(model.Name, out var elemByName))
            {
                return elemByName;
            }

            return null;
        }

        public IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers)
        {
            var resolvedElements = new List<Element>();
            if (identifiers == null) return resolvedElements;

            foreach (var model in identifiers)
            {
                if (model == null) continue;
                var elem = ResolveElement(model, doc);
                if (elem != null)
                {
                    resolvedElements.Add(elem);
                }
                else
                {
                    SerializationResultModel.LogWarning($"Dependency not found: {model.Name} ({model.Class})");
                }
            }

            return resolvedElements;
        }
    }
}
