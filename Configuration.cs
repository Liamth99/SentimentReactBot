namespace SentimentReactBot;

public class Configuration
{
    public required string   DiscordToken { get; init; }
    public required string   Model        { get; init; }
    public required string   OllamaUri        { get; init; }
    public          string?  Personality  { get; init; }
    public required string[] Emojis       { get; init; }
}