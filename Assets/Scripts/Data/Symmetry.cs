using System;

namespace Tangram.Data
{
    /// <summary>
    /// 8 個方向（×45°）與 16 種整體變換（8 旋轉 × 2 鏡射）。
    /// 約定：Apply = 旋轉 ∘ 鏡射，鏡射是對 X 軸（y → −y）。
    /// </summary>
    public static class Symmetry
    {
        public const int DirectionCount = 8;

        /// <param name="value">任意整數，可以是負的。</param>
        /// <returns>0~7 的餘數。C# 的 % 對負數會回負值，這裡補正。</returns>
        public static int Modulo8(int value) => ((value % 8) + 8) % 8;

        /// <summary>沿指定方向走指定長度的位移向量。</summary>
        /// <param name="direction">0~7 的方向，0 = +X，逆時針每格 45°。超出範圍會先取模。</param>
        /// <param name="length">要走的長度。</param>
        /// <returns>位移向量。</returns>
        /// <exception cref="InvalidOperationException">
        /// 斜向（奇數方向）時長度的有理部是奇數，分量會離開 ℤ[√2]。
        /// </exception>
        public static ExactVector2 Step(int direction, Scalar length)
        {
            switch (Modulo8(direction))
            {
                case 0: return new ExactVector2(length, Scalar.Zero);
                case 2: return new ExactVector2(Scalar.Zero, length);
                case 4: return new ExactVector2(-length, Scalar.Zero);
                case 6: return new ExactVector2(Scalar.Zero, -length);
            }

            Scalar diagonalComponent = length.MultiplyByHalfRoot2();   // 斜向分量 = 長度·√2/2
            switch (Modulo8(direction))
            {
                case 1: return new ExactVector2(diagonalComponent, diagonalComponent);
                case 3: return new ExactVector2(-diagonalComponent, diagonalComponent);
                case 5: return new ExactVector2(-diagonalComponent, -diagonalComponent);
                default: return new ExactVector2(diagonalComponent, -diagonalComponent); // 7
            }
        }

        /// <summary>把向量拆成方向與長度。模型內的向量必定落在 8 個方向之一。</summary>
        /// <param name="vector">要拆解的向量。</param>
        /// <param name="direction">成功時是 0~7 的方向；失敗時是 0。</param>
        /// <param name="length">成功時是長度（必為正）；失敗時是 0。</param>
        /// <returns>
        /// 是否拆解成功。零向量或不在 45° 網格上（|x| ≠ |y| 的斜向）會回 false。
        /// </returns>
        public static bool TryDecompose(ExactVector2 vector, out int direction, out Scalar length)
        {
            direction = 0;
            length = Scalar.Zero;
            if (vector.X.IsZero && vector.Y.IsZero) return false;

            if (vector.Y.IsZero)
            {
                direction = vector.X.Sign > 0 ? 0 : 4;
                length = vector.X.Abs;
                return true;
            }
            if (vector.X.IsZero)
            {
                direction = vector.Y.Sign > 0 ? 2 : 6;
                length = vector.Y.Abs;
                return true;
            }

            if (vector.X.Abs != vector.Y.Abs) return false;   // 不在 45° 網格上

            int signX = vector.X.Sign;
            int signY = vector.Y.Sign;
            direction = signX > 0 ? (signY > 0 ? 1 : 7) : (signY > 0 ? 3 : 5);
            length = vector.X.Abs.MultiplyByRoot2();
            return true;
        }

        /// <param name="point">要旋轉的點。</param>
        /// <returns>繞原點逆時針轉 90° 的結果。純粹換位加取負，永遠精確。</returns>
        public static ExactVector2 Rotate90(ExactVector2 point)
            => new ExactVector2(-point.Y, point.X);

        /// <summary>(x,y) → ((x−y)·√2/2, (x+y)·√2/2)。有理部須為偶數，assert 在 MultiplyByHalfRoot2。</summary>
        /// <param name="point">要旋轉的點。</param>
        /// <returns>繞原點逆時針轉 45° 的結果。</returns>
        /// <exception cref="InvalidOperationException">x±y 的有理部是奇數，結果會離開 ℤ[√2]。</exception>
        public static ExactVector2 Rotate45(ExactVector2 point)
            => new ExactVector2((point.X - point.Y).MultiplyByHalfRoot2(),
                                (point.X + point.Y).MultiplyByHalfRoot2());

        /// <param name="point">要鏡射的點。</param>
        /// <returns>對 X 軸鏡射（y 取負）的結果。</returns>
        public static ExactVector2 MirrorAcrossXAxis(ExactVector2 point)
            => new ExactVector2(point.X, -point.Y);

        /// <summary>
        /// 套用姿態。順序是**先翻面再旋轉**，不含平移。
        /// 先轉 90° 的倍數、最後才轉 45°，這樣中途不會離開 ℤ[√2]。
        /// </summary>
        /// <param name="rotation">0~7 的旋轉值，超出範圍會先取模。</param>
        /// <param name="flipped">是否先對 X 軸鏡射。</param>
        /// <param name="point">原型座標系的點。</param>
        /// <returns>套用姿態後的點（仍相對於片自己的原點）。</returns>
        public static ExactVector2 Apply(int rotation, bool flipped, ExactVector2 point)
        {
            if (flipped) point = MirrorAcrossXAxis(point);
            rotation = Modulo8(rotation);

            // 先轉 90° 的倍數，避免中途離開 ℤ[√2]
            for (int quarterTurn = 0; quarterTurn < rotation / 2; quarterTurn++)
                point = Rotate90(point);

            if ((rotation & 1) != 0) point = Rotate45(point);
            return point;
        }

        /// <summary>方向版的 Apply。翻面讓方向取負，旋轉讓方向加值。</summary>
        /// <param name="rotation">0~7 的旋轉值。</param>
        /// <param name="flipped">是否翻面。</param>
        /// <param name="direction">0~7 的原方向。</param>
        /// <returns>套用姿態後的 0~7 方向。</returns>
        public static int ApplyToDirection(int rotation, bool flipped, int direction)
            => Modulo8((flipped ? -direction : direction) + rotation);

        /// <summary>16 種整體變換。canonical key 就是對這 16 種各算一次取最小。</summary>
        public static readonly (int rotation, bool flipped)[] GlobalTransforms = BuildGlobalTransforms();

        /// <returns>8 個旋轉 × 2 個翻面，共 16 組 (rotation, flipped)。</returns>
        private static (int, bool)[] BuildGlobalTransforms()
        {
            var transforms = new (int, bool)[16];
            int writeIndex = 0;
            for (int rotation = 0; rotation < 8; rotation++)
            {
                transforms[writeIndex++] = (rotation, false);
                transforms[writeIndex++] = (rotation, true);
            }
            return transforms;
        }

        /// <summary>
        /// 把整體變換疊到區域姿態上。因為鏡射與旋轉不可交換
        /// （鏡射 ∘ 旋轉 r = 旋轉 −r ∘ 鏡射），整體有翻面時區域旋轉要取負。
        /// </summary>
        /// <param name="globalRotation">整體變換的旋轉值 0~7。</param>
        /// <param name="globalFlipped">整體變換是否翻面。</param>
        /// <param name="rotation">片原本的旋轉值 0~7。</param>
        /// <param name="flipped">片原本是否翻面。</param>
        /// <returns>疊加後的 (rotation, flipped)。</returns>
        public static (int rotation, bool flipped) Compose(
            int globalRotation, bool globalFlipped, int rotation, bool flipped)
            => (Modulo8(globalRotation + (globalFlipped ? -rotation : rotation)),
                globalFlipped ^ flipped);
    }
}
