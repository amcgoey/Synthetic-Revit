using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using NUnit.Framework;
using Synthetic.Platform.Revit;
using Synthetic.Platform.Settings;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Platform.Settings
{
    [TestFixture]
    [RevitAdapterTest]
    public class LegacySettingsMigrationRevitTests : RevitTemplateRollbackFixture
    {
        private static readonly Guid LegacyStorageIdGuid = new Guid("0b5fd0ef-3558-47d2-81b5-d1918952d2b1");
        private static readonly Guid LegacyConfigSettingsGuid = new Guid("5855a6d8-e694-46e5-bd71-c227a305143b");
        private static readonly Guid ThirdPartyVendorSchemaGuid = new Guid("c4e6b932-7a1d-4d6e-9f12-8b3a5c7e9014");

        private bool _previousSuppressDiskFallback;

        protected override void OnTestSetUp()
        {
            _previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
            SettingsManager.ClearCache();
        }

        protected override void OnTestTearDown()
        {
            SettingsManager.ClearCache();
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = _previousSuppressDiskFallback;
        }

        [Test]
        public void PurgeLegacySchemas_AfterCommittedSeed_PurgesAllLegacyEntities_AndPreservesModernAndThirdPartyDataStorage()
        {
            WithRolledBackDocument(doc =>
            {
                Schema legacyStorageIdSchema = GetOrCreateSimpleSchema(
                    LegacyStorageIdGuid,
                    "SyntheticStorageId",
                    "Id",
                    typeof(Guid));
                Schema legacyConfigSchema = GetOrCreateSimpleSchema(
                    LegacyConfigSettingsGuid,
                    "SyntheticConfig",
                    "Settings",
                    typeof(string));
                Schema thirdPartySchema = GetOrCreateSimpleSchema(
                    ThirdPartyVendorSchemaGuid,
                    "ThirdPartyVendorTestStorage",
                    "VendorPayload",
                    typeof(string));
                Schema modernSettingsSchema = SyntheticSettingsJsonSchema.GetSchema();

                ElementId legacyStorageIdElementId = ElementId.InvalidElementId;
                ElementId legacyConfigElementId = ElementId.InvalidElementId;
                ElementId legacyMaterialsElementId = ElementId.InvalidElementId;
                ElementId modernStorageElementId = ElementId.InvalidElementId;
                ElementId thirdPartyVendorElementId = ElementId.InvalidElementId;
                ElementId thirdPartyPlainElementId = ElementId.InvalidElementId;

                const string thirdPartyPayload = "third-party-vendor-payload-v1";
                var modernFileUtility = new FileUtilitySettings
                {
                    ArchiveDirectories = new List<string> { @"C:\Archive\Survive" },
                    AlternatePaths = new Dictionary<string, List<string>>
                    {
                        { @"C:\Primary", new List<string> { @"C:\Mirror" } }
                    }
                };

                doc.ExecuteTransaction("Seed Legacy Modern and ThirdParty DataStorage", FailurePipelines.ObserveOnly(), () =>
                {
                    DataStorage legacyIdStorage = DataStorage.Create(doc);
                    legacyIdStorage.Name = "Legacy_StorageId_Host";
                    Entity idEntity = new Entity(legacyStorageIdSchema);
                    idEntity.Set("Id", Guid.NewGuid());
                    legacyIdStorage.SetEntity(idEntity);
                    legacyStorageIdElementId = legacyIdStorage.Id;

                    DataStorage legacyConfigStorage = DataStorage.Create(doc);
                    legacyConfigStorage.Name = "Legacy_Config_Host";
                    Entity configEntity = new Entity(legacyConfigSchema);
                    configEntity.Set("Settings", "{\"Worksets\":{\"WorksetFile\":\"Legacy.xlsx\"}}");
                    legacyConfigStorage.SetEntity(configEntity);
                    legacyConfigElementId = legacyConfigStorage.Id;

                    DataStorage legacyMaterialsStorage = DataStorage.Create(doc);
                    legacyMaterialsStorage.Name = "Synthetic_Materials";
                    legacyMaterialsElementId = legacyMaterialsStorage.Id;

                    SettingsManager.Save(doc, modernFileUtility);
                    modernStorageElementId = new FilteredElementCollector(doc)
                        .OfClass(typeof(DataStorage))
                        .Cast<DataStorage>()
                        .Single(ds => ds.Name.Equals("Synthetic_" + FileUtilitySettings.Name, StringComparison.OrdinalIgnoreCase))
                        .Id;

                    DataStorage thirdPartyVendorStorage = DataStorage.Create(doc);
                    thirdPartyVendorStorage.Name = "ThirdParty_Vendor_Storage";
                    Entity vendorEntity = new Entity(thirdPartySchema);
                    vendorEntity.Set("VendorPayload", thirdPartyPayload);
                    thirdPartyVendorStorage.SetEntity(vendorEntity);
                    thirdPartyVendorElementId = thirdPartyVendorStorage.Id;

                    DataStorage thirdPartyPlainStorage = DataStorage.Create(doc);
                    thirdPartyPlainStorage.Name = "ThirdParty_Plain_Storage";
                    thirdPartyPlainElementId = thirdPartyPlainStorage.Id;
                });

                Assert.IsFalse(doc.IsModifiable, "Seeding transaction must be committed before calling PurgeLegacySchemas(doc).");

                Assert.IsNotNull(doc.GetElement(legacyStorageIdElementId));
                Assert.IsNotNull(doc.GetElement(legacyConfigElementId));
                Assert.IsNotNull(doc.GetElement(legacyMaterialsElementId));
                Assert.IsNotNull(doc.GetElement(modernStorageElementId));
                Assert.IsNotNull(doc.GetElement(thirdPartyVendorElementId));
                Assert.IsNotNull(doc.GetElement(thirdPartyPlainElementId));

                LegacySettingsMigration.PurgeLegacySchemas(doc);

                Assert.IsFalse(doc.IsModifiable, "Document must not remain modifiable after PurgeLegacySchemas(doc).");

                Assert.IsNull(doc.GetElement(legacyStorageIdElementId), "Legacy SyntheticStorageId DataStorage must be purged.");
                Assert.IsNull(doc.GetElement(legacyConfigElementId), "Legacy SyntheticConfig DataStorage must be purged.");
                Assert.IsNull(doc.GetElement(legacyMaterialsElementId), "Legacy Synthetic_Materials DataStorage must be purged.");

                AssertZeroRemainingLegacyDataStorages(doc, legacyStorageIdSchema, legacyConfigSchema);

                DataStorage? survivingModern = doc.GetElement(modernStorageElementId) as DataStorage;
                Assert.IsNotNull(survivingModern, "Modern SyntheticSettingsJsonSchema DataStorage must survive purge.");
                Entity survivingModernEntity = survivingModern!.GetEntity(modernSettingsSchema);
                Assert.IsTrue(survivingModernEntity.IsValid(), "Modern SyntheticSettingsJsonSchema entity must remain valid.");
                Assert.AreEqual(FileUtilitySettings.Name, survivingModernEntity.Get<string>(SyntheticSettingsJsonSchema.ModuleKeyFieldName));

                SettingsManager.ClearCache(doc);
                FileUtilitySettings reloadedFileUtility = SettingsManager.Get<FileUtilitySettings>(doc);
                CollectionAssert.AreEqual(new[] { @"C:\Archive\Survive" }, reloadedFileUtility.ArchiveDirectories);
                Assert.IsTrue(reloadedFileUtility.AlternatePaths.ContainsKey(@"C:\Primary"));
                CollectionAssert.AreEqual(new[] { @"C:\Mirror" }, reloadedFileUtility.AlternatePaths[@"C:\Primary"]);

                DataStorage? survivingThirdParty = doc.GetElement(thirdPartyVendorElementId) as DataStorage;
                Assert.IsNotNull(survivingThirdParty, "Unrelated third-party DataStorage with schema must survive purge.");
                Entity survivingThirdPartyEntity = survivingThirdParty!.GetEntity(thirdPartySchema);
                Assert.IsTrue(survivingThirdPartyEntity.IsValid(), "Third-party schema entity must remain valid.");
                Assert.AreEqual(thirdPartyPayload, survivingThirdPartyEntity.Get<string>("VendorPayload"));

                Assert.IsNotNull(doc.GetElement(thirdPartyPlainElementId), "Unrelated plain DataStorage must survive purge.");
            });
        }

        [Test]
        public void PurgeLegacySchemas_CombinedLegacyEntitiesAndCaseInsensitiveName_PurgesCleanlyAndIsIdempotent()
        {
            WithRolledBackDocument(doc =>
            {
                Schema legacyStorageIdSchema = GetOrCreateSimpleSchema(
                    LegacyStorageIdGuid,
                    "SyntheticStorageId",
                    "Id",
                    typeof(Guid));
                Schema legacyConfigSchema = GetOrCreateSimpleSchema(
                    LegacyConfigSettingsGuid,
                    "SyntheticConfig",
                    "Settings",
                    typeof(string));
                Schema thirdPartySchema = GetOrCreateSimpleSchema(
                    ThirdPartyVendorSchemaGuid,
                    "ThirdPartyVendorTestStorage",
                    "VendorPayload",
                    typeof(string));

                ElementId combinedLegacyId = ElementId.InvalidElementId;
                ElementId lowercaseMaterialsId = ElementId.InvalidElementId;
                ElementId modernSyncId = ElementId.InvalidElementId;
                ElementId thirdPartyId = ElementId.InvalidElementId;

                doc.ExecuteTransaction("Seed Combined Legacy Entities", FailurePipelines.ObserveOnly(), () =>
                {
                    DataStorage combinedLegacy = DataStorage.Create(doc);
                    combinedLegacy.Name = "Legacy_Combined_Host";
                    Entity idEntity = new Entity(legacyStorageIdSchema);
                    idEntity.Set("Id", Guid.NewGuid());
                    combinedLegacy.SetEntity(idEntity);
                    Entity configEntity = new Entity(legacyConfigSchema);
                    configEntity.Set("Settings", "{\"Combined\":true}");
                    combinedLegacy.SetEntity(configEntity);
                    combinedLegacyId = combinedLegacy.Id;

                    DataStorage lowercaseMaterials = DataStorage.Create(doc);
                    lowercaseMaterials.Name = "synthetic_materials";
                    lowercaseMaterialsId = lowercaseMaterials.Id;

                    SettingsManager.Save(doc, new SyncSettings { LinkedFilePath = @"C:\Firm\SurvivingSync.json" });
                    modernSyncId = new FilteredElementCollector(doc)
                        .OfClass(typeof(DataStorage))
                        .Cast<DataStorage>()
                        .Single(ds => ds.Name.Equals("Synthetic_" + SyncSettings.Name, StringComparison.OrdinalIgnoreCase))
                        .Id;

                    DataStorage thirdParty = DataStorage.Create(doc);
                    thirdParty.Name = "ThirdParty_Other_Storage";
                    Entity vendorEntity = new Entity(thirdPartySchema);
                    vendorEntity.Set("VendorPayload", "keep-intact");
                    thirdParty.SetEntity(vendorEntity);
                    thirdPartyId = thirdParty.Id;
                });

                Assert.IsFalse(doc.IsModifiable, "Seeding transaction must be committed before calling PurgeLegacySchemas(doc).");

                LegacySettingsMigration.PurgeLegacySchemas(doc);
                LegacySettingsMigration.PurgeLegacySchemas(doc);

                Assert.IsFalse(doc.IsModifiable);
                Assert.IsNull(doc.GetElement(combinedLegacyId), "Combined legacy DataStorage must be purged without double-delete failure.");
                Assert.IsNull(doc.GetElement(lowercaseMaterialsId), "Case-insensitive synthetic_materials DataStorage must be purged.");
                AssertZeroRemainingLegacyDataStorages(doc, legacyStorageIdSchema, legacyConfigSchema);

                Assert.IsNotNull(doc.GetElement(modernSyncId), "Modern SyncSettings DataStorage must survive repeated purge calls.");
                SettingsManager.ClearCache(doc);
                SyncSettings reloadedSync = SettingsManager.Get<SyncSettings>(doc);
                Assert.AreEqual(@"C:\Firm\SurvivingSync.json", reloadedSync.LinkedFilePath);

                DataStorage? survivingThirdParty = doc.GetElement(thirdPartyId) as DataStorage;
                Assert.IsNotNull(survivingThirdParty);
                Assert.AreEqual("keep-intact", survivingThirdParty!.GetEntity(thirdPartySchema).Get<string>("VendorPayload"));
            });
        }

        [Test]
        public void PurgeLegacySchemas_WhenDocumentIsNull_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => LegacySettingsMigration.PurgeLegacySchemas(null!));
        }

        private static Schema GetOrCreateSimpleSchema(Guid schemaGuid, string schemaName, string fieldName, Type fieldType)
        {
            Schema? existing = Schema.Lookup(schemaGuid);
            if (existing != null)
            {
                return existing;
            }

            SchemaBuilder builder = new SchemaBuilder(schemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName(schemaName);
            builder.AddSimpleField(fieldName, fieldType);
            return builder.Finish();
        }

        private static void AssertZeroRemainingLegacyDataStorages(
            Document doc,
            Schema legacyStorageIdSchema,
            Schema legacyConfigSchema)
        {
            List<DataStorage> remainingStorages = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();

            foreach (DataStorage storage in remainingStorages)
            {
                Assert.IsFalse(
                    storage.Name.Equals("Synthetic_Materials", StringComparison.OrdinalIgnoreCase),
                    $"Unexpected remaining 'Synthetic_Materials' DataStorage (Id={storage.Id}).");

                Entity idEntity = storage.GetEntity(legacyStorageIdSchema);
                Assert.IsFalse(
                    idEntity != null && idEntity.IsValid(),
                    $"Unexpected remaining SyntheticStorageId entity on DataStorage '{storage.Name}' (Id={storage.Id}).");

                Entity configEntity = storage.GetEntity(legacyConfigSchema);
                Assert.IsFalse(
                    configEntity != null && configEntity.IsValid(),
                    $"Unexpected remaining SyntheticConfig entity on DataStorage '{storage.Name}' (Id={storage.Id}).");
            }
        }
    }
}
