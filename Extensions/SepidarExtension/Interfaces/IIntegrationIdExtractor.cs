namespace Sepidar.Extension.Interfaces;

public interface IIntegrationIdExtractor
{
    int Extract(string serial, int digitCount);
}

