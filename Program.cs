using DSharpPlus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SentimentReactBot;


var config = new ConfigurationBuilder().AddJsonFile("./appsettings.json").Build().Get<Configuration>();

if (config is null)
    throw new ArgumentException("Unable to bind configuration.");

if(string.IsNullOrWhiteSpace(config.DiscordToken))
    throw new ArgumentException("Discord token required.", nameof(config.DiscordToken));

if(string.IsNullOrWhiteSpace(config.Model))
    throw new ArgumentException("Model to run in Ollama is required", nameof(config.Model));

if(string.IsNullOrWhiteSpace(config.OllamaUri))
    throw new ArgumentException("Uri to ollama instance is required, default for a local instance is 'http://localhost:11434'", nameof(config.OllamaUri));

if(config.Emojis.Length is 0)
    throw new ArgumentException("At least one emoji is required.", nameof(config.Emojis));

DiscordConfiguration discordConfiguration = new()
{
    Token   = config.DiscordToken,
    Intents = DiscordIntents.DirectMessages | DiscordIntents.MessageContents | DiscordIntents.GuildMessages | DiscordIntents.GuildEmojis | DiscordIntents.DirectMessageReactions | DiscordIntents.GuildMessageReactions,
#if DEBUG
    MinimumLogLevel = LogLevel.Debug
#endif
};

DiscordClient client = new (discordConfiguration);

await client.ConnectAsync();

string message =
    $"""
    You are a sentiment analysis AI embedded inside a Discord bot named {client.CurrentUser.Username}.

    {config.Personality}

    Your ONLY job is to analyze the emotional tone of the user's message and respond with EXACTLY ONE emoji or the word skip. Do not send any emoji unless it the context of the message matches the tone to prevent spam.
    
    Default behavior: skip. Only react when the emotional tone is clear and strong.
    
    When to OUTPUT AN EMOJI:
    - The message clearly expresses strong emotion
    - The message strongly matches the meaning of one of the available emojis
    
    When to OUTPUT "skip":
    - Neutral conversation
    - Simple questions
    - Short replies (ok, yeah, nice, lol, etc.)
    - Informational or factual messages
    - Messages where sentiment is unclear
    - Messages where reacting would feel spammy
    - You are unsure of the context
    
    Rules:
    - Output EXACTLY ONE item
    - Either ONE emoji OR "skip"
    - Never output text
    - Never explain
    - Never output multiple emojis
    - Do not react just for misspelling or punctuation issues
    - Never invent emojis
    - If unsure, output "skip"

    Emojis Available to you:
    {string.Join("\n", config.Emojis.Select(x => $"- {x}"))}
    - skip (this skips the response because one isn't needed)

    Remember: ONE EMOJI ONLY. And not every message deserves a response.
    """;


using MessageReactor reactor = await MessageReactor.GetReactorAsync(client, config, message);

client.MessageCreated += reactor.ReactToMessageAsync;
client.MessageUpdated += reactor.RemoveReactions;

await Task.Delay(-1);
