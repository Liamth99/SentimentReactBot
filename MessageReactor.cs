using System.Threading.Channels;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace SentimentReactBot;

public class MessageReactor : IDisposable
{
    private readonly OllamaApiClient _ollamaClient;
    private readonly Chat            _chat;
    private readonly DiscordClient   _client;

    private readonly Configuration _config;

    private readonly Channel<MessageCreateEventArgs> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processor;

    private DateTime _lastRecap = DateTime.MinValue;

    private MessageReactor(Configuration config, Chat chat, DiscordClient client, OllamaApiClient ollamaClient)
    {
        _chat         = chat;
        _client       = client;
        _ollamaClient = ollamaClient;
        _config       = config;

        _channel = Channel.CreateBounded<MessageCreateEventArgs>(
            new BoundedChannelOptions(10)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = true,
            });

        _processor = Task.Run(ProcessMessagesAsync);
    }

    public static async Task<MessageReactor> GetReactorAsync(DiscordClient client, Configuration config, string contextMessage)
    {
        var uri     = new Uri(config.OllamaUri);
        var ollama  = new OllamaApiClient(uri);
        var chat    = new Chat(ollama);

        ollama.SelectedModel = config.Model;

        await foreach (var status in ollama.PullModelAsync(ollama.SelectedModel))
        {
            if(status is not null)
                client.Logger.Log(LogLevel.Information, new EventId(900, "Ollama"), "{StatusPercent:P} {StatusStatus}", status.Percent / 100, status.Status);
        }

        string response = string.Empty;

        await foreach (var s in chat.SendAsync(contextMessage))
            response += s;

        client.Logger.LogInformation(new EventId(900, "Ollama"), response);
        var reactor = new MessageReactor(config, chat, client, ollama);

        return reactor;
    }

    private async Task ProcessMessagesAsync()
    {
        await foreach (var args in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                _client.Logger.LogInformation(new EventId(901, "React"), "Processing \"{Content}\" from {User}", args.Message.Content, args.Author.Username);

                string response = string.Empty;

                await foreach (var s in _chat.SendAsync($"{args.Author.Username} sent: {args.Message.Content}"))
                    response += s;

                _client.Logger.LogDebug(new EventId(900, "Ollama"), response);

                response = response.Trim();

                if (response.Contains("skip", StringComparison.OrdinalIgnoreCase))
                {
                    _client.Logger.LogInformation(new EventId(900, "React"), "Skipping");
                    continue;
                }

                if (!response.StartsWith(':'))
                    response = ":" + response;

                if (!response.EndsWith(':'))
                    response += ":";

                _client.Logger.LogInformation(new EventId(901, "React"), "Responding with {0} to {1}'s message.", response, args.Author.Username);

                await args.Message.CreateReactionAsync(DiscordEmoji.FromName(_client, response));
            }
            catch (Exception ex)
            {
                _client.Logger.LogError(new EventId(901, "React"), ex, "Error processing message");
            }
        }
    }

    public async Task ReactToMessageAsync(DiscordClient _, MessageCreateEventArgs args)
    {
        if(args.Author.IsBot)
            return;

        if (string.IsNullOrWhiteSpace(args.Message.Content))
        {
            _client.Logger.LogWarning("Message was empty, check bot intents are set up correctly.");
            return;
        }

        if (args.Message.Content.Trim().Equals("recap", StringComparison.OrdinalIgnoreCase) && args.Message.ReferencedMessage is not null)
        {
            await RecapMessagesAsync(args.Channel, args.Message, args.Message.ReferencedMessage);
            return;
        }

        await _channel.Writer.WriteAsync(args);
    }

    public async Task RecapMessagesAsync(DiscordChannel channel, DiscordMessage commandMessage, DiscordMessage message)
    {
        if (DateTime.Now - _lastRecap < TimeSpan.FromMinutes(5))
        {
            await commandMessage.CreateReactionAsync(DiscordEmoji.FromName(_client, ":sleeping:"));
            return;
        }

        _lastRecap = DateTime.Now;

        var messages = await channel.GetMessagesAfterAsync(message.Id);

        var responseMessage = await channel.SendMessageAsync($"Generating recap of {messages.Count} messages{(messages.Count == 100 ? "(the max amount)" : "")}. (this could take a minute or two)");

        string response = string.Empty;

        await foreach (var stream in _ollamaClient.GenerateAsync($"Tone:{_config.Personality}\n\nPlease give a brief one paragraph recap of these messages. Your message must not exceed 2000 characters.\n\n{string.Join("\n\n", messages.Select(x => $"{x.Author.Username}:\n{x.Content}"))}"))
        {
            if(stream is not null)
                response += stream.Response;
        }

        if(string.IsNullOrWhiteSpace(response))
            await responseMessage.ModifyAsync("Recap failed to generate.");

        else if (response.Length > 2_000)
            await responseMessage.ModifyAsync(response.Substring(0, 1980) + "...\n\nCut Due Length"); // Figure something is better than nothing

        else
            await responseMessage.ModifyAsync(response);
    }

    // This prevents users from saying one thing to bait a response, then editing their message.
    public Task RemoveReactions(DiscordClient client, MessageUpdateEventArgs args)
    {
        var reactions = args.Message.Reactions.Where(x => x.IsMe).ToArray();

        foreach (DiscordReaction reaction in reactions)
        {
            _ = args.Message.DeleteReactionAsync(reaction.Emoji, client.CurrentUser, reason: "User edited message to trick me, but i saw it coming.");
        }

        if(reactions.Length > 0)
            client.Logger.LogInformation("User {0} edited their message, clearing reactions.", args.Message.Author.Username);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();

        try { _processor.Wait(); }
        catch { /* ignored */ }

        if (_chat.Client is OllamaApiClient client)
            client.Dispose();
    }
}