using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ElementIdModelTests
    {
        [Test]
        public void StandardSymmetry_PositiveIdNonTemplate_SerializesAndDeserializesId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = 12345,
                IsTemplate = false,
                Name = "TestElement",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "abc-123-xyz"
            };

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("Id"), "JSON should contain the 'Id' property.");
            Assert.AreEqual(12345, (long)jObj["Id"]!, "Serialized ID should match original positive ID.");
            Assert.IsTrue(jObj.ContainsKey("UniqueId"), "JSON should contain the 'UniqueId' property.");
            Assert.AreEqual("abc-123-xyz", (string)jObj["UniqueId"]!, "Serialized UniqueId should match original.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(12345, roundTrip!.Id, "Deserialized ID should match original positive ID.");
            Assert.AreEqual("abc-123-xyz", roundTrip.UniqueId, "Deserialized UniqueId should match original.");
        }

        [Test]
        public void TemplateIDOmission_PositiveIdTemplate_OmitsId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = 12345,
                IsTemplate = true,
                Name = "TestElement",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "abc-123-xyz"
            };

            // Act
            var shouldSerializeId = model.ShouldSerializeId();
            var shouldSerializeUniqueId = model.ShouldSerializeUniqueId();
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsFalse(shouldSerializeId, "ShouldSerializeId should return false for a positive ID in a template.");
            Assert.IsFalse(shouldSerializeUniqueId, "ShouldSerializeUniqueId should return false for a template.");
            
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsFalse(jObj.ContainsKey("Id"), "JSON should not contain the 'Id' property when IsTemplate is true and ID is positive.");
            Assert.IsFalse(jObj.ContainsKey("UniqueId"), "JSON should not contain the 'UniqueId' property when IsTemplate is true.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0, roundTrip!.Id, "Deserialized ID should evaluate to default (0) because it was omitted during serialization.");
            Assert.IsNull(roundTrip.UniqueId, "Deserialized UniqueId should evaluate to null because it was omitted during serialization.");
        }

        [Test]
        public void BuiltInPreservation_NegativeIdTemplate_PreservesId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = -2000100, // Built-in category element ID
                IsTemplate = true,
                Name = "Walls",
                Class = "Autodesk.Revit.DB.Category",
                Category = ""
            };

            // Act
            var shouldSerializeId = model.ShouldSerializeId();
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsTrue(shouldSerializeId, "ShouldSerializeId should return true for a negative ID in a template.");
            
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("Id"), "JSON should contain the 'Id' property even if IsTemplate is true for negative IDs.");
            Assert.AreEqual(-2000100, (long)jObj["Id"]!, "Serialized ID should match original negative ID.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(-2000100, roundTrip!.Id, "Deserialized ID should match original negative ID.");
        }

        #region 5-Step Identity Resolution & Equality Tests

        [Test]
        public void Equals_Step1_UniqueIdMatch_ReturnsTrue()
        {
            var a = new ElementIdModel { UniqueId = "GUID-1234", Id = 100, Name = "WallA", Class = "Autodesk.Revit.DB.Wall" };
            var b = new ElementIdModel { UniqueId = "guid-1234", Id = 999, Name = "WallB", Class = "Autodesk.Revit.DB.Wall" };

            Assert.IsTrue(a.Equals(b), "Step 1: Matching UniqueId (case-insensitive) should equate models regardless of different Id or Name.");
            Assert.IsTrue(a == b, "Operator == should match Equals result.");
            Assert.IsFalse(a != b, "Operator != should be false for equal models.");
        }

        [Test]
        public void Equals_Step2_PositiveAndBuiltInNegativeIds_ReturnsTrue()
        {
            var pos1 = new ElementIdModel { Id = 54321, Class = "Autodesk.Revit.DB.Wall" };
            var pos2 = new ElementIdModel { Id = 54321, Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsTrue(pos1.Equals(pos2), "Step 2: Identical positive IDs should equate models.");

            var builtIn1 = new ElementIdModel { Id = -2000100, Class = "Autodesk.Revit.DB.Category" };
            var builtIn2 = new ElementIdModel { Id = -2000100, Class = "Autodesk.Revit.DB.Category" };
            Assert.IsTrue(builtIn1.Equals(builtIn2), "Step 2: Preserved built-in negative IDs should equate models.");
        }

        [Test]
        public void Equals_Step2_DefaultInvalidModels_HandledCorrectly()
        {
            var invalid1 = new ElementIdModel { Id = -1 };
            var invalid2 = new ElementIdModel { Id = -1 };
            Assert.IsTrue(invalid1.Equals(invalid2), "Two default invalid models with no UniqueId/Name should be equal.");

            var invalidNamed1 = new ElementIdModel { Id = -1, Name = "Wall 1", Class = "Autodesk.Revit.DB.Wall" };
            var invalidNamed2 = new ElementIdModel { Id = -1, Name = "Door 1", Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsFalse(invalidNamed1.Equals(invalidNamed2), "Models with Id = -1 but different names must not falsely match on Step 2.");
        }

        [Test]
        public void Equals_Step3_ClassTypeGuard_PreventsMismatch()
        {
            var wall = new ElementIdModel { Id = 100, Name = "Standard", Class = "Autodesk.Revit.DB.Wall" };
            var floor = new ElementIdModel { Id = 100, Name = "Standard", Class = "Autodesk.Revit.DB.Floor" };

            Assert.IsFalse(wall.Equals(floor), "Step 3 Type Guard: Different Class types must prevent equality despite identical Id or Name.");
        }

        [Test]
        public void Equals_Step4_NameAndCategory_CaseInsensitiveMatch()
        {
            var a = new ElementIdModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.Wall", Category = "Walls" };
            var b = new ElementIdModel { Name = "generic wall", Class = "autodesk.revit.db.wall", Category = "walls" };

            Assert.IsTrue(a.Equals(b), "Step 4: Matching Name, Class, and Category (case-insensitive) should equate models.");

            var c = new ElementIdModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.Wall", Category = "Doors" };
            Assert.IsFalse(a.Equals(c), "Step 4: Mismatched Category should prevent equality.");
        }

        [Test]
        public void Equals_Step5_AliasesCrossMatch_ReturnsTrue()
        {
            var main = new ElementIdModel
            {
                Name = "Wall_Standard_v2",
                Class = "Autodesk.Revit.DB.Wall",
                Aliases = new List<string> { "Wall_Standard_v1", "Legacy_Wall" }
            };

            var legacy = new ElementIdModel
            {
                Name = "Wall_Standard_v1",
                Class = "Autodesk.Revit.DB.Wall"
            };

            Assert.IsTrue(main.Equals(legacy), "Step 5: Main model alias matching secondary model name should return true.");
            Assert.IsTrue(legacy.Equals(main), "Step 5: Symmetry requirement - secondary model matching main model alias.");

            var bothAliased = new ElementIdModel
            {
                Name = "New_Wall_Name",
                Class = "Autodesk.Revit.DB.Wall",
                Aliases = new List<string> { "Legacy_Wall" }
            };
            Assert.IsTrue(main.Equals(bothAliased), "Step 5: Intersecting aliases should match.");
        }

        [Test]
        public void GetHashCode_Consistency_SameForEqualObjects()
        {
            var a = new ElementIdModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.Wall", Category = "Walls", Id = 100 };
            var b = new ElementIdModel { Name = "GENERIC WALL", Class = "autodesk.revit.db.wall", Category = "WALLS", Id = 100 };

            Assert.IsTrue(a.Equals(b), "Models should be equal.");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Equal objects must yield identical hash codes regardless of casing.");
        }

        [Test]
        public void DictionaryLookup_ElementIdModel_Succeeds()
        {
            var key1 = new ElementIdModel { Name = "Wall_A", Class = "Autodesk.Revit.DB.Wall", Id = 101 };
            var key2 = new ElementIdModel { Name = "wall_a", Class = "Autodesk.Revit.DB.Wall", Id = 101 };

            var dict = new Dictionary<ElementIdModel, string>
            {
                { key1, "WinningValue" }
            };

            Assert.IsTrue(dict.ContainsKey(key2), "Dictionary lookup should succeed for value-equal ElementIdModel instance.");
            Assert.AreEqual("WinningValue", dict[key2], "Dictionary value retrieval should match expected value.");
        }

        [Test]
        public void Equals_Step1_UniqueIdMismatch_ReturnsFalse_EvenWhenIdOrNameMatch()
        {
            var a = new ElementIdModel { UniqueId = "UID-111", Id = 100, Name = "Door 1", Class = "Autodesk.Revit.DB.FamilyInstance" };
            var b = new ElementIdModel { UniqueId = "UID-222", Id = 100, Name = "Door 1", Class = "Autodesk.Revit.DB.FamilyInstance" };

            Assert.IsFalse(a.Equals(b), "Mismatched UniqueIds must return false immediately and not fall through to Id or Name.");
            Assert.IsFalse(a == b, "Operator == should return false for mismatched UniqueIds.");
            Assert.IsTrue(a != b, "Operator != should return true for mismatched UniqueIds.");
        }

        [Test]
        public void GetHashCode_UniqueIdMatch_EmptyName_HasSameHashCode()
        {
            var a = new ElementIdModel { UniqueId = "GUID-1234", Id = 100, Class = "Autodesk.Revit.DB.Wall" };
            var b = new ElementIdModel { UniqueId = "guid-1234", Id = 999, Class = "Autodesk.Revit.DB.Wall" };

            Assert.IsTrue(a.Equals(b), "Models with matching UniqueId should be equal.");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Equal objects matching on UniqueId with empty Name must yield identical hash codes regardless of differing Id.");
        }

        [Test]
        public void GetHashCode_AsymmetricEquality_UniqueIdAndName_Matches_NameOnly()
        {
            var fullModel = new ElementIdModel { UniqueId = "GUID-1234", Id = 100, Name = "WallA", Class = "Autodesk.Revit.DB.Wall" };
            var nameOnlyModel = new ElementIdModel { Name = "wAlLa", Class = "autodesk.revit.db.wall" };

            Assert.IsTrue(fullModel.Equals(nameOnlyModel), "Full model and Name-only model with matching Name and Class must be equal.");
            Assert.IsTrue(nameOnlyModel.Equals(fullModel), "Symmetry requirement: Name-only model must equal Full model.");
            Assert.AreEqual(fullModel.GetHashCode(), nameOnlyModel.GetHashCode(), "Full model and Name-only model matching on Name must produce identical hash codes.");
        }

        [Test]
        public void GetHashCode_AsymmetricEquality_DifferingInvalidIds_MatchesOnName()
        {
            var model1 = new ElementIdModel { Id = -1, Name = "Door Standard", Class = "Autodesk.Revit.DB.FamilyInstance" };
            var model2 = new ElementIdModel { Id = -2, Name = "door standard", Class = "autodesk.revit.db.familyinstance" };

            Assert.IsTrue(model1.Equals(model2), "Models with differing invalid IDs (-1 vs -2) and matching Name must be equal.");
            Assert.AreEqual(model1.GetHashCode(), model2.GetHashCode(), "Models with differing invalid IDs matching on Name must produce identical hash codes.");
        }

        [Test]
        public void DictionaryLookup_AsymmetricModels_Succeeds()
        {
            var fullKey = new ElementIdModel { UniqueId = "GUID-1234", Id = 101, Name = "Wall_A", Class = "Autodesk.Revit.DB.Wall" };
            var nameOnlyLookupKey = new ElementIdModel { Name = "wall_a", Class = "Autodesk.Revit.DB.Wall" };

            var dict = new Dictionary<ElementIdModel, string>
            {
                { fullKey, "WinningValue" }
            };

            Assert.IsTrue(dict.ContainsKey(nameOnlyLookupKey), "Dictionary lookup should succeed using asymmetric Name-only ElementIdModel lookup key.");
            Assert.AreEqual("WinningValue", dict[nameOnlyLookupKey], "Dictionary value retrieval should match expected value for asymmetric key.");
        }

        [Test]
        public void GetHashCode_Consistency_AcrossAllFallbackSteps()
        {
            // Fallback 1: UniqueId match (no Name)
            var u1 = new ElementIdModel { UniqueId = "GUID-ABC", Id = 10, Class = "Autodesk.Revit.DB.Wall" };
            var u2 = new ElementIdModel { UniqueId = "guid-abc", Id = 20, Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsTrue(u1.Equals(u2));
            Assert.AreEqual(u1.GetHashCode(), u2.GetHashCode(), "Step 1: UniqueId match must produce identical HashCodes.");

            // Fallback 2: Id match (no UniqueId or Name)
            var i1 = new ElementIdModel { Id = 500, Class = "Autodesk.Revit.DB.Wall" };
            var i2 = new ElementIdModel { Id = 500, Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsTrue(i1.Equals(i2));
            Assert.AreEqual(i1.GetHashCode(), i2.GetHashCode(), "Step 2: Id match must produce identical HashCodes.");

            // Fallback 4: Name match (no UniqueId or Id)
            var n1 = new ElementIdModel { Name = "Shared Wall", Category = "Walls" };
            var n2 = new ElementIdModel { Name = "shared wall", Category = "walls" };
            Assert.IsTrue(n1.Equals(n2));
            Assert.AreEqual(n1.GetHashCode(), n2.GetHashCode(), "Step 4: Name match must produce identical HashCodes.");

            // Fallback: Default/Empty models
            var d1 = new ElementIdModel();
            var d2 = new ElementIdModel();
            Assert.IsTrue(d1.Equals(d2));
            Assert.AreEqual(d1.GetHashCode(), d2.GetHashCode(), "Default empty models must produce identical HashCodes.");
        }

        #endregion
    }
}
