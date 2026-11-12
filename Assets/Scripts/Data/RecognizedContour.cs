using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// ⭐ <b>辨識模組交回來的一條輪廓</b>，讀進來、檢查過、換成模型座標（2026-10-14，掃描模式）。
    ///
    /// <b>格式照辨識模組實際輸出的那份</b>（<c>Resources/ScanSamples/u2net_unity_contour.json</c>），
    /// 不是原本約定的 <c>rings</c> 格式——讀檔這一側照實際的輸出來讀，
    /// 比改辨識模組那一側快。差別：欄位叫 <c>points</c>／<c>width</c>／<c>height</c>、<b>只有外框沒有洞</b>、沒有名字。
    ///
    /// ⚠️ <b>座標是 y 朝上</b>（Unity 的習慣，原點在圖的左下角）——<b>不是</b>原本約定的 y 朝下（2026-10-17 更正）。
    /// 原本照 y 朝下讀、又翻了一次，結果整隻貓上下顛倒；盒子與照片從同一份 JSON 算，所以兩個一起倒、彼此還對得上，
    /// 直到照片對照疊在畫面上才看出來。照 y 朝上換算，點的範圍正好對上附圖那隻貓——
    /// 之前懷疑的「樣本跟附圖對不上」也是同一個原因。檔名 <c>u2net_unity_contour</c> 本身就是線索。
    ///
    /// <b>為什麼放 <c>Data</c></b>：一行畫面程式都沒有，放這裡才進得了 <c>dotnet test</c>。
    /// 拿到 JSON 字串之前的事（相機、辨識模組）在 <c>GameObject/ContourScanning.cs</c>。
    ///
    /// ⚠️ <b>這裡全是浮點，而且只能停在這裡</b>（鐵則一）。輪廓只拿來給逼近搜尋<b>打分</b>，
    /// 不會直接變成題目——真正進 <c>BoardState</c> 的永遠是逼近挑出來的精確 <c>Placement</c>。
    ///
    /// ⚠️ <b>沒有檢查自我交叉</b>。交叉的輪廓面積會算錯（兩半互相抵銷），但不會讓程式出事。
    /// 真的逼近搜尋寫好之後，如果樣本裡出現這種情況再加。
    /// </summary>
    public sealed class RecognizedContour
    {
        /// <summary>
        /// 檔案的格式，<b>必須跟辨識模組的輸出一致</b>。
        ///
        /// 💡 檔案裡每個點還有 <c>normalizedX</c>／<c>normalizedY</c>，那只是 <c>x / width</c>，資訊重複，
        /// 這裡刻意不讀——之後反正要縮放到面積 32，用哪一組都一樣。<c>JsonUtility</c> 遇到不認得的欄位不吭聲，所以留著也無害。
        /// </summary>
        [Serializable]
        public class ContourDto
        {
            /// <summary>
            /// 圖片寬度（像素）。<b>等於交給辨識模組的那張照片的寬度</b>（<c>IContourRecognizer</c> 的約定）。
            /// 💡 最早的樣本是 320（模型的輸入尺寸），2026-10-20 接上真的模組之後已經換算回照片尺寸。
            /// </summary>
            public int width;

            /// <summary>辨識時的圖片高度（像素）。</summary>
            public int height;

            /// <summary>點的數量。拿來檢查檔案有沒有被截斷。</summary>
            public int pointCount;

            /// <summary>外框的點，<b>照片的像素座標、y 朝上</b>（原點在左下）。</summary>
            public PointDto[] points;
        }

        /// <summary>一個點。</summary>
        [Serializable]
        public class PointDto
        {
            /// <summary>像素座標，往右是正。</summary>
            public double x;

            /// <summary>像素座標，<b>往上是正</b>（0 是圖的最下面）。</summary>
            public double y;
        }

        /// <summary>
        /// 換到模型座標之後的面積：<b>七片的面積和</b>。
        /// 輪廓一定要縮到這麼大才能拿來比——大小這個維度在七巧板裡是固定的，不歸照片決定。
        /// </summary>
        public const double ModelArea = 32.0;

        /// <summary>
        /// 面積小於這個（像素²）就當成沒有面積。
        /// 共線的點、或是辨識模組只交回一條線時會走到這裡；不擋的話縮放倍率會是無限大。
        /// </summary>
        private const double MinimumPixelArea = 1.0;

        /// <summary>辨識時的圖片寬度（像素）。</summary>
        public readonly int ImageWidth;

        /// <summary>辨識時的圖片高度（像素）。</summary>
        public readonly int ImageHeight;

        /// <summary>
        /// 原始的點，<b>照片的像素座標、y 朝上</b>，順序照檔案（去掉重複點之後）。
        /// 之後要把題目疊回照片上畫時用得到。
        /// </summary>
        public readonly IReadOnlyList<Vector2> PixelPoints;

        /// <summary>
        /// ⭐ <b>逼近搜尋要的那一份</b>：模型座標的外框。
        /// <list type="bullet">
        /// <item><b>y 朝上</b>，跟照片的像素座標同方向，<b>不翻</b></item>
        /// <item><b>面積剛好 <see cref="ModelArea"/></b>，等比縮放，不變形</item>
        /// <item><b>重心在原點</b></item>
        /// <item><b>逆時針</b>，跟專案裡其他多邊形一樣</item>
        /// <item>⚠️ <b>不轉方向</b>：照片裡是正的貓，這裡也是正的（逼近出來的題目也不 roll 相機）</item>
        /// </list>
        /// </summary>
        public readonly IReadOnlyList<Vector2> ModelOutline;

        // ───────── 像素 → 輪廓座標的換算（2026-10-14，照片對照要用）─────────
        //
        // ⭐ 算輪廓與換照片用的是同一組數字：存起來，不重算。分兩份算的話哪天改了一邊，
        // 照片就會跟輪廓錯開，而且不報錯。

        /// <summary>輪廓的面積重心 x，像素單位。</summary>
        private readonly double centroidX;

        /// <summary>輪廓的面積重心 y，像素單位（y 朝上）。</summary>
        private readonly double centroidY;

        /// <summary>一個像素等於幾個模型單位：√(32 ÷ 輪廓的像素面積)。</summary>
        public readonly double ModelUnitsPerPixel;

        private RecognizedContour(int imageWidth, int imageHeight, List<Vector2> pixelPoints)
        {
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            PixelPoints = pixelPoints.AsReadOnly();

            (double signedArea, double x, double y) = MeasureArea(pixelPoints);
            centroidX = x;
            centroidY = y;
            // 面積跟長度的平方成正比，所以長度乘上 √(目標 ÷ 現在)
            ModelUnitsPerPixel = Math.Sqrt(ModelArea / Math.Abs(signedArea));

            var outline = new List<Vector2>(pixelPoints.Count);
            foreach (Vector2 point in pixelPoints) outline.Add(PixelToModel(point.x, point.y));

            // 順時針就反過來，讓輪廓一律逆時針。只有順序變，形狀與位置不變
            if (signedArea < 0.0) outline.Reverse();
            ModelOutline = outline.AsReadOnly();
        }

        /// <summary>
        /// 照片上的一個點（像素座標、y 朝上）換成輪廓座標（模型單位、y 朝上、重心在原點）：移重心、等比縮放，<b>不翻</b>。
        /// <see cref="ModelOutline"/> 就是把每個 <see cref="PixelPoints"/> 丟進這一支算出來的。
        /// </summary>
        /// <param name="pixelX">像素 x。</param>
        /// <param name="pixelY">像素 y（往上是正）。</param>
        /// <returns>輪廓座標。</returns>
        public Vector2 PixelToModel(double pixelX, double pixelY) =>
            new Vector2((float)((pixelX - centroidX) * ModelUnitsPerPixel),
                        (float)((pixelY - centroidY) * ModelUnitsPerPixel));

        /// <summary>
        /// 整張圖（0 ～ 寬、0 ～ 高）在輪廓座標裡佔的範圍。照片是長方形、換算只有縮放平移，
        /// 所以結果還是一個<b>正的長方形</b>：像素 (0, 0) 是圖的左下角，(寬, 高) 是右上角。
        /// </summary>
        /// <returns>左下角與右上角。</returns>
        public (Vector2 minimum, Vector2 maximum) ImageBoundsInModel() =>
            (PixelToModel(0.0, 0.0), PixelToModel(ImageWidth, ImageHeight));

        /// <summary>
        /// 照片大小的剪影：每個像素（中心）在不在輪廓裡。照片對照的<b>替身</b>用它畫（2026-10-14）。
        ///
        /// 索引是 <c>row × 寬 + column</c>，<b>第 0 列是照片最上面那一列</b>（照一般看圖的順序）。
        /// ⚠️ 像素座標是 y 朝上，所以第 row 列的中心是 y ＝ 高 − (row ＋ 0.5)。
        /// ⚠️ Unity 貼圖的第 0 列是最下面，轉成貼圖時要上下對調。
        /// </summary>
        /// <returns>長度 寬 × 高 的陣列，true ＝ 在輪廓裡。</returns>
        public bool[] SilhouetteMask()
        {
            var mask = new bool[ImageWidth * ImageHeight];
            for (int row = 0; row < ImageHeight; row++)
                for (int column = 0; column < ImageWidth; column++)
                    mask[row * ImageWidth + column] = ContainsPixel(column + 0.5, ImageHeight - (row + 0.5));
            return mask;
        }

        /// <summary>點在不在輪廓裡（像素座標，射線法、奇偶規則）。</summary>
        private bool ContainsPixel(double x, double y)
        {
            bool inside = false;
            for (int i = 0, previous = PixelPoints.Count - 1; i < PixelPoints.Count; previous = i++)
            {
                double xi = PixelPoints[i].x, yi = PixelPoints[i].y;
                double xj = PixelPoints[previous].x, yj = PixelPoints[previous].y;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// 讀辨識模組交回來的 JSON。
        /// </summary>
        /// <param name="json">整份 JSON 的文字。</param>
        /// <returns>檢查過、已經換好模型座標的輪廓。</returns>
        /// <exception cref="ArgumentException">
        /// 讀不進來，訊息會講原因：不是 JSON、沒有 <c>points</c>、圖片尺寸不是正數、
        /// <c>pointCount</c> 跟實際點數對不上、點不到三個、面積是零。
        /// </exception>
        public static RecognizedContour FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("輪廓 JSON 是空的。");

            ContourDto dto;
            try
            {
                dto = JsonUtility.FromJson<ContourDto>(json);
            }
            catch (Exception error)
            {
                // Unity 與 dotnet 測試替身丟的例外型別不一樣，統一包成 ArgumentException，呼叫端只要接一種
                throw new ArgumentException($"輪廓 JSON 讀不進來：{error.Message}", error);
            }

            // ⚠️ JsonUtility 對不認得的欄位不吭聲，整份是別的東西時 dto 會是「每個欄位都是預設值」。
            // ⚠️ 而 Unity 的「預設值」對陣列是<b>空陣列不是 null</b>（dotnet 的測試替身才是 null），兩種都要擋
            if (dto == null || dto.points == null || dto.points.Length == 0)
                throw new ArgumentException("輪廓 JSON 裡沒有 points 欄位，可能不是辨識模組的格式。");
            if (dto.width <= 0 || dto.height <= 0)
                throw new ArgumentException($"圖片尺寸不合法：{dto.width} × {dto.height}。");

            // 檔案自己報的點數對不上，最可能是傳輸或寫檔時被截斷——那條輪廓少了一截，不能拿來用
            if (dto.pointCount != dto.points.Length)
                throw new ArgumentException($"pointCount 寫 {dto.pointCount}，實際有 {dto.points.Length} 個點，檔案可能不完整。");

            List<Vector2> pixelPoints = RemoveRepeatedPoints(dto.points);
            if (pixelPoints.Count < 3)
                throw new ArgumentException($"輪廓只有 {pixelPoints.Count} 個不重複的點，至少要 3 個。");

            // 面積是零就擋掉，不然縮放倍率是無限大。放在建構之前，建構子裡才能放心地除
            double signedArea = MeasureArea(pixelPoints).signedArea;
            if (Math.Abs(signedArea) < MinimumPixelArea)
                throw new ArgumentException($"輪廓的面積是 {Math.Abs(signedArea):0.###} 像素²，點可能全部共線。");

            return new RecognizedContour(dto.width, dto.height, pixelPoints);
        }

        /// <summary>
        /// 拿掉<b>連續重複</b>的點，包括「最後一點重複寫了第一點」那種收尾。
        ///
        /// 💡 格式約定是不要重複寫第一點，但這裡還是容忍——那是很常見的寫法（OpenCV 有些函式就會這樣），
        /// 不擋的話會多一條長度 0 的邊，之後算邊的方向時除以 0。
        /// </summary>
        /// <param name="points">檔案裡的點。</param>
        /// <returns>沒有連續重複的點。</returns>
        private static List<Vector2> RemoveRepeatedPoints(PointDto[] points)
        {
            var result = new List<Vector2>(points.Length);
            foreach (PointDto point in points)
            {
                if (point == null) continue;
                var position = new Vector2((float)point.x, (float)point.y);
                if (result.Count > 0 && SamePosition(result[result.Count - 1], position)) continue;
                result.Add(position);
            }

            while (result.Count > 1 && SamePosition(result[0], result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return result;
        }

        /// <summary>
        /// 像素座標裡兩點算不算同一點。<b>只有這裡用容差</b>：這些是浮點的像素值，跟模型的精確座標無關。
        /// </summary>
        private static bool SamePosition(Vector2 left, Vector2 right) =>
            Math.Abs(left.x - right.x) < 1e-6 && Math.Abs(left.y - right.y) < 1e-6;

        /// <summary>
        /// 有向面積與面積重心，鞋帶公式一起算。像素座標是 y 朝上，所以逆時針的面積是正的。
        ///
        /// 中間一律用 <c>double</c> 算——像素值到幾百，平方之後用 <c>float</c> 算面積會丟掉好幾位。
        /// </summary>
        /// <param name="pixelPoints">去掉重複點之後的像素座標，至少 3 個。</param>
        /// <returns>有向面積（像素²），與重心（像素座標）。面積是零時重心沒有意義。</returns>
        private static (double signedArea, double centroidX, double centroidY) MeasureArea(List<Vector2> pixelPoints)
        {
            int count = pixelPoints.Count;
            double doubledArea = 0.0, sumX = 0.0, sumY = 0.0;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                double x0 = pixelPoints[i].x, y0 = pixelPoints[i].y;
                double x1 = pixelPoints[next].x, y1 = pixelPoints[next].y;
                double cross = x0 * y1 - x1 * y0;
                doubledArea += cross;
                sumX += (x0 + x1) * cross;
                sumY += (y0 + y1) * cross;
            }

            if (doubledArea == 0.0) return (0.0, 0.0, 0.0);
            return (doubledArea / 2.0, sumX / (3.0 * doubledArea), sumY / (3.0 * doubledArea));
        }
    }
}
