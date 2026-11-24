using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Platform.Revit;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM.Operations.Diffing;
using Synthetic.RevitDOM;
using SyntheticTests.Fixtures;

namespace SyntheticTests.RevitDOM
{
    [TestFixture]
    [RevitAdapterTest]
    public class RevitIdentityServiceResolutionRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void ResolveElement_ByUniqueId_ResolvesCorrectElement()
        {
            WithRolledBackDocument(doc =>
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist in the project.");

                var service = new RevitIdentityService();
                ElementIdModel model = service.ToModel(defaultLevel!.Id, doc);

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve the element.");
                Assert.AreEqual(defaultLevel.UniqueId, resolvedElem.UniqueId, "Resolved element UniqueId should match.");
            });
        }

        [Test]
        public void ResolveElement_ByStandardIdFallback_ResolvesCorrectElement()
        {
            WithRolledBackDocument(doc =>
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    Class = defaultLevel.GetType().FullName ?? string.Empty,
                    Name = defaultLevel.Name,
                    UniqueId = null // Force standard ID fallback
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by standard ID fallback.");
                Assert.AreEqual(defaultLevel.Id, resolvedElem.Id, "Resolved element ID should match.");
            });
        }

        [Test]
        public void ResolveElement_ByNameAndClassFallback_ResolvesCorrectElement()
        {
            WithRolledBackDocument(doc =>
            {
                WallType? defaultWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultWallType, "A WallType should exist.");

                var model = new ElementIdModel
                {
                    Id = 0,
                    UniqueId = null,
                    Name = defaultWallType!.Name,
                    Class = typeof(WallType).FullName ?? string.Empty
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by Name and Class fallback.");
                Assert.AreEqual(defaultWallType.Id, resolvedElem.Id, "Resolved element ID should match.");
            });
        }

        [Test]
        public void ResolveElement_ByAliasesFallback_ResolvesCorrectElement()
        {
            WithRolledBackDocument(doc =>
            {
                WallType? defaultWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultWallType, "A WallType should exist.");

                var model = new ElementIdModel
                {
                    Id = 0,
                    UniqueId = null,
                    Name = "NonExistentName",
                    Class = typeof(WallType).FullName ?? string.Empty,
                    Aliases = new List<string> { "FakeAlias1", defaultWallType!.Name, "FakeAlias2" }
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by Aliases fallback.");
                Assert.AreEqual(defaultWallType.Id, resolvedElem.Id, "Resolved element ID should match.");
            });
        }

        [Test]
        public void ResolveElement_TypeGuardMismatch_ReturnsNull()
        {
            WithRolledBackDocument(doc =>
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    UniqueId = defaultLevel.UniqueId,
                    Class = typeof(WallType).FullName ?? string.Empty, // Intentionally wrong class
                    Name = defaultLevel.Name
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNull(resolvedElem, "ResolveElement should return null due to Type Guard mismatch.");
            });
        }

        [Test]
        public void ResolveElementId_BuiltInCategory_OST_Walls_ReturnsCorrectId()
        {
            WithRolledBackDocument(doc =>
            {
                var model = new ElementIdModel
                {
                    Name = "BuiltInCategory.OST_Walls"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInCategory.OST_Walls;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            });
        }

        [Test]
        public void ResolveElementId_BuiltInParameter_ALL_MODEL_MARK_ReturnsCorrectId()
        {
            WithRolledBackDocument(doc =>
            {
                var model = new ElementIdModel
                {
                    Name = "BuiltInParameter.ALL_MODEL_MARK"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInParameter.ALL_MODEL_MARK;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            });
        }

        [Test]
        public void ResolveElementId_MalformedEnum_GracefulFallback()
        {
            WithRolledBackDocument(doc =>
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel);

                var model = new ElementIdModel
                {
                    Name = "BuiltInCategory.OST_Walls_Malformed",
                    UniqueId = defaultLevel!.UniqueId // fallback target
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreEqual(defaultLevel.Id, resolvedId, "Should gracefully fall back to UniqueId fallback.");
            });
        }

        [Test]
        public void ResolveElementId_JustEnumValue_ReturnsCorrectId()
        {
            WithRolledBackDocument(doc =>
            {
                var model = new ElementIdModel
                {
                    Name = "OST_Walls"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInCategory.OST_Walls;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            });
        }

        [Test]
        public void GetElementsByElementIdModels_SuccessPath_ResolvesAllElements()
        {
            WithRolledBackDocument(doc =>
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Element? defaultView = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");
                Assert.IsNotNull(defaultView, "A default View element should exist.");

                var service = new RevitIdentityService();
                var modelLevel = service.ToModel(defaultLevel!.Id, doc);
                var modelView = service.ToModel(defaultView!.Id, doc);

                var identifiers = new List<ElementIdModel> { modelLevel, modelView };

                // Act
                var resolved = service.GetElementsByElementIdModels(doc, identifiers).ToList();

                // Assert
                Assert.AreEqual(2, resolved.Count);
                Assert.IsTrue(resolved.Any(e => e.UniqueId == defaultLevel.UniqueId));
                Assert.IsTrue(resolved.Any(e => e.UniqueId == defaultView.UniqueId));
            });
        }

        [Test]
        public void GetElementsByElementIdModels_DegradationPath_LogsWarningAndFiltersNulls()
        {
            WithRolledBackDocument(doc =>
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                var service = new RevitIdentityService();
                var modelLevel = service.ToModel(defaultLevel!.Id, doc);

                var modelFictitious = new ElementIdModel
                {
                    Name = "Fictitious Level",
                    Class = "Autodesk.Revit.DB.Level",
                    UniqueId = "nonexistent-guid-value-12345"
                };

                var identifiers = new List<ElementIdModel> { modelLevel, modelFictitious };

                // Clear thread warnings beforehand
                SerializationResultModel.ClearWarnings();

                // Act
                var resolved = service.GetElementsByElementIdModels(doc, identifiers).ToList();

                // Assert
                Assert.AreEqual(1, resolved.Count);
                Assert.AreEqual(defaultLevel.UniqueId, resolved[0].UniqueId);

                var warnings = SerializationResultModel.CurrentThreadWarnings;
                Assert.AreEqual(1, warnings.Count);
                Assert.AreEqual("Dependency not found: Fictitious Level (Autodesk.Revit.DB.Level)", warnings[0]);
            });
        }

        [Test]
        public void ResolveElementId_SolidShortcut_OnlyMatchesLinePatternReferencesAndNotOtherElementsNamedSolid()
        {
            WithRolledBackDocument(doc =>
            {
                ElementId solidMatId = ElementId.InvalidElementId;
                doc.ExecuteTransaction("Create Material Named Solid", FailurePipelines.ObserveOnly(), () =>
                {
                    solidMatId = Material.Create(doc, "Solid");
                });

                var service = new RevitIdentityService();

                var materialModel = new ElementIdModel
                {
                    Name = "Solid",
                    Class = "Autodesk.Revit.DB.Material",
                    Id = 0
                };
                ElementId resolvedMaterialId = service.ResolveElementId(materialModel, doc);
                Assert.AreEqual(solidMatId, resolvedMaterialId, "Non-LinePatternElement named 'Solid' must resolve to the Material, not Solid LinePattern.");

                var diffMaterialParam = new ParameterModel
                {
                    Name = "Material",
                    StorageType = "ElementId",
                    ValueElemId = materialModel
                };
                Assert.AreEqual(
                    GetElementIdValue(solidMatId).ToString(),
                    PocoToRevitDiffEngine.GetJsonParameterValue(doc, diffMaterialParam, service));

                var linePatternModel = new ElementIdModel
                {
                    Name = "Solid",
                    Class = "Autodesk.Revit.DB.LinePatternElement",
                    Id = 0
                };
                Assert.AreEqual(LinePatternElement.GetSolidPatternId(), service.ResolveElementId(linePatternModel, doc));

                var unqualifiedSolidModel = new ElementIdModel
                {
                    Name = "Solid",
                    Class = string.Empty,
                    Id = 0
                };
                Assert.AreEqual(LinePatternElement.GetSolidPatternId(), service.ResolveElementId(unqualifiedSolidModel, doc));
            });
        }

        [Test]
        public void ResolveElementId_NegativeSentinelWithEmptyName_PreservesNegativeElementId()
        {
            WithRolledBackDocument(doc =>
            {
                var service = new RevitIdentityService();
                long wallCatSentinel = (long)BuiltInCategory.OST_Walls;
                var sentinelModel = new ElementIdModel
                {
                    Id = wallCatSentinel,
                    Name = string.Empty,
                    Class = string.Empty
                };

                ElementId resolvedId = service.ResolveElementId(sentinelModel, doc);
                Assert.AreEqual(wallCatSentinel, GetElementIdValue(resolvedId));

                var diffParam = new ParameterModel
                {
                    Name = "SentinelParam",
                    StorageType = "ElementId",
                    ValueElemId = sentinelModel
                };
                Assert.AreEqual(
                    wallCatSentinel.ToString(),
                    PocoToRevitDiffEngine.GetJsonParameterValue(doc, diffParam, service));

                var noneModel = new ElementIdModel { Name = "<None>", Id = -1 };
                Assert.AreEqual(ElementId.InvalidElementId, service.ResolveElementId(noneModel, doc));
            });
        }

        private static long GetElementIdValue(ElementId id)
        {
            var valueProp = id.GetType().GetProperty("Value");
            if (valueProp != null)
            {
                return (long)valueProp.GetValue(id)!;
            }

            var integerValueProp = id.GetType().GetProperty("IntegerValue");
            if (integerValueProp != null)
            {
                return Convert.ToInt64(integerValueProp.GetValue(id));
            }

            return 0;
        }
    }
}
