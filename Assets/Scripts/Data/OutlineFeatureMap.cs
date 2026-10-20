using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>格子在輪廓上屬於哪一種（<see cref="OutlineFeatureMap"/>）。</summary>
    public enum OutlineCellKind
    {
        /// <summary>輪廓外面，而且不是缺口。</summary>
        Outside,

        /// <summary>輪廓裡面，而且不是突出的部位（身體）。</summary>
        Inside,

        /// <summary>⭐ 輪廓裡面、<b>細的突出部位</b>：耳朵、尾巴、角。</summary>
        Protrusion,

        /// <summary>⭐ 輪廓外面、<b>窄的缺口</b>：兩耳之間、頸部、魚身與魚尾之間。</summary>
        Notch
    }

    /// <summary>
    /// ⭐ <b>輪廓的特徵格子</b>（2026-10-20，使用者定「重點是要有特徵」）。逼近搜尋拿它加權打分：
    /// 蓋到突出的部位多給分、填進缺口多扣分，耳朵、尾巴才不會被大片吃掉。
    ///
    /// <b>怎麼找</b>（影像處理的開運算與閉運算，在格子上做）：
    /// <list type="bullet">
    /// <item><b>突出 ＝ 輪廓 − 開運算</b>：輪廓先往內縮 r、再往外脹 r。比 2r 細的部位在內縮時整個消失，
    ///   脹的時候長不回來——沒長回來的格子就是突出</item>
    /// <item><b>缺口 ＝ 閉運算 − 輪廓</b>：反過來，先脹 r 再縮 r。比 2r 窄的凹縫在脹的時候被填起來，
    ///   縮的時候不會再打開——被填起來的那些輪廓外的格子就是缺口</item>
    /// </list>
    ///
    /// <b>格子</b>：每格中心在不在輪廓裡（射線法）。半徑 &gt; 0 時四周多留一圈空白（2r 再多兩格），
    /// 脹的時候才不會碰到邊。格線一律對齊格子邊長的整數倍，所以留不留空白，<b>格子中心的位置都一樣</b>——
    /// 逼近搜尋不開特徵時的分數因此跟加這支之前一模一樣（測試 <c>特徵權重設成0時結果跟第三版一模一樣</c> 釘住）。
    ///
    /// <b>距離</b>：兩格中心的歐氏距離 ≤ r 算「在 r 以內」。脹的做法是從邊界格往外蓋一個圓形印章，
    /// 成本是「邊界格數 × 印章格數」，面積 32 的輪廓在格子 1/8、半徑 0.75 時大約幾萬次。
    ///
    /// ⚠️ 浮點只用在這裡與打分，跟鐵則一不衝突：特徵只影響<b>排名</b>，挑出來的永遠是精確的 <see cref="Placement"/>。
    /// </summary>
    public sealed class OutlineFeatureMap
    {
        /// <summary>格子邊長（模型單位）。</summary>
        public readonly double CellSize;

        /// <summary>第 0 欄的左邊界 x、第 0 列的下邊界 y。<b>一定是 <see cref="CellSize"/> 的整數倍</b>。</summary>
        public readonly double OriginX;

        /// <inheritdoc cref="OriginX"/>
        public readonly double OriginY;

        /// <summary>
        /// 不留空白的話原點在哪（＝輪廓外接框往下取整到格子的整數倍）。
        /// 逼近搜尋的第一片取樣點對齊它，<b>留不留空白取樣點都一樣</b>。
        /// </summary>
        public readonly double UnpaddedOriginX;

        /// <inheritdoc cref="UnpaddedOriginX"/>
        public readonly double UnpaddedOriginY;

        /// <summary>欄數。</summary>
        public readonly int ColumnCount;

        /// <summary>列數。</summary>
        public readonly int RowCount;

        /// <summary>輪廓裡有幾格（身體加突出）。</summary>
        public readonly int InsideCellCount;

        /// <summary>突出的格數。</summary>
        public readonly int ProtrusionCellCount;

        /// <summary>缺口的格數。</summary>
        public readonly int NotchCellCount;

        /// <summary>每格的種類，索引是 <c>列 × 欄數 ＋ 欄</c>，第 0 列在最下面（y 朝上）。</summary>
        private readonly OutlineCellKind[] kinds;

        /// <param name="outline">輪廓（模型座標，逆時針或順時針都可以）。</param>
        /// <param name="cellSize">格子邊長，必須是正數。</param>
        /// <param name="featureRadius">開、閉運算的半徑 r（模型單位）。<b>0 ＝ 不找特徵</b>，只分裡外，也不留空白。</param>
        /// <exception cref="ArgumentNullException"><paramref name="outline"/> 是 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">格子邊長不是正數，或半徑是負數。</exception>
        public OutlineFeatureMap(IReadOnlyList<Vector2> outline, double cellSize, double featureRadius)
        {
            if (outline == null) throw new ArgumentNullException(nameof(outline));
            if (!(cellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "格子邊長必須是正數。");
            if (!(featureRadius >= 0.0)) throw new ArgumentOutOfRangeException(nameof(featureRadius), featureRadius, "半徑不能是負數。");

            CellSize = cellSize;

            double minimumX = double.MaxValue, minimumY = double.MaxValue;
            double maximumX = double.MinValue, maximumY = double.MinValue;
            foreach (Vector2 point in outline)
            {
                minimumX = Math.Min(minimumX, point.x); maximumX = Math.Max(maximumX, point.x);
                minimumY = Math.Min(minimumY, point.y); maximumY = Math.Max(maximumY, point.y);
            }

            // 格線對齊 cellSize 的整數倍，片那邊才能用同一套索引
            UnpaddedOriginX = Math.Floor(minimumX / cellSize) * cellSize;
            UnpaddedOriginY = Math.Floor(minimumY / cellSize) * cellSize;

            // 留白一律是整數格，原點還是 cellSize 的整數倍，格子中心的位置不變
            int paddingCells = featureRadius > 0.0 ? (int)Math.Ceiling(2.0 * featureRadius / cellSize) + 2 : 0;
            OriginX = UnpaddedOriginX - paddingCells * cellSize;
            OriginY = UnpaddedOriginY - paddingCells * cellSize;
            ColumnCount = (int)Math.Ceiling((maximumX - UnpaddedOriginX) / cellSize) + 1 + 2 * paddingCells;
            RowCount = (int)Math.Ceiling((maximumY - UnpaddedOriginY) / cellSize) + 1 + 2 * paddingCells;

            var inside = new bool[ColumnCount * RowCount];
            for (int row = 0; row < RowCount; row++)
                for (int column = 0; column < ColumnCount; column++)
                    inside[row * ColumnCount + column] = ContainsPoint(outline, OriginX + (column + 0.5) * cellSize,
                                                                                OriginY + (row + 0.5) * cellSize);

            kinds = new OutlineCellKind[inside.Length];
            if (featureRadius > 0.0)
            {
                Offset[] stamp = Stamp(featureRadius / cellSize);

                // 開運算：縮（離外面超過 r）再脹
                bool[] nearOutside = Dilate(Not(inside), stamp);
                var eroded = new bool[inside.Length];
                for (int i = 0; i < inside.Length; i++) eroded[i] = inside[i] && !nearOutside[i];
                bool[] opened = Dilate(eroded, stamp);

                // 閉運算：脹再縮（離「脹完的外面」超過 r）
                bool[] dilated = Dilate(inside, stamp);
                bool[] nearUndilated = Dilate(Not(dilated), stamp);

                for (int i = 0; i < inside.Length; i++)
                {
                    if (inside[i]) kinds[i] = opened[i] ? OutlineCellKind.Inside : OutlineCellKind.Protrusion;
                    else kinds[i] = nearUndilated[i] ? OutlineCellKind.Outside : OutlineCellKind.Notch;
                }
            }
            else
            {
                for (int i = 0; i < inside.Length; i++) kinds[i] = inside[i] ? OutlineCellKind.Inside : OutlineCellKind.Outside;
            }

            foreach (OutlineCellKind kind in kinds)
            {
                if (kind == OutlineCellKind.Inside || kind == OutlineCellKind.Protrusion) InsideCellCount++;
                if (kind == OutlineCellKind.Protrusion) ProtrusionCellCount++;
                if (kind == OutlineCellKind.Notch) NotchCellCount++;
            }
        }

        /// <summary>一格的種類。格子範圍外一律是 <see cref="OutlineCellKind.Outside"/>。</summary>
        /// <param name="column">欄。</param>
        /// <param name="row">列（第 0 列在最下面）。</param>
        /// <returns>那一格的種類。</returns>
        public OutlineCellKind KindOfCell(int column, int row) =>
            column >= 0 && column < ColumnCount && row >= 0 && row < RowCount
                ? kinds[row * ColumnCount + column]
                : OutlineCellKind.Outside;

        /// <summary>一個點落在哪一格、那一格是什麼種類。給測試與驗收工具用。</summary>
        /// <param name="x">模型座標 x。</param>
        /// <param name="y">模型座標 y。</param>
        /// <returns>那一格的種類。</returns>
        public OutlineCellKind KindAt(double x, double y) =>
            KindOfCell((int)Math.Floor((x - OriginX) / CellSize), (int)Math.Floor((y - OriginY) / CellSize));

        // ───────── 脹（圓形印章）─────────

        private readonly struct Offset
        {
            public readonly int Column;
            public readonly int Row;
            public Offset(int column, int row) { Column = column; Row = row; }
        }

        /// <summary>半徑 <paramref name="radiusInCells"/>（以格為單位）的圓：中心距離 ≤ 半徑的所有位移。</summary>
        private static Offset[] Stamp(double radiusInCells)
        {
            var offsets = new List<Offset>();
            int reach = (int)Math.Floor(radiusInCells);
            double limit = radiusInCells * radiusInCells;
            for (int row = -reach; row <= reach; row++)
                for (int column = -reach; column <= reach; column++)
                    if (column * column + row * row <= limit) offsets.Add(new Offset(column, row));
            return offsets.ToArray();
        }

        /// <summary>
        /// 脹：離 <paramref name="source"/> 任何一格的中心距離 ≤ r 的格子（含 source 自己）。
        /// 只從 source 的<b>邊界格</b>蓋印章——內部的格子蓋出來的範圍一定被邊界格蓋過。
        /// 格子範圍外當成「不是 source」，所以碰到邊的格子也算邊界格。
        /// </summary>
        private bool[] Dilate(bool[] source, Offset[] stamp)
        {
            var result = (bool[])source.Clone();
            for (int row = 0; row < RowCount; row++)
                for (int column = 0; column < ColumnCount; column++)
                {
                    if (!source[row * ColumnCount + column] || !IsBoundary(source, column, row)) continue;
                    foreach (Offset offset in stamp)
                    {
                        int targetColumn = column + offset.Column, targetRow = row + offset.Row;
                        if (targetColumn < 0 || targetColumn >= ColumnCount || targetRow < 0 || targetRow >= RowCount) continue;
                        result[targetRow * ColumnCount + targetColumn] = true;
                    }
                }
            return result;
        }

        private bool IsBoundary(bool[] source, int column, int row) =>
            column == 0 || row == 0 || column == ColumnCount - 1 || row == RowCount - 1
            || !source[row * ColumnCount + column - 1] || !source[row * ColumnCount + column + 1]
            || !source[(row - 1) * ColumnCount + column] || !source[(row + 1) * ColumnCount + column];

        private static bool[] Not(bool[] mask)
        {
            var result = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++) result[i] = !mask[i];
            return result;
        }

        /// <summary>點在不在任意多邊形裡（射線法，奇偶規則）。輪廓不是凸的。</summary>
        private static bool ContainsPoint(IReadOnlyList<Vector2> polygon, double pointX, double pointY)
        {
            bool result = false;
            for (int i = 0, previous = polygon.Count - 1; i < polygon.Count; previous = i++)
            {
                double xi = polygon[i].x, yi = polygon[i].y;
                double xj = polygon[previous].x, yj = polygon[previous].y;
                if ((yi > pointY) != (yj > pointY) &&
                    pointX < (xj - xi) * (pointY - yi) / (yj - yi) + xi)
                    result = !result;
            }
            return result;
        }
    }
}
