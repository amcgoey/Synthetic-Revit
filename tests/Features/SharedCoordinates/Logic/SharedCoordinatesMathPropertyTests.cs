using System;
using CsCheck;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Models;

namespace SyntheticTests.Features.SharedCoordinates
{
    [TestFixture]
    [UnitTest]
    public class SharedCoordinatesMathPropertyTests
    {
        private static readonly Gen<Vector3D> GenBoundedVector = Gen.Select(
            Gen.Double[-10000.0, 10000.0],
            Gen.Double[-10000.0, 10000.0],
            Gen.Double[-10000.0, 10000.0],
            (x, y, z) => new Vector3D(x, y, z));

        private static readonly Gen<RigidTransform3D> GenEulerOrthonormalFrame = Gen.Select(
            Gen.Double[-2.0 * Math.PI, 2.0 * Math.PI],
            Gen.Double[-2.0 * Math.PI, 2.0 * Math.PI],
            Gen.Double[-2.0 * Math.PI, 2.0 * Math.PI],
            GenBoundedVector,
            (rx, ry, rz, origin) => CreateEulerFrame(origin, rx, ry, rz));

        private static readonly Gen<RigidTransform3D> GenHorizontalFrame = Gen.Select(
            Gen.Double[-10000.0, 10000.0],
            Gen.Double[-10000.0, 10000.0],
            Gen.Double[-10000.0, 10000.0],
            Gen.Double[-2.0 * Math.PI, 2.0 * Math.PI],
            (ew, ns, elev, angle) => RigidTransform3D.FromProjectPosition(ew, ns, elev, angle));

        private static readonly Gen<RigidTransform3D> GenAnyOrthonormalFrame =
            Gen.OneOf(GenEulerOrthonormalFrame, GenHorizontalFrame);

        [Test]
        public void RigidTransform3D_OrthonormalFrames_SatisfyInverseIdentityAndPointRoundTrip()
        {
            Gen.Select(GenAnyOrthonormalFrame, GenBoundedVector)
                .Sample((transform, point) =>
                {
                    AssertOrthonormalBasis(transform, 1e-12);

                    RigidTransform3D inverse = transform.Inverse;
                    AssertOrthonormalBasis(inverse, 1e-12);

                    RigidTransform3D forwardRoundTrip = transform.Multiply(inverse);
                    RigidTransform3D reverseRoundTrip = inverse.Multiply(transform);

                    AssertTransformsClose(forwardRoundTrip, RigidTransform3D.Identity, 1e-9, "T * T^-1");
                    AssertTransformsClose(reverseRoundTrip, RigidTransform3D.Identity, 1e-9, "T^-1 * T");

                    Vector3D recoveredFromForward = inverse.OfPoint(transform.OfPoint(point));
                    Vector3D recoveredFromReverse = transform.OfPoint(inverse.OfPoint(point));

                    AssertVectorsClose(recoveredFromForward, point, 1e-9, "T^-1(T(p))");
                    AssertVectorsClose(recoveredFromReverse, point, 1e-9, "T(T^-1(p))");
                }, iter: 400);
        }

        [Test]
        public void RigidTransform3D_Composition_IsAssociativeAndMatchesPointApplication()
        {
            Gen.Select(GenAnyOrthonormalFrame, GenAnyOrthonormalFrame, GenAnyOrthonormalFrame, GenBoundedVector)
                .Sample((a, b, c, point) =>
                {
                    RigidTransform3D leftAssoc = (a * b) * c;
                    RigidTransform3D rightAssoc = a * (b * c);

                    AssertTransformsClose(leftAssoc, rightAssoc, 1e-8, "(A * B) * C == A * (B * C)");

                    Vector3D composedPoint = (a * b).OfPoint(point);
                    Vector3D sequentialPoint = a.OfPoint(b.OfPoint(point));

                    AssertVectorsClose(composedPoint, sequentialPoint, 1e-8, "(A * B)(p) == A(B(p))");
                }, iter: 400);
        }

        [Test]
        public void NormalizeAngle_FiniteAnglesAndPiBoundaries_StayInHalfOpenRangeAndAreIdempotent()
        {
            Gen<double> genAngles = Gen.OneOf(
                Gen.Double[-1000.0, 1000.0],
                Gen.Double.Normal,
                Gen.OneOfConst(
                    -Math.PI,
                    Math.PI,
                    -0.0,
                    0.0,
                    -2.0 * Math.PI,
                    2.0 * Math.PI,
                    -3.0 * Math.PI,
                    3.0 * Math.PI));

            genAngles.Sample(angle =>
            {
                double normalized = SharedCoordinatesMath.NormalizeAngle(angle);

                Assert.IsFalse(double.IsNaN(normalized), $"NormalizeAngle({angle}) returned NaN.");
                Assert.IsTrue(
                    normalized > -Math.PI && normalized <= Math.PI,
                    $"NormalizeAngle({angle}) = {normalized} fell outside (-pi, pi].");

                if (normalized == 0.0)
                {
                    Assert.IsFalse(
                        double.IsNegativeInfinity(1.0 / normalized),
                        "NormalizeAngle must canonicalize negative zero to +0.0.");
                }

                double twice = SharedCoordinatesMath.NormalizeAngle(normalized);
                Assert.AreEqual(normalized, twice, "NormalizeAngle must be idempotent.");

                if (Math.Abs(angle) <= 1000.0)
                {
                    Assert.AreEqual(Math.Cos(angle), Math.Cos(normalized), 1e-10, "Cosine must be invariant under NormalizeAngle.");
                    Assert.AreEqual(Math.Sin(angle), Math.Sin(normalized), 1e-10, "Sine must be invariant under NormalizeAngle.");
                }
            }, iter: 500);

            Assert.AreEqual(Math.PI, SharedCoordinatesMath.NormalizeAngle(-Math.PI));
            Assert.AreEqual(Math.PI, SharedCoordinatesMath.NormalizeAngle(Math.PI));
        }

        [Test]
        public void ComputeAlignment_CurrentEqualsExpected_HasExactZeroDrift()
        {
            Gen.Select(GenAnyOrthonormalFrame, GenHorizontalFrame, GenHorizontalFrame)
                .Sample((arbitraryFrame, hostFrame, linkFrame) =>
                {
                    CoordinateTransformResult directResult = SharedCoordinatesMath.ComputeAlignmentFromExpected(arbitraryFrame, arbitraryFrame);
                    Assert.IsFalse(directResult.IsOutOfSync);
                    Assert.AreEqual(0.0, directResult.DeltaAngle);
                    Assert.AreEqual(Vector3D.Zero, directResult.DeltaTranslation);
                    Assert.AreEqual(0.0, directResult.DeltaTranslationLength);

                    RigidTransform3D expectedFromPair = SharedCoordinatesMath.ComputeExpectedTransform(hostFrame, linkFrame);
                    CoordinateTransformResult pairResult = SharedCoordinatesMath.ComputeAlignment(hostFrame, linkFrame, expectedFromPair);
                    Assert.IsFalse(pairResult.IsOutOfSync);
                    Assert.AreEqual(0.0, pairResult.DeltaAngle);
                    Assert.AreEqual(Vector3D.Zero, pairResult.DeltaTranslation);
                    Assert.AreEqual(0.0, pairResult.DeltaTranslationLength);

                    CoordinateTransformResult sameHostAndLink = SharedCoordinatesMath.ComputeAlignment(
                        arbitraryFrame,
                        arbitraryFrame,
                        RigidTransform3D.Identity);
                    Assert.IsFalse(sameHostAndLink.IsOutOfSync);
                    Assert.LessOrEqual(Math.Abs(sameHostAndLink.DeltaAngle), 1e-9);
                    Assert.LessOrEqual(sameHostAndLink.DeltaTranslationLength, 1e-9);
                }, iter: 300);
        }

        [Test]
        public void ComputeAlignmentFromExpected_Perturbations_SatisfyToleranceThresholdsAndMonotonicity()
        {
            Gen<RigidTransform3D> genBaseHorizontal = Gen.Select(
                Gen.Double[-1000.0, 1000.0],
                Gen.Double[-1000.0, 1000.0],
                Gen.Double[-1000.0, 1000.0],
                Gen.Double[-Math.PI, Math.PI],
                (ew, ns, elev, yaw) => CreateHorizontalFrame(new Vector3D(ew, ns, elev), yaw));

            Gen<(double s1, double s2)> genOrderedScales = Gen.Select(
                Gen.Int[0, 500],
                Gen.Int[0, 500],
                (a, b) =>
                {
                    int min = Math.Min(a, b);
                    int max = Math.Max(a, b);
                    return (min / 500.0, max / 500.0);
                });

            Gen<Vector3D> genSmallUnitOrBoundedOffset = Gen.Select(
                Gen.Double[-1.0, 1.0],
                Gen.Double[-1.0, 1.0],
                Gen.Double[-1.0, 1.0],
                (x, y, z) => new Vector3D(x, y, z));

            Gen.Select(
                    genBaseHorizontal,
                    Gen.Double[-Math.PI + 1e-4, Math.PI - 1e-4],
                    genSmallUnitOrBoundedOffset,
                    genOrderedScales,
                    Gen.Double[0.0, SharedCoordinatesMath.Tolerance * 0.5],
                    Gen.Double[SharedCoordinatesMath.Tolerance * 2.0, Math.PI - 1e-4])
                .Sample((current, baseAngle, rawDirection, scales, subTolFactor, overAngle) =>
                {
                    // 1. Sub-tolerance perturbation is always in-sync.
                    double dirLen = rawDirection.Length;
                    Vector3D unitDir = dirLen > 1e-12 ? rawDirection * (1.0 / dirLen) : Vector3D.UnitX;
                    double subAngle = baseAngle >= 0.0 ? subTolFactor : -subTolFactor;
                    Vector3D subOffset = unitDir * (SharedCoordinatesMath.Tolerance * 0.5);
                    RigidTransform3D subExpected = ApplyHorizontalDelta(current, subAngle, subOffset);
                    CoordinateTransformResult subResult = SharedCoordinatesMath.ComputeAlignmentFromExpected(current, subExpected);
                    Assert.IsFalse(subResult.IsOutOfSync, "Sub-tolerance perturbation must remain in sync.");

                    // 2. Above-tolerance rotation is always out-of-sync.
                    double signedOverAngle = baseAngle >= 0.0 ? overAngle : -overAngle;
                    RigidTransform3D overAngleExpected = ApplyHorizontalDelta(current, signedOverAngle, Vector3D.Zero);
                    CoordinateTransformResult overAngleResult = SharedCoordinatesMath.ComputeAlignmentFromExpected(current, overAngleExpected);
                    Assert.IsTrue(overAngleResult.IsOutOfSync, "Above-tolerance rotation must flag IsOutOfSync.");

                    // 3. Above-tolerance translation is always out-of-sync.
                    Vector3D overOffset = unitDir * (SharedCoordinatesMath.Tolerance * 2.5);
                    RigidTransform3D overOffsetExpected = ApplyHorizontalDelta(current, 0.0, overOffset);
                    CoordinateTransformResult overOffsetResult = SharedCoordinatesMath.ComputeAlignmentFromExpected(current, overOffsetExpected);
                    Assert.IsTrue(overOffsetResult.IsOutOfSync, "Above-tolerance translation must flag IsOutOfSync.");

                    // 4. Monotonicity under scaling: if scale1 is out of sync, scale2 >= scale1 must also be out of sync.
                    double maxTranslationScale = SharedCoordinatesMath.Tolerance * 5.0;
                    double thetaMax = baseAngle;
                    Vector3D deltaMax = unitDir * maxTranslationScale;

                    RigidTransform3D exp1 = ApplyHorizontalDelta(current, scales.s1 * thetaMax, deltaMax * scales.s1);
                    RigidTransform3D exp2 = ApplyHorizontalDelta(current, scales.s2 * thetaMax, deltaMax * scales.s2);

                    CoordinateTransformResult res1 = SharedCoordinatesMath.ComputeAlignmentFromExpected(current, exp1);
                    CoordinateTransformResult res2 = SharedCoordinatesMath.ComputeAlignmentFromExpected(current, exp2);

                    if (res1.IsOutOfSync)
                    {
                        Assert.IsTrue(
                            res2.IsOutOfSync,
                            $"Tolerance monotonicity violated: s1={scales.s1} was out of sync (dAngle={res1.DeltaAngle}, dTrans={res1.DeltaTranslationLength}), but s2={scales.s2} was in sync (dAngle={res2.DeltaAngle}, dTrans={res2.DeltaTranslationLength}).");
                    }

                    Assert.GreaterOrEqual(Math.Abs(res2.DeltaAngle) + 1e-11, Math.Abs(res1.DeltaAngle));
                    Assert.GreaterOrEqual(res2.DeltaTranslationLength + 1e-11, res1.DeltaTranslationLength);
                }, iter: 400);
        }

        private static RigidTransform3D CreateEulerFrame(Vector3D origin, double rx, double ry, double rz)
        {
            double cx = Math.Cos(rx);
            double sx = Math.Sin(rx);
            double cy = Math.Cos(ry);
            double sy = Math.Sin(ry);
            double cz = Math.Cos(rz);
            double sz = Math.Sin(rz);

            var basisX = new Vector3D(cz * cy, sz * cy, -sy);
            var basisY = new Vector3D((cz * sy * sx) - (sz * cx), (sz * sy * sx) + (cz * cx), cy * sx);
            var basisZ = new Vector3D((cz * sy * cx) + (sz * sx), (sz * sy * cx) - (cz * sx), cy * cx);

            return new RigidTransform3D(origin, basisX, basisY, basisZ);
        }

        private static RigidTransform3D CreateHorizontalFrame(Vector3D origin, double yaw)
        {
            double c = Math.Cos(yaw);
            double s = Math.Sin(yaw);
            return new RigidTransform3D(
                origin,
                new Vector3D(c, s, 0.0),
                new Vector3D(-s, c, 0.0),
                Vector3D.UnitZ);
        }

        private static RigidTransform3D ApplyHorizontalDelta(RigidTransform3D baseFrame, double deltaAngle, Vector3D deltaOrigin)
        {
            double baseYaw = Math.Atan2(baseFrame.BasisX.Y, baseFrame.BasisX.X);
            double targetYaw = SharedCoordinatesMath.NormalizeAngle(baseYaw + deltaAngle);
            return CreateHorizontalFrame(baseFrame.Origin + deltaOrigin, targetYaw);
        }

        private static void AssertOrthonormalBasis(RigidTransform3D transform, double tolerance)
        {
            Assert.AreEqual(1.0, transform.BasisX.Length, tolerance, "BasisX must be unit length.");
            Assert.AreEqual(1.0, transform.BasisY.Length, tolerance, "BasisY must be unit length.");
            Assert.AreEqual(1.0, transform.BasisZ.Length, tolerance, "BasisZ must be unit length.");
            Assert.AreEqual(0.0, transform.BasisX.Dot(transform.BasisY), tolerance, "BasisX and BasisY must be orthogonal.");
            Assert.AreEqual(0.0, transform.BasisY.Dot(transform.BasisZ), tolerance, "BasisY and BasisZ must be orthogonal.");
            Assert.AreEqual(0.0, transform.BasisZ.Dot(transform.BasisX), tolerance, "BasisZ and BasisX must be orthogonal.");
        }

        private static void AssertTransformsClose(
            RigidTransform3D actual,
            RigidTransform3D expected,
            double tolerance,
            string context)
        {
            AssertVectorsClose(actual.Origin, expected.Origin, tolerance, context + ".Origin");
            AssertVectorsClose(actual.BasisX, expected.BasisX, tolerance, context + ".BasisX");
            AssertVectorsClose(actual.BasisY, expected.BasisY, tolerance, context + ".BasisY");
            AssertVectorsClose(actual.BasisZ, expected.BasisZ, tolerance, context + ".BasisZ");
        }

        private static void AssertVectorsClose(Vector3D actual, Vector3D expected, double tolerance, string context)
        {
            Assert.AreEqual(expected.X, actual.X, tolerance, context + ".X");
            Assert.AreEqual(expected.Y, actual.Y, tolerance, context + ".Y");
            Assert.AreEqual(expected.Z, actual.Z, tolerance, context + ".Z");
        }
    }
}
