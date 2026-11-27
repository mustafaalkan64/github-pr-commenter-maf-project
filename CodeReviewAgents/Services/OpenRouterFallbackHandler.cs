using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace CodeReviewAgents.Services;

/// <summary>
/// Automatically falls back to alternative OpenRouter models with jittered backoff
/// when a free upstream provider returns HTTP 429 (rate limit) or 5xx/404 errors.
/// Respects per-request model overrides (Model Tiering) and never downgrades paid
/// or non-OpenRouter provider models to free fallbacks.
/// </summary>
public sealed class OpenRouterFallbackHandler : DelegatingHandler
{
    private static readonly string[] DefaultFreeFallbacks =
    [
        "google/gemma-4-26b-a4b-it:free",
        "nvidia/nemotron-3.5-lightning:free",
        "google/gemma-4-31b-it:free",
        "inclusionai/ling-3.2-flash:free",
        "baidu/ernie-5.5-flash-preview:free",
        "openrouter/free"
    ];

    private readonly bool _isOpenRouterEndpoint;
    private readonly IReadOnlyList<string> _configuredFallbacks;

    public OpenRouterFallbackHandler(
        string baseUrl,
        IEnumerable<string>? configuredFallbacks = null,
        HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new SocketsHttpHandler())
    {
        _isOpenRouterEndpoint = baseUrl.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);
        _configuredFallbacks = configuredFallbacks?
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.Content is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var originalBody = await request.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/json";

        // Only rewrite chat/completions JSON payloads that contain a "model" property.
        JsonObject? parsedRoot = null;
        try
        {
            parsedRoot = JsonNode.Parse(originalBody) as JsonObject;
        }
        catch
        {
            // Non-JSON payload; pass through untouched.
        }

        var requestedModel = parsedRoot?["model"]?.GetValue<string>();
        if (parsedRoot is null || string.IsNullOrWhiteSpace(requestedModel))
        {
            using var passthrough = CloneRequest(request, originalBody, mediaType);
            return await base.SendAsync(passthrough, cancellationToken);
        }

        var candidateModels = BuildCandidateList(requestedModel);
        if (candidateModels.Count <= 1)
        {
            using var passthrough = CloneRequest(request, originalBody, mediaType);
            return await base.SendAsync(passthrough, cancellationToken);
        }

        for (var i = 0; i < candidateModels.Count; i++)
        {
            var candidateModel = candidateModels[i];
            parsedRoot["model"] = candidateModel;
            var payload = parsedRoot.ToJsonString();

            using var attemptRequest = CloneRequest(request, payload, mediaType);
            var response = await base.SendAsync(attemptRequest, cancellationToken);

            if (!ShouldFallback(response.StatusCode) || i == candidateModels.Count - 1)
            {
                return response;
            }

            var nextModel = candidateModels[i + 1];
            var statusCode = (int)response.StatusCode;
            var delayMs = 1200 + Random.Shared.Next(300, 1500) + (i * 500);

            Console.WriteLine(
                $"[OpenRouter] '{candidateModel}' HTTP {statusCode} döndü. {delayMs}ms sonra yedek modele geçiliyor: '{nextModel}'...");

            response.Dispose();
            await Task.Delay(delayMs, cancellationToken);
        }

        throw new InvalidOperationException("OpenRouter yedek model listesi boş.");
    }

    private static bool ShouldFallback(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.NotFound
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original,
        string jsonBody,
        string mediaType)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
            Content = new StringContent(jsonBody, Encoding.UTF8, mediaType)
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private IReadOnlyList<string> BuildCandidateList(string requestedModel)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? model)
        {
            if (!string.IsNullOrWhiteSpace(model) && seen.Add(model.Trim()))
            {
                list.Add(model.Trim());
            }
        }

        TryAdd(requestedModel);

        foreach (var fallback in _configuredFallbacks)
        {
            TryAdd(fallback);
        }

        var isFreeOpenRouterModel = _isOpenRouterEndpoint
            && (requestedModel.EndsWith(":free", StringComparison.OrdinalIgnoreCase)
                || requestedModel.Equals("openrouter/free", StringComparison.OrdinalIgnoreCase));

        if (isFreeOpenRouterModel)
        {
            foreach (var fallback in DefaultFreeFallbacks)
            {
                TryAdd(fallback);
            }
        }

        return list;
    }
}
