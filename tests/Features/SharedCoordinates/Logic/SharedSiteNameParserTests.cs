using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Services;

namespace SyntheticTests.Features.SharedCoordinates
{
    [TestFixture]
    [UnitTest]
    public class SharedSiteNameParserTests
    {
        [Test]
        public void ResolveFromInstanceName_HasLocationKeyword_ExtractsSiteUsingLastOccurrenceAndCanonicalCasing()
        {
            // Single " : location " with empty site list extracts raw site name
            Assert.AreEqual(
                "Floor 3",
                SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : location Floor 3", Array.Empty<string>()));

            // Multiple " : location " occurrences select the last valid segment (LastIndexOf)
            Assert.AreEqual(
                "Floor 3",
                SharedSiteNameParser.ResolveFromInstanceName("Project : location Old.rvt : 1 : location Floor 3", new[] { "Floor 3" }));

            // Case-insensitive match returns canonical casing from knownLinkSiteNames
            Assert.AreEqual(
                "Floor 3",
                SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : LOCATION floor 3 : extra", new[] { "Floor 3" }));

            // Composite name with earlier " : location " followed by trailing " : location <Not Shared>"
            Assert.AreEqual(
                "Floor_3_Test",
                SharedSiteNameParser.ResolveFromInstanceName(
                    "UnitModel.rvt : 1 : location Floor_3_Test : location <Not Shared>",
                    new[] { "Floor_3_Test" }));
        }

        [Test]
        public void ResolveFromInstanceName_TrailingSegmentMatchesKnownSite_ReturnsCanonicalCasing()
        {
            string? resolved = SharedSiteNameParser.ResolveFromInstanceName(
                "Unit_A.rvt : 1 : floor 3",
                new[] { "Other Site", "Floor 3" });

            Assert.AreEqual("Floor 3", resolved);
        }

        [Test]
        public void ResolveFromInstanceName_TrailingSegmentDoesNotMatchKnownSite_ReturnsNull()
        {
            string? resolved = SharedSiteNameParser.ResolveFromInstanceName(
                "Unit_A.rvt : 1 : Floor 3",
                new[] { "Different Site" });

            Assert.IsNull(resolved);
        }

        [Test]
        public void ResolveFromInstanceName_ExplicitNotShared_ReturnsNull()
        {
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 2 : <Not Shared>", Array.Empty<string>()));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : location <Not Shared>", new[] { "Floor 3" }));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 2 : <not shared>", new[] { "2" }));
        }

        [Test]
        public void ResolveFromInstanceName_EndsWithNotSharedWithMatchingNumericSite_ReturnsNull()
        {
            string? resolved = SharedSiteNameParser.ResolveFromInstanceName(
                "Unit.rvt : 1 : <Not Shared>",
                new[] { "1" });

            Assert.IsNull(
                resolved,
                "Instances with <Not Shared> must return null instead of falling through to match numeric site '1'.");
        }

        [Test]
        public void ResolveFromInstanceName_NullWhitespaceAndMalformedSiteLists_HandledSafely()
        {
            // Null / whitespace / single-segment instance names
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName(null, new[] { "Floor 3" }));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName(string.Empty, new[] { "Floor 3" }));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("   ", new[] { "Floor 3" }));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("Floor 3", new[] { "Floor 3" }));

            // Null or empty knownLinkSiteNames
            Assert.AreEqual("Floor 3", SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : location Floor 3", null));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : Floor 3", null));
            Assert.IsNull(SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : Floor 3", Array.Empty<string>()));

            // Null, empty, <Not Shared>, and case-duplicate entries inside knownLinkSiteNames
            var noisySites = new List<string> { null!, string.Empty, "   ", "<Not Shared>", "Floor 3", "floor 3", "FLOOR 3" };
            Assert.AreEqual("Floor 3", SharedSiteNameParser.ResolveFromInstanceName("Unit_A.rvt : 1 : FLOOR 3", noisySites));
        }
    }
}
