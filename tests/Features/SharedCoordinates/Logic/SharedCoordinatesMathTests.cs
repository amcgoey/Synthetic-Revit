using System;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Models;

namespace SyntheticTests.Features.SharedCoordinates
{
    [TestFixture]
    [UnitTest]
    public class SharedCoordinatesMathTests
    {
        private static RigidTransform3D CreateTransform(
            Vector3D? origin = null,
            Vector3D? basisX = null,
            Vector3D? basisY = null,
            Vector3D? basisZ = null)
        {
            return new RigidTransform3D(
                origin ?? Vector3D.Zero,
                basisX ?? Vector3D.UnitX,
                basisY ?? Vector3D.UnitY,
                basisZ ?? Vector3D.UnitZ);
        }

        [Test]
        public void ComputeExpectedTransform_BothIdentity_ReturnsIdentity()
        {
            RigidTransform3D host = RigidTransform3D.Identity;
            RigidTransform3D link = RigidTransform3D.Identity;

            RigidTransform3D expected = SharedCoordinatesMath.ComputeExpectedTransform(host, link);

            Assert.AreEqual(0.0, expected.Origin.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.Origin.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.Origin.Z, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(1.0, expected.BasisX.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.BasisX.Y, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeExpectedTransform_HostAndLinkPureTranslations_DerivesRelativeTranslation()
        {
            // Host transform origin at (100, 200, 0)
            RigidTransform3D host = CreateTransform(
                new Vector3D(100.0, 200.0, 0.0),
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(0.0, 1.0, 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            // Link transform origin at (150, 230, 25)
            RigidTransform3D link = CreateTransform(
                new Vector3D(150.0, 230.0, 25.0),
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(0.0, 1.0, 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            RigidTransform3D expected = SharedCoordinatesMath.ComputeExpectedTransform(host, link);

            // Relative origin = (100 - 150, 200 - 230, 0 - 25) = (-50, -30, -25)
            Assert.AreEqual(-50.0, expected.Origin.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(-30.0, expected.Origin.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(-25.0, expected.Origin.Z, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeDeltaAngle_IdenticalOrientations_ReturnsZero()
        {
            RigidTransform3D cur = RigidTransform3D.Identity;
            RigidTransform3D exp = RigidTransform3D.Identity;

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);

            Assert.AreEqual(0.0, delta, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeDeltaAngle_QuarterTurnRotation_ReturnsPiOverTwo()
        {
            RigidTransform3D cur = CreateTransform(
                Vector3D.Zero,
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(0.0, 1.0, 0.0),
                new Vector3D(0.0, 0.0, 1.0));
            RigidTransform3D exp = CreateTransform(
                Vector3D.Zero,
                new Vector3D(0.0, 1.0, 0.0),
                new Vector3D(-1.0, 0.0, 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);

            Assert.AreEqual(Math.PI / 2.0, delta, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeDeltaAngle_NearBoundaryWrapping_AvoidsFullTurnSpin()
        {
            // Current angle = +179 degrees (+3.124139 rad)
            double angleCur = 179.0 * Math.PI / 180.0;
            RigidTransform3D cur = CreateTransform(
                Vector3D.Zero,
                new Vector3D(Math.Cos(angleCur), Math.Sin(angleCur), 0.0),
                new Vector3D(-Math.Sin(angleCur), Math.Cos(angleCur), 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            // Expected angle = -179 degrees (-3.124139 rad)
            double angleExp = -179.0 * Math.PI / 180.0;
            RigidTransform3D exp = CreateTransform(
                Vector3D.Zero,
                new Vector3D(Math.Cos(angleExp), Math.Sin(angleExp), 0.0),
                new Vector3D(-Math.Sin(angleExp), Math.Cos(angleExp), 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);

            // Shortest angle delta from +179 deg to -179 deg is +2 degrees (+0.034906 rad)
            double expectedDelta = 2.0 * Math.PI / 180.0;
            Assert.AreEqual(expectedDelta, delta, 1e-4);
        }

        [Test]
        public void ComputeDeltaTranslation_CalculatesAccurateVector()
        {
            RigidTransform3D cur = CreateTransform(new Vector3D(10.0, 20.0, 30.0));
            RigidTransform3D exp = CreateTransform(new Vector3D(15.0, 18.0, 42.0));

            Vector3D delta = SharedCoordinatesMath.ComputeDeltaTranslation(cur, exp);

            Assert.AreEqual(5.0, delta.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(-2.0, delta.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(12.0, delta.Z, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeAlignment_AlreadyAligned_FlagsNotOutOfSync()
        {
            RigidTransform3D host = RigidTransform3D.Identity;
            RigidTransform3D link = CreateTransform(new Vector3D(10.0, 20.0, 30.0));
            RigidTransform3D cur = SharedCoordinatesMath.ComputeExpectedTransform(host, link);

            CoordinateTransformResult result = SharedCoordinatesMath.ComputeAlignment(host, link, cur);

            Assert.IsFalse(result.IsOutOfSync);
            Assert.AreEqual(0.0, result.DeltaAngle, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, result.DeltaTranslationLength, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeAlignment_PerturbationBelowTolerance_ConsideredAlreadyAligned()
        {
            // 1e-6 perturbation is strictly less than 1e-5 tolerance
            RigidTransform3D cur = CreateTransform(new Vector3D(0.0, 0.0, 0.0));
            RigidTransform3D exp = CreateTransform(new Vector3D(1e-6, 0.0, 0.0));

            CoordinateTransformResult result = SharedCoordinatesMath.ComputeAlignmentFromExpected(cur, exp);

            Assert.IsFalse(result.IsOutOfSync);
        }

        [Test]
        public void ComputeAlignment_PerturbationAboveTolerance_FlagsOutOfSync()
        {
            // 2e-5 perturbation is greater than 1e-5 tolerance
            RigidTransform3D cur = CreateTransform(new Vector3D(0.0, 0.0, 0.0));
            RigidTransform3D exp = CreateTransform(new Vector3D(2e-5, 0.0, 0.0));

            CoordinateTransformResult result = SharedCoordinatesMath.ComputeAlignmentFromExpected(cur, exp);

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

        [Test]
        public void ComputeExpectedTransform_PureElevation_AlignsVerticalDisplacement()
        {
            RigidTransform3D hostTotal = RigidTransform3D.FromProjectPosition(0.0, 0.0, 0.0, 0.0);
            RigidTransform3D linkTotal = RigidTransform3D.FromProjectPosition(0.0, 0.0, 50.0, 0.0);

            RigidTransform3D expected = SharedCoordinatesMath.ComputeExpectedTransform(hostTotal, linkTotal);

            // T_expected origin must be +50 in host internal coordinates
            Assert.AreEqual(0.0, expected.Origin.X, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(0.0, expected.Origin.Y, SharedCoordinatesMath.Tolerance);
            Assert.AreEqual(50.0, expected.Origin.Z, SharedCoordinatesMath.Tolerance);
        }

        [Test]
        public void ComputeExpectedTransform_TrueNorthRotation_ComputesCorrectRelativeAngleAndBasis()
        {
            RigidTransform3D hostTotal = RigidTransform3D.FromProjectPosition(0.0, 0.0, 0.0, 0.0);

            // Link rotated by 30 degrees (pi / 6) True North angle
            double angle = Math.PI / 6.0;
            RigidTransform3D linkTotal = RigidTransform3D.FromProjectPosition(100.0, 200.0, 0.0, angle);

            RigidTransform3D expected = SharedCoordinatesMath.ComputeExpectedTransform(hostTotal, linkTotal);

            // Revit GetTotalTransform() rotates Shared -> Internal by -angle, so T_expected = T_hostTotal * T_linkTotal^-1 rotates by +angle
            double deltaAngle = SharedCoordinatesMath.ComputeDeltaAngle(RigidTransform3D.Identity, expected);
            Assert.AreEqual(angle, deltaAngle, 1e-5);

            // Verify basis vectors match +angle rotation
            Assert.AreEqual(Math.Cos(angle), expected.BasisX.X, 1e-5);
            Assert.AreEqual(Math.Sin(angle), expected.BasisX.Y, 1e-5);
        }

        [Test]
        public void ComputeAlignment_BoundaryAngleWrapping_CorrectlyComputesShortestDelta()
        {
            // Current transform at +179 degrees
            double curAngle = 179.0 * Math.PI / 180.0;
            RigidTransform3D cur = CreateTransform(
                Vector3D.Zero,
                new Vector3D(Math.Cos(curAngle), Math.Sin(curAngle), 0.0),
                new Vector3D(-Math.Sin(curAngle), Math.Cos(curAngle), 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            // Expected transform at -179 degrees
            double expAngle = -179.0 * Math.PI / 180.0;
            RigidTransform3D exp = CreateTransform(
                Vector3D.Zero,
                new Vector3D(Math.Cos(expAngle), Math.Sin(expAngle), 0.0),
                new Vector3D(-Math.Sin(expAngle), Math.Cos(expAngle), 0.0),
                new Vector3D(0.0, 0.0, 1.0));

            CoordinateTransformResult result = SharedCoordinatesMath.ComputeAlignmentFromExpected(cur, exp);

            Assert.IsTrue(result.IsOutOfSync);
            // Shortest turn is +2 degrees (+0.0349 rad), not -358 degrees
            double expectedDelta = 2.0 * Math.PI / 180.0;
            Assert.AreEqual(expectedDelta, result.DeltaAngle, 1e-4);
        }

        [Test]
        public void ComputeAlignment_NonZeroHostAndLinkProjectPositions_ComputesExpectedDeltas()
        {
            RigidTransform3D hostTotal = RigidTransform3D.FromProjectPosition(50.0, 50.0, 10.0, 0.0);
            RigidTransform3D linkTotal = RigidTransform3D.FromProjectPosition(20.0, 10.0, 0.0, 0.0);
            RigidTransform3D current = RigidTransform3D.Identity;

            CoordinateTransformResult result = SharedCoordinatesMath.ComputeAlignment(hostTotal, linkTotal, current);

            Assert.IsTrue(result.IsOutOfSync);
            Assert.AreEqual(0.0, result.DeltaAngle, 1e-6);
            Assert.AreEqual(-30.0, result.DeltaTranslation.X, 1e-6);
            Assert.AreEqual(-40.0, result.DeltaTranslation.Y, 1e-6);
            Assert.AreEqual(-10.0, result.DeltaTranslation.Z, 1e-6);
        }

        [Test]
        public void NormalizeAngle_MinusPiAndOddNegativeMultiples_WrapCleanlyToPlusPi()
        {
            Assert.AreEqual(Math.PI, SharedCoordinatesMath.NormalizeAngle(-Math.PI));
            Assert.AreEqual(Math.PI, SharedCoordinatesMath.NormalizeAngle(Math.PI));
            Assert.AreEqual(Math.PI, SharedCoordinatesMath.NormalizeAngle(-3.0 * Math.PI), 1e-12);

            double normalizedNegativeZero = SharedCoordinatesMath.NormalizeAngle(-0.0);
            Assert.AreEqual(0.0, normalizedNegativeZero);
            Assert.IsFalse(double.IsNegativeInfinity(1.0 / normalizedNegativeZero));

            double once = SharedCoordinatesMath.NormalizeAngle(-Math.PI);
            double twice = SharedCoordinatesMath.NormalizeAngle(once);
            Assert.AreEqual(once, twice);
        }

        [Test]
        public void ComputeDeltaAngle_HalfTurnMinusPi_WrapsCleanlyToPlusPi()
        {
            RigidTransform3D cur = CreateTransform(
                Vector3D.Zero,
                new Vector3D(0.0, 1.0, 0.0),
                new Vector3D(-1.0, 0.0, 0.0),
                Vector3D.UnitZ);
            RigidTransform3D exp = CreateTransform(
                Vector3D.Zero,
                new Vector3D(0.0, -1.0, 0.0),
                new Vector3D(1.0, 0.0, 0.0),
                Vector3D.UnitZ);

            double delta = SharedCoordinatesMath.ComputeDeltaAngle(cur, exp);
            Assert.AreEqual(Math.PI, delta, 1e-12);
            Assert.IsTrue(delta > -Math.PI && delta <= Math.PI);

            RigidTransform3D curZero = RigidTransform3D.Identity;
            RigidTransform3D expNegativeZeroPi = CreateTransform(
                Vector3D.Zero,
                new Vector3D(-1.0, -0.0, 0.0),
                new Vector3D(0.0, -1.0, 0.0),
                Vector3D.UnitZ);

            double deltaNegZero = SharedCoordinatesMath.ComputeDeltaAngle(curZero, expNegativeZeroPi);
            Assert.AreEqual(Math.PI, deltaNegZero, 1e-12);
        }

        [Test]
        public void CoordinateTransformResult_DefaultInstance_InitializesTransformsToIdentity()
        {
            var result = new CoordinateTransformResult();

            Assert.AreEqual(RigidTransform3D.Identity, result.ExpectedTransform);
            Assert.AreEqual(RigidTransform3D.Identity, result.CurrentTransform);
            Assert.AreEqual(Vector3D.Zero, result.DeltaTranslation);
            Assert.AreEqual(0.0, result.DeltaTranslationLength);
            Assert.AreEqual(0.0, result.DeltaAngle);
            Assert.IsFalse(result.IsOutOfSync);
        }

        [Test]
        public void Vector3D_LengthSubtractAndDotHelpers_ComputeExpectedValues()
        {
            var a = new Vector3D(4.0, 5.0, 6.0);
            var b = new Vector3D(1.0, 1.0, 6.0);

            Vector3D diff = a.Subtract(b);
            Assert.AreEqual(new Vector3D(3.0, 4.0, 0.0), diff);
            Assert.AreEqual(diff, a - b);
            Assert.AreEqual(5.0, diff.Length, 1e-12);
            Assert.AreEqual(45.0, a.Dot(b), 1e-12);
            Assert.AreEqual(0.0, Vector3D.Zero.Length);
        }

        [Test]
        public void RigidTransform3D_MultiplyAndInverse_SatisfyInverseIdentity()
        {
            RigidTransform3D transform = RigidTransform3D.FromProjectPosition(120.5, -45.25, 33.75, Math.PI / 5.0);
            RigidTransform3D roundTrip = transform.Multiply(transform.Inverse);

            Assert.AreEqual(0.0, roundTrip.Origin.X, 1e-12);
            Assert.AreEqual(0.0, roundTrip.Origin.Y, 1e-12);
            Assert.AreEqual(0.0, roundTrip.Origin.Z, 1e-12);
            Assert.AreEqual(1.0, roundTrip.BasisX.X, 1e-12);
            Assert.AreEqual(0.0, roundTrip.BasisX.Y, 1e-12);
            Assert.AreEqual(0.0, roundTrip.BasisY.X, 1e-12);
            Assert.AreEqual(1.0, roundTrip.BasisY.Y, 1e-12);
            Assert.AreEqual(1.0, roundTrip.BasisZ.Z, 1e-12);
        }
    }
}
