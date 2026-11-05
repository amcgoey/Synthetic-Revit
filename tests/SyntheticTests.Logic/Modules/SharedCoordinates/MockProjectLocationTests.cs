using System;
using Autodesk.Revit.DB;
using NUnit.Framework;

namespace SyntheticTests.Modules.SharedCoordinates
{
    [TestFixture]
    public class MockProjectLocationTests
    {
        [Test]
        public void Transform_Inverse_HandlesCombinedRotationAndTranslation()
        {
            // Given a transform with rotation (e.g. 30 degrees around Z) and translation (10, 20, 30)
            double angle = Math.PI / 6.0; // 30 deg
            var t = new Transform
            {
                BasisX = new XYZ(Math.Cos(angle), Math.Sin(angle), 0),
                BasisY = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0),
                BasisZ = new XYZ(0, 0, 1),
                Origin = new XYZ(10.0, 20.0, 30.0)
            };

            var tInv = t.Inverse;

            // Identity round-trip for arbitrary points
            var p1 = new XYZ(100.0, -50.0, 25.0);
            var roundTrip1 = tInv.OfPoint(t.OfPoint(p1));
            Assert.AreEqual(p1.X, roundTrip1.X, 1e-6);
            Assert.AreEqual(p1.Y, roundTrip1.Y, 1e-6);
            Assert.AreEqual(p1.Z, roundTrip1.Z, 1e-6);

            var roundTrip2 = t.OfPoint(tInv.OfPoint(p1));
            Assert.AreEqual(p1.X, roundTrip2.X, 1e-6);
            Assert.AreEqual(p1.Y, roundTrip2.Y, 1e-6);
            Assert.AreEqual(p1.Z, roundTrip2.Z, 1e-6);

            // Origin of inverse must be -R^T * Origin
            // R^T * Origin = (cos * 10 + sin * 20, -sin * 10 + cos * 20, 30)
            double expectedInvOriginX = -(Math.Cos(angle) * 10.0 + Math.Sin(angle) * 20.0);
            double expectedInvOriginY = -(-Math.Sin(angle) * 10.0 + Math.Cos(angle) * 20.0);
            double expectedInvOriginZ = -30.0;

            Assert.AreEqual(expectedInvOriginX, tInv.Origin.X, 1e-6);
            Assert.AreEqual(expectedInvOriginY, tInv.Origin.Y, 1e-6);
            Assert.AreEqual(expectedInvOriginZ, tInv.Origin.Z, 1e-6);
        }

        [Test]
        public void ProjectLocation_WhenSetProjectPositionCalled_ComputesRevitExactSharedToInternalTransform()
        {
            var loc = new ProjectLocation { Name = "Site A" };
            double angle = Math.PI / 4.0; // 45 deg: cos = sin = sqrt(2)/2 ~= 0.70710678
            double ew = 100.0;
            double ns = 200.0;
            double elev = 50.0;

            loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(ew, ns, elev, angle));

            var totalTransform = loc.GetTotalTransform();

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            // Basis vectors
            Assert.AreEqual(cos, totalTransform.BasisX.X, 1e-6);
            Assert.AreEqual(sin, totalTransform.BasisX.Y, 1e-6);
            Assert.AreEqual(0.0, totalTransform.BasisX.Z, 1e-6);

            Assert.AreEqual(-sin, totalTransform.BasisY.X, 1e-6);
            Assert.AreEqual(cos, totalTransform.BasisY.Y, 1e-6);
            Assert.AreEqual(0.0, totalTransform.BasisY.Z, 1e-6);

            Assert.AreEqual(0.0, totalTransform.BasisZ.X, 1e-6);
            Assert.AreEqual(0.0, totalTransform.BasisZ.Y, 1e-6);
            Assert.AreEqual(1.0, totalTransform.BasisZ.Z, 1e-6);

            // Origin: (-cos * ew + sin * ns, -sin * ew - cos * ns, -elev)
            double expectedOriginX = -cos * ew + sin * ns;
            double expectedOriginY = -sin * ew - cos * ns;
            double expectedOriginZ = -elev;

            Assert.AreEqual(expectedOriginX, totalTransform.Origin.X, 1e-6);
            Assert.AreEqual(expectedOriginY, totalTransform.Origin.Y, 1e-6);
            Assert.AreEqual(expectedOriginZ, totalTransform.Origin.Z, 1e-6);
        }

        [Test]
        public void ProjectLocation_GetProjectPosition_InvertsTotalTransformAtInternalPoints()
        {
            var loc = new ProjectLocation { Name = "Site A" };
            double angle = 0.5;
            double ew = 120.0;
            double ns = -80.0;
            double elev = 35.0;

            loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(ew, ns, elev, angle));

            // Internal origin (0, 0, 0) should return the exact ProjectPosition
            var posAtOrigin = loc.GetProjectPosition(XYZ.Zero);
            Assert.AreEqual(ew, posAtOrigin.EastWest, 1e-6);
            Assert.AreEqual(ns, posAtOrigin.NorthSouth, 1e-6);
            Assert.AreEqual(elev, posAtOrigin.Elevation, 1e-6);
            Assert.AreEqual(angle, posAtOrigin.Angle, 1e-6);

            // Internal point P should have shared coordinates equal to totalTransform.Inverse.OfPoint(P)
            var pInternal = new XYZ(10.0, 20.0, 5.0);
            var posAtP = loc.GetProjectPosition(pInternal);
            var expectedSharedPt = loc.GetTotalTransform().Inverse.OfPoint(pInternal);

            Assert.AreEqual(expectedSharedPt.X, posAtP.EastWest, 1e-6);
            Assert.AreEqual(expectedSharedPt.Y, posAtP.NorthSouth, 1e-6);
            Assert.AreEqual(expectedSharedPt.Z, posAtP.Elevation, 1e-6);
            Assert.AreEqual(angle, posAtP.Angle, 1e-6);
        }

        [Test]
        public void ProjectLocation_ExplicitMockTotalTransform_OverridesDerivedTransform()
        {
            var loc = new ProjectLocation { Name = "Site Custom" };
            loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(100.0, 200.0, 50.0, 0.0));

            var customTransform = new Transform
            {
                Origin = new XYZ(999.0, 888.0, 777.0)
            };
            loc.MockTotalTransform = customTransform;

            Assert.AreSame(customTransform, loc.GetTotalTransform());
            Assert.AreEqual(999.0, loc.GetTotalTransform().Origin.X);
        }
    }
}
