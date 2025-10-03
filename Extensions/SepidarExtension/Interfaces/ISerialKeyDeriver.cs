namespace Sepidar.Extension.Interfaces;

public interface ISerialKeyDeriver
{
    string BuildKey(string serial);
    IEnumerable<(string Key, string Name)> GenerateCandidateKeys(string serial);
}

