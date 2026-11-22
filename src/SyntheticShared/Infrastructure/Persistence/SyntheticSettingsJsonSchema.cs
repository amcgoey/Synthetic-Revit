using System;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
namespace Synthetic.Infrastructure.Persistence{
    /// <summary>
    /// Schema definition for storing settings modules as serialized JSON string payloads inside Revit extensible storage.
    /// </summary>
    public static class SyntheticSettingsJsonSchema
    {
        private static readonly Guid SchemaGuid = new Guid("8a07153a-c85c-444f-9e79-50c117d91e60");
        /// <summary>
        /// The name of the settings schema.
        /// </summary>
        public const string SchemaName = "SyntheticSettingsJson";

        /// <summary>
        /// Description of the settings schema.
        /// </summary>
        public const string SchemaDocumentation = "Universal JSON payload storage for Synthetic settings.";

        /// <summary>
        /// The name of the ModuleKey field.
        /// </summary>
        public const string ModuleKeyFieldName = "ModuleKey";

        /// <summary>
        /// The name of the JsonData field.
        /// </summary>
        public const string JsonDataFieldName = "JsonData";

        /// <summary>
        /// Retrieves the existing settings schema or constructs and registers it if missing.
        /// </summary>
        /// <returns>The registered Autodesk.Revit.DB.ExtensibleStorage.Schema.</returns>
        public static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder schemaBuilder = new SchemaBuilder(SchemaGuid);
            schemaBuilder.SetReadAccessLevel(AccessLevel.Public);
            schemaBuilder.SetWriteAccessLevel(AccessLevel.Public);
            schemaBuilder.SetSchemaName(SchemaName);
            schemaBuilder.SetDocumentation(SchemaDocumentation);

            schemaBuilder.AddSimpleField(ModuleKeyFieldName, typeof(string));
            schemaBuilder.AddSimpleField(JsonDataFieldName, typeof(string));

            return schemaBuilder.Finish();
        }
    }
}
