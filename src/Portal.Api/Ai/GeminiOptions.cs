namespace Portal.Api.Ai;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string Provider { get; set; } = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3.8-flash";
    public string PromptVersion { get; set; } = "v1";
}
