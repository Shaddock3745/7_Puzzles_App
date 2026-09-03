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

        public static int Modulo8(int value) => ((value % 8) + 8) % 8;

        /// <summary>沿指定方向走指定長度的位移向量。</summary>
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

        public static ExactVector2 Rotate90(ExactVector2 point)
            => new ExactVector2(-point.Y, point.X);

        /// <summary>(x,y) → ((x−y)·√2/2, (x+y)·√2/2)。有理部須為偶數，assert 在 MultiplyByHalfRoot2。</summary>
        public static ExactVector2 Rotate45(ExactVector2 point)
            => new ExactVector2((point.X - point.Y).MultiplyByHalfRoot2(),
                                (point.X + point.Y).MultiplyByHalfRoot2());

        public static ExactVector2 MirrorAcrossXAxis(ExactVector2 point)
            => new ExactVector2(point.X, -point.Y);

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

        public static int ApplyToDirection(int rotation, bool flipped, int direction)
            => Modulo8((flipped ? -direction : direction) + rotation);

        /// <summary>16 種整體變換。</summary>
        public static readonly (int rotation, bool flipped)[] GlobalTransforms = BuildGlobalTransforms();

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
        public static (int rotation, bool flipped) Compose(
            int globalRotation, bool globalFlipped, int rotation, bool flipped)
            => (Modulo8(globalRotation + (globalFlipped ? -rotation : rotation)),
                globalFlipped ^ flipped);
    }
}
