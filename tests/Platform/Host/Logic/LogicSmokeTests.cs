using NUnit.Framework;

namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [UnitTest]
    public class LogicSmokeTests
    {
        [Test]
        public void StandardLogicSmokeTest()
        {
            int expected = 10;
            int actual = 5 + 5;
            Assert.AreEqual(expected, actual, "Simple addition logic should pass.");
        }
    }
}
