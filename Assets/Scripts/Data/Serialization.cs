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

    /// <summary><see cref="Scalar"/> 的存檔形狀。</summary>
    [Serializable]
    public class ScalarDto
    {
        public long rationalPart;
        public long root2Coefficient;
    }

    /// <summary><see cref="ExactVector2"/> 的存檔形狀。</summary>
    [Serializable]
    public class ExactVector2Dto
    {
        public ScalarDto x;
        public ScalarDto y;
    }

    /// <summary><see cref="Edge"/> 的存檔形狀。</summary>
    [Serializable]
    public class EdgeDto
    {
        public int direction;
        public ScalarDto length;
    }

    /// <summary><see cref="Walk"/> 的存檔形狀。</summary>
    [Serializable]
    public class WalkDto
    {
        public ExactVector2Dto anchor;
        public List<EdgeDto> edges;
    }

    /// <summary><see cref="PiecePose"/> 的存檔形狀。</summary>
    [Serializable]
    public class PieceDto
    {
        public string kind;
        public int rotation;
        public bool flipped;
    }

    /// <summary><see cref="Link"/> 的存檔形狀。規格寫的巢狀陣列在這裡攤成四個欄位。</summary>
    [Serializable]
    public class LinkDto
    {
        public int firstPiece;
        public int firstNode;
        public int secondPiece;
        public int secondNode;
    }

    /// <summary>puzzles 表的一列。</summary>
    [Serializable]
    public class PuzzleDto
    {
        public string key;
        public List<WalkDto> walks;
        public string source;
    }

    /// <summary>arrangements 表的一列。</summary>
    [Serializable]
    public class ArrangementDto
    {
        public string key;
        public string puzzleKey;
        public List<PieceDto> pieces;   // 7
        public List<LinkDto> links;     // 6
        public string createdAt;        // ISO-8601 UTC
    }

    /// <summary>
    /// 模型物件與存檔形狀的互轉，以及 JSON 的進出口。
    /// 這是模型層唯一 <c>using UnityEngine</c> 的地方；換儲存後端只要改這一支。
    /// </summary>
    public static class Serialization
    {
        /// <param name="scalar">模型的精確數值。</param>
        /// <returns>對應的 DTO。</returns>
        public static ScalarDto ToDto(Scalar scalar)
            => new ScalarDto
            {
                rationalPart = scalar.RationalPart,
                root2Coefficient = scalar.Root2Coefficient
            };

        /// <param name="dto">存檔形狀。</param>
        /// <returns>對應的 Scalar。</returns>
        public static Scalar ToScalar(ScalarDto dto)
            => new Scalar(dto.rationalPart, dto.root2Coefficient);

        /// <param name="vector">模型的精確向量。</param>
        /// <returns>對應的 DTO。</returns>
        public static ExactVector2Dto ToDto(ExactVector2 vector)
            => new ExactVector2Dto { x = ToDto(vector.X), y = ToDto(vector.Y) };

        /// <param name="dto">存檔形狀。</param>
        /// <returns>對應的向量。</returns>
        public static ExactVector2 ToExactVector2(ExactVector2Dto dto)
            => new ExactVector2(ToScalar(dto.x), ToScalar(dto.y));

        /// <param name="puzzle">要存的題目。</param>
        /// <returns>對應的 DTO，key 原樣帶過去，不重算。</returns>
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
        /// <param name="dto">存檔形狀。</param>
        /// <returns>
        /// 還原的題目。<c>source</c> 不是 <c>"user_drawn"</c> 時一律當成 generated。
        /// 載入外部來源的資料時，保險做法是自己再重算一次 key 比對。
        /// </returns>
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

        /// <param name="arrangement">要存的解。</param>
        /// <param name="createdAtUtc">建立時間，傳 null 就用現在的 UTC 時間。</param>
        /// <returns>對應的 DTO，兩個 key 都原樣帶過去，不重算。</returns>
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
        /// <param name="dto">存檔形狀。</param>
        /// <returns>還原的解。要驗證合不合法就再呼叫 <see cref="Arrangement.Decode"/>。</returns>
        /// <exception cref="ArgumentException">pieces 裡有不認得的片種字串。</exception>
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

        /// <param name="puzzle">要序列化的題目。</param>
        /// <param name="pretty">是否輸出有縮排的格式。</param>
        /// <returns>JSON 字串。</returns>
        public static string ToJson(Puzzle puzzle, bool pretty = false)
            => JsonUtility.ToJson(ToDto(puzzle), pretty);

        /// <param name="arrangement">要序列化的解。</param>
        /// <param name="pretty">是否輸出有縮排的格式。</param>
        /// <returns>JSON 字串。</returns>
        public static string ToJson(Arrangement arrangement, bool pretty = false)
            => JsonUtility.ToJson(ToDto(arrangement), pretty);

        /// <param name="json">JSON 字串。</param>
        /// <returns>還原的題目。</returns>
        public static Puzzle PuzzleFromJson(string json)
            => FromDto(JsonUtility.FromJson<PuzzleDto>(json));

        /// <param name="json">JSON 字串。</param>
        /// <returns>還原的解。</returns>
        public static Arrangement ArrangementFromJson(string json)
            => FromDto(JsonUtility.FromJson<ArrangementDto>(json));
    }
}
