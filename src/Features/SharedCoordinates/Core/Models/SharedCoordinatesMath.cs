using System;

namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Pure 3D coordinate transformation and alignment math for Shared Site Link Repositioning.
    /// In accordance with ADR 023, calculates expected link placement transforms and derivations of
    /// rotation and translation deltas with strict tolerance checking.
    /// </summary>
    public static class SharedCoordinatesMath
    {
        /// <summary>
        /// Precision tolerance for rotation angle (radians) and translation distance (feet).
        /// Values below this threshold are considered already aligned.
        /// </summary>
        public const double Tolerance = 1e-5;

        /// <summary>
        /// Computes the expected link instance placement transform in the host model:
        /// T_expected = T_hostTotal * T_linkTotal^-1
        /// </summary>
        /// <param name="hostTotalTransform">The total transform of the active host ProjectLocation (Shared-to-Internal).</param>
        /// <param name="linkTotalTransform">The total transform of the assigned linked ProjectLocation (Shared-to-Internal).</param>
        /// <returns>The expected placement transform for the linked model instance in host internal coordinates.</returns>
        public static RigidTransform3D ComputeExpectedTransform(
            RigidTransform3D hostTotalTransform,
            RigidTransform3D linkTotalTransform)
        {
            return hostTotalTransform.Multiply(linkTotalTransform.Inverse);
        }

        /// <summary>
        /// Computes the horizontal rotation angle delta (around Z axis) between current and expected transforms,
        /// normalized to (-pi, pi].
        /// </summary>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <param name="expectedTransform">The expected placement transform of the link instance.</param>
        /// <returns>The rotation delta angle in radians, normalized to (-pi, pi].</returns>
        public static double ComputeDeltaAngle(
            RigidTransform3D currentTransform,
            RigidTransform3D expectedTransform)
        {
            double alphaCurrent = Math.Atan2(currentTransform.BasisX.Y, currentTransform.BasisX.X);
            double alphaExpected = Math.Atan2(expectedTransform.BasisX.Y, expectedTransform.BasisX.X);
            return NormalizeAngle(alphaExpected - alphaCurrent);
        }

        /// <summary>
        /// Computes the 3D translation delta vector:
        /// deltaP = expectedTransform.Origin - currentTransform.Origin
        /// </summary>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <param name="expectedTransform">The expected placement transform of the link instance.</param>
        /// <returns>The 3D translation delta vector in feet.</returns>
        public static Vector3D ComputeDeltaTranslation(
            RigidTransform3D currentTransform,
            RigidTransform3D expectedTransform)
        {
            return expectedTransform.Origin.Subtract(currentTransform.Origin);
        }

        /// <summary>
        /// Computes the full 3D alignment result comparing current placement against host and link project locations.
        /// </summary>
        /// <param name="hostTotalTransform">The total transform of the active host ProjectLocation (Shared-to-Internal).</param>
        /// <param name="linkTotalTransform">The total transform of the assigned linked ProjectLocation (Shared-to-Internal).</param>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <returns>A <see cref="CoordinateTransformResult"/> containing deltas and sync status.</returns>
        public static CoordinateTransformResult ComputeAlignment(
            RigidTransform3D hostTotalTransform,
            RigidTransform3D linkTotalTransform,
            RigidTransform3D currentTransform)
        {
            RigidTransform3D expected = ComputeExpectedTransform(hostTotalTransform, linkTotalTransform);
            return ComputeAlignmentFromExpected(currentTransform, expected);
        }

        /// <summary>
        /// Computes the full 3D alignment result comparing current placement against an expected transform.
        /// </summary>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <param name="expectedTransform">The expected placement transform of the link instance.</param>
        /// <returns>A <see cref="CoordinateTransformResult"/> containing deltas and sync status.</returns>
        public static CoordinateTransformResult ComputeAlignmentFromExpected(
            RigidTransform3D currentTransform,
            RigidTransform3D expectedTransform)
        {
            double deltaAngle = ComputeDeltaAngle(currentTransform, expectedTransform);
            Vector3D deltaTranslation = ComputeDeltaTranslation(currentTransform, expectedTransform);

            bool isOutOfSync = Math.Abs(deltaAngle) > Tolerance || deltaTranslation.Length > Tolerance;

            return new CoordinateTransformResult
            {
                ExpectedTransform = expectedTransform,
                CurrentTransform = currentTransform,
                DeltaAngle = deltaAngle,
                DeltaTranslation = deltaTranslation,
                IsOutOfSync = isOutOfSync
            };
        }

        /// <summary>
        /// Normalizes an angle in radians to (-pi, pi], wrapping -pi cleanly to +pi.
        /// </summary>
        /// <param name="angle">The angle in radians.</param>
        /// <returns>The normalized angle in radians.</returns>
        public static double NormalizeAngle(double angle)
        {
            if (double.IsNaN(angle) || double.IsInfinity(angle))
            {
                return double.NaN;
            }

            const double twoPi = 2.0 * Math.PI;
            double normalized = angle % twoPi;

            if (normalized <= -Math.PI)
            {
                normalized += twoPi;
            }
            else if (normalized > Math.PI)
            {
                normalized -= twoPi;
            }

            if (normalized <= -Math.PI)
            {
                return Math.PI;
            }

            return normalized == 0.0 ? 0.0 : normalized;
        }
    }
}
