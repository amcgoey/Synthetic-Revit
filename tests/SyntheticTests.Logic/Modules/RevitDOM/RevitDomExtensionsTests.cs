using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class RevitDomExtensionsTests
    {
        [Test]
        public void DeepClone_SimpleModel_PropertiesPreserved()
        {
            // Arrange
            var original = new ElementIdModel
            {
                Id = 98765,
                Name = "SimpleElement",
                Class = "Autodesk.Revit.DB.Level",
                Category = "Levels",
                UniqueId = "level-uuid-123",
                IsTemplate = false // Set to false to ensure Id and UniqueId are serialized
            };

            // Act
            var clone = original.DeepClone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.AreNotSame(original, clone);
            Assert.AreEqual(original.Id, clone.Id);
            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreEqual(original.Class, clone.Class);
            Assert.AreEqual(original.Category, clone.Category);
            Assert.AreEqual(original.UniqueId, clone.UniqueId);
            Assert.IsFalse(clone.IsTemplate, "IsTemplate is marked [JsonIgnore] and defaults to false in constructor.");
        }

        [Test]
        public void DeepClone_NestedModel_CollectionsCloned()
        {
            // Arrange
            var original = new ElementModel
            {
                Id = 1111,
                Name = "Parent",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "parent-uuid",
                IsTemplate = false, // Set to false to ensure Id is serialized
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel
                    {
                        Id = 2222,
                        Name = "Height",
                        Value = "3000",
                        StorageType = "Double"
                    },
                    new ParameterModel
                    {
                        Id = 3333,
                        Name = "Comments",
                        Value = "Clean standards",
                        StorageType = "String"
                    }
                }
            };

            // Act
            var clone = original.DeepClone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.AreNotSame(original, clone);
            Assert.AreEqual(original.Parameters.Count, clone.Parameters.Count);
            
            for (int i = 0; i < original.Parameters.Count; i++)
            {
                Assert.AreNotSame(original.Parameters[i], clone.Parameters[i], "Nested items must have different references.");
                Assert.AreEqual(original.Parameters[i].Id, clone.Parameters[i].Id);
                Assert.AreEqual(original.Parameters[i].Name, clone.Parameters[i].Name);
                Assert.AreEqual(original.Parameters[i].Value, clone.Parameters[i].Value);
                Assert.AreEqual(original.Parameters[i].StorageType, clone.Parameters[i].StorageType);
            }
        }

        [Test]
        public void DeepClone_StateIsolation_ModifyingCloneDoesNotAffectOriginal()
        {
            // Arrange
            var original = new ElementModel
            {
                Id = 5555,
                Name = "OriginalElement",
                IsTemplate = false,
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel
                    {
                        Name = "Color",
                        Value = "Red"
                    }
                }
            };

            var clone = original.DeepClone();

            // Act
            clone.Name = "ClonedElement";
            clone.Parameters[0].Value = "Blue";

            // Assert
            Assert.AreEqual("OriginalElement", original.Name, "Modifying the clone's primitive property must not affect the original.");
            Assert.AreEqual("Red", original.Parameters[0].Value, "Modifying the clone's nested list items must not affect the original.");
        }

        [Test]
        public void DeepClone_RevitScrubbing_JsonIgnoredPropertiesDropped()
        {
            // Arrange
            var original = new ElementModel
            {
                Id = 7777,
                Name = "ScrubTest",
                IsTemplate = false, // Set to false to ensure Id is serialized
                Element = new object() // Mocked live Revit Element reference
            };

            // Act
            var clone = original.DeepClone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.IsNull(clone.Element, "Properties marked with [JsonIgnore] must be dropped during deep cloning.");
            
            // Note: Since ElementId has [JsonIgnore], the property ElementId gets initialized to a new empty ElementIdModel.
            // But because Name/Id/UniqueId delegate to ElementId and are serialized directly on ElementModel,
            // they are successfully restored in the clone.
            Assert.AreEqual(original.Id, clone.Id);
            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreNotSame(original.ElementId, clone.ElementId);
        }
    }
}
