using System;
using System.Collections.Generic;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// ⭐ <b>把一個題目的外框畫成正方形的剪影貼圖</b>（2026-10-21，選題畫面的縮圖用）。
///
/// 做法跟 <see cref="ContourStandInPhoto"/> 同一招：題目的範圍等比例塞進正方形（四周留一點邊），每個像素的中心用射線法數穿過幾條邊，
/// 奇數在裡面。<b>外環與洞環一起數</b>，所以洞自然是空的，不用分辨哪個環是洞。
///
/// 貼圖是執行時建的，<b>由呼叫端釋放</b>（不釋放會一直佔記憶體）。
/// </summary>
public static class PuzzleSilhouette
{
    /// <summary>背景（透明）。</summary>
    private static readonly Color32 Background = new Color32(0, 0, 0, 0);

    /// <summary>剪影。</summary>
    private static readonly Color32 Silhouette = new Color32(255, 255, 255, 255);

    /// <summary>四周留幾成的邊，剪影才不會貼到貼圖邊緣。</summary>
    private const double Margin = 0.06;

    /// <param name="puzzle">要畫的題目（所有環）。</param>
    /// <param name="size">貼圖的邊長（像素），至少 8。</param>
    /// <returns>正方形、RGBA32、沒有 mipmap 的貼圖；剪影是白的、背景透明。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="puzzle"/> 是 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> 太小。</exception>
    public static Texture2D Draw(Puzzle puzzle, int size)
    {
        if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
        if (size < 8) throw new ArgumentOutOfRangeException(nameof(size), size, "縮圖至少要 8 像素。");

        // 所有環的邊，浮點（只拿來畫）
        var edges = new List<(double startX, double startY, double endX, double endY)>();
        double minimumX = double.MaxValue, minimumY = double.MaxValue, maximumX = double.MinValue, maximumY = double.MinValue;
        foreach (Walk walk in puzzle.Walks)
        {
            ExactVector2[] vertices = walk.Vertices();
            for (int i = 0; i < vertices.Length; i++)
            {
                ExactVector2 start = vertices[i], end = vertices[(i + 1) % vertices.Length];
                double startX = start.X.ToDouble(), startY = start.Y.ToDouble(), endX = end.X.ToDouble(), endY = end.Y.ToDouble();
                edges.Add((startX, startY, endX, endY));
                minimumX = Math.Min(minimumX, Math.Min(startX, endX));
                maximumX = Math.Max(maximumX, Math.Max(startX, endX));
                minimumY = Math.Min(minimumY, Math.Min(startY, endY));
                maximumY = Math.Max(maximumY, Math.Max(startY, endY));
            }
        }

        // 等比例：長的那一邊塞滿（扣掉邊），短的那一邊置中
        double span = Math.Max(maximumX - minimumX, maximumY - minimumY);
        if (span <= 0.0) span = 1.0;
        double scale = size * (1.0 - 2.0 * Margin) / span;
        double offsetX = (size - (maximumX - minimumX) * scale) / 2.0;
        double offsetY = (size - (maximumY - minimumY) * scale) / 2.0;

        var pixels = new Color32[size * size];
        for (int row = 0; row < size; row++)
        {
            // Unity 貼圖的第 0 列是最下面，模型的 y 也朝上：不用對調
            double y = minimumY + (row + 0.5 - offsetY) / scale;
            for (int column = 0; column < size; column++)
            {
                double x = minimumX + (column + 0.5 - offsetX) / scale;
                pixels[row * size + column] = Contains(edges, x, y) ? Silhouette : Background;
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "題目剪影縮圖",
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
