namespace MeDan.Api.Helpers;

public static class EnumJson
{
    /// <summary>
    /// Serializes an enum to camelCase to match the Flutter app's Dart enum `.name`
    /// (e.g. RoomType.DoublyShared → "doublyShared", RoomStatus.Available → "available").
    /// </summary>
    public static string ToCamel(this Enum value)
    {
        var s = value.ToString();
        return s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    }
}
