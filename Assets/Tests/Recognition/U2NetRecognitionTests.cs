using System;
using NUnit.Framework;
using Tangram.Data;
using UnityEngine;

public sealed class U2NetRecognitionTests
{
    [Serializable] private class Dto
    {
        public int width, height, pointCount;
        public Point[] points;
    }
    [Serializable] private class Point { public float x, y; }

    private static Texture2D MakeMask(int width, int height, Func<int, int, bool> foreground)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte value = foreground(x, y) ? (byte)255 : (byte)0;
            pixels[y * width + x] = new Color32(value, value, value, 255);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    [TestCase(1024, 768, 320, 240, 0, 40)]
    [TestCase(768, 1024, 240, 320, 40, 0)]
    [TestCase(320, 320, 320, 320, 0, 0)]
    public void LetterboxKeepsPhotoAspect(int width, int height, int cw, int ch, int left, int top)
    {
        var geometry = new U2NetInputGeometry(width, height);
        Assert.AreEqual(cw, geometry.ContentWidth);
        Assert.AreEqual(ch, geometry.ContentHeight);
        Assert.AreEqual(left, geometry.Left);
        Assert.AreEqual(top, geometry.Top);
    }

    [Test]
    public void InputUsesTopFirstNchwWithoutChangingPhoto()
    {
        var photo = MakeMask(8, 4, (x, y) => y >= 2);
        try
        {
            var before = photo.GetPixels32();
            var geometry = new U2NetInputGeometry(photo.width, photo.height);
            var input = geometry.CreateInput(photo);
            Assert.AreEqual(3 * 320 * 320, input.Length);
            int topSample = (geometry.Top + 20) * 320 + 160;
            int bottomSample = (geometry.Top + geometry.ContentHeight - 21) * 320 + 160;
            Assert.Greater(input[topSample], input[bottomSample]);
            CollectionAssert.AreEqual(before, photo.GetPixels32());
        }
        finally { UnityEngine.Object.DestroyImmediate(photo); }
    }

    [Test]
    public void RemovesPaddingAndFlipsTopLeftOutputToBottomLeftOnce()
    {
        var geometry = new U2NetInputGeometry(1024, 768);
        var probabilities = new float[320 * 320];
        // 照片的上緣，模型位置 y=40；輸出遮罩應在 y=239。
        probabilities[40 * 320 + 12] = 1;
        var mask = geometry.CreateMask(probabilities);
        try
        {
            Assert.AreEqual(320, mask.width);
            Assert.AreEqual(240, mask.height);
            Assert.AreEqual(255, mask.GetPixels32()[239 * 320 + 12].r);
            Assert.AreEqual(0, mask.GetPixels32()[12].r);
        }
        finally { UnityEngine.Object.DestroyImmediate(mask); }
    }

    [Test]
    public void JsonUsesPhotoDimensionsAndPixelCoordinates()
    {
        var mask = MakeMask(320, 240, (x, y) => x >= 40 && x < 240 && y >= 30 && y < 180);
        try
        {
            var json = U2NetMaskContour.CreateJson(mask, 1024, 768);
            var parsed = RecognizedContour.FromJson(json);
            Assert.AreEqual(1024, parsed.ImageWidth);
            Assert.AreEqual(768, parsed.ImageHeight);
            var dto = JsonUtility.FromJson<Dto>(json);
            Assert.AreEqual(1024, dto.width);
            Assert.AreEqual(768, dto.height);
            Assert.AreEqual(dto.points.Length, dto.pointCount);
            Assert.That(dto.pointCount, Is.InRange(20, 150));
            float minX = float.MaxValue, maxX = 0, minY = float.MaxValue, maxY = 0;
            foreach (var point in dto.points)
            {
                minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
                minY = Math.Min(minY, point.y); maxY = Math.Max(maxY, point.y);
            }
            Assert.That(minX, Is.EqualTo(128).Within(0.01));
            Assert.That(maxX, Is.EqualTo(768).Within(0.01));
            Assert.That(minY, Is.EqualTo(96).Within(0.01));
            Assert.That(maxY, Is.EqualTo(576).Within(0.01));
        }
        finally { UnityEngine.Object.DestroyImmediate(mask); }
    }

    [Test]
    public void KeepsLargestOuterLoopAndDiscardsHoleAndSmallObject()
    {
        var mask = MakeMask(100, 100, (x, y) =>
            (x >= 20 && x < 80 && y >= 20 && y < 80 && !(x >= 40 && x < 60 && y >= 40 && y < 60)) ||
            (x >= 2 && x < 6 && y >= 2 && y < 6));
        try
        {
            var dto = JsonUtility.FromJson<Dto>(U2NetMaskContour.CreateJson(mask, 100, 100));
            double area = 0;
            for (int i = 0; i < dto.points.Length; i++)
            {
                var a = dto.points[i]; var b = dto.points[(i + 1) % dto.points.Length];
                area += a.x * b.y - b.x * a.y;
            }
            Assert.That(Math.Abs(area) / 2, Is.EqualTo(3600).Within(0.01));
        }
        finally { UnityEngine.Object.DestroyImmediate(mask); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyAndTinyMasksFail(bool tiny)
    {
        var mask = MakeMask(100, 100, (x, y) => tiny && x < 2 && y < 2);
        try { Assert.Throws<InvalidOperationException>(() => U2NetMaskContour.CreateJson(mask, 100, 100)); }
        finally { UnityEngine.Object.DestroyImmediate(mask); }
    }

    [Test]
    public void ConstantProbabilityMapFailsInsteadOfReturningBlankContour()
    {
        var geometry = new U2NetInputGeometry(1024, 768);
        Assert.Throws<InvalidOperationException>(() => geometry.CreateMask(new float[320 * 320]));
    }
}
