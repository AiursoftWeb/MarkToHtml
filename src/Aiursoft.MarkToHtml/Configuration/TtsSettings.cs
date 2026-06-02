namespace Aiursoft.MarkToHtml.Configuration;

public class TtsSettings
{
    /// <summary>
    /// Base URL of the TTS API service. Defaults to https://tts.aiursoft.com/.
    /// </summary>
    public string BaseUrl { get; init; } = "https://tts.aiursoft.com/";

    /// <summary>
    /// Bearer token for authenticating with the TTS API.
    /// </summary>
    public string ApiToken { get; init; } = string.Empty;

    /// <summary>
    /// Default voice name to use for speech synthesis (e.g. "reporter-zh").
    /// </summary>
    public string DefaultVoice { get; init; } = "reporter-zh";

    /// <summary>
    /// Default audio output format: mp3, opus, aac, flac, wav, pcm.
    /// </summary>
    public string DefaultFormat { get; init; } = "mp3";

    /// <summary>
    /// Default speech speed (0.25 – 4.0).
    /// </summary>
    public float DefaultSpeed { get; init; } = 1.0f;
}
