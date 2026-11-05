using System;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Modules.SharedCoordinates.Models;

namespace SyntheticTests.Modules.SharedCoordinates
{
    [TestFixture]
    public class SharedCoordinatesMathTests
    {
        private static Transform CreateTransform(XYZ? origin = null, XYZ? basisX = null, XYZ? basisY = null, XYZ? basisZ = null)
        {
            var t = Transform.Identity;
            if (origin != null) t.Origin = origin;
            if (basisX != null) t.set_Basis(0, basisX);
            if (basisY != null) t.set_Basis(1, basisY);
            if (basisZ != null) t.set_Basis(2, basisZ);
            return t;
        }

        [Test]
        public void ComputeExpectedTransform_BothIdentity_ReturnsIdentity()
        {
            var host = Transform.Identity;
            var link = Transform.Identity;

            var expected = SharedCoordinatesMath.ComputeExpectedTransform(host, link);

            Assert.AreEqual(0.0, expected.Origin.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.Origin.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.Origin.Z, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(1.0, expected.BasisX.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.BasisX.Y, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeExpectedTransform_HostAndLinkPureTranslations_DerivesRelativeTranslation()
        {
            // Host survey point at (100, 200, 0)
            var host = CreateTransform(
                new XYZ(100.0, 200.0, 0.0),
                new XYZ(1, 0, 0),
                new XYZ(0, 1, 0),
                new XYZ(0, 0, 1));

            // Link survey point at (150, 230, 25)
            var link = CreateTransform(
                new XYZ(150.0, 230.0, 25.0),
                new XYZ(1, 0, 0),
                new XYZ(0, 1, 0),
                new XYZ(0, 0, 1));

            var expected = SharedCoordinatesMath.ComputeExpectedTransform(host, link);

            // Relative origin = (150 - 100, 230 - 200, 25 - 0) = (50, 30, 25)
            Assert.AreEqual(50.0, expected.Origin.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(30.0, expected.Origin.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(25.0, expected.Origin.Z, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeDeltaAngle_IdenticalOrientations_ReturnsZero()
        {
            var cur = Transform.Identity;
            var exp = Transform.Identity;

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);

            Assert.AreEqual(0.0, delta, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeDeltaAngle_QuarterTurnRotation_ReturnsPiOverTwo()
        {
            var cur = CreateTransform(XYZ.Zero, new XYZ(1, 0, 0), new XYZ(0, 1, 0), new XYZ(0, 0, 1));
            var exp = CreateTransform(XYZ.Zero, new XYZ(0, 1, 0), new XYZ(-1, 0, 0), new XYZ(0, 0, 1));

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);

            Assert.AreEqual(Math.PI / 2.0, delta, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeDeltaAngle_NearBoundaryWrapping_AvoidsFullTurnSpin()
        {
            // Current angle = +179 degrees (+3.124139 rad)
            double angleCur = 179.0 * Math.PI / 180.0;
            var cur = CreateTransform(
                XYZ.Zero,
                new XYZ(Math.Cos(angleCur), Math.Sin(angleCur), 0),
                new XYZ(-Math.Sin(angleCur), Math.Cos(angleCur), 0),
                new XYZ(0, 0, 1));

            // Expected angle = -179 degrees (-3.124139 rad)
            double angleExp = -179.0 * Math.PI / 180.0;
            var exp = CreateTransform(
                XYZ.Zero,
                new XYZ(Math.Cos(angleExp), Math.Sin(angleExp), 0),
                new XYZ(-Math.Sin(angleExp), Math.Cos(angleExp), 0),
                new XYZ(0, 0, 1));

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);

            // The shortest angle delta from +179 deg to -179 deg is +2 degrees (+0.034906 rad)
            double expectedDelta = 2.0 * Math.PI / 180.0;
            Assert.AreEqual(expectedDelta, delta, 1e-4);
        }

        [Test]
        public void ComputeDeltaTranslation_CalculatesAccurateVector()
        {
            var cur = CreateTransform(new XYZ(10.0, 20.0, 30.0));
            var exp = CreateTransform(new XYZ(15.0, 18.0, 42.0));

            var delta = SharedCoordinatesMath.ComputeDeltaTranslation(cur, exp);

            Assert.AreEqual(5.0, delta.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(-2.0, delta.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(12.0, delta.Z, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeAlignment_AlreadyAligned_FlagsNotOutOfSync()
        {
            var host = Transform.Identity;
            var link = CreateTransform(new XYZ(10, 20, 30));
            var cur = CreateTransform(new XYZ(10, 20, 30));

            var result = SharedCoordinatesMath.ComputeAlignment(host, link, cur);

            Assert.IsFalse(result.IsOutOfSync);
            Assert.AreEqual(0.0, result.DeltaAngle, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, result.DeltaTranslationLength, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeAlignment_PerturbationBelowTolerance_ConsideredAlreadyAligned()
        {
            // 1e-6 perturbation is strictly less than 1e-5 tolerance
            var cur = CreateTransform(new XYZ(0.0, 0.0, 0.0));
            var exp = CreateTransform(new XYZ(1e-6, 0.0, 0.0));

            var result = SharedCoordinatesMath.ComputeAlignmentFromExpected(cur, exp);

            Assert.IsFalse(result.IsOutOfSync);
        }

        [Test]
        public void ComputeAlignment_PerturbationAboveTolerance_FlagsOutOfSync()
        {
            // 2e-5 perturbation is greater than 1e-5 tolerance
            var cur = CreateTransform(new XYZ(0.0, 0.0, 0.0));
            var exp = CreateTransform(new XYZ(2e-5, 0.0, 0.0));

            var result = SharedCoordinatesMath.ComputeAlignmentFromExpected(cur, exp);

            Assert.IsTrue(result.IsOutOfSync);
        }

        [Test]
        public void NormalizeAngle_NormalizesBeyondPiRange()
        {
            double angle = 3.0 * Math.PI + 0.2; // 3pi + 0.2 -> -pi + 0.2
            double norm = SharedCoordinatesMath.NormalizeAngle(angle);

            Assert.IsTrue(norm <= Math.PI && norm > -Math.PI);
            Assert.AreEqual(-Math.PI + 0.2, norm, 1e-6);
        }
    }
}
