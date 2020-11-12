using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Disqord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MudaeFarm
{
    public class DiscordConfigurationSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new DiscordConfigurationProvider();
    }

    public class DiscordConfigurationProvider : IConfigurationProvider
    {
        ICredentialManager _credentials;
        ILogger<DiscordConfigurationProvider> _logger;
        DiscordClient _client;
        IGuild _guild;

        public async Task InitializeAsync(IServiceProvider services, DiscordClient client, CancellationToken cancellationToken = default)
        {
            _credentials = services.GetService<ICredentialManager>();
            _logger      = services.GetService<ILogger<DiscordConfigurationProvider>>();
            _client      = client;
            _guild       = FindConfigurationGuild() ?? await CreateConfigurationGuild(cancellationToken);

            var watch = Stopwatch.StartNew();

            foreach (var channel in await _guild.GetChannelsAsync())
            {
                if (channel is IMessageChannel textChannel)
                    await ReloadAsync(textChannel, false, cancellationToken);
            }

            NotifyReload(); // reload in bulk and notify once

            client.MessageReceived     += e => ReloadAsync(e.Message.Channel, true, cancellationToken);
            client.MessageUpdated      += e => ReloadAsync(e.Channel, true, cancellationToken);
            client.MessageDeleted      += e => ReloadAsync(e.Channel, true, cancellationToken);
            client.MessagesBulkDeleted += e => ReloadAsync(e.Channel, true, cancellationToken);

            client.ChannelCreated += e => ReloadAsync(e.Channel, true, cancellationToken);
            client.ChannelDeleted += e => ReloadAsync(e.Channel, true, cancellationToken);
            client.ChannelUpdated += e => ReloadAsync(e.NewChannel, true, cancellationToken);

            _logger.LogInformation($"Loaded all configuration in {watch.Elapsed.TotalSeconds:F}s.");
        }

        IGuild FindConfigurationGuild()
        {
            foreach (var guild in _client.Guilds.Values)
            {
                if (guild.OwnerId != _client.CurrentUser.Id)
                    continue;

                var topic = guild.TextChannels.Values.FirstOrDefault(c => c.Name == "information")?.Topic ?? "";
                var lines = topic.Split('\n');

                var userId  = null as ulong?;
                var profile = null as string;

                foreach (var line in lines)
                {
                    var parts = line.Split(':', 2);

                    if (parts.Length != 2)
                        continue;

                    var key   = parts[0].Trim();
                    var value = parts[1].Trim(' ', '*'); // ignore bolding

                    switch (key.ToLowerInvariant())
                    {
                        case "mudaefarm" when ulong.TryParse(value, out var uid):
                            userId = uid;
                            break;

                        case "profile":
                            profile = value;
                            break;
                    }
                }

                if (userId == _client.CurrentUser.Id && _credentials.SelectedProfile.Equals(profile, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Using configuration server '{guild.Name}' ({guild.Id}).");
                    return guild;
                }
            }

            return null;
        }

        async Task<IGuild> CreateConfigurationGuild(CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Initializing a new configuration server. This may take a while...");

            var watch = Stopwatch.StartNew();

            var profile   = _credentials.SelectedProfile;
            var guildName = $"MudaeFarm ({profile})";

            if (string.IsNullOrEmpty(profile) || profile.Equals("default", StringComparison.OrdinalIgnoreCase))
                guildName = "MudaeFarm";

            var regions = await _client.GetVoiceRegionsAsync();
            var guild   = await _client.CreateGuildAsync(guildName, (regions.FirstOrDefault(v => v.IsOptimal) ?? regions.First()).Id);

            // delete default channels
            foreach (var channel in await guild.GetChannelsAsync())
                await channel.DeleteAsync();

            var information = await guild.CreateTextChannelAsync("information", c => c.Topic = $"MudaeFarm: **{_client.CurrentUser.Id}**\nProfile: **{_credentials.SelectedProfile}**\nVersion: {Updater.CurrentVersion.ToString(3)}");
            await guild.CreateTextChannelAsync("wished-characters", c => c.Topic = "Configure your character wishlist here. Glob expressions are supported. Names are *case-insensitive*.");
            await guild.CreateTextChannelAsync("wished-anime", c => c.Topic      = "Configure your anime wishlist here. Glob expressions are supported. Names are *case-insensitive*.");
            await guild.CreateTextChannelAsync("command-channels", c => c.Topic      = "Configure channels to enable MudaeFarm autorolling by sending the __channel ID__.");
            await guild.CreateTextChannelAsync("claim-channels", c => c.Topic = "Configure channels to enable MudaeFarm claiming by sending the __channel ID__.");
            await guild.CreateTextChannelAsync("claim-replies", c => c.Topic     = "Configure automatic reply messages when you claim a character. One message is randomly selected. Refer to https://github.com/chiyadev/MudaeFarm for advanced templating.");
            await guild.CreateTextChannelAsync("wishlist-users", c => c.Topic    = "Configure wishlists of other users to be claimed by sending the __user ID__.");

            var notice = await information.SendMessageAsync(@"
This is your MudaeFarm server where you can configure the bot.

Check <https://github.com/chiyadev/MudaeFarm> for detailed usage guidelines!
".Trim());

            await notice.PinAsync();

            Task addSection<T>(string section, T defaultValue = default) where T : class, new()
                => information.SendMessageAsync($"> {section}\n```json\n{JsonConvert.SerializeObject(defaultValue ?? new T(), Formatting.Indented, new StringEnumConverter())}\n```");

            await addSection<GeneralOptions>("General");
            await addSection<ClaimingOptions>("Claiming", new ClaimingOptions { KakeraTargets = new HashSet<KakeraType>(Enum.GetValues(typeof(KakeraType)).Cast<KakeraType>()) });
            await addSection<RollingOptions>("Rolling");

            _logger.LogInformation($"Took {watch.Elapsed.TotalSeconds:F}s to initialize configuration server {guild.Id}.");

            return guild;
        }

        const int _loadMessages = 1000;

        static async IAsyncEnumerable<IUserMessage> EnumerateMessagesAsync(IMessageChannel channel, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var page in channel.GetMessagesEnumerable(_loadMessages))
            foreach (var message in page)
            {
                if (message is IUserMessage userMessage)
                    yield return userMessage;
            }
        }

        static readonly JsonSerializerSettings _deserializeSettings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        static T DeserializeOrCreate<T>(string value, Action<T, string> configure) where T : class, new()
        {
            if (value.StartsWith('{'))
                return JsonConvert.DeserializeObject<T>(value, _deserializeSettings);

            var t = new T();
            configure(t, value);
            return t;
        }

        static string[] SplitIfNotJson(string value)
        {
            if (value.StartsWith('{'))
                return new[] { value };

            return value.Split('\n');
        }

        static string ConvertWishItemToRegex(string s)
        {
            // wish items are globs
            s = $"^{Regex.Escape(s).Replace("\\*", ".*").Replace("\\?", ".")}$";

            // replace spaces with space expressions
            s = s.Replace("\\ ", "\\s+");

            return s;
        }

        static readonly Regex _sectionRegex = new Regex(@"^>\s*(?<section>.*?)\s*```json\s*(?={)(?<data>.*)(?<=})\s*```$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        async Task ReloadAsync(IChannel ch, bool notifyReload, CancellationToken cancellationToken = default)
        {
            if (!(ch is IMessageChannel channel && ch is IGuildChannel guildChannel && guildChannel.GuildId == _guild.Id))
                return;

            try
            {
                var valid = true;
                var watch = Stopwatch.StartNew();

                switch (channel.Name)
                {
                    case "information":
                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                        {
                            var match = _sectionRegex.Match(message.Content);

                            if (match.Success)
                            {
                                var section = match.Groups["section"].Value;
                                var data    = match.Groups["data"].Value;

                                // from v4: miscellaneous section is merged into general
                                if (section.Equals("miscellaneous", StringComparison.OrdinalIgnoreCase))
                                {
                                    await message.DeleteAsync();
                                    continue;
                                }

                                var dataObj = JsonConvert.DeserializeObject(data, section switch
                                {
                                    GeneralOptions.Section  => typeof(GeneralOptions),
                                    ClaimingOptions.Section => typeof(ClaimingOptions),
                                    RollingOptions.Section  => typeof(RollingOptions),

                                    _ => throw new NotSupportedException($"Unknown configuration section '{section}'.")
                                }, _deserializeSettings);

                                // check data string and reserialized data; this will prettify the message
                                var dataPretty = JsonConvert.SerializeObject(dataObj, Formatting.Indented, new StringEnumConverter());

                                if (data != dataPretty)
                                    await message.ModifyAsync(m => m.Content = $"> {section}\n```json\n{dataPretty}\n```");

                                SetSection(section, dataObj);
                            }
                        }

                        break;

                    case "wished-characters":
                        var characters = new CharacterWishlist();

                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                        foreach (var line in SplitIfNotJson(message.Content))
                            characters.Items.Add(DeserializeOrCreate<CharacterWishlist.Item>(line, (x, v) => x.Name = ConvertWishItemToRegex(v)));

                        SetSection(CharacterWishlist.Section, characters);
                        break;

                    case "wished-anime":
                        var anime = new AnimeWishlist();

                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                        foreach (var line in SplitIfNotJson(message.Content))
                            anime.Items.Add(DeserializeOrCreate<AnimeWishlist.Item>(line, (x, v) => x.Name = ConvertWishItemToRegex(v)));

                        SetSection(AnimeWishlist.Section, anime);
                        break;

                    case "command-channels":
                        var commandChannels = new CommandChannelList();

                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                        {
                            if (ulong.TryParse(message.Content, out var id))
                            {
                                if (_client.GetChannel(id) is IGuildChannel c)
                                    await message.ModifyAsync(m => m.Content = $"<#{id}> - **{_client.GetGuild(c.GuildId).Name}**");

                                commandChannels.Items.Add(new CommandChannelList.Item { Id = id });
                                continue;
                            }

                            var mentionedCommandChannel = message.GetChannelIds().FirstOrDefault();

                            if (mentionedCommandChannel != 0)
                            {
                                commandChannels.Items.Add(new CommandChannelList.Item { Id = mentionedCommandChannel });
                                continue;
                            }

                            commandChannels.Items.Add(DeserializeOrCreate<CommandChannelList.Item>(message.Content, (x, v) => x.Id = ulong.Parse(v)));
                        }

                        SetSection(CommandChannelList.Section, commandChannels);
                        break;

                    case "claim-channels":
                        var claimChannels = new ClaimChannelList();

                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                        {
                            if (ulong.TryParse(message.Content, out var id))
                            {
                                if (_client.GetChannel(id) is IGuildChannel c)
                                    await message.ModifyAsync(m => m.Content = $"<#{id}> - **{_client.GetGuild(c.GuildId).Name}**");

                                claimChannels.Items.Add(new ClaimChannelList.Item { Id = id });
                                continue;
                            }

                            var mentionedClaimChannel = message.GetChannelIds().FirstOrDefault();

                            if (mentionedClaimChannel != 0)
                            {
                                claimChannels.Items.Add(new ClaimChannelList.Item { Id = mentionedClaimChannel });
                                continue;
                            }

                            claimChannels.Items.Add(DeserializeOrCreate<ClaimChannelList.Item>(message.Content, (x, v) => x.Id = ulong.Parse(v)));
                        }

                        SetSection(ClaimChannelList.Section, claimChannels);
                        break;

                    case "claim-replies":
                        var replies = new ReplyList();

                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                            replies.Items.Add(DeserializeOrCreate<ReplyList.Item>(message.Content, (x, v) => x.Content = v));

                        SetSection(ReplyList.Section, replies);
                        break;

                    case "wishlist-users":
                        var wishlists = new UserWishlistList();

                        await foreach (var message in EnumerateMessagesAsync(channel, cancellationToken))
                        {
                            if (ulong.TryParse(message.Content, out var id))
                            {
                                if (_client.GetUser(id) is IUser u)
                                    await message.ModifyAsync(m => m.Content = $"<@{id}> - **{u.Name}#{u.Discriminator}**");

                                wishlists.Items.Add(new UserWishlistList.Item { Id = id });
                                continue;
                            }

                            var mentionedUser = message.GetUserIds().FirstOrDefault();

                            if (mentionedUser != 0)
                            {
                                wishlists.Items.Add(new UserWishlistList.Item { Id = id });
                                continue;
                            }

                            wishlists.Items.Add(DeserializeOrCreate<UserWishlistList.Item>(message.Content, (x, v) => x.Id = ulong.Parse(v)));
                        }

                        SetSection(UserWishlistList.Section, wishlists);
                        break;

                    default:
                        valid = false;
                        break;
                }

                if (valid)
                    _logger.LogDebug($"Reloaded configuration channel '{channel.Name}' ({channel.Id}) in {watch.Elapsed.TotalMilliseconds:F}ms.");

                if (notifyReload)
                    NotifyReload();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, $"Could not reload configuration channel '{channel.Name}' ({channel.Id}).");
            }
        }

        void NotifyReload() => Interlocked.Exchange(ref _reloadToken, new ConfigurationReloadToken()).OnReload();

        readonly ConcurrentDictionary<string, IConfigurationProvider> _providers = new ConcurrentDictionary<string, IConfigurationProvider>(StringComparer.OrdinalIgnoreCase);

        void SetSection(string section, object data)
        {
            // use System.Text.Json because JsonStreamConfigurationProvider uses that to deserialize
            var provider = new JsonStreamConfigurationProvider(new JsonStreamConfigurationSource { Stream = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data)) });
            provider.Load();

            _providers[section] = provider;

            if (_logger?.IsEnabled(LogLevel.Debug) == true) // null in ctor
                _logger.LogDebug($"Set configuration section '{section}': {JsonConvert.SerializeObject(data)}");
        }

        public bool TryGet(string key, out string value)
        {
            var parts = key.Split(':', 2);

            if (parts.Length == 2 && _providers.TryGetValue(parts[0], out var provider))
                return provider.TryGet(parts[1], out value);

            value = default;
            return false;
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string parentPath)
        {
            foreach (var (section, provider) in _providers)
            {
                if (!parentPath.StartsWith(section, StringComparison.OrdinalIgnoreCase))
                    continue;

                var nestedPath = parentPath.Substring(section.Length).TrimStart(':');

                if (nestedPath.Length == 0)
                    nestedPath = null;

                foreach (var key in provider.GetChildKeys(Enumerable.Empty<string>(), nestedPath))
                    yield return key;
            }

            foreach (var key in earlierKeys)
                yield return key;
        }

        ConfigurationReloadToken _reloadToken = new ConfigurationReloadToken();

        public IChangeToken GetReloadToken() => _reloadToken;

        void IConfigurationProvider.Load() { }
        void IConfigurationProvider.Set(string key, string value) { }
    }
}