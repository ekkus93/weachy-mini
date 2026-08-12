#nullable enable

using System;

namespace ReachyMini.AppState
{
    public readonly struct ReachyVector3D : IEquatable<ReachyVector3D>
    {
        public ReachyVector3D(double x, double y, double z)
        {
            RequireFinite(x, nameof(x));
            RequireFinite(y, nameof(y));
            RequireFinite(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

        public ReachyVector3D Normalized()
        {
            double magnitude = Magnitude;
            if (!IsFinite(magnitude) || magnitude <= 1.0e-12)
            {
                throw new InvalidOperationException(
                    "A zero-length or non-finite vector cannot be normalized.");
            }
            return new ReachyVector3D(
                X / magnitude,
                Y / magnitude,
                Z / magnitude);
        }

        public bool Equals(ReachyVector3D other)
        {
            return X.Equals(other.X) &&
                Y.Equals(other.Y) &&
                Z.Equals(other.Z);
        }

        public override bool Equals(object? obj)
        {
            return obj is ReachyVector3D other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public static bool operator ==(
            ReachyVector3D left,
            ReachyVector3D right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ReachyVector3D left,
            ReachyVector3D right)
        {
            return !left.Equals(right);
        }

        private static void RequireFinite(double value, string name)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Coordinate values must be finite.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public readonly struct ReachyMatrix3x3 : IEquatable<ReachyMatrix3x3>
    {
        public ReachyMatrix3x3(
            double m00,
            double m01,
            double m02,
            double m10,
            double m11,
            double m12,
            double m20,
            double m21,
            double m22)
        {
            RequireFinite(m00, nameof(m00));
            RequireFinite(m01, nameof(m01));
            RequireFinite(m02, nameof(m02));
            RequireFinite(m10, nameof(m10));
            RequireFinite(m11, nameof(m11));
            RequireFinite(m12, nameof(m12));
            RequireFinite(m20, nameof(m20));
            RequireFinite(m21, nameof(m21));
            RequireFinite(m22, nameof(m22));
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
        }

        public static ReachyMatrix3x3 Identity => new ReachyMatrix3x3(
            1.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
            1.0);

        public double M00 { get; }

        public double M01 { get; }

        public double M02 { get; }

        public double M10 { get; }

        public double M11 { get; }

        public double M12 { get; }

        public double M20 { get; }

        public double M21 { get; }

        public double M22 { get; }

        public double Determinant =>
            M00 * (M11 * M22 - M12 * M21) -
            M01 * (M10 * M22 - M12 * M20) +
            M02 * (M10 * M21 - M11 * M20);

        public ReachyMatrix3x3 Transposed()
        {
            return new ReachyMatrix3x3(
                M00,
                M10,
                M20,
                M01,
                M11,
                M21,
                M02,
                M12,
                M22);
        }

        public ReachyMatrix3x3 Inverse()
        {
            double determinant = Determinant;
            if (!IsFinite(determinant) || Math.Abs(determinant) <= 1.0e-12)
            {
                throw new InvalidOperationException(
                    "The 3x3 matrix is singular and cannot be inverted.");
            }

            double inverse = 1.0 / determinant;
            return new ReachyMatrix3x3(
                (M11 * M22 - M12 * M21) * inverse,
                (M02 * M21 - M01 * M22) * inverse,
                (M01 * M12 - M02 * M11) * inverse,
                (M12 * M20 - M10 * M22) * inverse,
                (M00 * M22 - M02 * M20) * inverse,
                (M02 * M10 - M00 * M12) * inverse,
                (M10 * M21 - M11 * M20) * inverse,
                (M01 * M20 - M00 * M21) * inverse,
                (M00 * M11 - M01 * M10) * inverse);
        }

        public ReachyVector3D Transform(ReachyVector3D vector)
        {
            return new ReachyVector3D(
                M00 * vector.X + M01 * vector.Y + M02 * vector.Z,
                M10 * vector.X + M11 * vector.Y + M12 * vector.Z,
                M20 * vector.X + M21 * vector.Y + M22 * vector.Z);
        }

        public ReachyVector3D TransformPixel(double x, double y)
        {
            ReachyVector3D homogeneous = Transform(
                new ReachyVector3D(x, y, 1.0));
            if (Math.Abs(homogeneous.Z) <= 1.0e-12)
            {
                throw new InvalidOperationException(
                    "The pixel transform produced a point at infinity.");
            }
            return new ReachyVector3D(
                homogeneous.X / homogeneous.Z,
                homogeneous.Y / homogeneous.Z,
                1.0);
        }

        public bool ApproximatelyEquals(
            ReachyMatrix3x3 other,
            double tolerance)
        {
            if (!IsFinite(tolerance) || tolerance < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tolerance),
                    tolerance,
                    "A matrix comparison tolerance must be finite and nonnegative.");
            }

            return Math.Abs(M00 - other.M00) <= tolerance &&
                Math.Abs(M01 - other.M01) <= tolerance &&
                Math.Abs(M02 - other.M02) <= tolerance &&
                Math.Abs(M10 - other.M10) <= tolerance &&
                Math.Abs(M11 - other.M11) <= tolerance &&
                Math.Abs(M12 - other.M12) <= tolerance &&
                Math.Abs(M20 - other.M20) <= tolerance &&
                Math.Abs(M21 - other.M21) <= tolerance &&
                Math.Abs(M22 - other.M22) <= tolerance;
        }

        public bool IsProperRotation(double tolerance = 1.0e-9)
        {
            ReachyMatrix3x3 orthogonality = Transposed() * this;
            return orthogonality.ApproximatelyEquals(Identity, tolerance) &&
                Math.Abs(Determinant - 1.0) <= tolerance;
        }

        public bool Equals(ReachyMatrix3x3 other)
        {
            return M00.Equals(other.M00) &&
                M01.Equals(other.M01) &&
                M02.Equals(other.M02) &&
                M10.Equals(other.M10) &&
                M11.Equals(other.M11) &&
                M12.Equals(other.M12) &&
                M20.Equals(other.M20) &&
                M21.Equals(other.M21) &&
                M22.Equals(other.M22);
        }

        public override bool Equals(object? obj)
        {
            return obj is ReachyMatrix3x3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(M00);
            hash.Add(M01);
            hash.Add(M02);
            hash.Add(M10);
            hash.Add(M11);
            hash.Add(M12);
            hash.Add(M20);
            hash.Add(M21);
            hash.Add(M22);
            return hash.ToHashCode();
        }

        public static bool operator ==(
            ReachyMatrix3x3 left,
            ReachyMatrix3x3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ReachyMatrix3x3 left,
            ReachyMatrix3x3 right)
        {
            return !left.Equals(right);
        }

        public static ReachyMatrix3x3 operator *(
            ReachyMatrix3x3 left,
            ReachyMatrix3x3 right)
        {
            return new ReachyMatrix3x3(
                left.M00 * right.M00 + left.M01 * right.M10 +
                    left.M02 * right.M20,
                left.M00 * right.M01 + left.M01 * right.M11 +
                    left.M02 * right.M21,
                left.M00 * right.M02 + left.M01 * right.M12 +
                    left.M02 * right.M22,
                left.M10 * right.M00 + left.M11 * right.M10 +
                    left.M12 * right.M20,
                left.M10 * right.M01 + left.M11 * right.M11 +
                    left.M12 * right.M21,
                left.M10 * right.M02 + left.M11 * right.M12 +
                    left.M12 * right.M22,
                left.M20 * right.M00 + left.M21 * right.M10 +
                    left.M22 * right.M20,
                left.M20 * right.M01 + left.M21 * right.M11 +
                    left.M22 * right.M21,
                left.M20 * right.M02 + left.M21 * right.M12 +
                    left.M22 * right.M22);
        }

        private static void RequireFinite(double value, string name)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Matrix values must be finite.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public readonly struct ReachyQuaternionD : IEquatable<ReachyQuaternionD>
    {
        public ReachyQuaternionD(double x, double y, double z, double w)
        {
            RequireFinite(x, nameof(x));
            RequireFinite(y, nameof(y));
            RequireFinite(z, nameof(z));
            RequireFinite(w, nameof(w));
            double magnitude = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (!IsFinite(magnitude) || magnitude <= 1.0e-12)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(w),
                    "A camera rotation quaternion must have finite nonzero length.");
            }
            X = x / magnitude;
            Y = y / magnitude;
            Z = z / magnitude;
            W = w / magnitude;
        }

        public static ReachyQuaternionD Identity =>
            new ReachyQuaternionD(0.0, 0.0, 0.0, 1.0);

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double W { get; }

        public ReachyQuaternionD Inverse()
        {
            return new ReachyQuaternionD(-X, -Y, -Z, W);
        }

        public ReachyMatrix3x3 ToRotationMatrix()
        {
            double xx = X * X;
            double yy = Y * Y;
            double zz = Z * Z;
            double xy = X * Y;
            double xz = X * Z;
            double yz = Y * Z;
            double wx = W * X;
            double wy = W * Y;
            double wz = W * Z;
            return new ReachyMatrix3x3(
                1.0 - 2.0 * (yy + zz),
                2.0 * (xy - wz),
                2.0 * (xz + wy),
                2.0 * (xy + wz),
                1.0 - 2.0 * (xx + zz),
                2.0 * (yz - wx),
                2.0 * (xz - wy),
                2.0 * (yz + wx),
                1.0 - 2.0 * (xx + yy));
        }

        public static ReachyQuaternionD FromAxisAngle(
            ReachyVector3D axis,
            double radians)
        {
            if (double.IsNaN(radians) || double.IsInfinity(radians))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radians),
                    radians,
                    "An axis-angle rotation must be finite.");
            }
            ReachyVector3D normalized = axis.Normalized();
            double half = radians * 0.5;
            double sine = Math.Sin(half);
            return new ReachyQuaternionD(
                normalized.X * sine,
                normalized.Y * sine,
                normalized.Z * sine,
                Math.Cos(half));
        }

        public static ReachyQuaternionD operator *(
            ReachyQuaternionD left,
            ReachyQuaternionD right)
        {
            return new ReachyQuaternionD(
                left.W * right.X + left.X * right.W +
                    left.Y * right.Z - left.Z * right.Y,
                left.W * right.Y - left.X * right.Z +
                    left.Y * right.W + left.Z * right.X,
                left.W * right.Z + left.X * right.Y -
                    left.Y * right.X + left.Z * right.W,
                left.W * right.W - left.X * right.X -
                    left.Y * right.Y - left.Z * right.Z);
        }

        public bool Equals(ReachyQuaternionD other)
        {
            return X.Equals(other.X) &&
                Y.Equals(other.Y) &&
                Z.Equals(other.Z) &&
                W.Equals(other.W);
        }

        public override bool Equals(object? obj)
        {
            return obj is ReachyQuaternionD other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, W);
        }

        public static bool operator ==(
            ReachyQuaternionD left,
            ReachyQuaternionD right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ReachyQuaternionD left,
            ReachyQuaternionD right)
        {
            return !left.Equals(right);
        }

        private static void RequireFinite(double value, string name)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Quaternion values must be finite.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
