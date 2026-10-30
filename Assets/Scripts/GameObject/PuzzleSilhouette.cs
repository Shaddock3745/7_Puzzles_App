using System;
using System.Collections.Generic;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// ⭐ <b>把一個題目的外框畫成剪影貼圖</b>（2026-10-21，選題畫面用）。
///
/// 做法跟 <see cref="ContourStandInPhoto"/> 同一招：每個像素的中心用射線法數穿過幾條邊，奇數在裡面。
/// <b>外環與洞環一起數</b>，所以洞自然是空的，不用分辨哪個環是洞。
///
/// ⭐ <b>2026-10-30 改成畫在呼叫端指定的範圍裡</b>（第二輪回饋：選題時把照片疊在剪影底下）。
/// 原本是把題目自己的範圍塞滿一個正方形——那樣剪影跟照片的位置、大小毫無關係，只能並排比。
/// 現在呼叫端交「整張照片在題目座標裡佔哪裡」，貼圖的四個角就正好對到照片的四個角，
/// 兩張 RawImage 一樣大疊起來就對齊，<b>版面那邊不用算任何位置</b>。
///
/// 貼圖是執行時建的，<b>由呼叫端釋放</b>（不釋放會一直佔記憶體）。
/// </summary>
public static class PuzzleSilhouette
{
    /// <summary>背景（透明）。</summary>
    private static readonly Color32 Background = new Color32(0, 0, 0, 0);

    /// <summary>剪影。白的，要什麼顏色與透明度由顯示它的 RawImage 的顏色乘上去。</summary>
    private static readonly Color32 Silhouette = new Color32(255, 255, 255, 255);

    /// <param name="puzzle">要畫的題目（所有環）。</param>
    /// <param name="width">貼圖寬（像素），至少 8。</param>
    /// <param name="height">貼圖高（像素），至少 8。</param>
    /// <param name="minimum">貼圖左下角對到的題目座標。</param>
    /// <param name="maximum">貼圖右上角對到的題目座標。</param>
    /// <returns>RGBA32、沒有 mipmap 的貼圖；剪影是白的、背景透明。範圍外的部分畫不到（裁掉）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="puzzle"/> 是 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">寬或高太小，或範圍是空的。</exception>
    public static Texture2D Draw(Puzzle puzzle, int width, int height, Vector2 minimum, Vector2 maximum)
    {
        if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
        if (width < 8) throw new ArgumentOutOfRangeException(nameof(width), width, "貼圖至少要 8 像素。");
        if (height < 8) throw new ArgumentOutOfRangeException(nameof(height), height, "貼圖至少要 8 像素。");
        if (!(maximum.x > minimum.x) || !(maximum.y > minimum.y))
            throw new ArgumentOutOfRangeException(nameof(maximum), $"範圍是空的：{minimum} ～ {maximum}。");

        // 所有環的邊，浮點（只拿來畫）
        var edges = new List<(double startX, double startY, double endX, double endY)>();
        foreach (Walk walk in puzzle.Walks)
        {
            ExactVector2[] vertices = walk.Vertices();
            for (int i = 0; i < vertices.Length; i++)
            {
                ExactVector2 start = vertices[i], end = vertices[(i + 1) % vertices.Length];
                edges.Add((start.X.ToDouble(), start.Y.ToDouble(), end.X.ToDouble(), end.Y.ToDouble()));
            }
        }

        // 每個像素的寬高（題目單位）。照片的換算只有等比縮放，所以兩個方向本來就一樣；分開算是不替呼叫端假設
        double pixelWidth = ((double)maximum.x - minimum.x) / width;
        double pixelHeight = ((double)maximum.y - minimum.y) / height;

        var pixels = new Color32[width * height];
        for (int row = 0; row < height; row++)
        {
            // Unity 貼圖的第 0 列是最下面，題目的 y 也朝上：不用對調
            double y = minimum.y + (row + 0.5) * pixelHeight;
            for (int column = 0; column < width; column++)
            {
                double x = minimum.x + (column + 0.5) * pixelWidth;
                pixels[row * width + column] = Contains(edges, x, y) ? Silhouette : Background;
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "題目剪影",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>射線法：往 +x 射出去穿過奇數條邊就在裡面。外環與洞環一起數，洞裡的點穿過偶數條。</summary>
    private static bool Contains(List<(double startX, double startY, double endX, double endY)> edges, double x, double y)
    {
        bool inside = false;
        foreach ((double startX, double startY, double endX, double endY) in edges)
        {
            if ((startY > y) == (endY > y)) continue;
            double crossingX = startX + (y - startY) * (endX - startX) / (endY - startY);
            if (x < crossingX) inside = !inside;
        }
        return inside;
    }
}
