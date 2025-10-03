using System.Net.Http;
using System.Text;
using SepidarGateway.Api.Interfaces;

namespace SepidarGateway.Api.Services;

public class CurlBuilder : ICurlBuilder
{
    private bool _nextPowerShell;

    public ICurlBuilder PowerShell()
    {
        _nextPowerShell = true;
        return this;
    }

    public string Build(string url, IDictionary<string, string>? headers = null, string? bodyJson = null, HttpMethod? method = null)
    {
        method ??= HttpMethod.Post;
        var methodName = method.Method.ToUpperInvariant();
        var ps = _nextPowerShell;
        _nextPowerShell = false; // reset

        if (ps)
        {
            static string Q(string s) => "\"" + s.Replace("`", "``").Replace("\"", "`\"") + "\"";
            var sb = new StringBuilder();
            sb.Append("curl --location");
            if (!string.IsNullOrWhiteSpace(methodName))
            {
                sb.Append(' ').Append("--request ").Append(methodName);
            }
            sb.Append(' ').Append(Q(url));
            if (headers is not null)
            {
                foreach (var (k, v) in headers)
                {
                    sb.Append(' ').Append("--header ").Append(Q($"{k}: {v}"));
                }
            }
            if (!string.IsNullOrEmpty(bodyJson))
            {
                sb.Append(' ').Append("--data ").Append(Q(bodyJson));
            }
            return sb.ToString();
        }
        else
        {
            static string Q(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";
            var sb = new StringBuilder();
            sb.Append("curl --location");
            if (!string.IsNullOrWhiteSpace(methodName))
            {
                sb.Append(' ').Append("--request ").Append(methodName);
            }
            sb.Append(' ').Append(Q(url));
            if (headers is not null)
            {
                foreach (var (k, v) in headers)
                {
                    sb.Append(' ').Append("--header ").Append(Q($"{k}: {v}"));
                }
            }
            if (!string.IsNullOrEmpty(bodyJson))
            {
                sb.Append(' ').Append("--data ").Append(Q(bodyJson));
            }
            return sb.ToString();
        }
    }
}

