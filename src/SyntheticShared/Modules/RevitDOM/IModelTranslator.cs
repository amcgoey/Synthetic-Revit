using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Non-generic pipeline interface for translating and mapping properties between Revit Elements and ObjectModels.
    /// Used by the internal Dispatcher to resolve and execute translators dynamically without reflection.
    /// </summary>
    internal interface IModelTranslator
    {
        /// <summary>
        /// Extracts class-specific properties from a Revit element and injects them into the ObjectModel.
        /// </summary>
        /// <param name="revitElement">The native Revit element source (or Category).</param>
        /// <param name="model">The target model state container.</param>
        /// <param name="doc">The active Revit document.</param>
        void ExtractSpecifics(object revitElement, ObjectModel model, Document doc);

        /// <summary>
        /// Injects class-specific properties from an ObjectModel into a Revit element, creating the element if it does not exist.
        /// </summary>
        /// <param name="model">The source model state container.</param>
        /// <param name="revitElement">The target native Revit element (or Category), or null if it needs to be created.</param>
        /// <param name="doc">The active Revit document.</param>
        /// <returns>The created or modified native Revit element (or Category).</returns>
        object? InjectSpecifics(ObjectModel model, object? revitElement, Document doc);
    }

    /// <summary>
    /// Strongly-typed pipeline interface for translating and mapping properties between a specific Revit subtype and ObjectModel subtype.
    /// </summary>
    /// <typeparam name="TRev">The specific Revit subtype (e.g. Element or Category).</typeparam>
    /// <typeparam name="TModel">The specific ObjectModel subtype.</typeparam>
    internal interface IModelTranslator<TRev, TModel> : IModelTranslator
        where TRev : class
        where TModel : ObjectModel
    {
        /// <summary>
        /// Extracts specific properties from a typed Revit element and injects them into a typed model.
        /// </summary>
        /// <param name="revitElement">The native Revit element.</param>
        /// <param name="model">The model state container.</param>
        /// <param name="doc">The active Revit document.</param>
        void ExtractSpecifics(TRev revitElement, TModel model, Document doc);

        /// <summary>
        /// Injects specific properties from a typed model into a typed Revit element, creating it if it does not exist.
        /// </summary>
        /// <param name="model">The model state container.</param>
        /// <param name="revitElement">The native Revit element, or null if it needs to be created.</param>
        /// <param name="doc">The active Revit document.</param>
        /// <returns>The created or modified native Revit element.</returns>
        TRev? InjectSpecifics(TModel model, TRev? revitElement, Document doc);
    }
}
