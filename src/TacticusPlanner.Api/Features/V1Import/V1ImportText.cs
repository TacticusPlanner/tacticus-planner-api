using System.Globalization;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>Text helpers shared by the V1 import parts.</summary>
internal static class V1ImportText
{
    /// <summary>
    /// <paramref name="value"/> cut to at most <paramref name="maxLength"/> UTF-16 code units on a text-element
    /// boundary, so a surrogate pair (an emoji) or a combining sequence is never split. A split pair is not valid
    /// UTF-16, and Npgsql's strict UTF-8 encoder refuses it when the row is saved.
    /// </summary>
    public static string CutToLength(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        var cut = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            var end = elements.ElementIndex + elements.GetTextElement().Length;
            if (end > maxLength)
                break;
            cut = end;
        }

        return value[..cut];
    }
}
