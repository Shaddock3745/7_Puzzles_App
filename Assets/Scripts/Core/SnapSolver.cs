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
    ///
    /// 盒子模式下的兩個硬限制（互動設計 §4）都在這裡把關：
    /// 候選必須完全落在盒內（由呼叫端傳 isAllowed 進來），而且不能與已放置的片重疊。
    /// 這支刻意不認識 Puzzle 或 Region，換規則時不用動它。
    /// </summary>
    public static class SnapSolver
    {
        /// <summary>吸附的手感參數。全部以螢幕像素為單位，才不會因解析度而變。</summary>
        public struct Settings
        {
            /// <summary>
            /// 一個模型單位（小三角的股 = 2）對應幾個**螢幕**像素。
            ///
            /// ⚠️ 不要跟 Unity **Sprite 匯入設定**的 Pixels Per Unit 搞混，兩者完全不同：
            /// 那個是「紋理像素 → 世界單位」，是固定的資產設定；
            /// 這個是「模型單位 → 螢幕像素」，由相機的 fit 算出來，**每一題都會變**。
            /// </summary>
            public float ScreenPixelsPerModelUnit;
            /// <summary>吸附半徑（螢幕像素，約 18）。</summary>
            public float SnapRadiusPixels;
            /// <summary>脫離半徑（螢幕像素，約 30）。遲滯：吸上去容易，甩開來難。</summary>
            public float ReleaseRadiusPixels;

            /// <param name="screenPixelsPerModelUnit">一個模型單位對應幾個螢幕像素。</param>
            /// <returns>吸附 18px、脫離 30px 的預設設定。</returns>
            public static Settings Default(float screenPixelsPerModelUnit) => new Settings
            {
                ScreenPixelsPerModelUnit = screenPixelsPerModelUnit,
                SnapRadiusPixels = 18f,
                ReleaseRadiusPixels = 30f
            };
        }

        /// <summary>一個候選落點。全部欄位都是唯讀的診斷資訊，UI 可以拿去做提示。</summary>
        public readonly struct Candidate
        {
            /// <summary>候選的精確擺放。</summary>
            public readonly Placement Placement;
            /// <summary>這個落點離手指多遠，換算成螢幕像素。</summary>
            public readonly float PixelDistance;
            /// <summary>吸到哪一個錨點來源：已放置清單裡的索引，−1 代表吸到盒子輪廓。</summary>
            public readonly int TargetPiece;
            /// <summary>吸到該來源的哪一個節點（片的節點索引，或 extraAnchors 的索引）。</summary>
            public readonly int TargetNode;
            /// <summary>用拖曳片的哪個節點去對。旋轉軸心用它。</summary>
            public readonly int DraggedNode;

            /// <param name="placement">候選的精確擺放。</param>
            /// <param name="pixelDistance">離手指的螢幕像素距離。</param>
            /// <param name="targetPiece">錨點來源的片索引，−1 代表盒子輪廓。</param>
            /// <param name="targetNode">錨點在該來源裡的索引。</param>
            /// <param name="draggedNode">拖曳片用來對位的節點索引。</param>
            public Candidate(Placement placement, float pixelDistance,
                             int targetPiece, int targetNode, int draggedNode)
            {
                Placement = placement;
                PixelDistance = pixelDistance;
                TargetPiece = targetPiece;
                TargetNode = targetNode;
                DraggedNode = draggedNode;
            }

            /// <summary>錨點是不是來自盒子輪廓而不是某一片。</summary>
            public bool IsBoxAnchor => TargetPiece < 0;
        }

        /// <summary>
        /// 一次拖曳更新的結果。建構式是私有的，只能透過 Snap / Free 產生，
        /// 避免出現「Snapped = true 但 Placement 沒填」這種狀態。
        /// </summary>
        public readonly struct DragResult
        {
            /// <summary>有沒有吸住。</summary>
            public readonly bool Snapped;
            /// <summary>Snapped = true 時有效，精確座標。</summary>
            public readonly Placement Placement;
            /// <summary>Snapped = false 時有效，自由拖曳的浮點位置。</summary>
            public readonly Vector2 FreePosition;
            /// <summary>吸附時用到的節點索引，旋轉軸心用它；沒吸住時是 −1。</summary>
            public readonly int PivotNode;

            private DragResult(bool snapped, Placement placement, Vector2 freePosition, int pivotNode)
            {
                Snapped = snapped;
                Placement = placement;
                FreePosition = freePosition;
                PivotNode = pivotNode;
            }

            /// <param name="placement">吸附後的精確擺放。</param>
            /// <param name="pivotNode">吸附用到的拖曳片節點索引。</param>
            /// <returns>吸住狀態的結果。</returns>
            public static DragResult Snap(Placement placement, int pivotNode)
                => new DragResult(true, placement, default, pivotNode);

            /// <param name="freePosition">自由拖曳的浮點位置。</param>
            /// <returns>沒吸住狀態的結果。</returns>
            public static DragResult Free(Vector2 freePosition)
                => new DragResult(false, default, freePosition, -1);
        }

        /// <summary>模型座標 → 浮點（只給繪圖與命中測試用，永不回寫）。</summary>
        /// <param name="exactPosition">模型的精確座標。</param>
        /// <returns>對應的浮點座標。模型單位與 Unity 單位是 1:1。</returns>
        public static Vector2 ToUnityVector2(ExactVector2 exactPosition)
            => new Vector2((float)exactPosition.X.ToDouble(), (float)exactPosition.Y.ToDouble());

        /// <summary>自由旋轉的角度 → 離散的旋轉值。旋轉天生離散，直接 round(θ/45°)。</summary>
        /// <param name="degrees">角度，可以是任意實數（含負值）。</param>
        /// <returns>0~7 的旋轉值。</returns>
        public static int RotationFromDegrees(float degrees)
            => Symmetry.Modulo8((int)Math.Round(degrees / 45.0, MidpointRounding.AwayFromZero));

        /// <param name="rotation">0~7 的旋轉值，超出範圍會先取模。</param>
        /// <returns>對應的角度（0、45、…、315）。</returns>
        public static float RotationToDegrees(int rotation) => Symmetry.Modulo8(rotation) * 45f;

        /// <summary>
        /// 翻面只對平行四邊形開放（其餘片翻面等同旋轉）。
        /// ⚠️ 規則本身住在 <see cref="PiecePrototype.CanFlip"/>——那是幾何事實不是 UI 規則，
        /// 而且出題器也要用同一份。這裡只是互動層的門面。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <returns>這一片的翻面鈕該不該亮。</returns>
        public static bool CanFlip(PieceKind kind) => PiecePrototype.CanFlip(kind);

        /// <summary>
        /// 一片實際有幾個不同的旋轉姿態。旋轉鈕要照這個數字循環，
        /// 不然玩家會看到「按了沒反應」（互動設計 §5）。
        /// ⚠️ 規則本身住在 <see cref="PiecePrototype.RotationCycleLength"/>，這裡只是門面。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <returns>三角形 8、正方形 2、平行四邊形 4。</returns>
        public static int RotationCycleLength(PieceKind kind)
            => PiecePrototype.RotationCycleLength(kind);

        /// <summary>
        /// 步驟 1+2：列出所有「錨點 − 拖曳片節點」的候選落點，依螢幕像素距離篩掉太遠的，
        /// 由近到遠排序。結果必為精確值。
        ///
        /// 錨點 = 已放置片的所有節點 ∪ extraAnchors（盒子輪廓的節點）。
        ///
        /// ⚠️ <b>「什麼叫貼合」不在這裡，在 <see cref="PlacementCandidates"/></b>。
        /// 這一支只多做三件互動層的事：把螢幕像素的半徑換算成模型單位、
        /// 標記錨點來自哪一片、依離手指的距離排序。出題器與求解器直接用那支，
        /// 所以三邊看到的「貼合」永遠是同一個定義。
        /// </summary>
        /// <param name="kind">拖曳中那一片的片種。</param>
        /// <param name="rotation">拖曳中的旋轉值 0~7。</param>
        /// <param name="flipped">拖曳中是否翻面。</param>
        /// <param name="freePosition">手指對應的浮點位置，也就是「這一片的平移量會是多少」的浮點估計。</param>
        /// <param name="placedPieces">已經放到盤上的片，**不要包含拖曳中的自己**。同時是錨點來源與重疊比對對象。</param>
        /// <param name="settings">手感參數。</param>
        /// <param name="currentlySnapped">上一幀有沒有吸住。決定用吸附半徑還是脫離半徑，這一個布林就是遲滯。</param>
        /// <param name="extraAnchors">額外的錨點，盒子模式傳 <c>boxRegion.BoundaryNodes</c>。傳 null 代表沒有。</param>
        /// <returns>
        /// 幾何上夠近的候選，依離手指的螢幕像素距離由近到遠排序。可能是空清單
        /// （手指太遠，或盤面與盒子都沒有錨點）。
        ///
        /// 這裡<b>只做距離篩選</b>——盒內限制與重疊都不管，那兩個都在
        /// <see cref="Resolve"/> 的「由近到遠、通過就 return」迴圈裡才驗。
        /// 因為第一個候選通常就通過，懶惰檢查只付一次的成本，
        /// 先過濾整份清單則要付 N 次（<c>Region.ContainsPolygon</c> 實測 7.3 µs／次）。
        /// </returns>
        public static List<Candidate> Candidates(PieceKind kind, int rotation, bool flipped,
                                                 Vector2 freePosition,
                                                 IReadOnlyList<Placement> placedPieces,
                                                 Settings settings,
                                                 bool currentlySnapped = false,
                                                 IReadOnlyList<ExactVector2> extraAnchors = null)
        {
            // 遲滯就是這一行：還沒吸住要靠很近才吸得上，吸住之後要拉很遠才甩得掉
            float radiusPixels = currentlySnapped ? settings.ReleaseRadiusPixels : settings.SnapRadiusPixels;
            double maximumDistanceInUnits = radiusPixels / Math.Max(settings.ScreenPixelsPerModelUnit, 1e-6f);
            double maximumDistanceSquared = maximumDistanceInUnits * maximumDistanceInUnits;

            // 一次算好，底下每一組錨點都重複用
            var pose = new PieceNodeSet(kind, rotation, flipped);

            var candidates = new List<Candidate>();
            var batch = new List<PlacementCandidate>();

            for (int pieceIndex = 0; pieceIndex < placedPieces.Count; pieceIndex++)
            {
                ExactVector2[] anchors = placedPieces[pieceIndex].Nodes();
                CollectFromAnchors(anchors, pieceIndex);
            }

            if (extraAnchors != null) CollectFromAnchors(extraAnchors, -1);

            candidates.Sort((left, right) => left.PixelDistance.CompareTo(right.PixelDistance));
            return candidates;

            // 把一組錨點交給共用的枚舉，再補上互動層才知道的兩件事：來源片索引與螢幕像素距離
            void CollectFromAnchors(IReadOnlyList<ExactVector2> anchors, int sourcePieceIndex)
            {
                // 每組錨點清空重用，這樣 batch 裡的順序就是這一組的順序，
                // 不必記 batch 的起點——而順序必須維持「錨點外層、節點內層」（見下面的 ⚠️）
                batch.Clear();
                PlacementCandidates.EnumerateWithin(
                    pose, anchors, batch,
                    freePosition.x, freePosition.y, maximumDistanceSquared);

                for (int i = 0; i < batch.Count; i++)
                {
                    PlacementCandidate candidate = batch[i];

                    // 只對通過篩選的候選算一次距離。通過的通常只有個位數，
                    // 所以「篩選時算過、這裡再算一次」比讓枚舉層回報距離划算，
                    // 也讓那一層不必知道螢幕像素的存在
                    ExactVector2 translation = candidate.Placement.Translation;
                    double deltaX = translation.X.ToDouble() - freePosition.x;
                    double deltaY = translation.Y.ToDouble() - freePosition.y;
                    double distanceSquared = deltaX * deltaX + deltaY * deltaY;

                    candidates.Add(new Candidate(
                        candidate.Placement,
                        (float)(Math.Sqrt(distanceSquared) * settings.ScreenPixelsPerModelUnit),
                        sourcePieceIndex, candidate.AnchorIndex, candidate.PieceNodeIndex));
                }
            }
        }

        /// <summary>
        /// 步驟 3+4：排序後逐一驗證重疊，第一個通過的採用；全落空就保持自由拖曳。
        /// 所以它永遠不會把片吸到會重疊或會出盒的位置。
        /// </summary>
        /// <param name="kind">拖曳中那一片的片種。</param>
        /// <param name="rotation">拖曳中的旋轉值 0~7。</param>
        /// <param name="flipped">拖曳中是否翻面。</param>
        /// <param name="freePosition">手指對應的浮點位置。</param>
        /// <param name="placedPieces">已經放到盤上的片，不要包含拖曳中的自己。</param>
        /// <param name="settings">手感參數。</param>
        /// <param name="currentlySnapped">上一幀有沒有吸住（遲滯）。</param>
        /// <param name="extraAnchors">額外錨點，盒子模式傳 <c>boxRegion.BoundaryNodes</c>。</param>
        /// <param name="isAllowed">
        /// 合法性判斷式，盒子模式傳 <c>placement =&gt; boxRegion.Contains(placement)</c>。
        /// 傳 null 代表不限制。<b>刻意在這個迴圈裡才驗，不是先過濾整份候選清單</b>
        /// ——第一個候選通常就通過，這樣只付一次的成本。
        /// </param>
        /// <returns>
        /// 吸住時是 <see cref="DragResult.Snap"/>（含精確擺放與旋轉軸心節點），
        /// 沒吸住時是 <see cref="DragResult.Free"/>（原樣帶回 freePosition）。
        /// 所以它永遠不會把片吸到會重疊或會出盒的位置。
        /// </returns>
        public static DragResult Resolve(PieceKind kind, int rotation, bool flipped,
                                         Vector2 freePosition,
                                         IReadOnlyList<Placement> placedPieces,
                                         Settings settings,
                                         bool currentlySnapped = false,
                                         IReadOnlyList<ExactVector2> extraAnchors = null,
                                         Func<Placement, bool> isAllowed = null)
        {
            List<Candidate> candidates = Candidates(
                kind, rotation, flipped, freePosition, placedPieces, settings,
                currentlySnapped, extraAnchors);

            foreach (Candidate candidate in candidates)
            {
                // 盒內限制排在重疊前面：出盒的候選多半會被包圍盒 0.6 µs 就擋掉
                if (isAllowed != null && !isAllowed(candidate.Placement)) continue;
                if (OverlapsAny(candidate.Placement, placedPieces)) continue;
                return DragResult.Snap(candidate.Placement, candidate.DraggedNode);
            }

            return DragResult.Free(freePosition);
        }

        /// <summary>
        /// 沒有任何錨點可用時的退路：取最近的整數格點。
        ///
        /// 盒子模式下幾乎用不到——盒子輪廓的節點已經在錨點集合裡，第一片就吸得到東西。
        /// 留著是給「沒有盒子」的情境（例如出題器的除錯視圖）。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <param name="rotation">旋轉值 0~7。</param>
        /// <param name="flipped">是否翻面。</param>
        /// <param name="freePosition">手指對應的浮點位置。</param>
        /// <param name="isAllowed">合法性判斷式，傳 null 代表不限制。</param>
        /// <param name="placement">成功時是落在最近整數格點的精確擺放；失敗時是 default。</param>
        /// <returns>取整後的位置是否通過 isAllowed。false 代表呼叫端該維持自由拖曳。</returns>
        public static bool TrySnapToOrigin(PieceKind kind, int rotation, bool flipped,
                                           Vector2 freePosition,
                                           Func<Placement, bool> isAllowed,
                                           out Placement placement)
        {
            long x = (long)Math.Round(freePosition.x, MidpointRounding.AwayFromZero);
            long y = (long)Math.Round(freePosition.y, MidpointRounding.AwayFromZero);
            placement = new Placement(kind, rotation, flipped, new ExactVector2(x, y));

            if (isAllowed != null && !isAllowed(placement))
            {
                placement = default;
                return false;
            }
            return true;
        }

        /// <summary>候選是否與任何已放置的片重疊。碰到不算重疊，只有內部交集才算。</summary>
        /// <param name="candidate">要檢查的擺放。</param>
        /// <param name="placedPieces">已放置的片，不含 candidate 自己。</param>
        /// <returns>只要與其中一片內部交集就回 true。</returns>
        public static bool OverlapsAny(Placement candidate, IReadOnlyList<Placement> placedPieces)
        {
            ExactVector2[] candidatePolygon = candidate.Vertices();
            for (int i = 0; i < placedPieces.Count; i++)
                if (Geometry.ConvexOverlap(candidatePolygon, placedPieces[i].Vertices())) return true;
            return false;
        }
    }
}
