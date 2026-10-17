using System;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// <see cref="PhotoGeometry"/>（2026-10-17，掃描模式取得照片）。
    ///
    /// <b>這一支在看什麼</b>：轉錯方向不會報錯，只會讓照片躺著、題目跟著歪——所以每個角度都逐格對答案。
    ///
    /// 所有的轉向都拿同一張 3 × 2 的小圖，像素值就是它原本的索引。<b>看起來</b>是這樣（上面那排是 y = 1）：
    /// <code>
    /// 3 4 5
    /// 0 1 2
    /// </code>
    /// 存起來是由下往上一排一排：0 1 2 3 4 5（跟 Unity 貼圖一樣，第 0 列在最下面）。
    /// 每個測試的註解先畫「轉完看起來的樣子」，再寫由下往上存的答案，兩邊可以對著看。
    /// </summary>
    public class PhotoGeometryTests
    {
        private static readonly int[] Sample = { 0, 1, 2, 3, 4, 5 };

        private static void AssertReoriented(int degrees, bool mirror, int expectedWidth, int expectedHeight, int[] expected)
        {
            (int[] pixels, int width, int height) = PhotoGeometry.Reorient(Sample, 3, 2, degrees, mirror);

            Assert.AreEqual(expectedWidth, width, "寬");
            Assert.AreEqual(expectedHeight, height, "高");
            CollectionAssert.AreEqual(expected, pixels);
        }

        [Test]
        public void 轉向_零度不鏡射_原樣但是新的陣列()
        {
            (int[] pixels, _, _) = PhotoGeometry.Reorient(Sample, 3, 2, 0, false);

            CollectionAssert.AreEqual(Sample, pixels);
            Assert.AreNotSame(Sample, pixels, "回傳的要是新陣列，呼叫端改它不該動到原本的");
        }

        [Test]
        public void 轉向_順時針90度()
        {
            // 0 3
            // 1 4
            // 2 5
            AssertReoriented(90, false, 2, 3, new[] { 2, 5, 1, 4, 0, 3 });
        }

        [Test]
        public void 轉向_180度()
        {
            // 2 1 0
            // 5 4 3
            AssertReoriented(180, false, 3, 2, new[] { 5, 4, 3, 2, 1, 0 });
        }

        [Test]
        public void 轉向_順時針270度()
        {
            // 5 2
            // 4 1
            // 3 0
            AssertReoriented(270, false, 2, 3, new[] { 3, 0, 4, 1, 5, 2 });
        }

        [Test]
        public void 轉向_負的與超過一圈的角度_等於繞回來的那個()
        {
            AssertReoriented(-90, false, 2, 3, new[] { 3, 0, 4, 1, 5, 2 });
            AssertReoriented(450, false, 2, 3, new[] { 2, 5, 1, 4, 0, 3 });
            AssertReoriented(360, false, 3, 2, Sample);
        }

        [Test]
        public void 轉向_只鏡射_上下對調()
        {
            // 0 1 2
            // 3 4 5
            AssertReoriented(0, true, 3, 2, new[] { 3, 4, 5, 0, 1, 2 });
        }

        [Test]
        public void 轉向_先鏡射再轉_順序不能反()
        {
            // 鏡射完是 0 1 2 / 3 4 5，再順時針 90 度：
            // 3 0
            // 4 1
            // 5 2
            AssertReoriented(90, true, 2, 3, new[] { 5, 2, 4, 1, 3, 0 });

            // 反過來（先轉再鏡射）會得到 2 5 / 1 4 / 0 3 由下往上 = 0 3 1 4 2 5，跟上面不一樣——
            // 所以這一條真的有在釘順序
            (int[] rotatedFirst, _, _) = PhotoGeometry.Reorient(Sample, 3, 2, 90, false);
            (int[] thenMirrored, _, _) = PhotoGeometry.Reorient(rotatedFirst, 2, 3, 0, true);
            CollectionAssert.AreNotEqual(new[] { 5, 2, 4, 1, 3, 0 }, thenMirrored);
        }

        [Test]
        public void 轉向_壞掉的輸入會丟例外()
        {
            Assert.Throws<ArgumentNullException>(() => PhotoGeometry.Reorient<int>(null, 3, 2, 0, false));
            Assert.Throws<ArgumentException>(() => PhotoGeometry.Reorient(Sample, 2, 2, 0, false), "數量對不上");
            Assert.Throws<ArgumentException>(() => PhotoGeometry.Reorient(Sample, 3, 2, 45, false), "不是 90 的倍數");
        }

        [Test]
        public void 縮圖_長邊縮到上限_比例不變()
        {
            Assert.AreEqual((1024, 768), PhotoGeometry.FitWithin(4000, 3000, 1024), "橫的");
            Assert.AreEqual((768, 1024), PhotoGeometry.FitWithin(3000, 4000, 1024), "直的");
            Assert.AreEqual((1024, 576), PhotoGeometry.FitWithin(1280, 720, 1024), "相機的 16:9");
        }

        [Test]
        public void 縮圖_本來就夠小的不放大()
        {
            Assert.AreEqual((800, 600), PhotoGeometry.FitWithin(800, 600, 1024));
            Assert.AreEqual((1024, 1024), PhotoGeometry.FitWithin(1024, 1024, 1024), "剛好等於上限");
        }

        [Test]
        public void 縮圖_極細長的圖短邊至少一個像素()
        {
            Assert.AreEqual((1024, 1), PhotoGeometry.FitWithin(5000, 1, 1024));
        }

        [Test]
        public void 縮圖_不是正數會丟例外()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGeometry.FitWithin(0, 10, 1024));
            Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGeometry.FitWithin(10, -1, 1024));
            Assert.Throws<ArgumentOutOfRangeException>(() => PhotoGeometry.FitWithin(10, 10, 0));
        }
    }
}
