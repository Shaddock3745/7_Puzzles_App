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

        /// <param name="rationalPart">有理部 n。</param>
        /// <param name="root2Coefficient">√2 的係數 m。</param>
        public Scalar(long rationalPart, long root2Coefficient)
        {
            RationalPart = rationalPart;
            Root2Coefficient = root2Coefficient;
        }

        /// <param name="value">整數值。</param>
        /// <returns>值為 value 的 Scalar（√2 係數為 0）。</returns>
        public static Scalar FromInteger(long value) => new Scalar(value, 0);

        /// <param name="multiple">√2 的倍數。</param>
        /// <returns>值為 multiple·√2 的 Scalar（有理部為 0）。</returns>
        public static Scalar FromRoot2Multiple(long multiple) => new Scalar(0, multiple);

        public static readonly Scalar Zero = new Scalar(0, 0);
        public static readonly Scalar One = new Scalar(1, 0);
        public static readonly Scalar Two = new Scalar(2, 0);
        public static readonly Scalar Root2 = new Scalar(0, 1);

        /// <param name="left">被加數。</param>
        /// <param name="right">加數。</param>
        /// <returns>兩者的和，兩個係數各自相加。</returns>
        public static Scalar operator +(Scalar left, Scalar right)
            => new Scalar(left.RationalPart + right.RationalPart,
                          left.Root2Coefficient + right.Root2Coefficient);

        /// <param name="left">被減數。</param>
        /// <param name="right">減數。</param>
        /// <returns>兩者的差。</returns>
        public static Scalar operator -(Scalar left, Scalar right)
            => new Scalar(left.RationalPart - right.RationalPart,
                          left.Root2Coefficient - right.Root2Coefficient);

        /// <param name="value">要取負的值。</param>
        /// <returns>相反數。</returns>
        public static Scalar operator -(Scalar value)
            => new Scalar(-value.RationalPart, -value.Root2Coefficient);

        /// <summary>(a+b√2)(c+d√2) = (ac+2bd) + (ad+bc)√2，乘法在 ℤ[√2] 內封閉。</summary>
        /// <param name="left">被乘數。</param>
        /// <param name="right">乘數。</param>
        /// <returns>兩者的積。</returns>
        public static Scalar operator *(Scalar left, Scalar right)
            => new Scalar(left.RationalPart * right.RationalPart + 2 * left.Root2Coefficient * right.Root2Coefficient,
                          left.RationalPart * right.Root2Coefficient + left.Root2Coefficient * right.RationalPart);

        /// <param name="value">被乘數。</param>
        /// <param name="multiplier">整數倍率。</param>
        /// <returns>兩個係數都乘上倍率的值。</returns>
        public static Scalar operator *(Scalar value, long multiplier)
            => new Scalar(value.RationalPart * multiplier, value.Root2Coefficient * multiplier);

        /// <summary>×√2</summary>
        /// <returns>自己乘上 √2 的結果，仍在 ℤ[√2] 內。</returns>
        public Scalar MultiplyByRoot2() => new Scalar(2 * Root2Coefficient, RationalPart);

        /// <summary>
        /// ×(√2/2)。(n+m√2)·√2/2 = m + (n/2)√2，需要 n 為偶數才留在 ℤ[√2] 內（§6 陷阱）。
        /// 這個 assert 要留著。
        /// </summary>
        /// <returns>自己乘上 √2/2 的結果。</returns>
        /// <exception cref="InvalidOperationException">有理部為奇數，結果會離開 ℤ[√2]。</exception>
        public Scalar MultiplyByHalfRoot2()
        {
            if ((RationalPart & 1L) != 0L)
                throw new InvalidOperationException($"MultiplyByHalfRoot2: 有理部必須為偶數，實際為 {this}");
            return new Scalar(Root2Coefficient, RationalPart / 2);
        }

        /// <summary>是不是 0。兩個係數都是 0 才算。</summary>
        public bool IsZero => RationalPart == 0 && Root2Coefficient == 0;

        /// <summary>
        /// 正負號。同號時直接看；異號時比 n² 與 2m²（√2 是無理數，兩者永遠不相等）。
        /// 全程整數，沒有浮點也沒有容差。
        /// </summary>
        /// <returns>正回 1、負回 −1、零回 0。</returns>
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

        /// <summary>絕對值。</summary>
        public Scalar Abs => Sign < 0 ? -this : this;

        /// <param name="other">要比較的值。</param>
        /// <returns>自己較小回負、相等回 0、較大回正。</returns>
        public int CompareTo(Scalar other) => (this - other).Sign;

        /// <param name="other">要比較的值。</param>
        /// <returns>兩個係數是否都相同。沒有容差。</returns>
        public bool Equals(Scalar other)
            => RationalPart == other.RationalPart && Root2Coefficient == other.Root2Coefficient;

        /// <param name="other">任意物件。</param>
        /// <returns>是否為相等的 Scalar。</returns>
        public override bool Equals(object other) => other is Scalar scalar && Equals(scalar);

        /// <returns>雜湊值。</returns>
        public override int GetHashCode()
            => unchecked((int)(RationalPart * 397) ^ (int)Root2Coefficient);

        /// <param name="left">左運算元。</param><param name="right">右運算元。</param>
        /// <returns>是否相等。</returns>
        public static bool operator ==(Scalar left, Scalar right) => left.Equals(right);

        /// <param name="left">左運算元。</param><param name="right">右運算元。</param>
        /// <returns>是否不相等。</returns>
        public static bool operator !=(Scalar left, Scalar right) => !left.Equals(right);

        /// <param name="left">左運算元。</param><param name="right">右運算元。</param>
        /// <returns>left 是否小於 right。</returns>
        public static bool operator <(Scalar left, Scalar right) => left.CompareTo(right) < 0;

        /// <param name="left">左運算元。</param><param name="right">右運算元。</param>
        /// <returns>left 是否大於 right。</returns>
        public static bool operator >(Scalar left, Scalar right) => left.CompareTo(right) > 0;

        /// <param name="left">左運算元。</param><param name="right">右運算元。</param>
        /// <returns>left 是否小於等於 right。</returns>
        public static bool operator <=(Scalar left, Scalar right) => left.CompareTo(right) <= 0;

        /// <param name="left">左運算元。</param><param name="right">右運算元。</param>
        /// <returns>left 是否大於等於 right。</returns>
        public static bool operator >=(Scalar left, Scalar right) => left.CompareTo(right) >= 0;

        /// <summary>只給繪圖／拖曳暫態用。</summary>
        /// <returns>近似的浮點值。<b>絕對不要拿這個算完再寫回模型。</b></returns>
        public double ToDouble() => RationalPart + Root2Coefficient * Root2AsDouble;

        /// <summary>序列化用的穩定字串（canonical key 的一部分）。</summary>
        /// <returns>形如 <c>"n|m"</c> 的字串。</returns>
        public string ToKey() => RationalPart.ToString() + "|" + Root2Coefficient.ToString();

        /// <returns>給人看的字串，例如 <c>"2"</c>、<c>"3√2"</c>、<c>"1+3√2"</c>。</returns>
        public override string ToString()
        {
            if (Root2Coefficient == 0) return RationalPart.ToString();
            if (RationalPart == 0) return Root2Coefficient + "√2";
            return RationalPart + (Root2Coefficient > 0 ? "+" : "") + Root2Coefficient + "√2";
        }
    }
}
