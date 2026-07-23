using System.Text.Json;
using System.Text.Json.Serialization;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Controllers;

/// <summary>
/// TTS (Text-to-Speech) controller that proxies requests to the configured TTS API.
/// </summary>
[Route("tts")]
public class TtsController(TtsService ttsService, IOptions<TtsSettings> options, ILogger<TtsController> logger) : Controller
{
    private readonly TtsSettings _settings = options.Value;

    /// <summary>
    /// JSON serializer options for camelCase output to the frontend.
    /// (Uses System.Text.Json instead of the globally-configured Newtonsoft.Json
    /// with DefaultContractResolver, which would emit PascalCase property names.)
    /// </summary>
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Get the list of available voices from the TTS API.
    /// </summary>
    [HttpGet("voices")]
    public async Task<IActionResult> Voices()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiToken))
        {
            logger.LogWarning("TTS ApiToken is not configured — returning empty voice list");
            return Content("[]", "application/json", System.Text.Encoding.UTF8);
        }

        try
        {
            var voices = await ttsService.GetVoicesAsync();
            logger.LogInformation("Returning {Count} voices to client", voices.Count);

            var result = voices.Select(v => new { v.Name, v.Filename, v.SizeKb });
            var json = JsonSerializer.Serialize(result, CamelCaseJsonOptions);
            return Content(json, "application/json", System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch voices from TTS API: {Message}", ex.Message);
            return StatusCode(502, new { error = $"Failed to fetch voices from TTS service: {ex.Message}" });
        }
    }

    /// <summary>
    /// Generate speech audio from text. Accepts form-encoded parameters.
    /// </summary>
    [HttpPost("speech")]
    public async Task<IActionResult> Speech([FromForm] string input, [FromForm] string? voice, [FromForm] string? response_format, [FromForm] float? speed)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return BadRequest(new { error = "The 'input' field is required." });
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiToken))
        {
            logger.LogWarning("TTS ApiToken is not configured — speech request rejected");
            return StatusCode(500, new { error = "TTS service is not configured. Please set TtsSettings:ApiToken in configuration." });
        }

        try
        {
            var (stream, contentType) = await ttsService.GenerateSpeechAsync(input, voice, response_format, speed);
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate TTS speech: {Message}", ex.Message);
            return StatusCode(502, new { error = $"Failed to generate speech from TTS service: {ex.Message}" });
        }
    }
}
