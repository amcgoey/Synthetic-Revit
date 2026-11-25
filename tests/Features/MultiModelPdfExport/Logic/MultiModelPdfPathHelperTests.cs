using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using SyntheticTests;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    [TestFixture]
    [UnitTest]
    public class MultiModelPdfPathHelperTests
    {
        [Test]
        public void SanitizeDocumentTitleToken_ReplacesInvalidCharsStripsExtensionAndTrailingDotsSpaces()
        {
            Assert.AreEqual("TowerA_Core_Shell_1_", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("TowerA:Core/Shell<1>.rvt"));
            Assert.AreEqual("Model Name", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("  Model Name . .RVT  "));
            Assert.AreEqual("Project_v2", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("Project\tv2. . "));
            Assert.AreEqual("DotName.Phase1", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("DotName.Phase1.rvt"));
        }

        [Test]
        public void SanitizeDocumentTitleToken_ReturnsUntitledWhenDetachedOrEmpty()
        {
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(null));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(string.Empty));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("   "));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(".rvt"));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken(" . .rvt "));
            Assert.AreEqual("Untitled", MultiModelPdfPathHelper.SanitizeDocumentTitleToken("..."));
        }

        [TestCase("CON", "_CON")]
        [TestCase("prn.rvt", "_prn")]
        [TestCase("AUX", "_AUX")]
        [TestCase("nul", "_nul")]
        [TestCase("COM1", "_COM1")]
        [TestCase("com9.rvt", "_com9")]
        [TestCase("LPT1", "_LPT1")]
        [TestCase("LPT9.archive.rvt", "_LPT9.archive")]
        [TestCase("CONFIG.rvt", "CONFIG")]
        [TestCase("COM10.rvt", "COM10")]
        public void SanitizeDocumentTitleToken_NeutralizesDosReservedNames(string input, string expected)
        {
            Assert.AreEqual(expected, MultiModelPdfPathHelper.SanitizeDocumentTitleToken(input));
        }

        [TestCase(@"C:\Exports\PDFs", true)]
        [TestCase(@"C:\", true)]
        [TestCase(@"\\fileserver\Projects\Deliverables", true)]
        [TestCase(@"\\fileserver\share", true)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase(@"Exports\PDFs", false)]
        [TestCase(@"C:Exports", false)]
        [TestCase(@"\Exports", false)]
        [TestCase(@"\\fileserver", false)]
        [TestCase(@"C:\Exports\Bad|Folder", false)]
        [TestCase(@"C:\Exports\Bad:Sub", false)]
        [TestCase(@"C:\Exports\CON\PDFs", false)]
        [TestCase(@"C:\Exports\TrailingDot.", false)]
        public void TryValidateOutputFolder_EnforcesRootedOrUncAndWindowsRules(string folder, bool expectedValid)
        {
            bool actual = MultiModelPdfPathHelper.TryValidateOutputFolder(folder, out string error);
            Assert.AreEqual(expectedValid, actual, $"Folder '{folder}' validation returned unexpected result. Error='{error}'.");
            if (!expectedValid)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
        }

        [Test]
        public void ValidateOutputPathLength_Accepts254CharsAndRejects255Chars()
        {
            string root = @"C:\Exports\";
            int stemLen254 = 254 - root.Length - 4; // + ".pdf" == 254
            string path254 = root + new string('a', stemLen254) + ".pdf";
            Assert.AreEqual(254, path254.Length);
            Assert.IsTrue(MultiModelPdfPathHelper.IsPathWithinSafeLimit(path254));
            Assert.DoesNotThrow(() => MultiModelPdfPathHelper.ValidateOutputPathLength(path254));

            string path255 = root + new string('a', stemLen254 + 1) + ".pdf";
            Assert.AreEqual(255, path255.Length);
            Assert.IsFalse(MultiModelPdfPathHelper.IsPathWithinSafeLimit(path255));
            Assert.Throws<PathTooLongException>(() => MultiModelPdfPathHelper.ValidateOutputPathLength(path255));
        }

        [Test]
        public void BuildCombinedPdfPath_HandlesDriveRootAndSanitizesToken()
        {
            Assert.AreEqual(@"C:\Host.pdf", MultiModelPdfPathHelper.BuildCombinedPdfPath(@"C:\", "Host"));
            Assert.AreEqual(@"C:\Exports\TowerA_1.pdf", MultiModelPdfPathHelper.BuildCombinedPdfPath(@"C:\Exports\", "TowerA:1"));
        }

        [Test]
        public void BuildSeparateDocumentFolderPath_BuildsSanitizedPerDocumentSubfolder()
        {
            Assert.AreEqual(@"C:\Exports\TowerA_Core", MultiModelPdfPathHelper.BuildSeparateDocumentFolderPath(@"C:\Exports\", "TowerA:Core"));
            Assert.AreEqual(@"C:\Host", MultiModelPdfPathHelper.BuildSeparateDocumentFolderPath(@"C:\", "Host"));
            Assert.AreEqual(@"C:\Exports\_CON", MultiModelPdfPathHelper.BuildSeparateDocumentFolderPath(@"C:\Exports", "CON"));
            Assert.AreEqual(@"C:\Exports\Untitled", MultiModelPdfPathHelper.BuildSeparateDocumentFolderPath(@"C:\Exports", "   "));
        }

        [Test]
        public void ResolveOutputTokens_EnforcesActiveOpenLinkedPriority_AndSuffixesLinkOrNumbered()
        {
            // Input intentionally given in reverse priority order (Linked -> Open -> Active)
            var candidates = new[]
            {
                new MultiModelPdfTokenCandidate("k-link", "TowerA.rvt", MultiModelPdfDocumentOrigin.Linked),
                new MultiModelPdfTokenCandidate("k-open", "TowerA.rvt", MultiModelPdfDocumentOrigin.OpenStandalone),
                new MultiModelPdfTokenCandidate("k-active", "TowerA.rvt", MultiModelPdfDocumentOrigin.Active)
            };

            IReadOnlyDictionary<string, MultiModelPdfResolvedToken> resolved =
                MultiModelPdfPathHelper.ResolveOutputTokens(candidates);

            Assert.AreEqual("TowerA", resolved["k-active"].FinalToken);
            Assert.IsFalse(resolved["k-active"].IsCollisionDisambiguated);

            Assert.AreEqual("TowerA_2", resolved["k-open"].FinalToken);
            Assert.IsTrue(resolved["k-open"].IsCollisionDisambiguated);

            Assert.AreEqual("TowerA (Link)", resolved["k-link"].FinalToken);
            Assert.IsTrue(resolved["k-link"].IsCollisionDisambiguated);
        }

        [Test]
        public void ResolveOutputTokens_ThreeOrMoreIdenticalTitlesAcrossOrigins_NeverCollide()
        {
            var candidates = new[]
            {
                new MultiModelPdfTokenCandidate("active", "Unit_1BR.rvt", MultiModelPdfDocumentOrigin.Active),
                new MultiModelPdfTokenCandidate("link-1", "Unit_1BR.rvt", MultiModelPdfDocumentOrigin.Linked),
                new MultiModelPdfTokenCandidate("link-2", "Unit_1BR.rvt", MultiModelPdfDocumentOrigin.Linked),
                new MultiModelPdfTokenCandidate("link-3", "Unit_1BR.rvt", MultiModelPdfDocumentOrigin.Linked),
                new MultiModelPdfTokenCandidate("open-1", "Unit_1BR.rvt", MultiModelPdfDocumentOrigin.OpenStandalone),
                new MultiModelPdfTokenCandidate("open-2", "Unit_1BR.rvt", MultiModelPdfDocumentOrigin.OpenStandalone)
            };

            IReadOnlyDictionary<string, MultiModelPdfResolvedToken> resolved =
                MultiModelPdfPathHelper.ResolveOutputTokens(candidates);

            Assert.AreEqual("Unit_1BR", resolved["active"].FinalToken);
            Assert.IsFalse(resolved["active"].IsCollisionDisambiguated);

            Assert.AreEqual("Unit_1BR_2", resolved["open-1"].FinalToken);
            Assert.AreEqual("Unit_1BR_3", resolved["open-2"].FinalToken);
            Assert.AreEqual("Unit_1BR (Link)", resolved["link-1"].FinalToken);
            Assert.AreEqual("Unit_1BR_4", resolved["link-2"].FinalToken);
            Assert.AreEqual("Unit_1BR_5", resolved["link-3"].FinalToken);

            var uniqueTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MultiModelPdfResolvedToken item in resolved.Values)
            {
                Assert.IsTrue(uniqueTokens.Add(item.FinalToken), $"Duplicate token emitted: {item.FinalToken}");
            }
            Assert.AreEqual(6, uniqueTokens.Count);
        }

        [Test]
        public void ResolveOutputTokens_ReservesPreExistingNumberedAndLinkTokens_AndCaseInsensitiveCollisions()
        {
            var candidates = new[]
            {
                new MultiModelPdfTokenCandidate("active", "tower.rvt", MultiModelPdfDocumentOrigin.Active),
                new MultiModelPdfTokenCandidate("open-dup", "TOWER.rvt", MultiModelPdfDocumentOrigin.OpenStandalone),
                new MultiModelPdfTokenCandidate("link-dup", "Tower.rvt", MultiModelPdfDocumentOrigin.Linked),
                new MultiModelPdfTokenCandidate("link-pre-2", "Tower_2.rvt", MultiModelPdfDocumentOrigin.Linked),
                new MultiModelPdfTokenCandidate("open-pre-link", "Tower (Link).rvt", MultiModelPdfDocumentOrigin.OpenStandalone)
            };

            IReadOnlyDictionary<string, MultiModelPdfResolvedToken> resolved =
                MultiModelPdfPathHelper.ResolveOutputTokens(candidates);

            Assert.AreEqual("tower", resolved["active"].FinalToken);
            Assert.IsFalse(resolved["active"].IsCollisionDisambiguated);

            // Pre-existing Tower_2 and Tower (Link) keep their real names without being marked renamed
            Assert.AreEqual("Tower_2", resolved["link-pre-2"].FinalToken);
            Assert.IsFalse(resolved["link-pre-2"].IsCollisionDisambiguated);

            Assert.AreEqual("Tower (Link)", resolved["open-pre-link"].FinalToken);
            Assert.IsFalse(resolved["open-pre-link"].IsCollisionDisambiguated);

            // Colliding Open skips reserved Tower_2 -> gets TOWER_3; colliding Linked skips reserved Tower (Link) -> gets Tower_4
            Assert.AreEqual("TOWER_3", resolved["open-dup"].FinalToken);
            Assert.IsTrue(resolved["open-dup"].IsCollisionDisambiguated);

            Assert.AreEqual("Tower_4", resolved["link-dup"].FinalToken);
            Assert.IsTrue(resolved["link-dup"].IsCollisionDisambiguated);
        }

        [Test]
        public void ResolveOutputTokens_HandlesDetachedEmptyUntitledAndSanitizedCollisions()
        {
            var candidates = new[]
            {
                new MultiModelPdfTokenCandidate("d1", "", MultiModelPdfDocumentOrigin.Active),
                new MultiModelPdfTokenCandidate("d2", "  .rvt ", MultiModelPdfDocumentOrigin.OpenStandalone),
                new MultiModelPdfTokenCandidate("s1", "Tower:1.rvt", MultiModelPdfDocumentOrigin.OpenStandalone),
                new MultiModelPdfTokenCandidate("s2", "Tower/1.rvt", MultiModelPdfDocumentOrigin.Linked)
            };

            IReadOnlyDictionary<string, MultiModelPdfResolvedToken> resolved =
                MultiModelPdfPathHelper.ResolveOutputTokens(candidates);

            Assert.AreEqual("Untitled", resolved["d1"].FinalToken);
            Assert.IsFalse(resolved["d1"].IsCollisionDisambiguated);
            Assert.AreEqual("Untitled_2", resolved["d2"].FinalToken);
            Assert.IsTrue(resolved["d2"].IsCollisionDisambiguated);

            Assert.AreEqual("Tower_1", resolved["s1"].FinalToken);
            Assert.IsFalse(resolved["s1"].IsCollisionDisambiguated);
            Assert.AreEqual("Tower_1 (Link)", resolved["s2"].FinalToken);
            Assert.IsTrue(resolved["s2"].IsCollisionDisambiguated);
        }
    }
}
