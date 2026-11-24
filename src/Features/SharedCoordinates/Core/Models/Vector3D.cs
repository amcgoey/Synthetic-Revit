using System;
using System.Globalization;

namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Immutable 3D Cartesian vector value type used for headless coordinate math in <c>Core</c>.
    /// </summary>
    public readonly struct Vector3D : IEquatable<Vector3D>
    {
        /// <summary>
        /// Gets the zero vector <c>(0, 0, 0)</c>.
        /// </summary>
        public static Vector3D Zero => new Vector3D(0.0, 0.0, 0.0);

        /// <summary>
        /// Gets the unit vector along the X axis <c>(1, 0, 0)</c>.
        /// </summary>
        public static Vector3D UnitX => new Vector3D(1.0, 0.0, 0.0);

        /// <summary>
        /// Gets the unit vector along the Y axis <c>(0, 1, 0)</c>.
        /// </summary>
        public static Vector3D UnitY => new Vector3D(0.0, 1.0, 0.0);

        /// <summary>
        /// Gets the unit vector along the Z axis <c>(0, 0, 1)</c>.
        /// </summary>
        public static Vector3D UnitZ => new Vector3D(0.0, 0.0, 1.0);

        /// <summary>
        /// Gets the X component.
        /// </summary>
        public double X { get; }

        /// <summary>
        /// Gets the Y component.
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// Gets the Z component.
        /// </summary>
        public double Z { get; }

        /// <summary>
        /// Gets the squared Euclidean length of this vector.
        /// </summary>
        public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

        /// <summary>
        /// Gets the Euclidean length of this vector.
        /// </summary>
        public double Length => Math.Sqrt(LengthSquared);

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector3D"/> struct.
        /// </summary>
        /// <param name="x">The X coordinate or component.</param>
        /// <param name="y">The Y coordinate or component.</param>
        /// <param name="z">The Z coordinate or component.</param>
        public Vector3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Returns the vector sum <c>this + other</c>.
        /// </summary>
        public Vector3D Add(Vector3D other) => new Vector3D(X + other.X, Y + other.Y, Z + other.Z);

        /// <summary>
        /// Returns the vector difference <c>this - other</c>.
        /// </summary>
        public Vector3D Subtract(Vector3D other) => new Vector3D(X - other.X, Y - other.Y, Z - other.Z);

        /// <summary>
        /// Returns this vector scaled by <paramref name="scalar"/>.
        /// </summary>
        public Vector3D Scale(double scalar) => new Vector3D(X * scalar, Y * scalar, Z * scalar);

        /// <summary>
        /// Returns the negated vector <c>-this</c>.
        /// </summary>
        public Vector3D Negate() => new Vector3D(-X, -Y, -Z);

        /// <summary>
        /// Computes the dot product <c>this · other</c>.
        /// </summary>
        public double Dot(Vector3D other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        /// <summary>
        /// Computes the dot product <paramref name="left"/> · <paramref name="right"/>.
        /// </summary>
        public static double Dot(Vector3D left, Vector3D right) => left.Dot(right);

        public static Vector3D operator +(Vector3D left, Vector3D right) => left.Add(right);

        public static Vector3D operator -(Vector3D left, Vector3D right) => left.Subtract(right);

        public static Vector3D operator -(Vector3D value) => value.Negate();

        public static Vector3D operator *(Vector3D vector, double scalar) => vector.Scale(scalar);

        public static Vector3D operator *(double scalar, Vector3D vector) => vector.Scale(scalar);

        public bool Equals(Vector3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is Vector3D other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 23) + (X == 0.0 ? 0.0 : X).GetHashCode();
                hash = (hash * 23) + (Y == 0.0 ? 0.0 : Y).GetHashCode();
                hash = (hash * 23) + (Z == 0.0 ? 0.0 : Z).GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(Vector3D left, Vector3D right) => left.Equals(right);

        public static bool operator !=(Vector3D left, Vector3D right) => !left.Equals(right);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
        }
    }
}
