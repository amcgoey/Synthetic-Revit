using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Settings;
using Synthetic.Platform.UI;
using Synthetic.Modules.StandardsManagement.ViewModels;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ViewModelSerializationTests
    {
        [SetUp]
        public void SetUp()
        {
            ProgressCoordinator.SuppressUI = true;
        }
        [Test]
        public void ViewSheetModel_Serialization_OmitsGraphicalPropertiesWhenNull()
        {
            // Arrange
            var sheetModel = new ViewSheetModel
            {
                Name = "Sheet_Test",
                Class = "Autodesk.Revit.DB.ViewSheet",
                Id = 12345
            };

            // Act
            var json = Json.Encode(sheetModel);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(json.Contains("\"Scale\""), "JSON should not contain Scale.");
            Assert.IsFalse(json.Contains("\"DetailLevel\""), "JSON should not contain DetailLevel.");
            Assert.IsFalse(json.Contains("\"DisplayStyle\""), "JSON should not contain DisplayStyle.");
            Assert.IsFalse(json.Contains("\"SunlightIntensity\""), "JSON should not contain SunlightIntensity.");
            Assert.IsFalse(json.Contains("\"ShadowIntensity\""), "JSON should not contain ShadowIntensity.");
            Assert.IsFalse(json.Contains("\"CropBoxActive\""), "JSON should not contain CropBoxActive.");
        }

        [Test]
        public void ViewScheduleModel_Serialization_OmitsGraphicalPropertiesWhenNull()
        {
            // Arrange
            var scheduleModel = new ViewScheduleModel
            {
                Name = "Schedule_Test",
                Class = "Autodesk.Revit.DB.ViewSchedule",
                Id = 67890
            };

            // Act
            var json = Json.Encode(scheduleModel);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(json.Contains("\"Scale\""), "JSON should not contain Scale.");
            Assert.IsFalse(json.Contains("\"DetailLevel\""), "JSON should not contain DetailLevel.");
            Assert.IsFalse(json.Contains("\"DisplayStyle\""), "JSON should not contain DisplayStyle.");
            Assert.IsFalse(json.Contains("\"SunlightIntensity\""), "JSON should not contain SunlightIntensity.");
            Assert.IsFalse(json.Contains("\"ShadowIntensity\""), "JSON should not contain ShadowIntensity.");
            Assert.IsFalse(json.Contains("\"CropBoxActive\""), "JSON should not contain CropBoxActive.");
        }

        [Test]
        public void StandardSerializationEngine_FromJson_RoutesModelTextAndSpotDimensionCorrectly()
        {
            // Arrange
            var json = @"{
                ""ModelTextTypes"": {
                    ""TestModelText"": {
                        ""Class"": ""Autodesk.Revit.DB.ModelTextType"",
                        ""Name"": ""TestModelText"",
                        ""Id"": 1010
                    }
                },
                ""SpotDimensionTypes"": {
                    ""TestSpotDimension"": {
                        ""Class"": ""Autodesk.Revit.DB.SpotDimensionType"",
                        ""Name"": ""TestSpotDimension"",
                        ""Id"": 2020
                    }
                }
            }";

            // Act
            var flatList = System.Linq.Enumerable.ToList(StandardSerializationEngine.FromJson(json));

            // Assert
            Assert.AreEqual(2, flatList.Count, "Flat list should contain exactly 2 element models.");
            
            var modelText = flatList.Find(x => x.Class == "Autodesk.Revit.DB.ModelTextType");
            var spotDim = flatList.Find(x => x.Class == "Autodesk.Revit.DB.SpotDimensionType");

            Assert.IsNotNull(modelText, "ModelTextType element model should be found in flat list.");
            Assert.IsNotNull(spotDim, "SpotDimensionType element model should be found in flat list.");
            Assert.AreEqual("TestModelText", modelText!.Name);
            Assert.AreEqual("TestSpotDimension", spotDim!.Name);
        }

        [Test]
        public void StandardSerializationEngine_ToJson_SortsModelTextAndSpotDimensionCorrectly()
        {
            // Arrange
            var modelText = new ElementTypeModel
            {
                Class = "Autodesk.Revit.DB.ModelTextType",
                Name = "TestModelText",
                Id = 1010
            };
            var spotDim = new ElementTypeModel
            {
                Class = "Autodesk.Revit.DB.SpotDimensionType",
                Name = "TestSpotDimension",
                Id = 2020
            };

            var list = new System.Collections.Generic.List<ObjectModel> { modelText, spotDim };

            // Act
            var json = StandardSerializationEngine.ToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ModelTextTypes\""), "JSON should contain ModelTextTypes property.");
            Assert.IsTrue(json.Contains("\"SpotDimensionTypes\""), "JSON should contain SpotDimensionTypes property.");
            Assert.IsTrue(json.Contains("\"TestModelText\""), "JSON should contain TestModelText.");
            Assert.IsTrue(json.Contains("\"TestSpotDimension\""), "JSON should contain TestSpotDimension.");
        }

        [Test]
        public void StandardSerializationEngine_SerializationAndDeserialization_RoutesToposolidTypeCorrectly()
        {
            // Arrange
            var topoType = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.ToposolidType",
                Name = "TestToposolidType",
                Id = 3030
            };
            var list = new System.Collections.Generic.List<ObjectModel> { topoType };

            // Act - Serialize
            var json = StandardSerializationEngine.ToJson(list);

            // Assert - Serialization
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ToposolidTypes\""), "JSON should contain ToposolidTypes property.");
            Assert.IsTrue(json.Contains("\"TestToposolidType\""), "JSON should contain TestToposolidType.");

            // Act - Deserialize
            var flatList = System.Linq.Enumerable.ToList(StandardSerializationEngine.FromJson(json));

            // Assert - Deserialization
            Assert.AreEqual(1, flatList.Count);
            var deserialized = flatList[0];
            Assert.IsInstanceOf<HostObjTypeModel>(deserialized);
            Assert.AreEqual("Autodesk.Revit.DB.ToposolidType", deserialized.Class);
            Assert.AreEqual("TestToposolidType", deserialized.Name);
        }

        [Test]
        public void StandardSerializationEngine_ToJson_EmitsCanonicalOrderAndFilledRegionTypesAndOmitsEmptyBuckets()
        {
            // Arrange
            var fillPattern = new FillPatternElementModel { Name = "PatternA", Class = "Autodesk.Revit.DB.FillPatternElement" };
            var material = new MaterialModel { Name = "MaterialA", Class = "Autodesk.Revit.DB.Material" };
            var region = new FilledRegionTypeModel { Name = "RegionA", Class = "Autodesk.Revit.DB.FilledRegionType" };
            var genericElem = new ElementModel { Name = "DirectShapeA", Class = "Autodesk.Revit.DB.DirectShape" };

            var list = new System.Collections.Generic.List<ObjectModel> { genericElem, region, material, fillPattern };

            // Act
            string json = StandardSerializationEngine.ToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"FilledRegionTypes\""), "Must use 'FilledRegionTypes' bucket name.");
            Assert.IsFalse(json.Contains("\"FillRegionTypes\""), "Must never emit 'FillRegionTypes'.");
            Assert.IsFalse(json.Contains("\"LinePatternElements\""), "Must omit empty buckets.");
            Assert.IsFalse(json.Contains("\"HostObjTypes\""), "Must omit empty buckets.");
            Assert.IsTrue(json.Contains("\"Elements\""), "Must preserve non-empty Elements list.");

            // Check canonical ordering in JSON: FillPatternElements (order 1) before Materials (order 4) before FilledRegionTypes (order 8) before Elements
            int idxPattern = json.IndexOf("\"FillPatternElements\"");
            int idxMat = json.IndexOf("\"Materials\"");
            int idxRegion = json.IndexOf("\"FilledRegionTypes\"");
            int idxElements = json.IndexOf("\"Elements\"");

            Assert.IsTrue(idxPattern < idxMat, "FillPatternElements must precede Materials.");
            Assert.IsTrue(idxMat < idxRegion, "Materials must precede FilledRegionTypes.");
            Assert.IsTrue(idxRegion < idxElements, "FilledRegionTypes must precede Elements.");
        }

        [Test]
        public void StandardSerializationEngine_ToJson_SortsBucketKeysDeterministicallyAndHandlesDuplicatesLastInWins()
        {
            // Arrange
            SerializationResultModel.ClearWarnings();
            var matB = new MaterialModel { Name = "b_Material", Class = "Autodesk.Revit.DB.Material" };
            var matA = new MaterialModel { Name = "a_Material", Class = "Autodesk.Revit.DB.Material" };
            var matADup = new MaterialModel { Name = "a_Material", Class = "Autodesk.Revit.DB.Material", UniqueId = "last-in-wins-uid" };

            var list = new System.Collections.Generic.List<ObjectModel> { matB, matA, matADup };

            // Act
            string json = StandardSerializationEngine.ToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings.Count > 0, "Warning must be logged for duplicate key.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("Duplicate key 'a_Material'"), "Warning must mention duplicate key.");
            Assert.IsTrue(json.Contains("\"last-in-wins-uid\""), "Last-in-wins entry must be preserved.");

            int idxA = json.IndexOf("\"a_Material\"");
            int idxB = json.IndexOf("\"b_Material\"");
            Assert.IsTrue(idxA < idxB, "Keys must be sorted deterministically OrdinalIgnoreCase.");
        }

        [Test]
        public void StandardSerializationEngine_FromJson_HandlesLegacyBucketsAndHydratesPolymorphically()
        {
            // Arrange - JSON with legacy 'FillRegionTypes', legacy 'GridTypes', legacy TextElementType inside 'ElementTypes', and ViewTemplates
            string json = @"{
                ""FillRegionTypes"": {
                    ""LegacyRegion"": {
                        ""Name"": ""LegacyRegion"",
                        ""Id"": 101
                    }
                },
                ""GridTypes"": {
                    ""LegacyGrid"": {
                        ""Name"": ""LegacyGrid"",
                        ""Id"": 102
                    }
                },
                ""ElementTypes"": {
                    ""LegacyTextElement"": {
                        ""Class"": ""Autodesk.Revit.DB.TextElementType"",
                        ""Name"": ""LegacyTextElement"",
                        ""Id"": 103
                    }
                },
                ""ViewTemplates"": {
                    ""PlanTemplate"": {
                        ""Class"": ""Autodesk.Revit.DB.ViewPlan"",
                        ""Name"": ""PlanTemplate"",
                        ""Id"": 104
                    }
                }
            }";

            // Act
            var models = System.Linq.Enumerable.ToList(StandardSerializationEngine.FromJson(json));

            // Assert
            Assert.AreEqual(4, models.Count, "All 4 entries across legacy and current buckets must be deserialized.");

            var region = models.Find(m => m.Name == "LegacyRegion");
            Assert.IsNotNull(region);
            Assert.IsInstanceOf<FilledRegionTypeModel>(region, "FillRegionTypes must hydrate to FilledRegionTypeModel.");
            Assert.AreEqual("Autodesk.Revit.DB.FilledRegionType", region!.Class, "Default Class must be inferred when omitted.");

            var grid = models.Find(m => m.Name == "LegacyGrid");
            Assert.IsNotNull(grid);
            Assert.IsInstanceOf<ElementTypeModel>(grid, "GridTypes must hydrate to ElementTypeModel.");
            Assert.AreEqual("Autodesk.Revit.DB.GridType", grid!.Class, "Default GridType class must be inferred when omitted.");

            var textElem = models.Find(m => m.Name == "LegacyTextElement");
            Assert.IsNotNull(textElem);
            Assert.AreEqual("Autodesk.Revit.DB.TextElementType", textElem!.Class, "TextElementType must be preserved from ElementTypes.");

            var template = models.Find(m => m.Name == "PlanTemplate");
            Assert.IsNotNull(template);
            Assert.IsInstanceOf<ViewPlanModel>(template, "ViewPlan class in ViewTemplates must hydrate polymorphically to ViewPlanModel.");
            Assert.IsTrue(template!.IsTemplate, "IsTemplate must be forced to true for ViewTemplates entries.");
        }

        [Test]
        public void StandardSerializationEngine_FromJson_MigratesAll26LegacyBucketsWithPolymorphicSubclassesAndInferredClass()
        {
            // Arrange - Legacy JSON with all 26 legacy bucket names, plain Name keys, omitted .Class,
            // legacy 'FillRegionTypes' bucket, retired marker buckets, and specialized subclass properties.
            string json = @"{
                ""FillPatternElements"": {
                    ""Pattern1"": { ""Name"": ""Pattern1"", ""Pattern"": { ""Target"": ""Model"" } }
                },
                ""LinePatternElements"": {
                    ""Line1"": { ""Name"": ""Line1"", ""Segments"": [ { ""Type"": ""Dash"", ""Length"": 0.5 } ] }
                },
                ""PropertySetElements"": {
                    ""Asset1"": { ""Name"": ""Asset1"" }
                },
                ""Materials"": {
                    ""Material1"": { ""Name"": ""Material1"", ""Color"": { ""Red"": 255, ""Green"": 0, ""Blue"": 0 } }
                },
                ""ElementTypes"": {
                    ""LegacyTextLabel"": { ""Class"": ""Autodesk.Revit.DB.TextElementType"", ""Name"": ""LegacyTextLabel"" },
                    ""DoorSymbol"": { ""Name"": ""DoorSymbol"", ""FamilyName"": ""Single-Flush"" }
                },
                ""Categories"": {
                    ""Walls"": { ""Name"": ""Walls"", ""CategoryType"": ""Model"" }
                },
                ""HostObjTypes"": {
                    ""Wall1"": { ""Name"": ""Wall1"", ""Structure"": { ""Layers"": [ { ""Width"": 0.8 } ] } }
                },
                ""FillRegionTypes"": {
                    ""LegacyRegion"": { ""Name"": ""LegacyRegion"", ""ForegroundPatternId"": { ""Id"": 123 } }
                },
                ""DimensionTypes"": {
                    ""Dim1"": { ""Name"": ""Dim1"", ""DimensionStyle"": ""Linear"" }
                },
                ""TextNoteTypes"": {
                    ""Note1"": { ""Name"": ""Note1"" }
                },
                ""ViewTemplates"": {
                    ""PlanTemplate"": { ""Class"": ""Autodesk.Revit.DB.ViewPlan"", ""Name"": ""PlanTemplate"" }
                },
                ""Views"": {
                    ""Sheet1"": { ""Class"": ""Autodesk.Revit.DB.ViewSheet"", ""Name"": ""Sheet1"" },
                    ""Schedule1"": { ""Class"": ""Autodesk.Revit.DB.ViewSchedule"", ""Name"": ""Schedule1"" }
                },
                ""GridTypes"": {
                    ""Grid1"": { ""Name"": ""Grid1"" }
                },
                ""LevelTypes"": {
                    ""Level1"": { ""Name"": ""Level1"" }
                },
                ""CurtainSystemTypes"": {
                    ""Curtain1"": { ""Name"": ""Curtain1"", ""Structure"": { ""Layers"": [ { ""Width"": 0.5 } ] } }
                },
                ""MullionTypes"": {
                    ""Mullion1"": { ""Name"": ""Mullion1"" }
                },
                ""FasciaTypes"": {
                    ""Fascia1"": { ""Name"": ""Fascia1"" }
                },
                ""GutterTypes"": {
                    ""Gutter1"": { ""Name"": ""Gutter1"" }
                },
                ""ViewFamilyTypes"": {
                    ""VFT1"": { ""Name"": ""VFT1"" }
                },
                ""BrowserOrganizations"": {
                    ""Org1"": { ""Name"": ""Org1"" }
                },
                ""ParameterElements"": {
                    ""Param1"": { ""Name"": ""Param1"", ""IsInstanceBinding"": true }
                },
                ""TextElementTypes"": {
                    ""TextLabel1"": { ""Name"": ""TextLabel1"" }
                },
                ""ModelTextTypes"": {
                    ""ModelText1"": { ""Name"": ""ModelText1"" }
                },
                ""ParameterFilters"": {
                    ""Filter1"": { ""Name"": ""Filter1"" }
                },
                ""SpotDimensionTypes"": {
                    ""Spot1"": { ""Name"": ""Spot1"" }
                },
                ""ToposolidTypes"": {
                    ""Topo1"": { ""Name"": ""Topo1"", ""Structure"": { ""Layers"": [ { ""Width"": 1.2 } ] } }
                }
            }";

            // Act
            var models = System.Linq.Enumerable.ToList(StandardSerializationEngine.FromJson(json));

            // Assert - All 28 entries across all 26 buckets must be deserialized
            Assert.AreEqual(28, models.Count, "All 28 entries across 26 buckets must deserialize.");

            // 1. FillPatternElementModel
            var pattern = models.Find(m => m.Name == "Pattern1") as FillPatternElementModel;
            Assert.IsNotNull(pattern);
            Assert.AreEqual("Autodesk.Revit.DB.FillPatternElement", pattern!.Class);
            Assert.IsNotNull(pattern.Pattern);
            Assert.AreEqual("Model", pattern.Pattern!.Target);

            // 2. LinePatternElementModel
            var line = models.Find(m => m.Name == "Line1") as LinePatternElementModel;
            Assert.IsNotNull(line);
            Assert.AreEqual("Autodesk.Revit.DB.LinePatternElement", line!.Class);
            Assert.AreEqual(1, line.Segments.Count);

            // 3. PropertySetElementModel
            var asset = models.Find(m => m.Name == "Asset1") as PropertySetElementModel;
            Assert.IsNotNull(asset);
            Assert.AreEqual("Autodesk.Revit.DB.PropertySetElement", asset!.Class);

            // 4. MaterialModel
            var mat = models.Find(m => m.Name == "Material1") as MaterialModel;
            Assert.IsNotNull(mat);
            Assert.AreEqual("Autodesk.Revit.DB.Material", mat!.Class);
            Assert.AreEqual(255, mat.Color?.Red);

            // 5. ElementTypes: DoorSymbol & LegacyTextLabel
            var door = models.Find(m => m.Name == "DoorSymbol") as ElementTypeModel;
            Assert.IsNotNull(door);
            Assert.AreEqual("Autodesk.Revit.DB.FamilySymbol", door!.Class, "Default Class for ElementTypes must be FamilySymbol.");

            var textLabel = models.Find(m => m.Name == "LegacyTextLabel") as ElementTypeModel;
            Assert.IsNotNull(textLabel);
            Assert.AreEqual("Autodesk.Revit.DB.TextElementType", textLabel!.Class, "TextElementType must be preserved from ElementTypes.");

            // 6. CategoryModel
            var cat = models.Find(m => m.Name == "Walls") as CategoryModel;
            Assert.IsNotNull(cat);
            Assert.AreEqual("Built-In Categories", cat!.Class);

            // 7. HostObjTypeModel (Wall) with Structure preserved
            var wall = models.Find(m => m.Name == "Wall1") as HostObjTypeModel;
            Assert.IsNotNull(wall);
            Assert.AreEqual("Autodesk.Revit.DB.WallType", wall!.Class);
            Assert.IsNotNull(wall.Structure, "HostObjTypeModel.Structure must be preserved.");
            Assert.AreEqual(1, wall.Structure!.Layers.Count);
            Assert.AreEqual(0.8, wall.Structure!.Layers[0].Width);

            // 8. FilledRegionTypeModel (from legacy 'FillRegionTypes' bucket)
            var region = models.Find(m => m.Name == "LegacyRegion") as FilledRegionTypeModel;
            Assert.IsNotNull(region, "FillRegionTypes must hydrate to FilledRegionTypeModel.");
            Assert.AreEqual("Autodesk.Revit.DB.FilledRegionType", region!.Class);
            Assert.IsNotNull(region.ForegroundPatternId, "ForegroundPatternId must be preserved on FilledRegionTypeModel.");
            Assert.AreEqual(123, region.ForegroundPatternId!.Id);

            // 9. DimensionTypeModel with DimensionStyle preserved
            var dim = models.Find(m => m.Name == "Dim1") as DimensionTypeModel;
            Assert.IsNotNull(dim);
            Assert.AreEqual("Autodesk.Revit.DB.DimensionType", dim!.Class);
            Assert.AreEqual("Linear", dim.DimensionStyle, "DimensionStyle must be preserved on DimensionTypeModel.");

            // 10. TextNoteType (ElementTypeModel)
            var note = models.Find(m => m.Name == "Note1") as ElementTypeModel;
            Assert.IsNotNull(note);
            Assert.AreEqual("Autodesk.Revit.DB.TextNoteType", note!.Class);

            // 11. ViewTemplates: polymorphic ViewPlanModel with IsTemplate = true
            var planTemplate = models.Find(m => m.Name == "PlanTemplate") as ViewPlanModel;
            Assert.IsNotNull(planTemplate, "ViewPlan in ViewTemplates must hydrate to ViewPlanModel.");
            Assert.IsTrue(planTemplate!.IsTemplate, "IsTemplate must be true for ViewTemplates.");

            // 12. Views: ViewSheetModel and ViewScheduleModel
            var sheet = models.Find(m => m.Name == "Sheet1") as ViewSheetModel;
            Assert.IsNotNull(sheet, "ViewSheet must hydrate to ViewSheetModel.");
            Assert.AreEqual("Autodesk.Revit.DB.ViewSheet", sheet!.Class);

            var schedule = models.Find(m => m.Name == "Schedule1") as ViewScheduleModel;
            Assert.IsNotNull(schedule, "ViewSchedule must hydrate to ViewScheduleModel.");

            // 13. GridTypes (retired marker -> ElementTypeModel)
            var grid = models.Find(m => m.Name == "Grid1") as ElementTypeModel;
            Assert.IsNotNull(grid);
            Assert.AreEqual("Autodesk.Revit.DB.GridType", grid!.Class);

            // 14. LevelTypes (retired marker -> ElementTypeModel)
            var level = models.Find(m => m.Name == "Level1") as ElementTypeModel;
            Assert.IsNotNull(level);
            Assert.AreEqual("Autodesk.Revit.DB.LevelType", level!.Class);

            // 15. CurtainSystemTypes (retired marker -> HostObjTypeModel with Structure)
            var curtain = models.Find(m => m.Name == "Curtain1") as HostObjTypeModel;
            Assert.IsNotNull(curtain);
            Assert.AreEqual("Autodesk.Revit.DB.CurtainSystemType", curtain!.Class);
            Assert.IsNotNull(curtain.Structure);
            Assert.AreEqual(1, curtain.Structure!.Layers.Count);
            Assert.AreEqual(0.5, curtain.Structure!.Layers[0].Width);

            // 16. MullionTypes (retired marker -> HostObjTypeModel)
            var mullion = models.Find(m => m.Name == "Mullion1") as HostObjTypeModel;
            Assert.IsNotNull(mullion);
            Assert.AreEqual("Autodesk.Revit.DB.MullionType", mullion!.Class);

            // 17. FasciaTypes (retired marker -> HostObjTypeModel)
            var fascia = models.Find(m => m.Name == "Fascia1") as HostObjTypeModel;
            Assert.IsNotNull(fascia);
            Assert.AreEqual("Autodesk.Revit.DB.Architecture.FasciaType", fascia!.Class);

            // 18. GutterTypes (retired marker -> HostObjTypeModel)
            var gutter = models.Find(m => m.Name == "Gutter1") as HostObjTypeModel;
            Assert.IsNotNull(gutter);
            Assert.AreEqual("Autodesk.Revit.DB.Architecture.GutterType", gutter!.Class);

            // 19. ViewFamilyTypes (retired marker -> ElementTypeModel)
            var vft = models.Find(m => m.Name == "VFT1") as ElementTypeModel;
            Assert.IsNotNull(vft);
            Assert.AreEqual("Autodesk.Revit.DB.ViewFamilyType", vft!.Class);

            // 20. BrowserOrganizations
            var org = models.Find(m => m.Name == "Org1") as BrowserOrganizationModel;
            Assert.IsNotNull(org);
            Assert.AreEqual("Autodesk.Revit.DB.BrowserOrganization", org!.Class);

            // 21. ParameterElements
            var param = models.Find(m => m.Name == "Param1") as ParameterElementModel;
            Assert.IsNotNull(param);
            Assert.AreEqual("Autodesk.Revit.DB.ParameterElement", param!.Class);
            Assert.IsTrue(param.IsInstanceBinding);

            // 22. TextElementTypes
            var textElem = models.Find(m => m.Name == "TextLabel1") as ElementTypeModel;
            Assert.IsNotNull(textElem);
            Assert.AreEqual("Autodesk.Revit.DB.TextElementType", textElem!.Class);

            // 23. ModelTextTypes
            var modelText = models.Find(m => m.Name == "ModelText1") as ElementTypeModel;
            Assert.IsNotNull(modelText);
            Assert.AreEqual("Autodesk.Revit.DB.ModelTextType", modelText!.Class);

            // 24. ParameterFilters
            var filter = models.Find(m => m.Name == "Filter1") as ParameterFilterElementModel;
            Assert.IsNotNull(filter);
            Assert.AreEqual("Autodesk.Revit.DB.ParameterFilterElement", filter!.Class);

            // 25. SpotDimensionTypes
            var spot = models.Find(m => m.Name == "Spot1") as ElementTypeModel;
            Assert.IsNotNull(spot);
            Assert.AreEqual("Autodesk.Revit.DB.SpotDimensionType", spot!.Class);

            // 26. ToposolidTypes
            var topo = models.Find(m => m.Name == "Topo1") as HostObjTypeModel;
            Assert.IsNotNull(topo);
            Assert.AreEqual("Autodesk.Revit.DB.ToposolidType", topo!.Class);
            Assert.IsNotNull(topo.Structure);
            Assert.AreEqual(1, topo.Structure!.Layers.Count);
            Assert.AreEqual(1.2, topo.Structure!.Layers[0].Width);
        }

        [Test]
        public void StandardSerializationEngine_FromJson_HandlesCorruptAndEmptyJsonCleanly()
        {
            // Empty / whitespace / null / invalid strings
            Assert.IsEmpty(StandardSerializationEngine.FromJson(string.Empty));
            Assert.IsEmpty(StandardSerializationEngine.FromJson("   "));
            Assert.IsEmpty(StandardSerializationEngine.FromJson(null!));
            Assert.IsEmpty(StandardSerializationEngine.FromJson("{}"));
            Assert.IsEmpty(StandardSerializationEngine.FromJson("[]"));
            Assert.IsEmpty(StandardSerializationEngine.FromJson("not valid json at all"));

            // JSON with null property values, non-JObject dictionary values, and a corrupt item alongside valid items
            string mixedJson = @"{
                ""NullBucket"": null,
                ""PrimitiveBucket"": 12345,
                ""TextNoteTypes"": {
                    ""ValidNote"": { ""Name"": ""ValidNote"", ""Id"": 10 },
                    ""CorruptNote"": ""just a string value instead of an object"",
                    ""AnotherValid"": { ""Name"": ""AnotherValid"", ""Id"": 20 }
                }
            }";

            var results = System.Linq.Enumerable.ToList(StandardSerializationEngine.FromJson(mixedJson));
            Assert.AreEqual(2, results.Count, "Should recover cleanly and deserialize only valid entries.");
            Assert.IsTrue(results.Any(m => m.Name == "ValidNote"));
            Assert.IsTrue(results.Any(m => m.Name == "AnotherValid"));
        }

        [Test]
        public void StandardsSourceTreeViewModel_LoadFileSource_LoadsLegacy26BucketJsonFixtureIntoHierarchy()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), "legacy_26_bucket_" + Guid.NewGuid() + ".json");
            try
            {
                string json = @"{
                    ""FillRegionTypes"": {
                        ""Region_Cross"": { ""Name"": ""Region_Cross"" }
                    },
                    ""GridTypes"": {
                        ""Grid_1"": { ""Name"": ""Grid_1"" }
                    },
                    ""LevelTypes"": {
                        ""Level_1"": { ""Name"": ""Level_1"" }
                    },
                    ""CurtainSystemTypes"": {
                        ""Curtain_5x10"": { ""Name"": ""Curtain_5x10"" }
                    },
                    ""MullionTypes"": {
                        ""Mullion_Rect"": { ""Name"": ""Mullion_Rect"" }
                    },
                    ""FasciaTypes"": {
                        ""Fascia_Flat"": { ""Name"": ""Fascia_Flat"" }
                    },
                    ""GutterTypes"": {
                        ""Gutter_Bevel"": { ""Name"": ""Gutter_Bevel"" }
                    },
                    ""ViewFamilyTypes"": {
                        ""VFT_FloorPlan"": { ""Name"": ""VFT_FloorPlan"" }
                    },
                    ""Materials"": {
                        ""Concrete"": { ""Name"": ""Concrete"" }
                    },
                    ""ElementTypes"": {
                        ""LegacyLabel"": { ""Class"": ""Autodesk.Revit.DB.TextElementType"", ""Name"": ""LegacyLabel"" }
                    }
                }";
                File.WriteAllText(tempFile, json);

                Document doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
                var parent = DashboardTestFactory.Create(doc, settings: new StandardsSettings());
                var treeVM = parent.SourceTreeViewModel;

                // Act
                treeVM.LoadFileSource(tempFile, "Legacy Source Fixture");

                // Assert
                Assert.AreEqual(1, treeVM.AvailableSources.Count, "Exactly 1 source should be loaded.");
                var source = treeVM.AvailableSources[0];
                Assert.AreEqual("Legacy Source Fixture", source.DisplayName);
                Assert.IsFalse(source.IsRevitSource);

                var groups = source.SourceHierarchy;
                Assert.IsTrue(groups.Count > 0, "SourceHierarchy should contain groups.");

                var groupNames = groups.Select(g => g.Name).ToList();
                Assert.IsTrue(groupNames.Contains("Annotations"), "Should contain Annotations group.");
                Assert.IsTrue(groupNames.Contains("System Types"), "Should contain System Types group.");
                Assert.IsTrue(groupNames.Contains("Views"), "Should contain Views group.");
                Assert.IsTrue(groupNames.Contains("Materials & Assets"), "Should contain Materials & Assets group.");

                // Check Annotations classes: Filled Region Types, Grid Types, Level Types, Label Types
                var annotationsGroup = groups.First(g => g.Name == "Annotations");
                var annClassNames = annotationsGroup.Children.Select(c => c.Name).ToList();
                Assert.IsTrue(annClassNames.Contains("Filled Region Types"), "Annotations must contain Filled Region Types.");
                Assert.IsTrue(annClassNames.Contains("Grid Types"), "Annotations must contain Grid Types.");
                Assert.IsTrue(annClassNames.Contains("Level Types"), "Annotations must contain Level Types.");
                Assert.IsTrue(annClassNames.Contains("Label Types"), "Annotations must contain Label Types.");

                // Check System Types classes: Curtain System Types, Mullion Types, Fascia Types, Gutter Types
                var systemGroup = groups.First(g => g.Name == "System Types");
                var sysClassNames = systemGroup.Children.Select(c => c.Name).ToList();
                Assert.IsTrue(sysClassNames.Contains("Curtain System Types"), "System Types must contain Curtain System Types.");
                Assert.IsTrue(sysClassNames.Contains("Mullion Types"), "System Types must contain Mullion Types.");
                Assert.IsTrue(sysClassNames.Contains("Fascia Types"), "System Types must contain Fascia Types.");
                Assert.IsTrue(sysClassNames.Contains("Gutter Types"), "System Types must contain Gutter Types.");

                // Check Views classes: View Family Types
                var viewsGroup = groups.First(g => g.Name == "Views");
                var viewClassNames = viewsGroup.Children.Select(c => c.Name).ToList();
                Assert.IsTrue(viewClassNames.Contains("View Family Types"), "Views must contain View Family Types.");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void ToJson_FromJson_PreservesCollidingNamesAcrossBucketsAndCompositeKeys()
        {
            // Arrange
            // 1. DimensionTypes: Linear vs Angular "Standard" + internal "Linear Dimension Style" (which must be excluded)
            var dimLinear = new DimensionTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.DimensionType",
                DimensionStyle = "Linear"
            };
            var dimAngular = new DimensionTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.DimensionType",
                DimensionStyle = "Angular"
            };
            var dimInternal = new DimensionTypeModel
            {
                Name = DimensionTypeModel.InternalDimStyleName,
                Class = "Autodesk.Revit.DB.DimensionType",
                DimensionStyle = "Linear"
            };

            // 2. FillPatternElements: Drafting vs Model "Crosshatch"
            var fpDrafting = new FillPatternElementModel
            {
                Name = "Crosshatch",
                Class = "Autodesk.Revit.DB.FillPatternElement",
                Pattern = new FillPatternModel { Name = "Crosshatch", Target = "Drafting", HostOrientation = "ToHost" }
            };
            var fpModel = new FillPatternElementModel
            {
                Name = "Crosshatch",
                Class = "Autodesk.Revit.DB.FillPatternElement",
                Pattern = new FillPatternModel { Name = "Crosshatch", Target = "Model", HostOrientation = "ToHost" }
            };

            // 3. Categories: "SubA" across "Walls" vs "Floors" + empty parent category name fallback
            var catWalls = new CategoryModel
            {
                Name = "SubA",
                Class = "Autodesk.Revit.DB.Category",
                ParentCategoryName = "Walls"
            };
            var catFloors = new CategoryModel
            {
                Name = "SubA",
                Class = "Autodesk.Revit.DB.Category",
                ParentCategoryName = "Floors"
            };
            var catTopLevel = new CategoryModel
            {
                Name = "TopLevelCat",
                Class = "Autodesk.Revit.DB.Category",
                ParentCategoryName = null // empty prefix fallback to bare "{Name}"
            };

            // 4. Multi-family system buckets: HostObjTypes "Partition" (Basic Wall vs Curtain Wall) + empty FamilyName fallback
            var hostBasic = new HostObjTypeModel
            {
                Name = "Partition",
                Class = "Autodesk.Revit.DB.WallType",
                FamilyName = "Basic Wall"
            };
            var hostCurtain = new HostObjTypeModel
            {
                Name = "Partition",
                Class = "Autodesk.Revit.DB.WallType",
                FamilyName = "Curtain Wall"
            };
            var hostEmptyFamily = new HostObjTypeModel
            {
                Name = "BareWall",
                Class = "Autodesk.Revit.DB.WallType",
                FamilyName = "" // empty prefix fallback to bare "{Name}"
            };

            // 5. SpotDimensionTypes: "Standard" across "Spot Elevations" vs "Spot Coordinates"
            var spotElevation = new ElementTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.SpotDimensionType",
                FamilyName = "Spot Elevations"
            };
            var spotCoord = new ElementTypeModel
            {
                Name = "Standard",
                Class = "Autodesk.Revit.DB.SpotDimensionType",
                FamilyName = "Spot Coordinates"
            };

            var inputModels = new List<ObjectModel>
            {
                dimLinear, dimAngular, dimInternal,
                fpDrafting, fpModel,
                catWalls, catFloors, catTopLevel,
                hostBasic, hostCurtain, hostEmptyFamily,
                spotElevation, spotCoord
            };

            // Act: ToJson
            string json = StandardSerializationEngine.ToJson(inputModels);

            // Assert: verify JSON keys and exclusion
            var root = JObject.Parse(json);

            // DimensionTypes
            Assert.IsNotNull(root["DimensionTypes"]);
            var dimDict = (JObject)root["DimensionTypes"]!;
            Assert.IsTrue(dimDict.ContainsKey("[Linear]_Standard"), "DimensionTypes must have [Linear]_Standard key.");
            Assert.IsTrue(dimDict.ContainsKey("[Angular]_Standard"), "DimensionTypes must have [Angular]_Standard key.");
            Assert.IsFalse(dimDict.ContainsKey(DimensionTypeModel.InternalDimStyleName), "Internal dimension style must be excluded.");
            Assert.AreEqual(2, dimDict.Count);

            // FillPatternElements
            Assert.IsNotNull(root["FillPatternElements"]);
            var fpDict = (JObject)root["FillPatternElements"]!;
            Assert.IsTrue(fpDict.ContainsKey("[Drafting]_Crosshatch"), "FillPatternElements must have [Drafting]_Crosshatch key.");
            Assert.IsTrue(fpDict.ContainsKey("[Model]_Crosshatch"), "FillPatternElements must have [Model]_Crosshatch key.");
            Assert.AreEqual(2, fpDict.Count);

            // Categories
            Assert.IsNotNull(root["Categories"]);
            var catDict = (JObject)root["Categories"]!;
            Assert.IsTrue(catDict.ContainsKey("[Walls]_SubA"), "Categories must have [Walls]_SubA key.");
            Assert.IsTrue(catDict.ContainsKey("[Floors]_SubA"), "Categories must have [Floors]_SubA key.");
            Assert.IsTrue(catDict.ContainsKey("TopLevelCat"), "Category with empty prefix must fall back to bare '{Name}'.");
            Assert.AreEqual(3, catDict.Count);

            // HostObjTypes
            Assert.IsNotNull(root["HostObjTypes"]);
            var hostDict = (JObject)root["HostObjTypes"]!;
            Assert.IsTrue(hostDict.ContainsKey("[Basic Wall]_Partition"), "HostObjTypes must have [Basic Wall]_Partition key.");
            Assert.IsTrue(hostDict.ContainsKey("[Curtain Wall]_Partition"), "HostObjTypes must have [Curtain Wall]_Partition key.");
            Assert.IsTrue(hostDict.ContainsKey("BareWall"), "HostObjType with empty FamilyName must fall back to bare '{Name}'.");
            Assert.AreEqual(3, hostDict.Count);

            // SpotDimensionTypes
            Assert.IsNotNull(root["SpotDimensionTypes"]);
            var spotDict = (JObject)root["SpotDimensionTypes"]!;
            Assert.IsTrue(spotDict.ContainsKey("[Spot Elevations]_Standard"), "SpotDimensionTypes must have [Spot Elevations]_Standard key.");
            Assert.IsTrue(spotDict.ContainsKey("[Spot Coordinates]_Standard"), "SpotDimensionTypes must have [Spot Coordinates]_Standard key.");
            Assert.AreEqual(2, spotDict.Count);

            // Act: FromJson
            var deserialized = StandardSerializationEngine.FromJson(json).ToList();

            // Assert: verify polymorphic hydration and count preservation
            Assert.AreEqual(12, deserialized.Count, "All 12 valid entries must deserialize without collision drops.");

            var deserializedDims = deserialized.OfType<DimensionTypeModel>().ToList();
            Assert.AreEqual(2, deserializedDims.Count);
            Assert.IsTrue(deserializedDims.Any(d => d.DimensionStyle == "Linear" && d.Name == "Standard"));
            Assert.IsTrue(deserializedDims.Any(d => d.DimensionStyle == "Angular" && d.Name == "Standard"));

            var deserializedFps = deserialized.OfType<FillPatternElementModel>().ToList();
            Assert.AreEqual(2, deserializedFps.Count);
            Assert.IsTrue(deserializedFps.Any(f => f.Pattern?.Target == "Drafting" && f.Name == "Crosshatch"));
            Assert.IsTrue(deserializedFps.Any(f => f.Pattern?.Target == "Model" && f.Name == "Crosshatch"));

            var deserializedCats = deserialized.OfType<CategoryModel>().ToList();
            Assert.AreEqual(3, deserializedCats.Count);
            Assert.IsTrue(deserializedCats.Any(c => c.ParentCategoryName == "Walls" && c.Name == "SubA"));
            Assert.IsTrue(deserializedCats.Any(c => c.ParentCategoryName == "Floors" && c.Name == "SubA"));
            Assert.IsTrue(deserializedCats.Any(c => c.Name == "TopLevelCat"));

            var deserializedHosts = deserialized.OfType<HostObjTypeModel>().ToList();
            Assert.AreEqual(3, deserializedHosts.Count);
            Assert.IsTrue(deserializedHosts.Any(h => h.FamilyName == "Basic Wall" && h.Name == "Partition"));
            Assert.IsTrue(deserializedHosts.Any(h => h.FamilyName == "Curtain Wall" && h.Name == "Partition"));
            Assert.IsTrue(deserializedHosts.Any(h => h.Name == "BareWall"));

            var deserializedSpots = deserialized.Where(m => m.Class == "Autodesk.Revit.DB.SpotDimensionType").OfType<ElementTypeModel>().ToList();
            Assert.AreEqual(2, deserializedSpots.Count);
            Assert.IsTrue(deserializedSpots.Any(s => s.FamilyName == "Spot Elevations" && s.Name == "Standard"));
            Assert.IsTrue(deserializedSpots.Any(s => s.FamilyName == "Spot Coordinates" && s.Name == "Standard"));
        }

        [Test]
        public void StandardSerializationEngine_ToJson_FamilySymbolCompositeKeysAndFallbacksAndRoundTrip()
        {
            var models = new List<ObjectModel>
            {
                new ElementTypeModel
                {
                    Name = "Standard",
                    Class = "Autodesk.Revit.DB.FamilySymbol",
                    Category = "Doors",
                    FamilyName = "Single-Flush"
                },
                new ElementTypeModel
                {
                    Name = "Standard",
                    Class = "Autodesk.Revit.DB.FamilySymbol",
                    Category = "Doors",
                    FamilyName = "Double-Flush"
                },
                new ElementTypeModel
                {
                    Name = "Standard",
                    Class = "Autodesk.Revit.DB.FamilySymbol",
                    Category = "Windows",
                    FamilyName = "Single-Flush"
                },
                new ElementTypeModel
                {
                    Name = "CatOnlySymbol",
                    Class = "Autodesk.Revit.DB.FamilySymbol",
                    Category = "Furniture",
                    FamilyName = ""
                },
                new ElementTypeModel
                {
                    Name = "FamOnlySymbol",
                    Class = "Autodesk.Revit.DB.FamilySymbol",
                    Category = "",
                    FamilyName = "Casework"
                },
                new ElementTypeModel
                {
                    Name = "BareSymbol",
                    Class = "Autodesk.Revit.DB.FamilySymbol",
                    Category = "",
                    FamilyName = ""
                }
            };

            string json = StandardSerializationEngine.ToJson(models);
            var root = JObject.Parse(json);
            var elemTypes = (JObject)root["ElementTypes"]!;
            Assert.IsNotNull(elemTypes);
            Assert.AreEqual(6, elemTypes.Count);
            Assert.IsTrue(elemTypes.ContainsKey("[Doors | Single-Flush]_Standard"));
            Assert.IsTrue(elemTypes.ContainsKey("[Doors | Double-Flush]_Standard"));
            Assert.IsTrue(elemTypes.ContainsKey("[Windows | Single-Flush]_Standard"));
            Assert.IsTrue(elemTypes.ContainsKey("[Furniture]_CatOnlySymbol"));
            Assert.IsTrue(elemTypes.ContainsKey("[Casework]_FamOnlySymbol"));
            Assert.IsTrue(elemTypes.ContainsKey("BareSymbol"));

            var roundTripped = StandardSerializationEngine.FromJson(json).OfType<ElementTypeModel>().ToList();
            Assert.AreEqual(6, roundTripped.Count);
            Assert.IsTrue(roundTripped.Any(m => m.Category == "Doors" && m.FamilyName == "Single-Flush" && m.Name == "Standard"));
            Assert.IsTrue(roundTripped.Any(m => m.Category == "Doors" && m.FamilyName == "Double-Flush" && m.Name == "Standard"));
            Assert.IsTrue(roundTripped.Any(m => m.Category == "Windows" && m.FamilyName == "Single-Flush" && m.Name == "Standard"));
        }

        [Test]
        public void StandardSerializationEngine_ToJson_ViewFamilyTypeCompositeKeysAndNullOmissionAndRoundTrip()
        {
            var vftFloor = new ElementTypeModel
            {
                Name = "Architectural Plan",
                Class = "Autodesk.Revit.DB.ViewFamilyType",
                FamilyName = "Floor Plan",
                ViewFamily = "FloorPlan"
            };
            var vftCeiling = new ElementTypeModel
            {
                Name = "Architectural Plan",
                Class = "Autodesk.Revit.DB.ViewFamilyType",
                FamilyName = "Ceiling Plan",
                ViewFamily = "CeilingPlan"
            };
            var vftLegacyFallback = new ElementTypeModel
            {
                Name = "Legacy Detail",
                Class = "Autodesk.Revit.DB.ViewFamilyType",
                FamilyName = "Detail",
                ViewFamily = null
            };
            var nonVft = new ElementTypeModel
            {
                Name = "Grid 1",
                Class = "Autodesk.Revit.DB.GridType",
                ViewFamily = null
            };

            string json = StandardSerializationEngine.ToJson(new ObjectModel[] { vftFloor, vftCeiling, vftLegacyFallback, nonVft });
            var root = JObject.Parse(json);

            // Null ViewFamily must be omitted from JSON
            var gridEntry = (JObject)root["GridTypes"]!["Grid 1"]!;
            Assert.IsFalse(gridEntry.ContainsKey("ViewFamily"), "ViewFamily must be omitted from JSON when null.");

            var vftDict = (JObject)root["ViewFamilyTypes"]!;
            Assert.AreEqual(3, vftDict.Count);
            Assert.IsTrue(vftDict.ContainsKey("[FloorPlan]_Architectural Plan"));
            Assert.IsTrue(vftDict.ContainsKey("[CeilingPlan]_Architectural Plan"));
            Assert.IsTrue(vftDict.ContainsKey("[Detail]_Legacy Detail"));
            Assert.AreEqual("FloorPlan", vftDict["[FloorPlan]_Architectural Plan"]!["ViewFamily"]?.ToString());
            Assert.IsFalse(((JObject)vftDict["[Detail]_Legacy Detail"]!).ContainsKey("ViewFamily"));

            var roundTripped = StandardSerializationEngine.FromJson(json).OfType<ElementTypeModel>().ToList();
            Assert.AreEqual(4, roundTripped.Count);
            Assert.IsTrue(roundTripped.Any(m => m.ViewFamily == "FloorPlan" && m.Name == "Architectural Plan"));
            Assert.IsTrue(roundTripped.Any(m => m.ViewFamily == "CeilingPlan" && m.Name == "Architectural Plan"));
            Assert.IsTrue(roundTripped.Any(m => m.ViewFamily == null && m.FamilyName == "Detail" && m.Name == "Legacy Detail"));
        }
    }
}
