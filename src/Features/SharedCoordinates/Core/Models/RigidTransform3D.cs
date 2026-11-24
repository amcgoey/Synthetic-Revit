using System;

namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Immutable 3D rigid-body affine transform value type representing orthonormal basis axes and translation origin.
    /// </summary>
    public readonly struct RigidTransform3D : IEquatable<RigidTransform3D>
    {
        /// <summary>
        /// Gets the identity transform with zero origin and standard orthonormal basis axes.
        /// </summary>
        public static RigidTransform3D Identity => new RigidTransform3D(
            Vector3D.Zero,
            Vector3D.UnitX,
            Vector3D.UnitY,
            Vector3D.UnitZ);

        /// <summary>
        /// Gets the translation origin of the transform.
        /// </summary>
        public Vector3D Origin { get; }

        /// <summary>
        /// Gets the X basis vector.
        /// </summary>
        public Vector3D BasisX { get; }

        /// <summary>
        /// Gets the Y basis vector.
        /// </summary>
        public Vector3D BasisY { get; }

        /// <summary>
        /// Gets the Z basis vector.
        /// </summary>
        public Vector3D BasisZ { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RigidTransform3D"/> struct.
        /// </summary>
        /// <param name="origin">The translation origin.</param>
        /// <param name="basisX">The X basis vector.</param>
        /// <param name="basisY">The Y basis vector.</param>
        /// <param name="basisZ">The Z basis vector.</param>
        public RigidTransform3D(Vector3D origin, Vector3D basisX, Vector3D basisY, Vector3D basisZ)
        {
            Origin = origin;
            BasisX = basisX;
            BasisY = basisY;
            BasisZ = basisZ;
        }

        /// <summary>
        /// Gets the rigid-body inverse transform assuming orthonormal basis axes.
        /// </summary>
        public RigidTransform3D Inverse
        {
            get
            {
                Vector3D invBasisX = new Vector3D(BasisX.X, BasisY.X, BasisZ.X);
                Vector3D invBasisY = new Vector3D(BasisX.Y, BasisY.Y, BasisZ.Y);
                Vector3D invBasisZ = new Vector3D(BasisX.Z, BasisY.Z, BasisZ.Z);
                Vector3D invOrigin = new Vector3D(
                    -BasisX.Dot(Origin),
                    -BasisY.Dot(Origin),
                    -BasisZ.Dot(Origin));

                return new RigidTransform3D(invOrigin, invBasisX, invBasisY, invBasisZ);
            }
        }

        /// <summary>
        /// Transforms a 3D point by rotation and translation: <c>Origin + BasisX*x + BasisY*y + BasisZ*z</c>.
        /// </summary>
        public Vector3D OfPoint(Vector3D point)
        {
            return Origin + (BasisX * point.X) + (BasisY * point.Y) + (BasisZ * point.Z);
        }

        /// <summary>
        /// Transforms a 3D direction vector by the basis axes without translation.
        /// </summary>
        public Vector3D OfVector(Vector3D vector)
        {
            return (BasisX * vector.X) + (BasisY * vector.Y) + (BasisZ * vector.Z);
        }

        /// <summary>
        /// Composes <c>this * right</c> so that <c>(this.Multiply(right)).OfPoint(p) == this.OfPoint(right.OfPoint(p))</c>.
        /// </summary>
        public RigidTransform3D Multiply(RigidTransform3D right)
        {
            return new RigidTransform3D(
                OfPoint(right.Origin),
                OfVector(right.BasisX),
                OfVector(right.BasisY),
                OfVector(right.BasisZ));
        }

        /// <summary>
        /// Constructs the Shared-to-Internal total transform corresponding to Revit <c>ProjectLocation.GetProjectPosition(XYZ.Zero)</c> inputs.
        /// </summary>
        /// <param name="eastWest">Shared East/West coordinate of the internal origin in feet.</param>
        /// <param name="northSouth">Shared North/South coordinate of the internal origin in feet.</param>
        /// <param name="elevation">Shared Elevation coordinate of the internal origin in feet.</param>
        /// <param name="angleRadians">Angle from True North in radians.</param>
        /// <returns>The Shared-to-Internal <see cref="RigidTransform3D"/>.</returns>
        public static RigidTransform3D FromProjectPosition(
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
            double cos = Math.Cos(angleRadians);
            double sin = Math.Sin(angleRadians);

            // Revit ProjectLocation.GetTotalTransform() maps Shared -> Internal with rotation -angleRadians
            // around Z (BasisX = (cos, -sin, 0), BasisY = (sin, cos, 0)) so shared (ew, ns, elev) maps to (0, 0, 0).
            Vector3D basisX = new Vector3D(cos, -sin, 0.0);
            Vector3D basisY = new Vector3D(sin, cos, 0.0);
            Vector3D basisZ = Vector3D.UnitZ;
            Vector3D origin = new Vector3D(
                (-cos * eastWest) - (sin * northSouth),
                (sin * eastWest) - (cos * northSouth),
                -elevation);

            return new RigidTransform3D(origin, basisX, basisY, basisZ);
        }

        public static RigidTransform3D operator *(RigidTransform3D left, RigidTransform3D right) => left.Multiply(right);

        public bool Equals(RigidTransform3D other)
        {
            return Origin.Equals(other.Origin)
                && BasisX.Equals(other.BasisX)
                && BasisY.Equals(other.BasisY)
                && BasisZ.Equals(other.BasisZ);
        }

        public override bool Equals(object? obj) => obj is RigidTransform3D other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 23) + Origin.GetHashCode();
                hash = (hash * 23) + BasisX.GetHashCode();
                hash = (hash * 23) + BasisY.GetHashCode();
                hash = (hash * 23) + BasisZ.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(RigidTransform3D left, RigidTransform3D right) => left.Equals(right);

        public static bool operator !=(RigidTransform3D left, RigidTransform3D right) => !left.Equals(right);
    }
}
