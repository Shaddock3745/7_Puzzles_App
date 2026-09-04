using System;

namespace Tangram.Data
{
    /// <summary>ℤ[√2]² 上的精確向量（§0）。</summary>
    [Serializable]
    public readonly struct ExactVector2 : IEquatable<ExactVector2>, IComparable<ExactVector2>
    {
        public readonly Scalar X;
        public readonly Scalar Y;

        /// <param name="x">X 座標。</param>
        /// <param name="y">Y 座標。</param>
        public ExactVector2(Scalar x, Scalar y) { X = x; Y = y; }

        /// <summary>兩個分量都是整數時的捷徑。</summary>
        /// <param name="x">X 座標的整數值。</param>
        /// <param name="y">Y 座標的整數值。</param>
        public ExactVector2(long x, long y) : this(Scalar.FromInteger(x), Scalar.FromInteger(y)) { }

        public static readonly ExactVector2 Zero = new ExactVector2(Scalar.Zero, Scalar.Zero);

        /// <param name="left">被加數。</param>
        /// <param name="right">加數。</param>
        /// <returns>逐分量相加的結果。</returns>
        public static ExactVector2 operator +(ExactVector2 left, ExactVector2 right)
            => new ExactVector2(left.X + right.X, left.Y + right.Y);

        /// <param name="left">被減數。</param>
        /// <param name="right">減數。</param>
        /// <returns>逐分量相減的結果。吸附的候選落點就是這樣算出來的。</returns>
        public static ExactVector2 operator -(ExactVector2 left, ExactVector2 right)
            => new ExactVector2(left.X - right.X, left.Y - right.Y);

        /// <param name="value">要取負的向量。</param>
        /// <returns>反向的向量。</returns>
        public static ExactVector2 operator -(ExactVector2 value)
            => new ExactVector2(-value.X, -value.Y);

        /// <summary>二維外積。</summary>
        /// <param name="left">左運算元。</param>
        /// <param name="right">右運算元。</param>
        /// <returns>
        /// 純量 <c>left.X·right.Y − left.Y·right.X</c>。
        /// 正 = right 在 left 的逆時針側，零 = 兩者共線。
        /// </returns>
        public static Scalar Cross(ExactVector2 left, ExactVector2 right)
            => left.X * right.Y - left.Y * right.X;

        /// <param name="left">左運算元。</param>
        /// <param name="right">右運算元。</param>
        /// <returns>內積 <c>left.X·right.X + left.Y·right.Y</c>。SAT 的投影用它。</returns>
        public static Scalar Dot(ExactVector2 left, ExactVector2 right)
            => left.X * right.X + left.Y * right.Y;

        /// <summary>字典序（先 X 後 Y），canonical 平移基準用。</summary>
        /// <param name="other">要比較的向量。</param>
        /// <returns>自己較小回負、相等回 0、較大回正。</returns>
        public int CompareTo(ExactVector2 other)
        {
            int comparison = X.CompareTo(other.X);
            return comparison != 0 ? comparison : Y.CompareTo(other.Y);
        }

        /// <param name="other">要比較的向量。</param>
        /// <returns>兩個分量是否都精確相等。沒有容差。</returns>
        public bool Equals(ExactVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        /// <param name="other">任意物件。</param>
        /// <returns>是否為相等的 ExactVector2。</returns>
        public override bool Equals(object other) => other is ExactVector2 vector && Equals(vector);

        /// <returns>雜湊值。可以直接當 Dictionary 的 key，接觸圖就是這樣建的。</returns>
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());

        /// <param name="left">左運算元。</param>
        /// <param name="right">右運算元。</param>
        /// <returns>是否精確相等。</returns>
        public static bool operator ==(ExactVector2 left, ExactVector2 right) => left.Equals(right);

        /// <param name="left">左運算元。</param>
        /// <param name="right">右運算元。</param>
        /// <returns>是否不相等。</returns>
        public static bool operator !=(ExactVector2 left, ExactVector2 right) => !left.Equals(right);

        /// <returns>序列化用的穩定字串，形如 <c>"n|m:n|m"</c>。canonical key 的一部分。</returns>
        public string ToKey() => X.ToKey() + ":" + Y.ToKey();

        /// <returns>給人看的字串，形如 <c>"(2, 1+3√2)"</c>。</returns>
        public override string ToString() => "(" + X + ", " + Y + ")";

        /// <summary>整體縮放。座標系 ×2 時用（中點測試需要，見 Region）。</summary>
        /// <param name="value">要縮放的向量。</param>
        /// <param name="multiplier">整數倍率。</param>
        /// <returns>兩個分量都乘上倍率的向量。</returns>
        public static ExactVector2 operator *(ExactVector2 value, long multiplier)
            => new ExactVector2(value.X * multiplier, value.Y * multiplier);
    }
}
