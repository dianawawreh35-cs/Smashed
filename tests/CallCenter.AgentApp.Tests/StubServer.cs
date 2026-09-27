using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using CallCenter.AgentApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The server, as far as <see cref="ApiClient"/> can tell: each request is
/// answered by a function of the test's choosing, and every request is kept.
/// </summary>
public sealed class StubServer : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

    public StubServer(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;

    /// <summary>Method and path of every request, in order.</summary>
    public List<string> Requests { get; } = [];

    public ApiClient Client(AgentSession session) =>
        new(new HttpClient(this) { BaseAddress = new Uri("http://server.test/") },
            session, NullLogger<ApiClient>.Instance);

    public static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body) };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    /// <summary>An RFC 7807 refusal carrying the server's <c>code</c>.</summary>
    public static HttpResponseMessage Problem(HttpStatusCode status, string? code) =>
        Json(status, new { title = "refused", code });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
        }

        return Task.FromResult(_answer(request));
    }
}
