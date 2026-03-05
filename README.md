# Sentiment React Bot

A generic Discord bot that reacts to messages using sentiment analysis powered by local LLMs via Ollama.

The bot analyzes the emotional tone of messages and reacts with exactly one emoji when the sentiment is clear. If the sentiment is neutral or unclear, the bot skips reacting to avoid spam.

**This bot is not a serious project and is only designed to be ran for short periods at a time.**

## Message Edit Protection

If a user edits their message after the bot reacts, the bot removes its reaction. This prevents users from baiting reactions and editing messages later.

## Recap Command

Reply to a message with:

`recap`

The bot will:
- Collect up to 100 messages after the referenced message
- Generate a short AI summary
- Post it in the channel

## Message Queue & Backpressure

To prevent the bot from overwhelming the LLM or blocking Discord event handlers, incoming messages are processed through a bounded asynchronous channel.

Behavior:
- The queue has a capacity of 10 messages.
- A single background task processes messages sequentially.
- Discord events can enqueue messages concurrently.
- If the queue becomes full, the oldest pending message is discarded to make room for the newest one.

# Requirements
- .NET 10 SDK
- A Discord bot token
- Ollama installed and running locally

## Configuration

Before running, create an appsettings.json file in the same directory as the bot with the following settings.

```json
{
  "DiscordToken": "YOUR_DISCORD_BOT_TOKEN",
  "Model": "llama3",
  "OllamaUri" : "http://localhost:11434",
  "Personality": "You are playful but respectful.",
  "Emojis": [
    ":smile:",
    ":sob:",
    ":angry:",
    ":thumbsup:",
    ":thinking:"
  ]
}
```

Example appsettings.json:

## Installing Ollama

Install Ollama:

https://ollama.com

Start the Ollama server:

```bash
ollama serve
```

## Running the bot
After configuring everything, open the directory in the terminal and run:

```bash
dotnet run -c Release
```