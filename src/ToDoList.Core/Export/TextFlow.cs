using System.Globalization;
using System.Text;

namespace ToDoList.Core.Export;

internal static class TextFlow
{
    // MigraDoc table rows do not split across pages. Bound each continuation row
    // by both graphemes and explicit line breaks so long plans cannot be clipped.
    public static IReadOnlyList<string> Chunks(string text, int maxCharacters = 260, int maxLines = 7)
    {
        var result = new List<string>();
        var buffer = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        var count = 0;
        var lines = 1;
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (count >= maxCharacters || (element == "\n" && lines >= maxLines))
            {
                result.Add(buffer.ToString());
                buffer.Clear();
                count = 0;
                lines = 1;
            }
            buffer.Append(element);
            count++;
            if (element == "\n") lines++;
        }
        if (buffer.Length > 0 || result.Count == 0) result.Add(buffer.ToString());
        return result;
    }
}
