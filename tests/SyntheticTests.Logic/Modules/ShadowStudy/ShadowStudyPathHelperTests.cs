using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyPathHelperTests
    {
        [Test]
        public void SanitizePathSegment_ReplacesIllegalCharactersAndHandlesEmpty()
        {
            Assert.AreEqual("Site_Plan_A_B_C", ShadowStudyPathHelper.SanitizePathSegment("Site/Plan:A*B?C"));
            Assert.AreEqual("Untitled", ShadowStudyPathHelper.SanitizePathSegment("   "));
            Assert.AreEqual("Untitled", ShadowStudyPathHelper.SanitizePathSegment(null));
        }

        [Test]
        public void AssignViewLabels_AppendsViewTypeOnlyOnNameCollision()
        {
            var views = new List<ShadowStudyViewItem>
            {
                new ShadowStudyViewItem { Id = "1", Name = "Main Plan", ViewType = "FloorPlan" },
                new ShadowStudyViewItem { Id = "2", Name = "Main Plan", ViewType = "CeilingPlan" },
                new ShadowStudyViewItem { Id = "3", Name = "North/South: View", ViewType = "Section" }
            };

            ShadowStudyPathHelper.AssignViewLabels(views);

            Assert.AreEqual("Main Plan (FloorPlan)", views[0].ViewLabel);
            Assert.AreEqual("Main Plan (CeilingPlan)", views[1].ViewLabel);
            Assert.AreEqual("North_South_ View", views[2].ViewLabel);
        }

        [Test]
        public void FormattingHelpers_ProduceExpectedDateAndCompositeNames()
        {
            string dateLabel = ShadowStudyPathHelper.FormatDateLabel(new DateTime(2026, 6, 21));
            Assert.AreEqual("260621", dateLabel);

            string presetLabel = ShadowStudyPathHelper.FormatDateLabel(new DateTime(2026, 6, 21), "Summer Solstice");
            Assert.AreEqual("260621 - Summer Solstice", presetLabel);

            Assert.AreEqual("0800", ShadowStudyPathHelper.FormatTimeSlot(new DateTime(2026, 6, 21, 8, 0, 0)));
            Assert.AreEqual("1730", ShadowStudyPathHelper.FormatTimeSlot(new DateTime(2026, 6, 21, 17, 30, 0)));

            Assert.AreEqual(
                "Level 1 - 260621 - Composite.png",
                ShadowStudyPathHelper.BuildCompositeFileName("Level 1", dateLabel));

            Assert.AreEqual(
                "Level 1 - 260621",
                ShadowStudyPathHelper.BuildFrameFolderName("Level 1", dateLabel));

            Assert.AreEqual(
                "Level 1 - 260621 - 0800.png",
                ShadowStudyPathHelper.BuildFrameFileName("Level 1", dateLabel, new DateTime(2026, 6, 21, 8, 0, 0)));

            Assert.AreEqual(
                "Level 1 - 260621 - 1330.png",
                ShadowStudyPathHelper.BuildFrameFileName("Level 1", dateLabel, new DateTime(2026, 6, 21, 13, 30, 0)));
        }

        [Test]
        public void ValidateOutputPathLength_ThrowsWhenExceedingWindowsSafeLimit()
        {
            string tooLong = Path.Combine("C:\\", new string('a', 260), "Composite.png");
            Assert.Throws<PathTooLongException>(() => ShadowStudyPathHelper.ValidateOutputPathLength(tooLong));
        }
    }
}
