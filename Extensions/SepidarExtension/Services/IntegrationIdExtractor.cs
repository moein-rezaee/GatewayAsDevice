using System.Globalization;
using Sepidar.Extension.Interfaces;

namespace Sepidar.Extension.Services;

public class IntegrationIdExtractor : IIntegrationIdExtractor
{
    public int Extract(string serial, int digitCount)
    {
        var digits = new string((serial ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < digitCount) throw new InvalidOperationException($"Serial must contain at least {digitCount} digits");
        var slice = digits[..digitCount];
        if (!int.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new InvalidOperationException("Cannot parse IntegrationID");
        return id;
    }
}

