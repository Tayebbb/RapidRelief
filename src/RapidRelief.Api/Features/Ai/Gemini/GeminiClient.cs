using System.Text;

namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>
/// Real Gemini transport (F8 chunk 2): named HttpClient "gemini" (BaseAddress pinned,
/// Timeout = Infinite), POST v1beta/models/{model}:generateContent with the API key as an
/// x-goog-api-key header (never in the URL, never logged). D-026 timeouts via per-request
/// linked CTS; zero retries — a 429 is just a breaker-counted failure. Any timeout/network/
/// non-2xx failure surfaces as <see cref="GeminiUnavailableException"/> with metadata only
/// (status code, no response body, no key).
/// </summary>
internal sealed class GeminiClient : IGeminiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public GeminiClient(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task<string> GenerateContentAsync(string requestBody, bool isVision, CancellationToken ct = default)
    {
        var apiKey = _config["Ai:Gemini:ApiKey"] ?? string.Empty;
        var model = _config["Ai:Gemini:Model"] ?? "gemini-3.7-flash";
        var timeoutSeconds = isVision
            ? _config.GetValue("Ai:Gemini:TimeoutSecondsVision", 20)
            : _config.GetValue("Ai:Gemini:TimeoutSecondsText", 10);

        var client = _httpClientFactory.CreateClient("gemini");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{model}:generateContent")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation is not a Gemini failure
        }
        catch (OperationCanceledException)
        {
            throw new GeminiUnavailableException(
                $"Gemini {(isVision ? "vision" : "text")} request timed out after {timeoutSeconds} s");
        }
        catch (HttpRequestException ex)
        {
            // Metadata-only message; the original (host-level detail, no headers) rides as inner.
            throw new GeminiUnavailableException($"Gemini request failed: {ex.GetType().Name}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new GeminiUnavailableException($"Gemini returned HTTP {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new GeminiUnavailableException(
                    $"Gemini response read timed out after {timeoutSeconds} s");
            }
        }
    }
}
