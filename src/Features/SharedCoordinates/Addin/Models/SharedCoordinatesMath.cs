using System;
using Autodesk.Revit.DB;

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
        public static Transform ComputeExpectedTransform(Transform hostTotalTransform, Transform linkTotalTransform)
        {
            if (hostTotalTransform == null) throw new ArgumentNullException(nameof(hostTotalTransform));
            if (linkTotalTransform == null) throw new ArgumentNullException(nameof(linkTotalTransform));

            return hostTotalTransform.Multiply(linkTotalTransform.Inverse);
        }

        /// <summary>
        /// Computes the horizontal rotation angle delta (around Z axis) between current and expected transforms:
        /// deltaTheta = atan2(sin(alpha_expected - alpha_current), cos(alpha_expected - alpha_current))
        /// Normalizes result to (-pi, pi].
        /// </summary>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <param name="expectedTransform">The expected placement transform of the link instance.</param>
        /// <returns>The rotation delta angle in radians, normalized to (-pi, pi].</returns>
        public static double ComputeDeltaAngle(Transform currentTransform, Transform expectedTransform)
        {
            if (currentTransform == null) throw new ArgumentNullException(nameof(currentTransform));
            if (expectedTransform == null) throw new ArgumentNullException(nameof(expectedTransform));

            double alphaCurrent = Math.Atan2(currentTransform.BasisX.Y, currentTransform.BasisX.X);
            double alphaExpected = Math.Atan2(expectedTransform.BasisX.Y, expectedTransform.BasisX.X);
            double diff = alphaExpected - alphaCurrent;

            return Math.Atan2(Math.Sin(diff), Math.Cos(diff));
        }

        /// <summary>
        /// Computes the 3D translation delta vector:
        /// deltaP = expectedTransform.Origin - currentTransform.Origin
        /// </summary>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <param name="expectedTransform">The expected placement transform of the link instance.</param>
        /// <returns>The 3D translation delta vector in feet.</returns>
        public static XYZ ComputeDeltaTranslation(Transform currentTransform, Transform expectedTransform)
        {
            if (currentTransform == null) throw new ArgumentNullException(nameof(currentTransform));
            if (expectedTransform == null) throw new ArgumentNullException(nameof(expectedTransform));

            return new XYZ(
                expectedTransform.Origin.X - currentTransform.Origin.X,
                expectedTransform.Origin.Y - currentTransform.Origin.Y,
                expectedTransform.Origin.Z - currentTransform.Origin.Z);
        }

        /// <summary>
        /// Computes the full 3D alignment result comparing current placement against host and link project locations.
        /// </summary>
        /// <param name="hostTotalTransform">The total transform of the active host ProjectLocation (Shared-to-Internal).</param>
        /// <param name="linkTotalTransform">The total transform of the assigned linked ProjectLocation (Shared-to-Internal).</param>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <returns>A <see cref="CoordinateTransformResult"/> containing deltas and sync status.</returns>
        public static CoordinateTransformResult ComputeAlignment(
            Transform hostTotalTransform,
            Transform linkTotalTransform,
            Transform currentTransform)
        {
            var expected = ComputeExpectedTransform(hostTotalTransform, linkTotalTransform);
            return ComputeAlignmentFromExpected(currentTransform, expected);
        }

        /// <summary>
        /// Evaluates link instance alignment against expected transform derived from host and link project locations.
        /// Alias for <see cref="ComputeAlignment(Transform, Transform, Transform)"/>.
        /// </summary>
        /// <param name="hostTotalTransform">The total transform of the active host ProjectLocation (Shared-to-Internal).</param>
        /// <param name="linkTotalTransform">The total transform of the assigned linked ProjectLocation (Shared-to-Internal).</param>
        /// <param name="linkInstanceTransform">The current placement transform of the link instance.</param>
        /// <returns>A <see cref="CoordinateTransformResult"/> containing deltas and sync status.</returns>
        public static CoordinateTransformResult EvaluateLinkAlignment(
            Transform hostTotalTransform,
            Transform linkTotalTransform,
            Transform linkInstanceTransform) => ComputeAlignment(hostTotalTransform, linkTotalTransform, linkInstanceTransform);

        /// <summary>
        /// Computes the full 3D alignment result comparing current placement against an expected transform.
        /// </summary>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <param name="expectedTransform">The expected placement transform of the link instance.</param>
        /// <returns>A <see cref="CoordinateTransformResult"/> containing deltas and sync status.</returns>
        public static CoordinateTransformResult ComputeAlignmentFromExpected(
            Transform currentTransform,
            Transform expectedTransform)
        {
            if (currentTransform == null) throw new ArgumentNullException(nameof(currentTransform));
            if (expectedTransform == null) throw new ArgumentNullException(nameof(expectedTransform));

            double deltaAngle = ComputeDeltaAngle(currentTransform, expectedTransform);
            XYZ deltaTranslation = ComputeDeltaTranslation(currentTransform, expectedTransform);

            double translationLen = Math.Sqrt(
                deltaTranslation.X * deltaTranslation.X +
                deltaTranslation.Y * deltaTranslation.Y +
                deltaTranslation.Z * deltaTranslation.Z);

            bool isOutOfSync = Math.Abs(deltaAngle) > Tolerance || translationLen > Tolerance;

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
        /// Normalizes an angle in radians to (-pi, pi].
        /// </summary>
        /// <param name="angle">The angle in radians.</param>
        /// <returns>The normalized angle in radians.</returns>
        public static double NormalizeAngle(double angle)
        {
            return Math.Atan2(Math.Sin(angle), Math.Cos(angle));
        }
    }
}
