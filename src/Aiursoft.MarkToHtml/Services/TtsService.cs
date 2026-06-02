using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Services;

public record VoiceItem(string Name, string Filename, long SizeKb);

public class TtsService(
    IHttpClientFactory httpClientFactory,
    IOptions<TtsSettings> options,
    ILogger<TtsService> logger) : ITransientDependency
{
    private readonly TtsSettings _settings = options.Value;

    /// <summary>
    /// Get the list of available voices from the TTS API.
    /// </summary>
    public async Task<List<VoiceItem>> GetVoicesAsync()
    {
        var client = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.BaseUrl.TrimEnd('/')}/api/voices");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiToken);

        logger.LogInformation("Fetching voices from TTS API: {Url}", request.RequestUri);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var voices = await response.Content.ReadFromJsonAsync<List<VoiceItem>>();
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

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/v1/audio/speech")
        {
            Content = content
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiToken);

        logger.LogInformation("Generating TTS speech: {Length} chars, voice={Voice}, format={Format}",
            input.Length, resolvedVoice, resolvedFormat);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        var contentType = ResolveContentType(resolvedFormat);
        return (stream, contentType);
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
