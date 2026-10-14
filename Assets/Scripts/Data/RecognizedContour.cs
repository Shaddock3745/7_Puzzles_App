using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// ⭐ <b>辨識模組交回來的一條輪廓</b>，讀進來、檢查過、換成模型座標（2026-10-14，掃描模式）。
    ///
    /// <b>格式照協作者實際交的那份</b>（<c>Resources/ScanSamples/u2net_unity_contour.json</c>），
    /// 不是 <c>協作交接_影片與輪廓.md</c> 第三節原本講好的 <c>rings</c> 格式——讀檔的程式是我們寫，
    /// 照他交的讀比叫他改快。差別：欄位叫 <c>points</c>／<c>width</c>／<c>height</c>、<b>只有外框沒有洞</b>、沒有名字。
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
        /// 檔案的格式，<b>必須跟協作者的模組輸出一致</b>。
        ///
        /// 💡 檔案裡每個點還有 <c>normalizedX</c>／<c>normalizedY</c>，那只是 <c>x / width</c>，資訊重複，
        /// 這裡刻意不讀——之後反正要縮放到面積 32，用哪一組都一樣。<c>JsonUtility</c> 遇到不認得的欄位不吭聲，所以留著也無害。
        /// </summary>
        [Serializable]
        public class ContourDto
        {
            /// <summary>辨識時的圖片寬度（像素）。⚠️ 這是模型的輸入尺寸（U²-Net 是 320），不一定是原照片的寬度。</summary>
            public int width;

            /// <summary>辨識時的圖片高度（像素）。</summary>
            public int height;

            /// <summary>點的數量。拿來檢查檔案有沒有被截斷。</summary>
            public int pointCount;

            /// <summary>外框的點，<b>照片的像素座標、y 朝下</b>。</summary>
            public PointDto[] points;
        }

        /// <summary>一個點。</summary>
        [Serializable]
        public class PointDto
        {
            /// <summary>像素座標，往右是正。</summary>
            public double x;

            /// <summary>像素座標，<b>往下是正</b>。</summary>
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
        /// 原始的點，<b>照片的像素座標、y 朝下</b>，順序照檔案（去掉重複點之後）。
        /// 之後要把題目疊回照片上畫時用得到。
        /// </summary>
        public readonly IReadOnlyList<Vector2> PixelPoints;

        /// <summary>
        /// ⭐ <b>逼近搜尋要的那一份</b>：模型座標的外框。
        /// <list type="bullet">
        /// <item><b>y 朝上</b>（照片的 y 朝下，翻過來）</item>
        /// <item><b>面積剛好 <see cref="ModelArea"/></b>，等比縮放，不變形</item>
        /// <item><b>重心在原點</b></item>
        /// <item><b>逆時針</b>，跟專案裡其他多邊形一樣</item>
        /// <item>⚠️ <b>不轉方向</b>：照片裡是正的貓，這裡也是正的（逼近出來的題目也不 roll 相機）</item>
        /// </list>
        /// </summary>
        public readonly IReadOnlyList<Vector2> ModelOutline;

        private RecognizedContour(int imageWidth, int imageHeight, List<Vector2> pixelPoints, List<Vector2> modelOutline)
        {
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            PixelPoints = pixelPoints.AsReadOnly();
            ModelOutline = modelOutline.AsReadOnly();
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

            return new RecognizedContour(dto.width, dto.height, pixelPoints, ToModelOutline(pixelPoints));
        }

        /// <summary>
        /// 拿掉<b>連續重複</b>的點，包括「最後一點重複寫了第一點」那種收尾。
        ///
        /// 💡 交接文件有講不要重複寫第一點，但這裡還是容忍——那是很常見的寫法（OpenCV 有些函式就會這樣），
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
        /// 像素座標 → 模型座標：翻 y、移重心、等比縮放到面積 32、轉成逆時針。
        ///
        /// 中間一律用 <c>double</c> 算，最後才轉 <c>float</c>——像素值到幾百，平方之後
        /// 用 <c>float</c> 算面積會丟掉好幾位。
        /// </summary>
        /// <param name="pixelPoints">去掉重複點之後的像素座標，至少 3 個。</param>
        /// <returns>模型座標的外框。</returns>
        /// <exception cref="ArgumentException">面積是零（點全部共線）。</exception>
        private static List<Vector2> ToModelOutline(List<Vector2> pixelPoints)
        {
            int count = pixelPoints.Count;

            // 先翻 y：照片 y 朝下、模型 y 朝上。平移量無所謂（等一下會移重心），所以直接取負號
            var flippedX = new double[count];
            var flippedY = new double[count];
            for (int i = 0; i < count; i++)
            {
                flippedX[i] = pixelPoints[i].x;
                flippedY[i] = -(double)pixelPoints[i].y;
            }

            // 鞋帶公式：有向面積與重心一起算。翻過 y 之後，逆時針的面積是正的
            double doubledArea = 0.0, centroidX = 0.0, centroidY = 0.0;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                double cross = flippedX[i] * flippedY[next] - flippedX[next] * flippedY[i];
                doubledArea += cross;
                centroidX += (flippedX[i] + flippedX[next]) * cross;
                centroidY += (flippedY[i] + flippedY[next]) * cross;
            }

            double area = doubledArea / 2.0;
            if (Math.Abs(area) < MinimumPixelArea)
                throw new ArgumentException($"輪廓的面積是 {Math.Abs(area):0.###} 像素²，點可能全部共線。");

            centroidX /= 3.0 * doubledArea;
            centroidY /= 3.0 * doubledArea;

            // 面積跟長度的平方成正比，所以長度乘上 √(目標 ÷ 現在)
            double scale = Math.Sqrt(ModelArea / Math.Abs(area));

            var outline = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
                outline.Add(new Vector2((float)((flippedX[i] - centroidX) * scale),
                                        (float)((flippedY[i] - centroidY) * scale)));

            // 順時針就反過來。只有順序變，形狀與位置不變
            if (area < 0.0) outline.Reverse();
            return outline;
        }
    }
}
