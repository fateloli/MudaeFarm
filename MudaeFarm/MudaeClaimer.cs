using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Disqord;
using Disqord.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MudaeFarm
{
    public interface IMudaeClaimer : IHostedService { }

    public class MudaeClaimer : BackgroundService, IMudaeClaimer
    {
        readonly IDiscordClientService _discord;
        readonly IMudaeUserFilter _userFilter;
        readonly IMudaeClaimCharacterFilter _characterFilter;
        readonly IMudaeClaimEmojiFilter _claimEmojiFilter;
        readonly IOptionsMonitor<ClaimingOptions> _options;
        readonly IOptionsMonitor<ClaimChannelList> _claimChannelList;
        readonly IMudaeCommandHandler _commandHandler;
        readonly IMudaeOutputParser _outputParser;
        readonly IMudaeReplySender _replySender;
        readonly INotificationSender _notification;
        readonly ILogger<MudaeClaimer> _logger;

        public MudaeClaimer(IDiscordClientService discord, IMudaeUserFilter userFilter, IMudaeClaimCharacterFilter characterFilter, IMudaeClaimEmojiFilter claimEmojiFilter, IOptionsMonitor<ClaimingOptions> options, IOptionsMonitor<ClaimChannelList> claimChannelList, IMudaeCommandHandler commandHandler, IMudaeOutputParser outputParser, IMudaeReplySender replySender, INotificationSender notification, ILogger<MudaeClaimer> logger)
        {
            _discord          = discord;
            _userFilter       = userFilter;
            _characterFilter  = characterFilter;
            _claimEmojiFilter = claimEmojiFilter;
            _options          = options;
            _claimChannelList = claimChannelList;
            _commandHandler   = commandHandler;
            _outputParser     = outputParser;
            _replySender      = replySender;
            _notification     = notification;
            _logger           = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var client = await _discord.GetClientAsync();

            Task handleReactionAdded(ReactionAddedEventArgs args)
            {
                // this needs to run in background because it waiting for Mudae output blocks message receive
                var _ = Task.Run(() => HandleReactionAdded(args), stoppingToken);
                return Task.CompletedTask;
            }

            client.MessageReceived += HandleMessageReceived;
            client.ReactionAdded   += handleReactionAdded;

            try
            {
                await Task.Delay(-1, stoppingToken);
            }
            finally
            {
                client.MessageReceived -= HandleMessageReceived;
                client.ReactionAdded   -= handleReactionAdded;
            }
        }

        readonly ConcurrentDictionary<ulong, ClaimState> _states = new ConcurrentDictionary<ulong, ClaimState>();

        sealed class ClaimState
        {
            public DateTime CooldownResetTime;
            public DateTime KakeraResetTime;
        }

#pragma warning disable 1998
        async Task HandleMessageReceived(MessageReceivedEventArgs e)
#pragma warning restore 1998
        {
            // purge old pending claims (avoids memory leak)
            foreach (var id in _pendingClaims.Keys)
            {
                if (_pendingClaims.TryGetValue(id, out var claim) && claim.CreatedTime.AddMinutes(1) < DateTime.Now)
                    _pendingClaims.TryRemove(id, out _);
            }

            foreach (var id in _pendingKakeraClaims.Keys)
            {
                if (_pendingKakeraClaims.TryGetValue(id, out var claim) && claim.CreatedTime.AddMinutes(1) < DateTime.Now)
                    _pendingKakeraClaims.TryRemove(id, out _);
            }

            var options = _options.CurrentValue;

            // enabled, message author is mudae, channel is configured, embed exists
            if (!options.Enabled || !(e.Message is IUserMessage message && _userFilter.IsMudae(message.Author)) || _claimChannelList.CurrentValue.Items.All(x => x.Id != message.ChannelId) || message.Embeds.Count == 0)
                return;

            var stopwatch = Stopwatch.StartNew();

            var claimChannel  = (IMessageChannel) e.Client.GetChannel(message.ChannelId);
            var guild    = claimChannel is IGuildChannel gc ? e.Client.GetGuild(gc.GuildId) : null;
            var logPlace = $"channel '{claimChannel.Name}' ({claimChannel.Id}, server '{guild?.Name}')";

            var embed       = message.Embeds[0];
            var description = embed.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var character   = new CharacterInfo(embed.Author.Name, description[0]);

            // ignore $im messages
            if (!options.BypassImCheck && description.Any(l => l.StartsWith("claims:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("likes:", StringComparison.OrdinalIgnoreCase)))
                return;

            _logger.LogDebug($"Detected character '{character}' in {logPlace}.");

            _pendingKakeraClaims[message.Id] = new PendingClaim(logPlace, claimChannel, message, character, stopwatch);

            // ignore already claimed
            if (embed.Footer?.Text.StartsWith("belongs", StringComparison.OrdinalIgnoreCase) == true)
                return;

            var wishedBy = message.Content.StartsWith("wished by", StringComparison.OrdinalIgnoreCase) ? message.GetUserIds().ToArray() : null;

            // must be wished or included in a user wishlist
            if (!_characterFilter.IsWished(character, wishedBy))
            {
                _logger.LogInformation($"Ignoring character '{character}' in {logPlace} because they are not wished.");
                return;
            }

            // check cooldown
            var state = _states.GetOrAdd(claimChannel.Id, new ClaimState());
            var now   = DateTime.Now;

            if (!options.IgnoreCooldown && now < state.CooldownResetTime)
            {
                _logger.LogWarning($"Ignoring character '{character}' in {logPlace} because of cooldown. Cooldown finishes in {state.CooldownResetTime - now}.");
                return;
            }

            _logger.LogWarning($"Attempting to claim character '{character}' in {logPlace}...");

            _pendingClaims[message.Id] = new PendingClaim(logPlace, claimChannel, message, character, stopwatch);
        }

        readonly struct PendingClaim
        {
            public readonly DateTime CreatedTime;

            public readonly string LogPlace;
            public readonly IMessageChannel ClaimChannel;
            public readonly IUserMessage Message;
            public readonly CharacterInfo Character;
            public readonly Stopwatch Stopwatch;

            public PendingClaim(string logPlace, IMessageChannel claimChannel, IUserMessage message, CharacterInfo character, Stopwatch stopwatch)
            {
                CreatedTime = DateTime.Now;

                LogPlace  = logPlace;
                ClaimChannel = claimChannel;
                Message   = message;
                Character = character;
                Stopwatch = stopwatch;
            }

            public void Deconstruct(out string logPlace, out IMessageChannel claimChannel, out IUserMessage message, out CharacterInfo character, out Stopwatch stopwatch)
            {
                logPlace  = LogPlace;
                claimChannel = ClaimChannel;
                message   = Message;
                character = Character;
                stopwatch = Stopwatch;
            }
        }

        readonly ConcurrentDictionary<ulong, PendingClaim> _pendingClaims = new ConcurrentDictionary<ulong, PendingClaim>();
        readonly ConcurrentDictionary<ulong, PendingClaim> _pendingKakeraClaims = new ConcurrentDictionary<ulong, PendingClaim>();

        async Task HandleReactionAdded(ReactionAddedEventArgs e)
        {
            var options = _options.CurrentValue;

            if (_claimEmojiFilter.IsClaimEmoji(e.Emoji) && _pendingClaims.TryRemove(e.Message.Id, out var claim))
            {
                var (logPlace, claimChannel, message, character, stopwatch) = claim;

                await Task.Delay(TimeSpan.FromSeconds(options.DelaySeconds));

                var replySubs = new
                {
                    character      = character.DisplayName.Split(' ', 2)[0].ToLowerInvariant(),
                    character_full = character.DisplayName.ToLowerInvariant(),
                    Character      = character.DisplayName.Split(' ', 2)[0],
                    Character_full = character.DisplayName,

                    anime = character.DisplayAnime.ToLowerInvariant(),
                    Anime = character.DisplayAnime
                };

                await _replySender.SendAsync(claimChannel, ReplyEvent.BeforeClaim, replySubs);

                IUserMessage response;

                try
                {
                    response = await _commandHandler.ReactAsync(message, e.Emoji);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Could not claim character '{character}' in {logPlace}.");
                    return;
                }

                if (_outputParser.TryParseClaimSucceeded(response.Content, out var claimer, out _) && claimer.Equals(e.Client.CurrentUser.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"Claimed character '{character}' in {logPlace} in {stopwatch.Elapsed.TotalMilliseconds}ms.");

                    if (options.NotifyOnCharacter)
                        _notification.SendToast($"Claimed character '{character}' in {logPlace}.");

                    await _replySender.SendAsync(claimChannel, ReplyEvent.ClaimSucceeded, replySubs);

                    return;
                }

                if (_outputParser.TryParseClaimFailed(response.Content, out var resetTime))
                {
                    _states.GetOrAdd(message.ChannelId, new ClaimState()).CooldownResetTime = DateTime.Now + resetTime;

                    _logger.LogWarning($"Could not claim character '{character}' in {logPlace} due to cooldown. Cooldown finishes in {resetTime}.");

                    await _replySender.SendAsync(claimChannel, ReplyEvent.ClaimFailed, replySubs);
                    return;
                }

                _logger.LogWarning($"Probably claimed character '{character}' in {logPlace}, but result could not be determined. Channel is probably busy.");

                if (options.NotifyOnCharacter)
                    _notification.SendToast($"Probably claimed character '{character}' in {logPlace}, but result could not be determined.");
            }

            else if (_claimEmojiFilter.IsKakeraEmoji(e.Emoji, out var kakera) && _pendingKakeraClaims.TryRemove(e.Message.Id, out claim))
            {
                var (logPlace, claimChannel, message, character, stopwatch) = claim;

                // check cooldown here (to allow skipping cooldown check for purple kakera)
                var state = _states.GetOrAdd(claimChannel.Id, new ClaimState());
                var now   = DateTime.Now;

                if (!options.KakeraIgnoreCooldown && now < state.KakeraResetTime && kakera != KakeraType.Purple)
                    return;

                if (!options.KakeraTargets.Contains(kakera))
                {
                    _logger.LogInformation($"Ignoring {kakera} kakera on character '{character}' in {logPlace} because it is not targeted.");
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(options.KakeraDelaySeconds));

                var replySubs = new
                {
                    kakera = kakera.ToString().ToLowerInvariant(),
                    Kakera = kakera.ToString()
                };

                await _replySender.SendAsync(claimChannel, ReplyEvent.BeforeKakera, replySubs);

                IUserMessage response;

                try
                {
                    response = await _commandHandler.ReactAsync(message, e.Emoji);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Could not claim {kakera} kakera on character '{character}' in {logPlace}.");
                    return;
                }

                if (_outputParser.TryParseKakeraSucceeded(response.Content, out var claimer, out _) && claimer.Equals(e.Client.CurrentUser.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"Claimed {kakera} kakera on character '{character}' in {logPlace} in {stopwatch.Elapsed.TotalMilliseconds}ms.");

                    if (options.NotifyOnKakera)
                        _notification.SendToast($"Claimed {kakera} kakera in {logPlace}.");

                    await _replySender.SendAsync(claimChannel, ReplyEvent.KakeraSucceeded, replySubs);
                    return;
                }

                if (_outputParser.TryParseKakeraFailed(response.Content, out var resetTime))
                {
                    state.KakeraResetTime = now + resetTime;

                    _logger.LogWarning($"Could not claim {kakera} kakera on character '{character}' in {logPlace} due to cooldown. Kakera is reset in {resetTime}.");

                    await _replySender.SendAsync(claimChannel, ReplyEvent.KakeraFailed, replySubs);
                    return;
                }

                _logger.LogWarning($"Probably claimed {kakera} kakera on character '{character}' in {logPlace}, but result could not be determined. Channel is probably busy.");

                if (options.NotifyOnKakera)
                    _notification.SendToast($"Probably claimed {kakera} kakera in {logPlace}.");
            }
        }
    }
}