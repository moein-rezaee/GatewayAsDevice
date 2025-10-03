using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Sepidar.Extension.Interfaces;

public interface IPublicKeyProcessor
{
    (bool Ok, string Xml, string Strategy) TryDecryptPublicKeyWithStrategies(string cypherB64, string ivB64, string serial);
    (string Modulus, string Exponent) TryParseRsaXml(string xml);
    bool TryGetRsaParameters(JsonNode? responseNode, out RSAParameters rsaParams);
}

