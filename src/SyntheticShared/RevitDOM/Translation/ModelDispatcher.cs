using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Central dispatcher responsible for explicitly registering and routing ObjectModel types and Revit Element types
    /// to their corresponding IModelTranslator implementations.
    /// Supports One-to-Many routing configurations (e.g., mapping multiple Revit types to a single translator).
    /// </summary>
    internal class ModelDispatcher
    {
        private readonly IIdentityService _identityService;
        private readonly Dictionary<Type, IModelTranslator> _modelTypeToTranslator = new Dictionary<Type, IModelTranslator>();
        private readonly Dictionary<Type, IModelTranslator> _revitTypeToTranslator = new Dictionary<Type, IModelTranslator>();
        private readonly Dictionary<Type, Type> _revitTypeToModelType = new Dictionary<Type, Type>();
        private readonly HashSet<Type> _ignoredTypes = new HashSet<Type>();
        private readonly HashSet<Type> _pendingTypes = new HashSet<Type>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelDispatcher"/> class.
        /// </summary>
        /// <param name="identityService">The identity service to use for element resolution and extraction.</param>
        public ModelDispatcher(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        /// <summary>
        /// Registers a translator instance mapping a specific ObjectModel type and multiple Revit types.
        /// </summary>
        /// <typeparam name="TModel">The ObjectModel subclass.</typeparam>
        /// <typeparam name="TTranslator">The IModelTranslator implementation.</typeparam>
        /// <param name="translator">The translator instance.</param>
        /// <param name="revitTypes">One or more Revit Element types to bind to this translator.</param>
        public void Register<TModel, TTranslator>(TTranslator translator, params Type[] revitTypes)
            where TModel : ObjectModel
            where TTranslator : IModelTranslator
        {
            if (translator == null) throw new ArgumentNullException(nameof(translator));

            _modelTypeToTranslator[typeof(TModel)] = translator;
            foreach (var revitType in revitTypes)
            {
                if (revitType != null)
                {
                    _revitTypeToTranslator[revitType] = translator;
                    _revitTypeToModelType[revitType] = typeof(TModel);
                }
            }
        }

        /// <summary>
        /// Registers a translator by instantiating it with a parameterless constructor.
        /// </summary>
        /// <typeparam name="TModel">The ObjectModel subclass.</typeparam>
        /// <typeparam name="TTranslator">The IModelTranslator implementation type.</typeparam>
        /// <param name="revitTypes">One or more Revit Element types to bind to this translator.</param>
        public void Register<TModel, TTranslator>(params Type[] revitTypes)
            where TModel : ObjectModel
            where TTranslator : IModelTranslator, new()
        {
            Register<TModel, TTranslator>(new TTranslator(), revitTypes);
        }

        /// <summary>
        /// Registers one or more Revit Element types to be explicitly ignored.
        /// Encountering these types during extraction will return null silently.
        /// </summary>
        public void RegisterIgnored(params Type[] revitTypes)
        {
            if (revitTypes == null) throw new ArgumentNullException(nameof(revitTypes));
            foreach (var type in revitTypes)
            {
                if (type != null)
                {
                    _ignoredTypes.Add(type);
                }
            }
        }

        /// <summary>
        /// Registers one or more Revit Element types as pending future support.
        /// Encountering these types during extraction will log a warning and return null.
        /// </summary>
        public void RegisterPending(params Type[] revitTypes)
        {
            if (revitTypes == null) throw new ArgumentNullException(nameof(revitTypes));
            foreach (var type in revitTypes)
            {
                if (type != null)
                {
                    _pendingTypes.Add(type);
                }
            }
        }

        /// <summary>
        /// Checks if a Revit Element type is explicitly ignored.
        /// </summary>
        public bool IsIgnored(Type revitType)
        {
            return _ignoredTypes.Contains(revitType);
        }

        /// <summary>
        /// Checks if a Revit Element type is explicitly marked as pending.
        /// </summary>
        public bool IsPending(Type revitType)
        {
            return _pendingTypes.Contains(revitType);
        }

        /// <summary>
        /// Extracts a native Revit Element into its corresponding ObjectModel.
        /// </summary>
        public ObjectModel? Extract(Element elem, bool isTemplate)
        {
            if (elem == null) return null;

            Type revitType = elem.GetType();

            if (_ignoredTypes.Contains(revitType))
            {
                return null;
            }

            if (_pendingTypes.Contains(revitType))
            {
                SerializationResultModel.LogWarning($"Type '{revitType.FullName}' pending future support.");
                return null;
            }

            if (_revitTypeToTranslator.TryGetValue(revitType, out var translator) &&
                _revitTypeToModelType.TryGetValue(revitType, out var modelType))
            {
                try
                {
                    var model = (ObjectModel)Activator.CreateInstance(modelType)!;
                    if (model is ElementModel elementModel)
                    {
                        PopulateElementModelCommon(elementModel, elem, isTemplate);
                    }

                    translator.ExtractSpecifics(elem, model, elem.Document);
                    return model;
                }
                catch (Exception ex)
                {
                    SerializationResultModel.LogWarning($"Type '{revitType.FullName}' extraction failed: {ex.Message}");
                    return null;
                }
            }

            try
            {
                if (elem is ElementType)
                {
                    var genericModel = new ElementTypeModel();
                    PopulateElementModelCommon(genericModel, elem, isTemplate);
                    SerializationResultModel.LogWarning($"Type '{revitType.FullName}' unsupported, falling back to generic ElementTypeModel.");
                    return genericModel;
                }
                else
                {
                    var genericModel = new ElementModel();
                    PopulateElementModelCommon(genericModel, elem, isTemplate);
                    SerializationResultModel.LogWarning($"Type '{revitType.FullName}' unsupported, falling back to generic ElementModel.");
                    return genericModel;
                }
            }
            catch (Exception ex)
            {
                SerializationResultModel.LogWarning($"Type '{revitType.FullName}' currently unsupported. Extraction failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts a Category into its corresponding CategoryModel.
        /// </summary>
        public ObjectModel? Extract(Category cat, Document doc, bool isTemplate)
        {
            if (cat == null) return null;

            if (_revitTypeToTranslator.TryGetValue(typeof(Category), out var translator) &&
                translator is IModelTranslator<Category, CategoryModel> catTranslator)
            {
                try
                {
                    var model = new CategoryModel { IsTemplate = isTemplate };
                    catTranslator.ExtractSpecifics(cat, model, doc);
                    return model;
                }
                catch (Exception ex)
                {
                    SerializationResultModel.LogWarning($"Category '{cat.Name}' extraction failed: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private void PopulateElementModelCommon(ElementModel model, Element elem, bool isTemplate)
        {
            model.Element = elem;
            model.Document = elem.Document;
            model.ElementId = _identityService.ToModel(elem.Id, elem.Document, isTemplate);
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

            ParameterEngine.ExtractParameters(elem, model, isTemplate, _identityService);
        }

        /// <summary>
        /// Resolves the registered IModelTranslator for a given ObjectModel type.
        /// </summary>
        /// <param name="modelType">The type of the ObjectModel subclass.</param>
        /// <returns>The resolved translator instance, or null if not registered.</returns>
        public IModelTranslator? GetTranslatorByModelType(Type modelType)
        {
            if (modelType == null) throw new ArgumentNullException(nameof(modelType));

            _modelTypeToTranslator.TryGetValue(modelType, out var translator);
            return translator;
        }

        /// <summary>
        /// Resolves the registered IModelTranslator for a given native Revit Element type.
        /// </summary>
        /// <param name="revitType">The type of the Revit Element subclass.</param>
        /// <returns>The resolved translator instance, or null if not registered.</returns>
        public IModelTranslator? GetTranslatorByRevitType(Type revitType)
        {
            if (revitType == null) throw new ArgumentNullException(nameof(revitType));

            _revitTypeToTranslator.TryGetValue(revitType, out var translator);
            return translator;
        }
    }
}
