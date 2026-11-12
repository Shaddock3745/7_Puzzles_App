using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 網格化的可行性驗證（中途回饋開工前的前置，2026-09-09）。
    ///
    /// <b>要回答的問題</b>：中途回饋的第二層要算「剩餘區域裂成幾塊、各多大」。
    /// 最省的做法是<b>把盒子切成 16 個小三角格</b>，剩餘區域＝沒被蓋到的格子，
    /// 連通塊＝格子的連通分量——這樣就完全不需要多邊形布林運算。
    ///
    /// 但它靠一個假設：<b>任何合法擺放的片，都是若干個「完整」cell 的聯集。</b>
    /// 這一組測試就是去驗那件事。
    /// </summary>
    public class CellGridTests
    {
        // ─────────────────────────────────────────────────────────────
        // 每種片切成小三角的固定表（原型座標）。
        //
        // ⚠️ 切法幾乎都是唯一的——**只有正方形有兩條對角線可選**。
        // 平行四邊形看起來有兩條，但另一條長 2√5，切出來不是小三角，所以也是唯一。
        //
        // 這份表暫時放在測試裡：先確認假設成不成立，再決定要不要變成正式的 API。
        // ─────────────────────────────────────────────────────────────

        /// <param name="kind">片種。</param>
        /// <returns>把這一片切成小三角的那幾格，原型座標。</returns>
        private static ExactVector2[][] PrototypeCells(PieceKind kind)
        {
            switch (kind)
            {
                case PieceKind.SmallTriangle:      // 自己就是一格
                    return new[]
                    {
                        Cell(0, 0, 2, 0, 0, 2)
                    };

                case PieceKind.Square:             // 沿對角線 (0,0)-(2,2)
                    return new[]
                    {
                        Cell(0, 0, 2, 0, 2, 2),
                        Cell(0, 0, 2, 2, 0, 2)
                    };

                case PieceKind.MediumTriangle:     // 直角頂點拉到斜邊中點 (0,2)
                    return new[]
                    {
                        Cell(0, 0, 2, 2, 0, 2),
                        Cell(0, 0, 0, 2, -2, 2)
                    };

                case PieceKind.Parallelogram:      // 唯一可切的對角線 (2,0)-(2,2)
                    return new[]
                    {
                        Cell(0, 0, 2, 0, 2, 2),
                        Cell(2, 0, 4, 2, 2, 2)
                    };

                case PieceKind.LargeTriangle:      // 先切成兩個中三角，各自再切一次
                    return new[]
                    {
                        Cell(0, 0, 2, 0, 2, 2),
                        Cell(2, 0, 4, 0, 2, 2),
                        Cell(0, 0, 2, 2, 0, 2),
                        Cell(0, 2, 2, 2, 0, 4)
                    };

                default:
                    return new ExactVector2[0][];
            }
        }

        private static ExactVector2[] Cell(int x0, int y0, int x1, int y1, int x2, int y2)
            => new[]
            {
                new ExactVector2(x0, y0), new ExactVector2(x1, y1), new ExactVector2(x2, y2)
            };

        /// <summary>一格（小三角）的有向面積 ×2。小三角面積 2，所以是 4。</summary>
        private static readonly Scalar CellDoubleArea = new Scalar(4, 0);

        /// <param name="piece">一片的絕對擺放。</param>
        /// <returns>這一片切出來的格子，已經變換到世界座標。</returns>
        private static List<ExactVector2[]> CellsOf(Placement piece)
        {
            var cells = new List<ExactVector2[]>();
            foreach (ExactVector2[] prototypeCell in PrototypeCells(piece.Kind))
            {
                var worldCell = new ExactVector2[prototypeCell.Length];
                for (int i = 0; i < prototypeCell.Length; i++)
                    worldCell[i] = piece.ToWorld(prototypeCell[i]);
                cells.Add(worldCell);
            }
            return cells;
        }

        /// <returns>經典大正方形當盒子，原點在 (0,0)。</returns>
        private static Region ClassicSquareBox()
            => Region.FromPuzzle(Puzzle.FromPlacements(Fixtures.ClassicSquare()), ExactVector2.Zero);

        // ─────────────────────────────────────────────────────────────
        // 一、切法本身對不對
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void 網格_每種片切出來的格數與面積都對()
        {
            var expectedCellCount = new Dictionary<PieceKind, int>
            {
                { PieceKind.LargeTriangle, 4 },
                { PieceKind.MediumTriangle, 2 },
                { PieceKind.SmallTriangle, 1 },
                { PieceKind.Square, 2 },
                { PieceKind.Parallelogram, 2 }
            };

            foreach (KeyValuePair<PieceKind, int> entry in expectedCellCount)
            {
                ExactVector2[][] cells = PrototypeCells(entry.Key);
                Assert.AreEqual(entry.Value, cells.Length, $"{entry.Key} 的格數");

                Scalar total = new Scalar(0, 0);
                foreach (ExactVector2[] cell in cells)
                {
                    Scalar doubleArea = Geometry.SignedDoubleArea(cell);
                    if (doubleArea.Sign < 0) doubleArea = doubleArea * -1;

                    Assert.AreEqual(CellDoubleArea, doubleArea,
                                    $"{entry.Key} 的某一格不是小三角（面積不對）");
                    total = total + doubleArea;
                }

                // 格子的面積總和要等於整片的面積
                Scalar pieceDoubleArea = PiecePrototype.Get(entry.Key).DoubleArea;
                Assert.AreEqual(pieceDoubleArea, total, $"{entry.Key} 的格子加起來不等於整片");
            }
        }

        [Test]
        public void 網格_同一片內的格子互不重疊()
        {
            foreach (PieceKind kind in (PieceKind[])System.Enum.GetValues(typeof(PieceKind)))
            {
                ExactVector2[][] cells = PrototypeCells(kind);
                for (int i = 0; i < cells.Length; i++)
                    for (int j = i + 1; j < cells.Length; j++)
                        Assert.IsFalse(Geometry.ConvexOverlap(cells[i], cells[j]),
                                       $"{kind} 的第 {i} 與第 {j} 格重疊了");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 二、參考解切出來剛好鋪滿盒子
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void 網格_大正方形切出十六格_互不重疊_而且鋪滿盒子()
        {
            Region box = ClassicSquareBox();
            Placement[] solution = Fixtures.ClassicSquare();

            var allCells = new List<ExactVector2[]>();
            foreach (Placement piece in solution) allCells.AddRange(CellsOf(piece));

            Assert.AreEqual(16, allCells.Count, "七片應該剛好切成 16 格");

            // 互不重疊。⚠️ ConvexOverlap 的「距離 0 算分離」正好對——
            // 相鄰的格子共邊，那不該算重疊
            for (int i = 0; i < allCells.Count; i++)
                for (int j = i + 1; j < allCells.Count; j++)
                    Assert.IsFalse(Geometry.ConvexOverlap(allCells[i], allCells[j]),
                                   $"第 {i} 與第 {j} 格重疊了");

            // 每格都在盒內
            for (int i = 0; i < allCells.Count; i++)
                Assert.IsTrue(box.ContainsPolygon(allCells[i]), $"第 {i} 格跑到盒子外面");

            // 💡「聯集 = 盒子」不用布林運算就證得出來：
            //    互不重疊 ＋ 每格都在盒內 ＋ 總面積 = 盒子面積  ⟹  聯集就是盒子
            Scalar totalDoubleArea = new Scalar(0, 0);
            foreach (ExactVector2[] cell in allCells)
            {
                Scalar doubleArea = Geometry.SignedDoubleArea(cell);
                if (doubleArea.Sign < 0) doubleArea = doubleArea * -1;
                totalDoubleArea = totalDoubleArea + doubleArea;
            }
            Assert.AreEqual(new Scalar(64, 0), totalDoubleArea, "16 格的總面積應該是 32（×2 = 64）");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、⚠️ 核心假設：合法擺放一定對齊格線嗎
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 一個擺放有沒有「對齊格線」。
        ///
        /// <b>判準是總帳</b>：跟它有正面積交集的格子數 × 2 必須等於它的面積。
        /// 橫跨格線的話它會碰到比「完整蓋住的格數」更多的格子，兩邊就對不上。
        ///
        /// ⚠️ 不用重心判斷，因為 ℤ[√2] 沒有除法，算不出「三點的平均」。
        /// </summary>
        /// <param name="piece">要檢查的擺放。</param>
        /// <param name="grid">盒子的 16 格。</param>
        /// <returns>對齊就是 true。</returns>
        private static bool IsCellAligned(Placement piece, List<ExactVector2[]> grid)
        {
            ExactVector2[] pieceVertices = piece.Vertices();

            int touchedCells = 0;
            foreach (ExactVector2[] cell in grid)
                if (Geometry.ConvexOverlap(cell, pieceVertices)) touchedCells++;

            Scalar pieceDoubleArea = PiecePrototype.Get(piece.Kind).DoubleArea;
            return pieceDoubleArea == CellDoubleArea * touchedCells;
        }

        [Test]
        public void 網格_參考解的每一片本來就對齊格線()
        {
            Region box = ClassicSquareBox();
            Placement[] solution = Fixtures.ClassicSquare();

            var grid = new List<ExactVector2[]>();
            foreach (Placement piece in solution) grid.AddRange(CellsOf(piece));

            foreach (Placement piece in solution)
                Assert.IsTrue(IsCellAligned(piece, grid),
                              $"{piece.Kind} 沒對齊——那表示 IsCellAligned 本身就錯了");
        }

        /// <summary>
        /// 掃過「所有能吸到盒子輪廓錨點、而且整片落在盒內」的擺放（盤面全空時）。
        ///
        /// ⚠️ 這正是 <c>SnapSolver.Candidates</c> 產生候選的方式：
        /// 把片的某個節點平移到某個錨點上。所以掃到的就是玩家真的放得出來的東西。
        /// </summary>
        /// <param name="box">盒子。</param>
        /// <param name="aligned">其中對齊格線的（已去重）。</param>
        /// <param name="misaligned">其中沒對齊格線的（已去重）。</param>
        private static void SweepLegalPlacements(Region box,
                                                 out HashSet<Placement> aligned,
                                                 out HashSet<Placement> misaligned)
        {
            var grid = new List<ExactVector2[]>();
            foreach (Placement piece in Fixtures.ClassicSquare()) grid.AddRange(CellsOf(piece));

            aligned = new HashSet<Placement>();
            misaligned = new HashSet<Placement>();
            ExactVector2[] anchors = box.BoundaryNodes;

            foreach (PieceKind kind in (PieceKind[])System.Enum.GetValues(typeof(PieceKind)))
            {
                // 翻面只對平行四邊形有意義，其餘片的鏡像等同某個旋轉
                int flipCount = kind == PieceKind.Parallelogram ? 2 : 1;

                // ⚠️ 只掃到片自己的旋轉循環長度為止。掃滿 0~7 的話，
                // 正方形（循環 2）與平行四邊形（循環 4）會有幾何上完全相同的重複，
                // 統計出來的比例就不代表任何東西
                int rotationCycle = kind == PieceKind.Square ? 2
                                  : kind == PieceKind.Parallelogram ? 4
                                  : 8;

                for (int flipIndex = 0; flipIndex < flipCount; flipIndex++)
                {
                    bool flipped = flipIndex == 1;

                    for (int rotation = 0; rotation < rotationCycle; rotation++)
                    {
                        var basePlacement = new Placement(kind, rotation, flipped, ExactVector2.Zero);

                        for (int nodeIndex = 0; nodeIndex < basePlacement.NodeCount; nodeIndex++)
                        {
                            ExactVector2 nodeAtOrigin = basePlacement.Node(nodeIndex);

                            foreach (ExactVector2 anchor in anchors)
                            {
                                Placement candidate =
                                    basePlacement.Translated(anchor - nodeAtOrigin);

                                if (!box.Contains(candidate)) continue;   // 出盒的不算合法

                                // 去重：同一個擺放會被不同的（節點, 錨點）組合命中好幾次
                                if (IsCellAligned(candidate, grid)) aligned.Add(candidate);
                                else misaligned.Add(candidate);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ⚠️ <b>整個網格化方案的核心假設是假的：合法擺放不保證對齊格線。</b>
        ///
        /// <b>原因</b>：盒子的格線建在 <b>√2 晶格</b>上（<c>Fixtures.ClassicSquare</c> 的座標
        /// 全部乘過 √2），所以參考解的大三角是 <c>(0,0),(4√2,0),(2√2,2√2)</c>——
        /// <b>相對於原型旋轉了 45°</b>。
        ///
        /// 於是<b>原型朝向</b>（rotation 0）的大三角放在 <c>(0,0)</c> 整片都在盒內
        /// （盒子是 <c>[0,4√2]²</c>，邊長約 5.66），但它跟格線差 45°，<b>必然橫切格子</b>。
        /// 而且它吸得到——盒子的角就是錨點。
        ///
        /// <b>後果</b>：第二層不能無條件相信格子的計數，必須加守衛（見下一條）。
        ///
        /// 💡 這條斷言的是「**確實存在**沒對齊的合法擺放」。哪天它紅了，
        /// 表示有人改了盒子或格線的定義——那時候要回來重新評估這整套。
        /// </summary>
        [Test]
        public void 網格_合法擺放不保證對齊格線()
        {
            Region box = ClassicSquareBox();
            SweepLegalPlacements(box, out HashSet<Placement> aligned,
                                 out HashSet<Placement> misaligned);

            Assert.Greater(aligned.Count, 0, "一個對齊的都沒有的話，是判準寫錯了");
            Assert.Greater(misaligned.Count, 0,
                           "假設居然成立了？那要回來把守衛拿掉，並重新檢查這條測試");

            // 具體那個反例：原型朝向的大三角放在盒子的角落
            var counterExample = new Placement(PieceKind.LargeTriangle, 0, false, ExactVector2.Zero);
            Assert.IsTrue(box.Contains(counterExample), "它必須是合法的，不然不算反例");
            Assert.IsTrue(misaligned.Contains(counterExample), "而且它必須是沒對齊的");
        }

        /// <summary>
        /// 守衛：<b>總帳對不上就這一次不給回饋</b>。
        ///
        /// 照 §8 對第三層定的原則——「超時 → 不給判斷（保持中性）。
        /// <b>寧可沉默也不要閃錯顏色</b>」。誤判的代價不對稱：
        /// 「該紅沒紅」只是少了幫助，「不該紅卻紅」是主動誤導玩家。
        ///
        /// 💡 而且這個守衛<b>就是 §8 的第一層</b>（剩餘面積的不變式檢查）。
        /// 原本它只是拿來抓程式 bug 的，現在它同時是「第二層能不能相信自己」的判準——
        /// 一個檢查兩個用途，而且本來就要做。
        /// </summary>
        [Test]
        public void 網格_總帳守衛分得出對齊與沒對齊()
        {
            Region box = ClassicSquareBox();
            SweepLegalPlacements(box, out HashSet<Placement> aligned,
                                 out HashSet<Placement> misaligned);

            var grid = new List<ExactVector2[]>();
            foreach (Placement piece in Fixtures.ClassicSquare()) grid.AddRange(CellsOf(piece));

            foreach (Placement piece in aligned) Assert.IsTrue(IsCellAligned(piece, grid));
            foreach (Placement piece in misaligned) Assert.IsFalse(IsCellAligned(piece, grid));

            // ⚠️ 這個數字決定第二層有多常「沉默」。太低的話網格化就不划算了
            int total = aligned.Count + misaligned.Count;
            TestContext.WriteLine(
                $"盤面全空時的合法擺放：共 {total} 個，對齊 {aligned.Count}、" +
                $"沒對齊 {misaligned.Count}（對齊率 {(double)aligned.Count / total:P1}）");
        }
    }
}
