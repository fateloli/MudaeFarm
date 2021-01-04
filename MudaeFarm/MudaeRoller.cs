using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Disqord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MudaeFarm
{
    public interface IMudaeRoller : IHostedService { }

    public class MudaeRoller : BackgroundService, IMudaeRoller
    {
        readonly IDiscordClientService _discord;
        readonly IOptionsMonitor<RollingOptions> _options;
        readonly IOptionsMonitor<CommandChannelList> _commandChannelList;
        readonly IMudaeCommandHandler _commandHandler;
        readonly IMudaeOutputParser _outputParser;
        readonly ILogger<MudaeRoller> _logger;

        public MudaeRoller(IDiscordClientService discord, IOptionsMonitor<RollingOptions> options, IOptionsMonitor<CommandChannelList> commandChannelList, IMudaeCommandHandler commandHandler, IMudaeOutputParser outputParser, ILogger<MudaeRoller> logger)
        {
            _discord            = discord;
            _options            = options;
            _commandChannelList = commandChannelList;
            _commandHandler     = commandHandler;
            _outputParser       = outputParser;
            _logger             = logger;
        }

        sealed class Roller
        {
            public readonly CancellationTokenSource Cancellation;
            public readonly CommandChannelList.Item CurrentItem;

            public Roller(CommandChannelList.Item item, CancellationToken cancellationToken)
            {
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                CurrentItem  = item;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var client  = await _discord.GetClientAsync();
            var rollers = new Dictionary<ulong, Roller>();

            void handleChange(CommandChannelList o)
            {
                var items = o.Items.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());

                lock (rollers)
                {
                    foreach (var (id, item) in items)
                    {
                        if (!rollers.TryGetValue(id, out var roller) || !roller.CurrentItem.Equals(item))
                        {
                            roller?.Cancellation.Cancel();
                            roller?.Cancellation.Dispose();

                            var newRoller         = new Roller(item, stoppingToken);
                            var cancellationToken = newRoller.Cancellation.Token;

                            rollers[id] = newRoller;

                            Task.Run(async () =>
                            {
                                try
                                {
                                    await RunAsync(client, item, cancellationToken);
                                }
                                catch (OperationCanceledException) { }
                            }, cancellationToken);
                        }
                    }

                    foreach (var id in rollers.Keys.ToArray())
                    {
                        if (!items.ContainsKey(id) && rollers.Remove(id, out var roller))
                        {
                            roller.Cancellation.Cancel();
                            roller.Cancellation.Dispose();
                        }
                    }
                }
            }

            var monitor = _commandChannelList.OnChange(handleChange);

            try
            {
                handleChange(_commandChannelList.CurrentValue);

                await Task.Delay(-1, stoppingToken);
            }
            finally
            {
                monitor.Dispose();

                lock (rollers)
                {
                    foreach (var roller in rollers.Values)
                        roller.Cancellation.Dispose();

                    rollers.Clear();
                }
            }
        }

        async Task RunAsync(DiscordClient client, CommandChannelList.Item commandChannelItem, CancellationToken cancellationToken = default)
        {
            if (!(client.GetChannel(commandChannelItem.Id) is IMessageChannel commandChannel))
            {
                _logger.LogWarning($"Could not find channel {commandChannelItem.Id} to roll in.");
                return;
            }

            await Task.WhenAll(RunRollAsync(client, commandChannel, cancellationToken), RunDailyKakeraAsync(commandChannel, cancellationToken), RunPokerollAsync(commandChannel, cancellationToken));
        }

        async Task RunRollAsync(DiscordClient client, IMessageChannel commandChannel, CancellationToken cancellationToken = default)
        {
            var logPlace = $"channel '{commandChannel.Name}' ({commandChannel.Id})";

            var batches = 0;
            var rolls   = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var options = _options.CurrentValue;

                if (!options.Enabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                IUserMessage response;

                try
                {
                    using (commandChannel.Typing())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(options.TypingDelaySeconds), cancellationToken);

                        response = await _commandHandler.SendAsync(commandChannel, options.Command, cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, $"Could not roll '{options.Command}' in {logPlace}.");

                    await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
                    continue;
                }

                ++rolls;

                if (response.Embeds.Count != 0)
                {
                    _logger.LogInformation($"Sent roll {rolls} of batch {batches} in {logPlace}.");

                    await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cancellationToken);
                    continue;
                }

                if (response.Content.StartsWith($"**{client.CurrentUser.Name}**", StringComparison.OrdinalIgnoreCase))
                {
                    if (_outputParser.TryParseRollRemaining(response.Content, out var remaining))
                    {
                        _logger.LogInformation($"Sent roll {rolls} of batch {batches} in {logPlace}. {remaining} rolls are remaining.");

                        await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cancellationToken);
                        continue;
                    }

                    if (_outputParser.TryParseRollLimited(response.Content, out var resetTime))
                    {
                        resetTime += TimeSpan.FromMinutes(1);

                        _logger.LogInformation($"Finished roll {rolls} of batch {batches} in {logPlace}. Next batch in {resetTime}.");
                        rolls = 0;
                        ++batches;

                        await Task.Delay(resetTime, cancellationToken);
                        continue;
                    }
                }

                _logger.LogWarning($"Could not handle Mudae response for command '{options.Command}'. Assuming a sane default of {options.DefaultPerHour} rolls per hour ({rolls} right now). Response: {response.Content}");

                if (rolls >= options.DefaultPerHour)
                {
                    _logger.LogInformation($"Preemptively finished roll {rolls} of batch {batches} in {logPlace}. Next batch in an hour.");
                    rolls = 0;
                    ++batches;

                    await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
                    continue;
                }
            }
        }

        async Task RunDailyKakeraAsync(IMessageChannel commandChannel, CancellationToken cancellationToken = default)
        {
            var logPlace = $"channel '{commandChannel.Name}' ({commandChannel.Id})";

            while (!cancellationToken.IsCancellationRequested)
            {
                var options = _options.CurrentValue;

                if (!options.DailyKakeraEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                IUserMessage response;

                try
                {
                    using (commandChannel.Typing())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(options.TypingDelaySeconds), cancellationToken);

                        response = await _commandHandler.SendAsync(commandChannel, options.DailyKakeraCommand, cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, $"Could not roll daily kakera in {logPlace}.");

                    await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
                    continue;
                }

                if (_outputParser.TryParseTime(response.Content, out var resetTime))
                {
                    _logger.LogInformation($"Could not claim daily kakera in {logPlace}. Next reset in {resetTime}.");

                    await Task.Delay(resetTime, cancellationToken);
                    continue;
                }

                // dk output doesn't really matter, because we'll have to wait a day anyway
                _logger.LogInformation($"Claimed daily kakera in {logPlace}.");

                await Task.Delay(TimeSpan.FromHours(options.DailyKakeraWaitHours), cancellationToken);
            }
        }

        async Task RunPokerollAsync(IMessageChannel commandChannel, CancellationToken cancellationToken = default)
        {
            var logPlace = $"channel '{commandChannel.Name}' ({commandChannel.Id})";

            while (!cancellationToken.IsCancellationRequested)
            {
                var options = _options.CurrentValue;

                if (!options.PokerollEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                IUserMessage response;

                try
                {
                    using (commandChannel.Typing())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(options.TypingDelaySeconds), cancellationToken);

                        response = await _commandHandler.SendAsync(commandChannel, options.PokerollCommand, cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, $"Could not do pokeroll in {logPlace}.");

                    await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
                    continue;
                }

                if (_outputParser.TryParseTime(response.Content, out var resetTime))
                {
                    _logger.LogInformation($"Could not do pokeroll in {logPlace}. Next reset in {resetTime}.");

                    await Task.Delay(resetTime, cancellationToken);
                    continue;
                }

                _logger.LogInformation($"Did pokeroll in {logPlace}.");

                await Task.Delay(TimeSpan.FromHours(2), cancellationToken);
            }
        }
    }
}
