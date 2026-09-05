// UnityEngine 的最小替身，讓 Assets/Scripts 底下的程式能在 Unity 外面用 dotnet 編譯與測試。
//
// ⚠️ 這個檔案一定要放在 Assets/ 外面。放進 Assets/ 的話 Unity 會一起編，
//    UnityEngine.Vector2 就會有兩份定義。Tools/ 在 Assets/ 外，Unity 完全不會看它。
//
// 只補「Assets/Scripts/Data 與 Assets/Scripts/Core 真的用到」的東西。
// MonoBehaviour、Camera、Screen 這些刻意不補——用到它們的程式屬於角色三，
// 本來就不該進測試（見 互動設計.md §2.4）。

namespace UnityEngine
{
    /// <summary>
    /// Unity 的二維浮點向量。這裡只補真 Unity 語意明確、沒有引擎特殊約定的部分。
    ///
    /// <b>刻意不補 <c>==</c> / <c>!=</c></b>：真 Unity 的 Vector2 相等是<b>有容差的</b>
    /// （兩點距離小於約 1e-5 就算相等），不是逐位元比較。補一個逐位元版本會讓測試
    /// 通過但實機行為不同，是最糟的一種假象。要比較就在測試裡用
    /// <c>Assert.AreEqual(expected, actual, tolerance)</c> 把容差明寫出來。
    /// </summary>
    public struct Vector2
    {
        public float x, y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);

        /// <summary>歐氏長度。</summary>
        public float magnitude => (float)System.Math.Sqrt(x * x + y * y);
        /// <summary>長度平方。要比大小時用這個，省一次開根號。</summary>
        public float sqrMagnitude => x * x + y * y;

        public static Vector2 operator +(Vector2 left, Vector2 right)
            => new Vector2(left.x + right.x, left.y + right.y);
        public static Vector2 operator -(Vector2 left, Vector2 right)
            => new Vector2(left.x - right.x, left.y - right.y);
        public static Vector2 operator -(Vector2 value)
            => new Vector2(-value.x, -value.y);
        public static Vector2 operator *(Vector2 value, float multiplier)
            => new Vector2(value.x * multiplier, value.y * multiplier);
        public static Vector2 operator *(float multiplier, Vector2 value)
            => new Vector2(value.x * multiplier, value.y * multiplier);
        public static Vector2 operator /(Vector2 value, float divisor)
            => new Vector2(value.x / divisor, value.y / divisor);

        /// <param name="from">起點。</param>
        /// <param name="to">終點。</param>
        /// <returns>兩點的歐氏距離。</returns>
        public static float Distance(Vector2 from, Vector2 to) => (from - to).magnitude;

        public override string ToString() => $"({x}, {y})";
    }

    /// <summary>Unity 的浮點數學工具。語意跟 System.Math 相同，只是收 float 回 float。</summary>
    public static class Mathf
    {
        public static float Min(float left, float right) => left < right ? left : right;
        public static float Max(float left, float right) => left > right ? left : right;
        public static float Abs(float value) => value < 0f ? -value : value;
        public static float Sqrt(float value) => (float)System.Math.Sqrt(value);

        /// <param name="value">要夾的值。</param>
        /// <param name="minimum">下界。</param>
        /// <param name="maximum">上界。</param>
        /// <returns>夾進 [minimum, maximum] 之後的值。</returns>
        public static float Clamp(float value, float minimum, float maximum)
            => value < minimum ? minimum : (value > maximum ? maximum : value);

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
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
