using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.Platform.IO;

namespace SyntheticTests.Platform.IO
{
    [TestFixture]
    [UnitTest]
    public class EnumUtilTests
    {
        private enum ByteBackedSample : byte
        {
            Zero = 0,
            Two = 2
        }

        [Test]
        public void GetEnumType_ResolvesKnownEnumAndRejectsNonEnumOrMissing()
        {
            Assert.AreEqual(typeof(DayOfWeek), EnumUtil.GetEnumType("System.DayOfWeek"));
            Assert.IsNull(EnumUtil.GetEnumType("System.String"));
            Assert.IsNull(EnumUtil.GetEnumType("Synthetic.DoesNotExist.FakeEnum"));
        }

        [Test]
        public void ParseAndIsDefined_HandleValidAndInvalidMembers()
        {
            Assert.AreEqual(DayOfWeek.Wednesday, EnumUtil.Parse("System.DayOfWeek", "Wednesday"));
            Assert.IsNull(EnumUtil.Parse("System.DayOfWeek", "NotADay"));
            Assert.IsNull(EnumUtil.Parse("Synthetic.MissingEnum", "Value"));

            Assert.IsTrue(EnumUtil.IsDefined("System.DayOfWeek", "Sunday"));
            Assert.IsFalse(EnumUtil.IsDefined("System.DayOfWeek", "Funday"));
        }

        [Test]
        public void GetNamesGetNameGetEnumsAndGetValue_ReturnExpectedMetadata()
        {
            List<string> names = EnumUtil.GetNames("System.DayOfWeek");
            CollectionAssert.AreEqual(
                new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
                names);

            Assert.AreEqual("Friday", EnumUtil.GetName(DayOfWeek.Friday));

            List<object> values = EnumUtil.GetEnums("System.DayOfWeek");
            Assert.AreEqual(7, values.Count);
            Assert.Contains(DayOfWeek.Monday, values);

            object underlyingInt = EnumUtil.GetValue(DayOfWeek.Tuesday);
            Assert.IsInstanceOf<int>(underlyingInt);
            Assert.AreEqual(2, underlyingInt);

            object underlyingByte = EnumUtil.GetValue(ByteBackedSample.Two);
            Assert.IsInstanceOf<byte>(underlyingByte);
            Assert.AreEqual((byte)2, underlyingByte);
        }
    }
}
