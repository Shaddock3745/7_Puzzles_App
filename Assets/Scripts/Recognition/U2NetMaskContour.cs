using System;
using System.Collections.Generic;
using UnityEngine;

public static class U2NetMaskContour
{
    [Serializable]
    private class ContourData
    {
        public int width;
        public int height;
        public int pointCount;
        public List<ContourPoint> points;
    }

    [Serializable]
    private class ContourPoint
    {
        public float x;
        public float y;
        public float normalizedX;
        public float normalizedY;
    }

    private struct GridPoint : IEquatable<GridPoint>
    {
        public int x;
        public int y;

        public GridPoint(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(GridPoint other)
        {
            return x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (x * 397) ^ y;
            }
        }
    }

    private struct Edge
    {
        public GridPoint start;
        public GridPoint end;

        public Edge(GridPoint start, GridPoint end)
        {
            this.start = start;
            this.end = end;
        }
    }

    /// <summary>從左下原點遮罩取最大外框，回傳照片像素座標 JSON，不寫檔。</summary>
    public static string CreateJson(Texture2D maskTexture, int photoWidth, int photoHeight)
    {
        if (maskTexture == null) throw new ArgumentNullException(nameof(maskTexture));
        if (photoWidth < 1 || photoHeight < 1) throw new ArgumentOutOfRangeException(nameof(photoWidth));
        var edges = BuildBoundaryEdges(ReadForegroundPixels(maskTexture));
        var contour = FindLargestLoop(TraceLoops(edges));
        // 太小的斑點不當成成功；門檻是模型遮罩像素，不隨照片尺寸改變。
        if (contour == null || Math.Abs(CalculateSignedArea(contour)) < 16f)
            throw new InvalidOperationException("沒有找到物體，或物體太小。");

        // 簡化只接受簡單多邊形；不允許 RDP 把狹窄凹口切成相交線。
        List<GridPoint> simplified = null;
        for (float tolerance = 1f; tolerance <= 16f; tolerance += 0.5f)
        {
            var candidate = SimplifyClosedContour(contour, tolerance);
            if (candidate.Count >= 3 && candidate.Count <= 150 && IsSimple(candidate))
            {
                simplified = candidate;
                break;
            }
        }
        if (simplified == null)
            throw new InvalidOperationException("輪廓過於複雜或有交叉，請換一張背景較單純的照片。");

        var points = new List<ContourPoint>();
        for (int i = 0; i < simplified.Count; i++)
        {
            GridPoint segmentStart = simplified[i], segmentEnd = simplified[(i + 1) % simplified.Count];
            // 簡單物體可能只有四個角；在線段上補點，不改變外形，滿足 20 點下限。
            int steps = simplified.Count < 20 ? (20 + simplified.Count - 1) / simplified.Count : 1;
            for (int j = 0; j < steps; j++)
            {
                float fraction = j / (float)steps;
                float normalizedX = (segmentStart.x + (segmentEnd.x - segmentStart.x) * fraction) / maskTexture.width;
                float normalizedY = (segmentStart.y + (segmentEnd.y - segmentStart.y) * fraction) / maskTexture.height;
                points.Add(new ContourPoint { x = normalizedX * photoWidth, y = normalizedY * photoHeight,
                    normalizedX = normalizedX, normalizedY = normalizedY });
            }
        }
        return JsonUtility.ToJson(new ContourData { width = photoWidth, height = photoHeight,
            pointCount = points.Count, points = points }, true);
    }

    private static long Cross(GridPoint segmentStart, GridPoint segmentEnd, GridPoint otherStart) =>
        (long)(segmentEnd.x - segmentStart.x) * (otherStart.y - segmentStart.y) - (long)(segmentEnd.y - segmentStart.y) * (otherStart.x - segmentStart.x);

    private static bool OnSegment(GridPoint segmentStart, GridPoint segmentEnd, GridPoint point) =>
        Cross(segmentStart, segmentEnd, point) == 0 && point.x >= Math.Min(segmentStart.x, segmentEnd.x) && point.x <= Math.Max(segmentStart.x, segmentEnd.x) &&
        point.y >= Math.Min(segmentStart.y, segmentEnd.y) && point.y <= Math.Max(segmentStart.y, segmentEnd.y);

    private static bool Intersects(GridPoint segmentStart, GridPoint segmentEnd, GridPoint otherStart, GridPoint otherEnd)
    {
        long startCross = Cross(segmentStart, segmentEnd, otherStart), endCross = Cross(segmentStart, segmentEnd, otherEnd);
        long otherStartCross = Cross(otherStart, otherEnd, segmentStart), otherEndCross = Cross(otherStart, otherEnd, segmentEnd);
        if (OnSegment(segmentStart, segmentEnd, otherStart) || OnSegment(segmentStart, segmentEnd, otherEnd) || OnSegment(otherStart, otherEnd, segmentStart) || OnSegment(otherStart, otherEnd, segmentEnd)) return true;
        return ((startCross > 0 && endCross < 0) || (startCross < 0 && endCross > 0)) &&
               ((otherStartCross > 0 && otherEndCross < 0) || (otherStartCross < 0 && otherEndCross > 0));
    }

    private static bool IsSimple(List<GridPoint> points)
    {
        int pointCount = points.Count;
        if (Math.Abs(CalculateSignedArea(points)) < 1f) return false;
        for (int i = 0; i < pointCount; i++)
        {
            GridPoint segmentStart = points[i], segmentEnd = points[(i + 1) % pointCount], previous = points[(i + pointCount - 1) % pointCount];
            if (segmentStart.Equals(segmentEnd)) return false;
            // 相鄰邊不能折返、互相重疊。
            if (Cross(previous, segmentStart, segmentEnd) == 0 &&
                (long)(segmentStart.x - previous.x) * (segmentEnd.x - segmentStart.x) + (long)(segmentStart.y - previous.y) * (segmentEnd.y - segmentStart.y) < 0) return false;
            for (int j = i + 1; j < pointCount; j++)
            {
                if (j == i + 1 || (i == 0 && j == pointCount - 1)) continue;
                if (Intersects(segmentStart, segmentEnd, points[j], points[(j + 1) % pointCount])) return false;
            }
        }
        return true;
    }

    private static bool[,] ReadForegroundPixels(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;

        Color32[] pixels = texture.GetPixels32();
        bool[,] foreground = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 color = pixels[y * width + x];

                int brightness =
                    color.r +
                    color.g +
                    color.b;

                foreground[x, y] = brightness >= 384;
            }
        }

        return foreground;
    }

    private static List<Edge> BuildBoundaryEdges(bool[,] foreground)
    {
        int width = foreground.GetLength(0);
        int height = foreground.GetLength(1);

        List<Edge> edges = new List<Edge>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!foreground[x, y])
                {
                    continue;
                }

                bool bottomIsBackground =
                    y == 0 || !foreground[x, y - 1];

                bool rightIsBackground =
                    x == width - 1 || !foreground[x + 1, y];

                bool topIsBackground =
                    y == height - 1 || !foreground[x, y + 1];

                bool leftIsBackground =
                    x == 0 || !foreground[x - 1, y];

                if (bottomIsBackground)
                {
                    edges.Add(new Edge(
                        new GridPoint(x, y),
                        new GridPoint(x + 1, y)
                    ));
                }

                if (rightIsBackground)
                {
                    edges.Add(new Edge(
                        new GridPoint(x + 1, y),
                        new GridPoint(x + 1, y + 1)
                    ));
                }

                if (topIsBackground)
                {
                    edges.Add(new Edge(
                        new GridPoint(x + 1, y + 1),
                        new GridPoint(x, y + 1)
                    ));
                }

                if (leftIsBackground)
                {
                    edges.Add(new Edge(
                        new GridPoint(x, y + 1),
                        new GridPoint(x, y)
                    ));
                }
            }
        }

        return edges;
    }

    private static List<List<GridPoint>> TraceLoops(List<Edge> edges)
    {
        Dictionary<GridPoint, List<int>> edgesByStart =
            new Dictionary<GridPoint, List<int>>();

        for (int i = 0; i < edges.Count; i++)
        {
            GridPoint start = edges[i].start;

            if (!edgesByStart.TryGetValue(start, out List<int> indices))
            {
                indices = new List<int>();
                edgesByStart.Add(start, indices);
            }

            indices.Add(i);
        }

        bool[] visited = new bool[edges.Count];
        List<List<GridPoint>> loops =
            new List<List<GridPoint>>();

        for (int i = 0; i < edges.Count; i++)
        {
            if (visited[i])
            {
                continue;
            }

            List<GridPoint> loop = TraceSingleLoop(
                i,
                edges,
                edgesByStart,
                visited
            );

            if (loop != null && loop.Count >= 3)
            {
                loops.Add(loop);
            }
        }

        return loops;
    }

    private static List<GridPoint> TraceSingleLoop(
        int firstEdgeIndex,
        List<Edge> edges,
        Dictionary<GridPoint, List<int>> edgesByStart,
        bool[] visited)
    {
        Edge firstEdge = edges[firstEdgeIndex];
        GridPoint firstPoint = firstEdge.start;

        List<GridPoint> loop = new List<GridPoint>
        {
            firstPoint
        };

        int currentEdgeIndex = firstEdgeIndex;
        int safetyLimit = edges.Count + 1;

        while (safetyLimit-- > 0)
        {
            if (visited[currentEdgeIndex])
            {
                return null;
            }

            visited[currentEdgeIndex] = true;

            Edge currentEdge = edges[currentEdgeIndex];
            GridPoint currentPoint = currentEdge.end;

            if (currentPoint.Equals(firstPoint))
            {
                return loop;
            }

            loop.Add(currentPoint);

            if (!edgesByStart.TryGetValue(
                    currentPoint,
                    out List<int> candidates))
            {
                return null;
            }

            int nextEdgeIndex = SelectNextEdge(
                currentEdge,
                candidates,
                edges,
                visited
            );

            if (nextEdgeIndex < 0)
            {
                return null;
            }

            currentEdgeIndex = nextEdgeIndex;
        }

        return null;
    }

    private static int SelectNextEdge(
        Edge currentEdge,
        List<int> candidates,
        List<Edge> edges,
        bool[] visited)
    {
        int incomingDirection = GetDirection(currentEdge);
        int bestIndex = -1;
        int bestScore = int.MinValue;

        foreach (int candidateIndex in candidates)
        {
            if (visited[candidateIndex])
            {
                continue;
            }

            int nextDirection = GetDirection(edges[candidateIndex]);
            int turn =
                (nextDirection - incomingDirection + 4) % 4;

            int score;

            switch (turn)
            {
                case 1:
                    score = 4;
                    break;

                case 0:
                    score = 3;
                    break;

                case 3:
                    score = 2;
                    break;

                default:
                    score = 1;
                    break;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = candidateIndex;
            }
        }

        return bestIndex;
    }

    private static int GetDirection(Edge edge)
    {
        int deltaX = edge.end.x - edge.start.x;
        int deltaY = edge.end.y - edge.start.y;

        if (deltaX > 0)
        {
            return 0;
        }

        if (deltaY > 0)
        {
            return 1;
        }

        if (deltaX < 0)
        {
            return 2;
        }

        return 3;
    }

    private static List<GridPoint> FindLargestLoop(
        List<List<GridPoint>> loops)
    {
        List<GridPoint> largestLoop = null;
        float largestArea = 0f;

        foreach (List<GridPoint> loop in loops)
        {
            float area = Mathf.Abs(CalculateSignedArea(loop));

            if (area > largestArea)
            {
                largestArea = area;
                largestLoop = loop;
            }
        }

        return largestLoop;
    }

    private static float CalculateSignedArea(
        List<GridPoint> points)
    {
        float area = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            GridPoint current = points[i];
            GridPoint next = points[(i + 1) % points.Count];

            area +=
                current.x * next.y -
                next.x * current.y;
        }

        return area * 0.5f;
    }

    private static List<GridPoint> SimplifyClosedContour(
        List<GridPoint> contour,
        float tolerance)
    {
        if (contour.Count <= 3 || tolerance <= 0f)
        {
            return new List<GridPoint>(contour);
        }

        int splitIndex = FindFarthestPointIndex(
            contour,
            contour[0]
        );

        if (splitIndex <= 0 ||
            splitIndex >= contour.Count - 1)
        {
            return new List<GridPoint>(contour);
        }

        List<GridPoint> firstPath =
            contour.GetRange(0, splitIndex + 1);

        List<GridPoint> secondPath =
            contour.GetRange(
                splitIndex,
                contour.Count - splitIndex
            );

        secondPath.Add(contour[0]);

        List<GridPoint> simplifiedFirst =
            SimplifyOpenPath(firstPath, tolerance);

        List<GridPoint> simplifiedSecond =
            SimplifyOpenPath(secondPath, tolerance);

        List<GridPoint> result =
            new List<GridPoint>(simplifiedFirst);

        for (int i = 1;
             i < simplifiedSecond.Count - 1;
             i++)
        {
            result.Add(simplifiedSecond[i]);
        }

        return result.Count >= 3
            ? result
            : new List<GridPoint>(contour);
    }

    private static int FindFarthestPointIndex(
        List<GridPoint> points,
        GridPoint origin)
    {
        int farthestIndex = 0;
        float farthestDistance = -1f;

        for (int i = 1; i < points.Count; i++)
        {
            float deltaX = points[i].x - origin.x;
            float deltaY = points[i].y - origin.y;

            float distance =
                deltaX * deltaX +
                deltaY * deltaY;

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthestIndex = i;
            }
        }

        return farthestIndex;
    }

    private static List<GridPoint> SimplifyOpenPath(
        List<GridPoint> points,
        float tolerance)
    {
        if (points.Count <= 2)
        {
            return new List<GridPoint>(points);
        }

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        SimplifySection(
            points,
            0,
            points.Count - 1,
            tolerance * tolerance,
            keep
        );

        List<GridPoint> result = new List<GridPoint>();

        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    private static void SimplifySection(
        List<GridPoint> points,
        int firstIndex,
        int lastIndex,
        float toleranceSquared,
        bool[] keep)
    {
        if (lastIndex <= firstIndex + 1)
        {
            return;
        }

        float maximumDistance = -1f;
        int farthestIndex = -1;

        for (int i = firstIndex + 1;
             i < lastIndex;
             i++)
        {
            float distance = DistanceToSegmentSquared(
                points[i],
                points[firstIndex],
                points[lastIndex]
            );

            if (distance > maximumDistance)
            {
                maximumDistance = distance;
                farthestIndex = i;
            }
        }

        if (maximumDistance <= toleranceSquared)
        {
            return;
        }

        keep[farthestIndex] = true;

        SimplifySection(
            points,
            firstIndex,
            farthestIndex,
            toleranceSquared,
            keep
        );

        SimplifySection(
            points,
            farthestIndex,
            lastIndex,
            toleranceSquared,
            keep
        );
    }

    private static float DistanceToSegmentSquared(
        GridPoint point,
        GridPoint segmentStart,
        GridPoint segmentEnd)
    {
        float segmentX =
            segmentEnd.x - segmentStart.x;

        float segmentY =
            segmentEnd.y - segmentStart.y;

        float lengthSquared =
            segmentX * segmentX +
            segmentY * segmentY;

        if (lengthSquared <= Mathf.Epsilon)
        {
            float deltaX = point.x - segmentStart.x;
            float deltaY = point.y - segmentStart.y;

            return deltaX * deltaX + deltaY * deltaY;
        }

        float projection =
            ((point.x - segmentStart.x) * segmentX +
             (point.y - segmentStart.y) * segmentY) /
            lengthSquared;

        projection = Mathf.Clamp01(projection);

        float closestX =
            segmentStart.x + projection * segmentX;

        float closestY =
            segmentStart.y + projection * segmentY;

        float differenceX = point.x - closestX;
        float differenceY = point.y - closestY;

        return
            differenceX * differenceX +
            differenceY * differenceY;
    }
}
