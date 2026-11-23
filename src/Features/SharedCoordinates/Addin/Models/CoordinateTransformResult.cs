using System;
using Autodesk.Revit.DB;

namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Encapsulates the 3D alignment transform delta between a link instance's current placement
    /// and its expected Level-Based Shared Site placement.
    /// </summary>
    public class CoordinateTransformResult
    {
        /// <summary>
        /// Gets or sets the expected placement transform derived from:
        /// T_expected = T_hostToShared^-1 * T_linkToShared
        /// </summary>
        public Transform ExpectedTransform { get; set; } = Transform.Identity;

        /// <summary>
        /// Gets or sets the link instance's current placement transform in the host model.
        /// </summary>
        public Transform CurrentTransform { get; set; } = Transform.Identity;

        /// <summary>
        /// Gets or sets the rotation delta around the Z axis in radians, normalized to (-pi, pi].
        /// </summary>
        public double DeltaAngle { get; set; }

        /// <summary>
        /// Gets or sets the 3D translation delta vector in feet:
        /// deltaP = ExpectedTransform.Origin - CurrentTransform.Origin
        /// </summary>
        public XYZ DeltaTranslation { get; set; } = XYZ.Zero;

        /// <summary>
        /// Gets the Euclidean length of the 3D translation delta vector in feet.
        /// </summary>
        public double DeltaTranslationLength => DeltaTranslation != null
            ? Math.Sqrt(DeltaTranslation.X * DeltaTranslation.X +
                        DeltaTranslation.Y * DeltaTranslation.Y +
                        DeltaTranslation.Z * DeltaTranslation.Z)
            : 0.0;

        /// <summary>
        /// Gets or sets a value indicating whether the instance is out of sync with its expected Shared Site transform
        /// beyond the tolerance threshold (1e-5).
        /// </summary>
        public bool IsOutOfSync { get; set; }
    }
}
