using System;
using UnityEngine;

/// <summary>320×320 補邊尺寸與遮罩回到照片的座標。照片不旋轉、不鏡射。</summary>
public readonly struct U2NetInputGeometry
{
    public const int Size = 320;
    public readonly int ContentWidth, ContentHeight, Left, Top;

    public U2NetInputGeometry(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        float scale = Size / (float)Math.Max(width, height);
        ContentWidth = Math.Max(1, Mathf.RoundToInt(width * scale));
        ContentHeight = Math.Max(1, Mathf.RoundToInt(height * scale));
        Left = (Size - ContentWidth) / 2;
        Top = (Size - ContentHeight) / 2;
    }

    /// <summary>NCHW 以左上角為原點；Unity 採左下角，這裡只翻一次 y。</summary>
    public float[] CreateInput(Texture2D photo)
    {
        int count = Size * Size;
        var data = new float[count * 3];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Color color = Color.black;
            if (x >= Left && x < Left + ContentWidth && y >= Top && y < Top + ContentHeight)
                color = photo.GetPixelBilinear((x - Left + 0.5f) / ContentWidth,
                    1f - (y - Top + 0.5f) / ContentHeight);
            int index = y * Size + x;
            // 沿用原模組 ImageNet 正規化；補邊也經過相同正規化。
            data[index] = (color.r - 0.485f) / 0.229f;
            data[count + index] = (color.g - 0.456f) / 0.224f;
            data[2 * count + index] = (color.b - 0.406f) / 0.225f;
        }
        return data;
    }

    /// <summary>去除補邊、翻回 y 朝上。後續只需按內容寬高縮放，不能把補邊也算進照片。</summary>
    public Texture2D CreateMask(float[] probabilities)
    {
        if (probabilities == null || probabilities.Length < Size * Size)
            throw new InvalidOperationException("U2Net 輸出資料不足。");
        float min = float.MaxValue, max = float.MinValue;
        for (int y = 0; y < ContentHeight; y++)
        for (int x = 0; x < ContentWidth; x++)
        {
            float value = probabilities[(Top + y) * Size + Left + x];
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException("U2Net 輸出包含無效數值。");
            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }
        if (max - min < 0.000001f)
            throw new InvalidOperationException("模型沒有找到可區分的前景。");
        var pixels = new Color32[ContentWidth * ContentHeight];
        for (int y = 0; y < ContentHeight; y++)
        for (int x = 0; x < ContentWidth; x++)
        {
            float value = probabilities[(Top + ContentHeight - 1 - y) * Size + Left + x];
            byte foreground = (value - min) / (max - min) >= 0.5f ? (byte)255 : (byte)0;
            pixels[y * ContentWidth + x] = new Color32(foreground, foreground, foreground, 255);
        }
        var mask = new Texture2D(ContentWidth, ContentHeight, TextureFormat.RGBA32, false);
        mask.SetPixels32(pixels);
        mask.Apply();
        return mask;
    }
}
