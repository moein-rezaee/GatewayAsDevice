using System.Text;
using Sepidar.Extension.Interfaces;

namespace Sepidar.Extension.Services;

public class SerialKeyDeriver : ISerialKeyDeriver
{
    public string BuildKey(string serial)
    {
        var src = (serial ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(src)) throw new InvalidOperationException("Invalid serial");
        var doubled = string.Concat(src, src);
        if (doubled.Length >= 16) return doubled[..16];
        var sb = new StringBuilder(16);
        while (sb.Length < 16) sb.Append(src);
        return sb.ToString()[..16];
    }

    public IEnumerable<(string Key, string Name)> GenerateCandidateKeys(string serial)
    {
        serial = (serial ?? string.Empty).Trim();
        var digits = new string(serial.Where(char.IsDigit).ToArray());
        yield return (CutOrRepeat(serial + serial, 16), "Serial+Serial Left16");
        yield return (CutOrRepeat(serial, 16), "Serial Left16");
        if (!string.IsNullOrEmpty(digits)) yield return (CutOrRepeat(digits, 16), "Digits Left/Repeat16");
        yield return (RepeatToLength(serial, 16), "Serial RepeatTo16");
        if (!string.IsNullOrEmpty(digits)) yield return (RepeatToLength(digits, 16), "Digits RepeatTo16");
        var up = serial.ToUpperInvariant();
        var down = serial.ToLowerInvariant();
        yield return (CutOrRepeat(up + up, 16), "Upper Serial+Serial Left16");
        yield return (CutOrRepeat(down + down, 16), "Lower Serial+Serial Left16");
    }

    private static string CutOrRepeat(string s, int len)
    {
        if (string.IsNullOrEmpty(s)) throw new InvalidOperationException("Empty for key");
        if (s.Length >= len) return s[..len];
        return RepeatToLength(s, len);
    }

    private static string RepeatToLength(string s, int len)
    {
        if (string.IsNullOrEmpty(s)) throw new InvalidOperationException("Empty for repeat");
        var sb = new StringBuilder(len);
        while (sb.Length < len) sb.Append(s);
        return sb.ToString()[..len];
    }
}

