using System;
using System.Collections.Generic;
using Tangram.Data;
using UnityEngine;

namespace Tangram.Core
{
    /// <summary>
    /// 拖曳吸附（附錄 B）。互動層，不入庫。
    ///
    /// 關鍵：候選集合裡全是精確座標 → 模型層根本無法表示「差一點點」的狀態。
    /// 不是靠容差判定相等，是近似值不存在。
    /// </summary>
    public static class SnapSolver
    {
        public struct Settings
        {
            /// <summary>一個模型單位（小三角的股 = 2）對應幾個螢幕像素。</summary>
            public float PixelsPerUnit;
            /// <summary>吸附半徑（螢幕像素，約 18）。</summary>
            public float SnapRadiusPixels;
            /// <summary>脫離半徑（螢幕像素，約 30）。遲滯：吸上去容易，甩開來難。</summary>
            public float ReleaseRadiusPixels;

            public static Settings Default(float pixelsPerUnit) => new Settings
            {
                PixelsPerUnit = pixelsPerUnit,
                SnapRadiusPixels = 18f,
                ReleaseRadiusPixels = 30f
            };
        }

        public readonly struct Candidate
        {
            public readonly Placement Placement;
            public readonly float PixelDistance;
            public readonly int TargetPiece;    // 吸到哪一片（已放置清單裡的索引）
            public readonly int TargetNode;     // 吸到它的哪個節點
            public readonly int DraggedNode;    // 用拖曳片的哪個節點去對

            public Candidate(Placement placement, float pixelDistance,
                             int targetPiece, int targetNode, int draggedNode)
            {
                Placement = placement;
                PixelDistance = pixelDistance;
                TargetPiece = targetPiece;
                TargetNode = targetNode;
                DraggedNode = draggedNode;
            }
        }

        public readonly struct DragResult
        {
            public readonly bool Snapped;
            public readonly Placement Placement;      // Snapped = true 時有效，精確座標
            public readonly Vector2 FreePosition;     // Snapped = false 時有效，自由拖曳的浮點位置
            public readonly int PivotNode;            // 吸附時用到的節點，旋轉軸心用它

            private DragResult(bool snapped, Placement placement, Vector2 freePosition, int pivotNode)
            {
                Snapped = snapped;
                Placement = placement;
                FreePosition = freePosition;
                PivotNode = pivotNode;
            }

            public static DragResult Snap(Placement placement, int pivotNode)
                => new DragResult(true, placement, default, pivotNode);

            public static DragResult Free(Vector2 freePosition)
                => new DragResult(false, default, freePosition, -1);
        }

        /// <summary>模型座標 → 浮點（只給繪圖與命中測試用，永不回寫）。</summary>
        public static Vector2 ToUnityVector2(ExactVector2 exactPosition)
            => new Vector2((float)exactPosition.X.ToDouble(), (float)exactPosition.Y.ToDouble());

        /// <summary>自由旋轉的角度 → 離散的旋轉值。旋轉天生離散，直接 round(θ/45°)。</summary>
        public static int RotationFromDegrees(float degrees)
            => Symmetry.Modulo8((int)Math.Round(degrees / 45.0, MidpointRounding.AwayFromZero));

        public static float RotationToDegrees(int rotation) => Symmetry.Modulo8(rotation) * 45f;

        /// <summary>翻面只對平行四邊形開放（其餘片翻面等同旋轉）。</summary>
        public static bool CanFlip(PieceKind kind) => kind == PieceKind.Parallelogram;

        /// <summary>
        /// 步驟 1+2：列出所有「已放置片節點 − 拖曳片節點」的候選落點，
        /// 依螢幕像素距離篩掉太遠的，由近到遠排序。結果必為精確值。
        /// </summary>
        public static List<Candidate> Candidates(PieceKind kind, int rotation, bool flipped,
                                                 Vector2 freePosition,
                                                 IReadOnlyList<Placement> placedPieces,
                                                 Settings settings,
                                                 bool currentlySnapped = false)
        {
            // 遲滯就是這一行：還沒吸住要靠很近才吸得上，吸住之後要拉很遠才甩得掉
            float radiusPixels = currentlySnapped ? settings.ReleaseRadiusPixels : settings.SnapRadiusPixels;
            double maximumDistanceInUnits = radiusPixels / Math.Max(settings.PixelsPerUnit, 1e-6f);
            double maximumDistanceSquared = maximumDistanceInUnits * maximumDistanceInUnits;

            PiecePrototype prototype = PiecePrototype.Get(kind);
            var draggedPieceNodes = new ExactVector2[prototype.NodeCount];
            for (int i = 0; i < prototype.NodeCount; i++)
                draggedPieceNodes[i] = Symmetry.Apply(rotation, flipped, prototype.Nodes[i]);

            var candidates = new List<Candidate>();
            for (int pieceIndex = 0; pieceIndex < placedPieces.Count; pieceIndex++)
            {
                ExactVector2[] targetNodes = placedPieces[pieceIndex].Nodes();

                for (int targetNodeIndex = 0; targetNodeIndex < targetNodes.Length; targetNodeIndex++)
                {
                    for (int draggedNodeIndex = 0; draggedNodeIndex < draggedPieceNodes.Length; draggedNodeIndex++)
                    {
                        // 精確：兩個節點座標相減就是候選落點
                        ExactVector2 candidateTranslation =
                            targetNodes[targetNodeIndex] - draggedPieceNodes[draggedNodeIndex];

                        double deltaX = candidateTranslation.X.ToDouble() - freePosition.x;
                        double deltaY = candidateTranslation.Y.ToDouble() - freePosition.y;
                        double distanceSquared = deltaX * deltaX + deltaY * deltaY;
                        if (distanceSquared > maximumDistanceSquared) continue;

                        candidates.Add(new Candidate(
                            new Placement(kind, rotation, flipped, candidateTranslation),
                            (float)(Math.Sqrt(distanceSquared) * settings.PixelsPerUnit),
                            pieceIndex, targetNodeIndex, draggedNodeIndex));
                    }
                }
            }

            candidates.Sort((left, right) => left.PixelDistance.CompareTo(right.PixelDistance));
            return candidates;
        }

        /// <summary>
        /// 步驟 3+4：排序後逐一驗證重疊，第一個通過的採用；全落空就保持自由拖曳。
        /// </summary>
        public static DragResult Resolve(PieceKind kind, int rotation, bool flipped,
                                         Vector2 freePosition,
                                         IReadOnlyList<Placement> placedPieces,
                                         Settings settings,
                                         bool currentlySnapped = false)
        {
            List<Candidate> candidates = Candidates(
                kind, rotation, flipped, freePosition, placedPieces, settings, currentlySnapped);

            foreach (Candidate candidate in candidates)
                if (!OverlapsAny(candidate.Placement, placedPieces))
                    return DragResult.Snap(candidate.Placement, candidate.DraggedNode);

            return DragResult.Free(freePosition);
        }

        /// <summary>盤面上還沒有任何片時的落點：直接取最近的整數格點，仍是精確值。</summary>
        public static Placement SnapToOrigin(PieceKind kind, int rotation, bool flipped,
                                             Vector2 freePosition)
        {
            long x = (long)Math.Round(freePosition.x, MidpointRounding.AwayFromZero);
            long y = (long)Math.Round(freePosition.y, MidpointRounding.AwayFromZero);
            return new Placement(kind, rotation, flipped, new ExactVector2(x, y));
        }

        public static bool OverlapsAny(Placement candidate, IReadOnlyList<Placement> placedPieces)
        {
            ExactVector2[] candidatePolygon = candidate.Vertices();
            for (int i = 0; i < placedPieces.Count; i++)
                if (Geometry.ConvexOverlap(candidatePolygon, placedPieces[i].Vertices())) return true;
            return false;
        }
    }
}
