using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Tools;

/// <summary>
/// Fetches the contents of a public web page or HTTP API on the agent's behalf. Unlike the file and
/// terminal tools this reaches <em>out</em> to the network rather than the workspace, so it is the
/// backend counterpart of a VS Code custom agent's <c>web/fetch</c> tool. For safety it only follows
/// absolute <c>http</c>/<c>https</c> URLs, caps the returned text, and relies on the injected
/// <see cref="HttpClient"/>'s timeout to bound a slow response. The registered client connects only to public
/// addresses (<see cref="ConnectToPublicAddressAsync"/>), so a prompt cannot reach loopback, private networks or
/// cloud metadata endpoints that hand out the host's credentials.
/// </summary>
public sealed class WebFetchTool(HttpClient http)
{
    private const int MaxChars = 20_000;

    /// <summary>Fetches an absolute http(s) URL and returns its response body as text.</summary>
    /// <param name="url">The absolute <c>http://</c> or <c>https://</c> URL to fetch.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The response body (truncated when large), or a message describing why the fetch failed.</returns>
    [Description("Fetch the contents of a public http(s) web page or HTTP API and return its text.")]
    public async Task<string> WebFetch(
        [Description("The absolute http:// or https:// URL to fetch, e.g. 'https://example.com'.")] string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Provide an absolute http:// or https:// URL.";
        }

        try
        {
            using var response = await http.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"Request to '{uri}' failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            }

            return body.Length > MaxChars
                ? body[..MaxChars] + $"\n… (truncated, {body.Length} characters total)"
                : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"Could not fetch '{uri}': {ex.Message}";
        }
    }

    /// <summary>
    /// A <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host itself and connects only to a public
    /// address. Checking at connect time covers redirects and DNS names that resolve to internal addresses.
    /// </summary>
    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.FirstOrDefault(IsPublic)
            ?? throw new HttpRequestException($"'{host}' does not resolve to a public address; only public web pages can be fetched.");

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Whether an address is publicly routable: not loopback, private, link-local, shared, multicast or reserved.</summary>
    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)   // shared address space (CGNAT)
                || (b[0] == 169 && b[1] == 254)                // link-local, including cloud metadata
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168));
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6
            && !address.Equals(IPAddress.IPv6Any)
            && !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal
            && !address.IsIPv6UniqueLocal && !address.IsIPv6Multicast;   // unique local includes fd00:ec2::254
    }

    /// <summary>Exposes this tool's method as an AI tool for an agent.</summary>
    public IList<AITool> AsTools() => [AIFunctionFactory.Create(WebFetch)];
}
