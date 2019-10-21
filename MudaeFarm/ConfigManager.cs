using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MudaeFarm
{
    public class ConfigManager
    {
        readonly DiscordSocketClient _client;

        public ConfigManager(DiscordSocketClient client)
        {
            _client = client;
        }

        IGuild _guild;

        ITextChannel _generalConfigChannel;
        ITextChannel _wishedCharacterChannel;
        ITextChannel _wishedAnimeChannel;
        ITextChannel _botChannelChannel;
        ITextChannel _claimReplyChannel;
        ITextChannel _wishlistUsersChannel;

        public string StateUpdateCommand;

        public bool ClaimEnabled;
        public TimeSpan ClaimDelay;
        public List<string> ClaimReplies;
        public int KakeraThreshold;
        public TimeSpan KakeraClaimDelay;
        public HashSet<KakeraType> KakeraTargets;
        public bool ClaimCustomEmotes;
        public HashSet<ulong> ClaimWishlistUserIds;

        public bool RollEnabled;
        public string RollCommand;
        public bool DailyKakeraEnabled;
        public string DailyKakeraCommand;
        public bool DailyKakeraStateUpdate;
        public TimeSpan RollTypingDelay;
        public TimeSpan? RollIntervalOverride;
        public HashSet<ulong> BotChannelIds;

        public Regex WishedCharacterRegex;
        public Regex WishedAnimeRegex;

        public bool AutoUpdate;

        public async Task InitializeAsync()
        {
            var measure = new MeasureContext();

            // find config guild
            var userId = _client.CurrentUser.Id;

            foreach (var guild in _client.Guilds)
            {
                if (guild.OwnerId == userId &&
                    guild.TextChannels.Any(c => c.Name == "information" && c.Topic.StartsWith($"MudaeFarm Configuration Server {userId}")))
                {
                    _guild = guild;
                    break;
                }
            }

            if (_guild == null)
                try
                {
                    Log.Warning("Initializing a new configuration server. This may take a while...");

                    _guild = await _client.CreateGuildAsync("MudaeFarm", await _client.GetOptimalVoiceRegionAsync());

                    // delete default channels
                    foreach (var c in await _guild.GetChannelsAsync())
                        await c.DeleteAsync();

                    var channel = await CreateChannelAsync(null, "information", $"MudaeFarm Configuration Server {userId} - Do not delete this channel!");

                    var message = await channel.SendMessageAsync(
                        "This is your MudaeFarm server where you can configure the bot.\n" +
                        "\n" +
                        "Check <https://github.com/chiyadev/MudaeFarm> for detailed usage guidelines!");

                    await message.PinAsync();
                }
                catch (Exception e)
                {
                    Log.Warning("Could not initialize configuration server. Try creating it manually.", e);

                    throw new DummyRestartException();
                }

            // load channels
            foreach (var channel in await _guild.GetTextChannelsAsync())
            {
                void set(string name, ref ITextChannel c)
                {
                    if (channel.Name == name)
                        c = channel;
                }

                set("information", ref _generalConfigChannel);
                set("wished-characters", ref _wishedCharacterChannel);
                set("wished-anime", ref _wishedAnimeChannel);
                set("bot-channels", ref _botChannelChannel);
                set("claim-replies", ref _claimReplyChannel);
                set("wishlist-users", ref _wishlistUsersChannel);
            }

            // create channel if not created already
            _wishedCharacterChannel = await CreateChannelAsync(_wishedCharacterChannel, "wished-characters", "Configure your character wishlist here. Wildcards characters are supported. Names are *case-insensitive*.");
            _wishedAnimeChannel     = await CreateChannelAsync(_wishedAnimeChannel, "wished-anime", "Configure your anime wishlist here. Wildcards characters are supported. Names are *case-insensitive*.");
            _botChannelChannel      = await CreateChannelAsync(_botChannelChannel, "bot-channels", "Configure channels to enable MudaeFarm autorolling/claiming by sending the __channel ID__.");
            _claimReplyChannel      = await CreateChannelAsync(_claimReplyChannel, "claim-replies", "Configure automatic reply messages when you claim a character. One message is randomly selected. Refer to https://github.com/chiyadev/MudaeFarm for advanced templating.");
            _wishlistUsersChannel   = await CreateChannelAsync(_wishlistUsersChannel, "wishlist-users", "Configure wishlists of other users to be claimed by sending the __user ID__.");

            // initial load
            await ReloadChannelAsync(_generalConfigChannel);
            await ReloadChannelAsync(_wishedCharacterChannel);
            await ReloadChannelAsync(_wishedAnimeChannel);
            await ReloadChannelAsync(_botChannelChannel);
            await ReloadChannelAsync(_claimReplyChannel);
            await ReloadChannelAsync(_wishlistUsersChannel);

            Log.Info($"Configuration loaded in {measure}.");

            // import old configuration (config.json)
            var legacyCfg = LegacyConfig.Load();

            if (legacyCfg != null)
            {
                Log.Warning("Importing legacy wishlist configuration. This may take a while...");

                if (legacyCfg.WishlistCharacters != null)
                    foreach (var character in legacyCfg.WishlistCharacters)
                    {
                        await _wishedCharacterChannel.SendMessageAsync(character);
                        Log.Debug(character);
                    }

                if (legacyCfg.WishlistAnime != null)
                    foreach (var anime in legacyCfg.WishlistAnime)
                    {
                        await _wishedAnimeChannel.SendMessageAsync(anime);
                        Log.Debug(anime);
                    }

                LegacyConfig.Delete();
            }

            // events
            _client.MessageReceived += message => ReloadChannelAsync(message.Channel);
            _client.MessageDeleted  += (cacheable, channel) => ReloadChannelAsync(channel);
            _client.MessageUpdated  += (cacheable, message, channel) => ReloadChannelAsync(channel);
        }

        async Task<ITextChannel> CreateChannelAsync(ITextChannel channel, string name, string topic)
        {
            if (channel == null)
            {
                channel = await _guild.CreateTextChannelAsync(name);

                Log.Debug($"Channel created: '#{channel}' - '{topic ?? "<null>"}'");
            }

            if (channel.Topic != topic)
                await channel.ModifyAsync(c => c.Topic = topic);

            return channel;
        }

        async Task<List<IUserMessage>> LoadMessagesAsync(IMessageChannel channel)
        {
            var author = _client.CurrentUser.Id;

            var list = new List<IUserMessage>();

            await channel.GetMessagesAsync(int.MaxValue)
                         .ForEachAsync(messages =>
                          {
                              // ReSharper disable once LoopCanBeConvertedToQuery
                              foreach (var message in messages.Where(m => m.Author.Id == author).OfType<IUserMessage>())
                              {
                                  if (!string.IsNullOrWhiteSpace(message.Content))
                                      list.Add(message);
                              }
                          });

            // chronological
            list.Reverse();

            return list;
        }

        async Task ReloadChannelAsync(IMessageChannel channel)
        {
            var measure = new MeasureContext();

            if (channel.Id == _generalConfigChannel.Id)
            {
                var messages = await LoadMessagesAsync(channel);
                var dict     = new Dictionary<string, ConfigPart>();

                foreach (var message in messages)
                {
                    if (!message.Content.StartsWith("> "))
                        continue;

                    var lines = message.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var key   = lines[0].Substring(2).Trim().ToLowerInvariant();
                    var value = string.Join("\n", lines.Skip(1).Where(l => !l.StartsWith("```")));

                    if (dict.ContainsKey(key))
                    {
                        if (message.Reactions.Count == 0)
                            await message.AddReactionAsync(new Emoji("\u0032\u20E3"));

                        continue;
                    }

                    dict[key] = new ConfigPart(message, value);
                }

                // general
                var general = await LoadConfigPartAsync<GeneralConfig>(channel, "General", dict);

                await _client.SetStatusAsync(general.FallbackStatus);

                StateUpdateCommand = general.StateUpdateCommand;

                // claiming
                var claim = await LoadConfigPartAsync(channel, "Claiming", dict, ClaimConfig.CreateDefault);

                ClaimEnabled      = claim.Enabled;
                ClaimDelay        = TimeSpan.FromSeconds(claim.Delay);
                KakeraThreshold   = claim.KakeraThreshold;
                KakeraClaimDelay  = TimeSpan.FromSeconds(claim.KakeraDelay);
                KakeraTargets     = claim.KakeraTargets;
                ClaimCustomEmotes = claim.CustomEmotes;

                // rolling
                var roll = await LoadConfigPartAsync<RollConfig>(channel, "Rolling", dict);

                RollEnabled            = roll.Enabled;
                RollCommand            = roll.Command;
                DailyKakeraEnabled     = roll.KakeraEnabled;
                DailyKakeraCommand     = roll.KakeraCommand;
                DailyKakeraStateUpdate = roll.KakeraStateUpdate;
                RollTypingDelay        = TimeSpan.FromSeconds(roll.TypingDelay);
                RollIntervalOverride   = roll.IntervalOverrideMinutes == null ? null as TimeSpan? : TimeSpan.FromMinutes(roll.IntervalOverrideMinutes.Value);

                // miscellaneous
                var miscellaneous = await LoadConfigPartAsync<MiscellaneousConfig>(channel, "Miscellaneous", dict);

                AutoUpdate = miscellaneous.AutoUpdate;
            }

            else if (channel.Id == _wishedCharacterChannel.Id)
            {
                WishedCharacterRegex = CreateWishlistRegex(await LoadMessagesAsync(channel));
            }

            else if (channel.Id == _wishedAnimeChannel.Id)
            {
                WishedAnimeRegex = CreateWishlistRegex(await LoadMessagesAsync(channel));
            }

            else if (channel.Id == _botChannelChannel.Id)
            {
                var messages   = await LoadMessagesAsync(channel);
                var channelIds = new HashSet<ulong>();

                foreach (var message in messages)
                {
                    var id = message.GetChannelIds().SingleOrDefault();

                    if (ulong.TryParse(message.Content, out var x))
                        id = x;

                    ITextChannel chan;

                    if ((chan = _client.GetChannel(id) as ITextChannel) == null || !channelIds.Add(id))
                    {
                        if (message.Reactions.Count == 0)
                            await message.AddReactionAsync(new Emoji("\u274C"));

                        continue;
                    }

                    if (message.Reactions.Count != 0)
                        await message.RemoveAllReactionsAsync();

                    if (!message.Content.StartsWith("<#"))
                        await message.ModifyAsync(m => m.Content = $"<#{chan.Id}> - **{chan.Guild.Name}**");
                }

                BotChannelIds = channelIds;
            }

            else if (channel.Id == _claimReplyChannel.Id)
            {
                ClaimReplies = (await LoadMessagesAsync(channel)).Select(m => m.Content).ToList();
            }

            else if (channel.Id == _wishlistUsersChannel.Id)
            {
                var messages = await LoadMessagesAsync(channel);
                var userIds  = new HashSet<ulong>();

                foreach (var message in messages)
                {
                    var id = message.GetUserIds().SingleOrDefault();

                    if (ulong.TryParse(message.Content, out var x))
                        id = x;

                    IUser user;

                    if ((user = _client.GetUser(id)) == null || !userIds.Add(id))
                    {
                        if (message.Reactions.Count == 0)
                            await message.AddReactionAsync(new Emoji("\u274C"));

                        continue;
                    }

                    if (message.Reactions.Count != 0)
                        await message.RemoveAllReactionsAsync();

                    if (!message.Content.StartsWith("<@"))
                        await message.ModifyAsync(m => m.Content = $"<@{user.Id}> - **{user.Username}#{user.Discriminator}**");
                }

                ClaimWishlistUserIds = userIds;
            }

            else
            {
                return;
            }

            Log.Debug($"Configuration channel '#{channel.Name}' reloaded in {measure}.");
        }

        static async Task<T> LoadConfigPartAsync<T>(IMessageChannel channel, string key, IReadOnlyDictionary<string, ConfigPart> dict, Func<T> defaultFactory = null)
            where T : class, new()
        {
            if (dict.TryGetValue(key.ToLowerInvariant(), out var part))
            {
                var error = false;

                var config = JsonConvert.DeserializeObject<T>(
                                 part.Value,
                                 new JsonSerializerSettings
                                 {
                                     Error = (sender, args) =>
                                     {
                                         args.ErrorContext.Handled = true;
                                         error                     = true;
                                     }
                                 })
                          ?? new T();

                if (error && part.Message.Reactions.Count == 0)
                {
                    await part.Message.AddReactionAsync(new Emoji("\u274C"));
                }

                else if (!error)
                {
                    if (part.Message.Reactions.Count != 0)
                        await part.Message.RemoveAllReactionsAsync();

                    var content = formatConfigMessage(config);

                    if (part.Message.Content != content)
                        await part.Message.ModifyAsync(m => m.Content = content);
                }

                return config;
            }

            else
            {
                var config = defaultFactory?.Invoke() ?? new T();

                await channel.SendMessageAsync(formatConfigMessage(config));

                return config;
            }

            string formatConfigMessage(T obj)
                => $"> {key}\n```json\n{JsonConvert.SerializeObject(obj, Formatting.Indented, new StringEnumConverter())}\n```";
        }

        static Regex CreateWishlistRegex(ICollection<IUserMessage> items)
        {
            if (items.Count == 0)
                return null;

            var s = new HashSet<string>(
                    items.SelectMany(x => x.Content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                         .Select(x => x.Trim().ToLowerInvariant()))
               .Select(GlobToRegex);

            return new Regex($"({string.Join(")|(", s)})", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        static string GlobToRegex(string s)
            => $"^{Regex.Escape(s).Replace("\\*", ".*").Replace("\\?", ".")}$";

#region Partial configs

        struct ConfigPart
        {
            public readonly IUserMessage Message;
            public readonly string Value;

            public ConfigPart(IUserMessage message, string value)
            {
                Message = message;
                Value   = value;
            }
        }

        public class GeneralConfig
        {
            [JsonProperty("fallback_status")]
            public UserStatus FallbackStatus { get; set; } = UserStatus.Idle;

            [JsonProperty("state_update_command")]
            public string StateUpdateCommand { get; set; } = "$tu";
        }

        public class ClaimConfig
        {
            public static ClaimConfig CreateDefault() => new ClaimConfig
            {
                KakeraTargets = new HashSet<KakeraType>(Enum.GetValues(typeof(KakeraType)).Cast<KakeraType>())
            };

            [JsonProperty("enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty("delay_seconds")]
            public double Delay { get; set; } = 0.2;

            [JsonProperty("kakera_threshold")]
            public int KakeraThreshold {get; set;} = 0;

            [JsonProperty("kakera_delay_seconds")]
            public double KakeraDelay { get; set; } = 0.2;

            [JsonProperty("kakera_targets")]
            public HashSet<KakeraType> KakeraTargets { get; set; }

            [JsonProperty("enable_custom_emotes")]
            public bool CustomEmotes { get; set; }
        }

        public class RollConfig
        {
            [JsonProperty("enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("command")]
            public string Command { get; set; } = "$w";

            [JsonProperty("daily_kakera_enabled")]
            public bool KakeraEnabled { get; set; }

            [JsonProperty("daily_kakera_command")]
            public string KakeraCommand { get; set; } = "$dk";

            [JsonProperty("daily_kakera_then_state_update")]
            public bool KakeraStateUpdate { get; set; } = true;

            [JsonProperty("typing_delay_seconds")]
            public double TypingDelay { get; set; } = 0.3;

            [JsonProperty("interval_override_minutes")]
            public double? IntervalOverrideMinutes { get; set; }
        }

        public class MiscellaneousConfig
        {
            [JsonProperty("auto_update")]
            public bool AutoUpdate { get; set; } = true;
        }

#endregion
    }
}