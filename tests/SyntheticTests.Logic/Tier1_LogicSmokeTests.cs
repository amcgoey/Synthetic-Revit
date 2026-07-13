using NUnit.Framework;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier1_LogicSmokeTests
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
