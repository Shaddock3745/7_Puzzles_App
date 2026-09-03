using System;

namespace Tangram.Data
{
    /// <summary>
    /// n + m√2（n、m 皆整數）。模型層唯一的數值型別（§0）。
    /// 浮點只允許出現在繪圖與拖曳暫態，永不寫回模型。
    /// </summary>
    [Serializable]
    public readonly struct Scalar : IEquatable<Scalar>, IComparable<Scalar>
    {
        public const double Root2AsDouble = 1.4142135623730951;

        public readonly long RationalPart;      // n
        public readonly long Root2Coefficient;  // m，√2 的係數

        public Scalar(long rationalPart, long root2Coefficient)
        {
            RationalPart = rationalPart;
            Root2Coefficient = root2Coefficient;
        }

        public static Scalar FromInteger(long value) => new Scalar(value, 0);
        public static Scalar FromRoot2Multiple(long multiple) => new Scalar(0, multiple);

        public static readonly Scalar Zero = new Scalar(0, 0);
        public static readonly Scalar One = new Scalar(1, 0);
        public static readonly Scalar Two = new Scalar(2, 0);
        public static readonly Scalar Root2 = new Scalar(0, 1);

        public static Scalar operator +(Scalar left, Scalar right)
            => new Scalar(left.RationalPart + right.RationalPart,
                          left.Root2Coefficient + right.Root2Coefficient);

        public static Scalar operator -(Scalar left, Scalar right)
            => new Scalar(left.RationalPart - right.RationalPart,
                          left.Root2Coefficient - right.Root2Coefficient);

        public static Scalar operator -(Scalar value)
            => new Scalar(-value.RationalPart, -value.Root2Coefficient);

        public static Scalar operator *(Scalar left, Scalar right)
            => new Scalar(left.RationalPart * right.RationalPart + 2 * left.Root2Coefficient * right.Root2Coefficient,
                          left.RationalPart * right.Root2Coefficient + left.Root2Coefficient * right.RationalPart);

        public static Scalar operator *(Scalar value, long multiplier)
            => new Scalar(value.RationalPart * multiplier, value.Root2Coefficient * multiplier);

        /// <summary>×√2</summary>
        public Scalar MultiplyByRoot2() => new Scalar(2 * Root2Coefficient, RationalPart);

        /// <summary>
        /// ×(√2/2)。(n+m√2)·√2/2 = m + (n/2)√2，需要 n 為偶數才留在 ℤ[√2] 內（§6 陷阱）。
        /// 這個 assert 要留著。
        /// </summary>
        public Scalar MultiplyByHalfRoot2()
        {
            if ((RationalPart & 1L) != 0L)
                throw new InvalidOperationException($"MultiplyByHalfRoot2: 有理部必須為偶數，實際為 {this}");
            return new Scalar(Root2Coefficient, RationalPart / 2);
        }

        public bool IsZero => RationalPart == 0 && Root2Coefficient == 0;

        public int Sign
        {
            get
            {
                if (RationalPart == 0 && Root2Coefficient == 0) return 0;
                if (RationalPart >= 0 && Root2Coefficient >= 0) return 1;
                if (RationalPart <= 0 && Root2Coefficient <= 0) return -1;

                // 異號：比較 n² 與 2m²（√2 為無理數，兩者不可能相等）
                long rationalSquared = RationalPart * RationalPart;
                long root2PartSquared = 2 * Root2Coefficient * Root2Coefficient;
                if (RationalPart > 0)
                    return rationalSquared > root2PartSquared ? 1 : (rationalSquared < root2PartSquared ? -1 : 0);
                return root2PartSquared > rationalSquared ? 1 : (root2PartSquared < rationalSquared ? -1 : 0);
            }
        }

        public Scalar Abs => Sign < 0 ? -this : this;

        public int CompareTo(Scalar other) => (this - other).Sign;

        public bool Equals(Scalar other)
            => RationalPart == other.RationalPart && Root2Coefficient == other.Root2Coefficient;

        public override bool Equals(object other) => other is Scalar scalar && Equals(scalar);

        public override int GetHashCode()
            => unchecked((int)(RationalPart * 397) ^ (int)Root2Coefficient);

        public static bool operator ==(Scalar left, Scalar right) => left.Equals(right);
        public static bool operator !=(Scalar left, Scalar right) => !left.Equals(right);
        public static bool operator <(Scalar left, Scalar right) => left.CompareTo(right) < 0;
        public static bool operator >(Scalar left, Scalar right) => left.CompareTo(right) > 0;
        public static bool operator <=(Scalar left, Scalar right) => left.CompareTo(right) <= 0;
        public static bool operator >=(Scalar left, Scalar right) => left.CompareTo(right) >= 0;

        /// <summary>只給繪圖／拖曳暫態用。</summary>
        public double ToDouble() => RationalPart + Root2Coefficient * Root2AsDouble;

        /// <summary>序列化用的穩定字串（canonical key 的一部分）。</summary>
        public string ToKey() => RationalPart.ToString() + "|" + Root2Coefficient.ToString();

        public override string ToString()
        {
            if (Root2Coefficient == 0) return RationalPart.ToString();
            if (RationalPart == 0) return Root2Coefficient + "√2";
            return RationalPart + (Root2Coefficient > 0 ? "+" : "") + Root2Coefficient + "√2";
        }
    }
}
