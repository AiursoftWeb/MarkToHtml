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
    /// Get the list of available voices from the TTS API.
    /// </summary>
    [HttpGet("voices")]
    public async Task<IActionResult> Voices()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiToken))
        {
            logger.LogWarning("TTS ApiToken is not configured");
            return Json(Array.Empty<object>());
        }

        try
        {
            var voices = await ttsService.GetVoicesAsync();
            return Json(voices.Select(v => new { v.Name, v.Filename, v.SizeKb }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch voices from TTS API");
            return StatusCode(502, new { error = "Failed to fetch voices from TTS service" });
        }
    }

    /// <summary>
    /// Generate speech audio from text. Accepts form-encoded or JSON body.
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
            logger.LogWarning("TTS ApiToken is not configured");
            return StatusCode(500, new { error = "TTS service is not configured. Please set TtsSettings:ApiToken in configuration." });
        }

        try
        {
            var (stream, contentType) = await ttsService.GenerateSpeechAsync(input, voice, response_format, speed);
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate TTS speech");
            return StatusCode(502, new { error = "Failed to generate speech from TTS service" });
        }
    }
}
