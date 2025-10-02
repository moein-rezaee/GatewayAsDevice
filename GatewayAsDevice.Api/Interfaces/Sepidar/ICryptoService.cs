using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Interfaces.Sepidar;

public interface ICryptoService
{
    (string Cipher, string Iv) EncryptIntegrationId(int integrationId, string key16);
    string DecryptToXml(string cypherBase64, string ivBase64, string key16);
    (bool Ok, string Xml, string Strategy) TryDecryptPublicKeyWithStrategies(string cypherB64, string ivB64, string serial);
    bool TryGetRsaParameters(JsonNode? responseNode, out RSAParameters rsaParams);
    (string Modulus, string Exponent) TryParseRsaXml(string xml);
    string BuildKeyFromSerial(string serial);
    byte[] GuidToRfc4122Bytes(Guid guid);
    string ToHex(byte[] bytes);
}

