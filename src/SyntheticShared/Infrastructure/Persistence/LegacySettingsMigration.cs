using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
namespace Synthetic.Infrastructure.Persistence{
    /// <summary>
    /// Utility class to find and purge legacy settings storage schemas from the Revit document database.
    /// </summary>
    public static class LegacySettingsMigration
    {
        private static readonly Guid LegacyStorageIdGuid = new Guid("0b5fd0ef-3558-47d2-81b5-d1918952d2b1");
        private static readonly Guid LegacyConfigSettingsGuid = new Guid("5855a6d8-e694-46e5-bd71-c227a305143b");

        private static Schema GetLegacyStorageIdSchema()
        {
            Schema schema = Schema.Lookup(LegacyStorageIdGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(LegacyStorageIdGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("SyntheticStorageId");
            builder.AddSimpleField("Id", typeof(Guid));
            return builder.Finish();
        }

        private static Schema GetLegacyConfigSettingsSchema()
        {
            Schema schema = Schema.Lookup(LegacyConfigSettingsGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(LegacyConfigSettingsGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("SyntheticConfig");
            builder.AddSimpleField("Settings", typeof(string));
            return builder.Finish();
        }

        /// <summary>
        /// Scans the document for DataStorage elements matching the legacy GUIDs and purges them.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        public static void PurgeLegacySchemas(Document doc)
        {
            if (doc == null) return;

            try
            {
                Schema legacyStorageIdSchema = GetLegacyStorageIdSchema();
                Schema legacyConfigSchema = GetLegacyConfigSettingsSchema();

                var dataStorages = new FilteredElementCollector(doc)
                    .OfClass(typeof(DataStorage))
                    .Cast<DataStorage>()
                    .ToList();

                var elementsToDelete = new List<ElementId>();

                foreach (var dataStorage in dataStorages)
                {
                    try
                    {
                        if (dataStorage.Name.Equals("Synthetic_Materials", StringComparison.OrdinalIgnoreCase))
                        {
                            elementsToDelete.Add(dataStorage.Id);
                            continue;
                        }

                        Entity idEntity = dataStorage.GetEntity(legacyStorageIdSchema);
                        if (idEntity != null && idEntity.IsValid())
                        {
                            elementsToDelete.Add(dataStorage.Id);
                            continue;
                        }

                        Entity configEntity = dataStorage.GetEntity(legacyConfigSchema);
                        if (configEntity != null && configEntity.IsValid())
                        {
                            elementsToDelete.Add(dataStorage.Id);
                        }
                    }
                    catch (Exception)
                    {
                        // Safely catch extensible storage exceptions
                    }
                }

                if (elementsToDelete.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "Purge Legacy Synthetic Settings"))
                    {
                        trans.Start();
                        foreach (var id in elementsToDelete)
                        {
                            try
                            {
                                doc.Delete(id);
                            }
                            catch (Exception)
                            {
                                // Safely handle element deletion errors
                            }
                        }
                        trans.Commit();
                    }
                }
            }
            catch (Exception)
            {
                // General safety fallback
            }
        }
    }
}
