namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x}, {y})";
    }

    public static class JsonUtility
    {
        private static readonly System.Text.Json.JsonSerializerOptions Opt =
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object o, bool pretty = false)
            => System.Text.Json.JsonSerializer.Serialize(o, o.GetType(), Opt);
        public static T FromJson<T>(string json)
            => System.Text.Json.JsonSerializer.Deserialize<T>(json, Opt);
    }
}
