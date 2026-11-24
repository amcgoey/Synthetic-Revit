using System;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SharedCoordinates
{
    [TestFixture]
    [RevitAdapterTest]
    public class RevitSharedCoordinatesAdapterRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void FromProjectPosition_PositiveNonZeroAngleAndOffsets_MatchesLiveProjectLocationGetTotalTransform()
        {
            WithRolledBackDocument(doc =>
            {
                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Positive_Acute",
                    eastWest: 125.5,
                    northSouth: -42.25,
                    elevation: 37.75,
                    angleRadians: Math.PI / 6.0);

                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Positive_Obtuse",
                    eastWest: 45.0,
                    northSouth: 310.75,
                    elevation: 18.5,
                    angleRadians: 2.0 * Math.PI / 3.0);
            });
        }

        [Test]
        public void FromProjectPosition_NegativeNonZeroAngleAndOffsets_MatchesLiveProjectLocationGetTotalTransform()
        {
            WithRolledBackDocument(doc =>
            {
                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Negative_Acute",
                    eastWest: -80.25,
                    northSouth: 210.5,
                    elevation: -14.75,
                    angleRadians: -Math.PI / 3.0);

                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Negative_Obtuse",
                    eastWest: -240.5,
                    northSouth: -95.0,
                    elevation: 62.25,
                    angleRadians: -3.0 * Math.PI / 4.0);
            });
        }

        private void VerifyProjectPositionParity(
            Document doc,
            string siteName,
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
            ProjectLocation? createdLocation = null;

            doc.ExecuteTransaction($"Create {siteName}", FailurePipelines.ObserveOnly(), () =>
            {
                ProjectLocation activeLoc = doc.ActiveProjectLocation;
                Assert.IsNotNull(activeLoc, "Baseline document must expose ActiveProjectLocation.");

                createdLocation = activeLoc.Duplicate(siteName);
                ProjectPosition pos = CreateProjectPosition(doc, eastWest, northSouth, elevation, angleRadians);
                createdLocation.SetProjectPosition(XYZ.Zero, pos);
            });

            Assert.IsNotNull(createdLocation, $"Failed to create ProjectLocation '{siteName}'.");

            ProjectPosition actualPosition = createdLocation!.GetProjectPosition(XYZ.Zero);
            Transform actualTotal = createdLocation.GetTotalTransform();

            RigidTransform3D computedFromInputs = RigidTransform3D.FromProjectPosition(
                eastWest,
                northSouth,
                elevation,
                angleRadians);
            RigidTransform3D computedFromRevitPosition = RigidTransform3D.FromProjectPosition(
                actualPosition.EastWest,
                actualPosition.NorthSouth,
                actualPosition.Elevation,
                actualPosition.Angle);

            AssertTransformParity(computedFromInputs, actualTotal, 1e-6, $"{siteName} (input parameters)");
            AssertTransformParity(computedFromRevitPosition, actualTotal, 1e-6, $"{siteName} (GetProjectPosition(XYZ.Zero))");
        }

        private static ProjectPosition CreateProjectPosition(
            Document doc,
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
#if REVIT2023
            return doc.Application.Create.NewProjectPosition(eastWest, northSouth, elevation, angleRadians);
#else
            return new ProjectPosition(eastWest, northSouth, elevation, angleRadians);
#endif
        }

        private static void AssertTransformParity(
            RigidTransform3D expected,
            Transform actual,
            double tolerance,
            string context)
        {
            Assert.AreEqual(expected.Origin.X, actual.Origin.X, tolerance, $"{context}: Origin.X mismatch");
            Assert.AreEqual(expected.Origin.Y, actual.Origin.Y, tolerance, $"{context}: Origin.Y mismatch");
            Assert.AreEqual(expected.Origin.Z, actual.Origin.Z, tolerance, $"{context}: Origin.Z mismatch");

            Assert.AreEqual(expected.BasisX.X, actual.BasisX.X, tolerance, $"{context}: BasisX.X mismatch");
            Assert.AreEqual(expected.BasisX.Y, actual.BasisX.Y, tolerance, $"{context}: BasisX.Y mismatch");
            Assert.AreEqual(expected.BasisX.Z, actual.BasisX.Z, tolerance, $"{context}: BasisX.Z mismatch");

            Assert.AreEqual(expected.BasisY.X, actual.BasisY.X, tolerance, $"{context}: BasisY.X mismatch");
            Assert.AreEqual(expected.BasisY.Y, actual.BasisY.Y, tolerance, $"{context}: BasisY.Y mismatch");
            Assert.AreEqual(expected.BasisY.Z, actual.BasisY.Z, tolerance, $"{context}: BasisY.Z mismatch");

            Assert.AreEqual(expected.BasisZ.X, actual.BasisZ.X, tolerance, $"{context}: BasisZ.X mismatch");
            Assert.AreEqual(expected.BasisZ.Y, actual.BasisZ.Y, tolerance, $"{context}: BasisZ.Y mismatch");
            Assert.AreEqual(expected.BasisZ.Z, actual.BasisZ.Z, tolerance, $"{context}: BasisZ.Z mismatch");
        }
    }
}
