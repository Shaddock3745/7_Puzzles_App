using System;

namespace Tangram.Data
{
    /// <summary>ℤ[√2]² 上的精確向量（§0）。</summary>
    [Serializable]
    public readonly struct ExactVector2 : IEquatable<ExactVector2>, IComparable<ExactVector2>
    {
        public readonly Scalar X;
        public readonly Scalar Y;

        public ExactVector2(Scalar x, Scalar y) { X = x; Y = y; }
        public ExactVector2(long x, long y) : this(Scalar.FromInteger(x), Scalar.FromInteger(y)) { }

        public static readonly ExactVector2 Zero = new ExactVector2(Scalar.Zero, Scalar.Zero);

        public static ExactVector2 operator +(ExactVector2 left, ExactVector2 right)
            => new ExactVector2(left.X + right.X, left.Y + right.Y);

        public static ExactVector2 operator -(ExactVector2 left, ExactVector2 right)
            => new ExactVector2(left.X - right.X, left.Y - right.Y);

        public static ExactVector2 operator -(ExactVector2 value)
            => new ExactVector2(-value.X, -value.Y);

        /// <summary>二維外積。</summary>
        public static Scalar Cross(ExactVector2 left, ExactVector2 right)
            => left.X * right.Y - left.Y * right.X;

        public static Scalar Dot(ExactVector2 left, ExactVector2 right)
            => left.X * right.X + left.Y * right.Y;

        /// <summary>字典序（先 X 後 Y），canonical 平移基準用。</summary>
        public int CompareTo(ExactVector2 other)
        {
            int comparison = X.CompareTo(other.X);
            return comparison != 0 ? comparison : Y.CompareTo(other.Y);
        }

        public bool Equals(ExactVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object other) => other is ExactVector2 vector && Equals(vector);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());

        public static bool operator ==(ExactVector2 left, ExactVector2 right) => left.Equals(right);
        public static bool operator !=(ExactVector2 left, ExactVector2 right) => !left.Equals(right);

        public string ToKey() => X.ToKey() + ":" + Y.ToKey();
        public override string ToString() => "(" + X + ", " + Y + ")";

        /// <summary>整體縮放。座標系 ×2 時用（中點測試需要，見 Region）。</summary>
        public static ExactVector2 operator *(ExactVector2 value, long multiplier)
            => new ExactVector2(value.X * multiplier, value.Y * multiplier);
    }
}
