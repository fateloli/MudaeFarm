using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MudaeFarm
{
    static class Program
    {
        public static ulong[] MudaeIds = new ulong[]
        {
            522749851922989068
        };

        static ILogger _logger;
        static DiscordSocketClient _discord;

        static async Task Main(string[] args)
        {
            // Configure services
            var services = configureServices(new ServiceCollection()).BuildServiceProvider();

            using (services.CreateScope())
            {
                _logger = services.GetService<ILogger<Program>>();
                _discord = services.GetService<DiscordSocketClient>();

                // Load configuration
                await LoadConfigAsync();

                // Register events
                _discord.Log += handleLogAsync;
                _discord.MessageReceived += handleMessageAsync;

                // Login
                var connectionSource = new TaskCompletionSource<object>();

                _discord.Connected += handleConnect;
                Task handleConnect()
                {
                    connectionSource.SetResult(null);
                    return Task.CompletedTask;
                }

                await _discord.LoginAsync(TokenType.User, _config.AuthToken);
                await _discord.StartAsync();

                await connectionSource.Task;

                _discord.Connected -= handleConnect;

                // Keep the bot running
                // TODO: graceful shutdown?
                while (true)
                {
                    if (_config.AutoRollInterval.HasValue)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(_config.AutoRollInterval.Value));

                        await sendRollAsync();
                    }
                    else
                        await Task.Delay(1000);
                }

                // Unregister events
                _discord.Log -= handleLogAsync;
                _discord.MessageReceived -= handleMessageAsync;

                // Logout
                await _discord.StopAsync();
                await _discord.LogoutAsync();
            }
        }

        static async Task sendRollAsync()
        {
            foreach (var channelId in _config.BotChannels)
            {
                var channel = _discord.GetChannel(channelId) as ITextChannel;

                await channel.SendMessageAsync("$mu");
            }
        }

        static IServiceCollection configureServices(IServiceCollection services) => services
            .AddSingleton<DiscordSocketClient>()
            .AddLogging(l => l.AddConsole());

        static Task handleLogAsync(LogMessage m)
        {
            var level = LogLevel.None;

            switch (m.Severity)
            {
                case LogSeverity.Verbose: level = LogLevel.Trace; break;
                case LogSeverity.Debug: level = LogLevel.Debug; break;
                case LogSeverity.Info: level = LogLevel.Information; break;
                case LogSeverity.Warning: level = LogLevel.Warning; break;
                case LogSeverity.Error: level = LogLevel.Error; break;
                case LogSeverity.Critical: level = LogLevel.Critical; break;
            }

            if (m.Exception == null)
                _logger.Log(level, m.Message);
            else
                _logger.Log(level, m.Exception, m.Exception.Message);

            return Task.CompletedTask;
        }

        static async Task handleMessageAsync(SocketMessage message)
        {
            if (!(message is SocketUserMessage userMessage))
                return;

            var author = message.Author.Id;

            if (author == _discord.CurrentUser.Id)
                await handleSelfCommandAsync(userMessage);

            else if (Array.IndexOf(MudaeIds, author) != -1)
                await handleMudaeMessageAsync(userMessage);
        }

        static async Task handleSelfCommandAsync(SocketUserMessage message)
        {
            var content = message.Content;

            if (!content.StartsWith('/'))
                return;

            content = content.Substring(1);

            var delimitor = content.IndexOf(' ');
            var command = delimitor == -1 ? content : content.Substring(0, delimitor);
            var argument = delimitor == -1 ? null : content.Substring(delimitor + 1);

            if (string.IsNullOrWhiteSpace(command))
                return;

            switch (command)
            {
                case "wishlist":
                    await message.ModifyAsync(m =>
                    {
                        m.Content = $"Character wishlist: {string.Join(", ", _config.WishlistCharacters)}";
                    });
                    return;
                case "wishlistani":
                    await message.ModifyAsync(m =>
                    {
                        m.Content = $"Anime wishlist: {string.Join(", ", _config.WishlistAnimes)}";
                    });
                    return;
                case "setchannel":
                    if (_config.BotChannels.Add(message.Channel.Id))
                        _logger.LogInformation($"Added bot channel '{message.Channel.Id}'.");
                    break;
                case "unsetchannel":
                    if (_config.BotChannels.Remove(message.Channel.Id))
                        _logger.LogInformation($"Removed bot channel '{message.Channel.Id}'.");
                    break;
            }

            if (string.IsNullOrWhiteSpace(argument))
                return;

            switch (command.ToLowerInvariant())
            {
                case "wish":
                    _config.WishlistCharacters.Add(argument.ToLowerInvariant());
                    _logger.LogInformation($"Added character '{argument}' to the wishlist.");
                    break;
                case "unwish":
                    _config.WishlistCharacters.Remove(argument.ToLowerInvariant());
                    _logger.LogInformation($"Removed character '{argument}' from the wishlist.");
                    break;
                case "wishani":
                    _config.WishlistAnimes.Add(argument.ToLowerInvariant());
                    _logger.LogInformation($"Added anime '{argument}' to the wishlist.");
                    break;
                case "unwishani":
                    _config.WishlistAnimes.Remove(argument.ToLowerInvariant());
                    _logger.LogInformation($"Removed anime '{argument}' from the wishlist.");
                    break;
                case "rollinterval":
                    if (double.TryParse(argument, out var rollInterval))
                    {
                        _config.AutoRollInterval = rollInterval;
                        _logger.LogInformation($"Set autoroll interval to every '{rollInterval}' minutes.");
                    }
                    break;
                default:
                    return;
            }

            await message.DeleteAsync();
            await SaveConfigAsync();
        }

        static async Task handleMudaeMessageAsync(SocketUserMessage message)
        {
            if (!message.Embeds.Any())
                return;

            var embed = message.Embeds.First();

            if (embed.Footer.HasValue &&
                embed.Footer.Value.Text.StartsWith("Belongs to", StringComparison.OrdinalIgnoreCase))
                return;

            if (!embed.Author.HasValue ||
                embed.Author.Value.IconUrl != null)
                return;

            var name = embed.Author.Value.Name.Trim();
            var anime = embed.Description.Trim();

            if (_config.WishlistCharacters.Contains(name) ||
                _config.WishlistAnimes.Contains(name))
            {
                _logger.LogInformation($"Found character '{name}', trying marriage.");

                await message.AddReactionAsync(new Emoji("\uD83D\uDC96"));
                await SaveConfigAsync();
            }
            else
                _logger.LogInformation($"Ignored character '{name}', not wished.");
        }

        static Config _config;

        static async Task LoadConfigAsync()
        {
            try
            {
                _config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync("config.json"));
            }
            catch (FileNotFoundException)
            {
                _config = new Config();
            }
        }

        static async Task SaveConfigAsync()
        {
            await File.WriteAllTextAsync("config.json", JsonConvert.SerializeObject(_config));
        }
    }
}
