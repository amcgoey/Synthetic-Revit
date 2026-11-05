using System;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.SharedCoordinates.Models
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
        /// T_expected = T_hostToShared^-1 * T_linkToShared
        /// </summary>
        /// <param name="hostToShared">The total transform of the active host ProjectLocation.</param>
        /// <param name="linkToShared">The total transform of the assigned linked ProjectLocation.</param>
        /// <returns>The expected placement transform for the linked model instance.</returns>
        public static Transform ComputeExpectedTransform(Transform hostToShared, Transform linkToShared)
        {
            if (hostToShared == null) throw new ArgumentNullException(nameof(hostToShared));
            if (linkToShared == null) throw new ArgumentNullException(nameof(linkToShared));

            return hostToShared.Inverse.Multiply(linkToShared);
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
        /// <param name="hostToShared">The total transform of the active host ProjectLocation.</param>
        /// <param name="linkToShared">The total transform of the assigned linked ProjectLocation.</param>
        /// <param name="currentTransform">The current placement transform of the link instance.</param>
        /// <returns>A <see cref="CoordinateTransformResult"/> containing deltas and sync status.</returns>
        public static CoordinateTransformResult ComputeAlignment(
            Transform hostToShared,
            Transform linkToShared,
            Transform currentTransform)
        {
            var expected = ComputeExpectedTransform(hostToShared, linkToShared);
            return ComputeAlignmentFromExpected(currentTransform, expected);
        }

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
