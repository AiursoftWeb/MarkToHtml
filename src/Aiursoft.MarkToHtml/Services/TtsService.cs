using System.Text;
using System.Text.Json;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Services;

public record VoiceItem(string Name, string Filename, double SizeKb);

public class TtsService(
    IHttpClientFactory httpClientFactory,
    IOptions<TtsSettings> options,
    ILogger<TtsService> logger) : ITransientDependency
{
    private readonly TtsSettings _settings = options.Value;

    /// <summary>
    /// JSON serializer options configured for snake_case naming, matching the TTS API convention.
    /// </summary>
    private static readonly JsonSerializerOptions TtsApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Get the list of available voices from the TTS API.
    /// </summary>
    public async Task<List<VoiceItem>> GetVoicesAsync()
    {
        var client = CreateTtsClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.BaseUrl.TrimEnd('/')}/api/voices");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiToken);

        logger.LogInformation("Fetching voices from TTS API: {Url}", request.RequestUri);

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("TTS voices API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"TTS voices API returned {(int)response.StatusCode}: {body}");
        }

        var voices = await response.Content.ReadFromJsonAsync<List<VoiceItem>>(TtsApiJsonOptions);
        logger.LogInformation("Fetched {Count} voices from TTS API", voices?.Count ?? 0);
        return voices ?? [];
    }

    /// <summary>
    /// Generate speech audio from text using the TTS API.
    /// Returns a stream of the audio content.
    /// </summary>
    public async Task<(Stream Stream, string ContentType)> GenerateSpeechAsync(
        string input,
        string? voice = null,
        string? format = null,
        float? speed = null)
    {
        var resolvedVoice = voice ?? _settings.DefaultVoice;
        var resolvedFormat = format ?? _settings.DefaultFormat;
        var resolvedSpeed = speed ?? _settings.DefaultSpeed;

        var requestBody = new
        {
            model = "tts-1",
            input,
            voice = resolvedVoice,
            response_format = resolvedFormat,
            speed = resolvedSpeed
        };

        var json = JsonSerializer.Serialize(requestBody, TtsApiJsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = CreateTtsClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/v1/audio/speech")
        {
            Content = content
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiToken);

        logger.LogInformation("Generating TTS speech: {Length} chars, voice={Voice}, format={Format}",
            input.Length, resolvedVoice, resolvedFormat);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            logger.LogError("TTS speech API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"TTS speech API returned {(int)response.StatusCode}: {body}");
        }

        var stream = await response.Content.ReadAsStreamAsync();
        var contentType = ResolveContentType(resolvedFormat);
        return (stream, contentType);
    }

    /// <summary>
    /// Creates an HttpClient configured for TTS API communication with a 30-second timeout.
    /// </summary>
    private HttpClient CreateTtsClient()
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string ResolveContentType(string format) => format.ToLowerInvariant() switch
    {
        "mp3" => "audio/mpeg",
        "opus" => "audio/opus",
        "aac" => "audio/aac",
        "flac" => "audio/flac",
        "wav" => "audio/wav",
        "pcm" => "audio/l16",
        _ => "audio/mpeg"
    };
}
