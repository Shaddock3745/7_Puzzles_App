using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tangram.Data
{
    // §3 Schema 的 JSONB 欄位。刻意攤平成 JsonUtility 吃得下的形狀
    // （公開欄位、沒有巢狀陣列、沒有 struct 的 readonly 欄位）。
    //
    // puzzles       key TEXT PK / walks JSONB / source TEXT
    // arrangements  key TEXT PK / puzzle_key TEXT FK(索引) / pieces JSONB / links JSONB / created_at

    [Serializable]
    public class ScalarDto
    {
        public long rationalPart;
        public long root2Coefficient;
    }

    [Serializable]
    public class ExactVector2Dto
    {
        public ScalarDto x;
        public ScalarDto y;
    }

    [Serializable]
    public class EdgeDto
    {
        public int direction;
        public ScalarDto length;
    }

    [Serializable]
    public class WalkDto
    {
        public ExactVector2Dto anchor;
        public List<EdgeDto> edges;
    }

    [Serializable]
    public class PieceDto
    {
        public string kind;
        public int rotation;
        public bool flipped;
    }

    [Serializable]
    public class LinkDto
    {
        public int firstPiece;
        public int firstNode;
        public int secondPiece;
        public int secondNode;
    }

    [Serializable]
    public class PuzzleDto
    {
        public string key;
        public List<WalkDto> walks;
        public string source;
    }

    [Serializable]
    public class ArrangementDto
    {
        public string key;
        public string puzzleKey;
        public List<PieceDto> pieces;   // 7
        public List<LinkDto> links;     // 6
        public string createdAt;        // ISO-8601 UTC
    }

    public static class Serialization
    {
        public static ScalarDto ToDto(Scalar scalar)
            => new ScalarDto
            {
                rationalPart = scalar.RationalPart,
                root2Coefficient = scalar.Root2Coefficient
            };

        public static Scalar ToScalar(ScalarDto dto)
            => new Scalar(dto.rationalPart, dto.root2Coefficient);

        public static ExactVector2Dto ToDto(ExactVector2 vector)
            => new ExactVector2Dto { x = ToDto(vector.X), y = ToDto(vector.Y) };

        public static ExactVector2 ToExactVector2(ExactVector2Dto dto)
            => new ExactVector2(ToScalar(dto.x), ToScalar(dto.y));

        public static PuzzleDto ToDto(Puzzle puzzle)
        {
            var dto = new PuzzleDto
            {
                key = puzzle.Key,
                source = puzzle.Source == PuzzleSource.Generated ? "generated" : "user_drawn",
                walks = new List<WalkDto>()
            };

            foreach (Walk walk in puzzle.Walks)
            {
                var walkDto = new WalkDto { anchor = ToDto(walk.Anchor), edges = new List<EdgeDto>() };
                foreach (Edge edge in walk.Edges)
                    walkDto.edges.Add(new EdgeDto { direction = edge.Direction, length = ToDto(edge.Length) });
                dto.walks.Add(walkDto);
            }
            return dto;
        }

        /// <summary>注意：不重算 key，相信檔案裡存的那一個。</summary>
        public static Puzzle FromDto(PuzzleDto dto)
        {
            var walks = new List<Walk>();
            foreach (WalkDto walkDto in dto.walks)
            {
                var edges = new Edge[walkDto.edges.Count];
                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                    edges[edgeIndex] = new Edge(walkDto.edges[edgeIndex].direction,
                                                ToScalar(walkDto.edges[edgeIndex].length));
                walks.Add(new Walk(ToExactVector2(walkDto.anchor), edges));
            }

            return new Puzzle(dto.key, walks.ToArray(),
                dto.source == "user_drawn" ? PuzzleSource.UserDrawn : PuzzleSource.Generated);
        }

        public static ArrangementDto ToDto(Arrangement arrangement, DateTime? createdAtUtc = null)
        {
            var dto = new ArrangementDto
            {
                key = arrangement.Key,
                puzzleKey = arrangement.PuzzleKey,
                pieces = new List<PieceDto>(),
                links = new List<LinkDto>(),
                createdAt = (createdAtUtc ?? DateTime.UtcNow).ToString("o")
            };

            foreach (PiecePose pose in arrangement.Pieces)
                dto.pieces.Add(new PieceDto
                {
                    kind = PieceKindNames.ToStorageString(pose.Kind),
                    rotation = pose.Rotation,
                    flipped = pose.IsFlipped
                });

            foreach (Link link in arrangement.Links)
                dto.links.Add(new LinkDto
                {
                    firstPiece = link.FirstPiece,
                    firstNode = link.FirstNode,
                    secondPiece = link.SecondPiece,
                    secondNode = link.SecondNode
                });

            return dto;
        }

        /// <summary>注意：不重算 key，相信檔案裡存的那一個。</summary>
        public static Arrangement FromDto(ArrangementDto dto)
        {
            var poses = new PiecePose[dto.pieces.Count];
            for (int pieceIndex = 0; pieceIndex < poses.Length; pieceIndex++)
                poses[pieceIndex] = new PiecePose(
                    PieceKindNames.FromStorageString(dto.pieces[pieceIndex].kind),
                    dto.pieces[pieceIndex].rotation,
                    dto.pieces[pieceIndex].flipped);

            var links = new Link[dto.links.Count];
            for (int linkIndex = 0; linkIndex < links.Length; linkIndex++)
                links[linkIndex] = new Link(
                    dto.links[linkIndex].firstPiece,
                    dto.links[linkIndex].firstNode,
                    dto.links[linkIndex].secondPiece,
                    dto.links[linkIndex].secondNode);

            return new Arrangement(poses, links, dto.key, dto.puzzleKey);
        }

        public static string ToJson(Puzzle puzzle, bool pretty = false)
            => JsonUtility.ToJson(ToDto(puzzle), pretty);

        public static string ToJson(Arrangement arrangement, bool pretty = false)
            => JsonUtility.ToJson(ToDto(arrangement), pretty);

        public static Puzzle PuzzleFromJson(string json)
            => FromDto(JsonUtility.FromJson<PuzzleDto>(json));

        public static Arrangement ArrangementFromJson(string json)
            => FromDto(JsonUtility.FromJson<ArrangementDto>(json));
    }
}
