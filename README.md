# NOTICE

MudaeFarm repository has been archived. I ([phosphene47](https://github.com/phosphene47)) has no intention of working on this project anymore.

I created this bot when I was playing with Mudae in 2018. It was meant to be a simple experiment with Discord selfbots, but I kept receiving bug reports and feature requests in this repository after I was done. I spent much time fixing those bugs and implementing those features, but it's not easy to convince myself to work on a project which I have zero interest in anymore.

You can still download MudaeFarm in the [Releases](https://github.com/chiyadev/MudaeFarm/releases) page, but please note that the bot may be completely broken. As of 2021/01/02 the bot still seems to be somewhat functioning. This may change anytime.

If you are experienced in C# programming and Discord.NET and wish to take over this project, please contact me on [Twitter](https://twitter.com/phosphene47).

# MudaeFarm

This is a ~~simple~~ bot that automatically rolls and claims Mudae waifus/husbandoes.

## Setup

1. Download and extract the [latest release](https://github.com/chiyadev/mudaefarm/releases).
2. Run `MudaeFarm.exe`.

You can bypass the "Windows protected your PC" popup by clicking "More info". Alternatively, you may build this project yourself using the .NET Core SDK.

3. Enter your user token. [How?](https://github.com/chiyadev/MudaeFarm/blob/master/User%20tokens.md)

## Initialization

On initial run, MudaeFarm will create a dedicated server named `MudaeFarm` for configuration. In this server you can edit your character wishlists and configurations for claiming and rolling.

It may take a while for this server to be created.

**MudaeFarm is disabled on all servers by default.** You must copy the *ID of the channel* in which you want to enable MudaeFarm (usually the bot/spam channel of that server), and send that ID in `#bot-channels`. See Configuration section below for details.

## Configuration

Configuration is written in JSON and stored in messages that you can edit at anytime. MudaeFarm will reload changes automatically. `#information` is the channel where most configurations are stored.

### `#information`

- **General**
    - `fallback_status`: When MudaeFarm is running in the background, the user status to set on your account. Other Discord clients that set a higher value will override this. Accepted values: `online`, `invisible`, `idle`, `dnd`.
    - `reply_typing_cpm`: When sending automatic replies, the number of characters to type in a minute i.e. "characters per minute". This is used to make automatic replies look realistically typed.
    - `auto_update`: Whether to check for updates and automatically update MudaeFarm in the background.

- **Claiming**
    - `enabled`: Whether autoclaiming is enabled.
    - `delay_seconds`: On finding a character that can be claimed, the number of seconds to wait before attempting to claiming it.
    - `ignore_cooldown`: Whether to attempt to claim characters regardless of claim cooldown.
    - `kakera_delay_seconds`: Same as `delay_seconds` but for kakera.
    - `kakera_ignore_cooldown`: Same as `ignore_cooldown` but for kakera.
    - `kakera_targets`: Specifies which types of kakera should be claimed.
    - `enable_custom_emotes`: Enables compatibility with servers that use custom emotes instead of the default heart emoji. This will cause heart emoji safety code to be bypassed.
    - `notify_char_claim`: Enables Windows 10 toast notifications when a character is claimed. (Make sure notifications are turned on in the Windows action center)
    - `notify_kakera_claim`: Same as `notify_char_claim` but for kakera.

- **Rolling**
    - `enabled`: Whether autorolling is enabled.
    - `command`: Command to use for rolling.
    - `daily_kakera_enabled`: Whether autorolling of daily kakera is enabled.
    - `daily_kakera_command`: Command to use for rolling daily kakera.
    - `typing_delay_seconds`: Number of seconds to type the rolling command before sending it.
    - `interval_seconds`: Interval in seconds between each roll (not applicable to daily kakera).
    - `default_per_hour`: Number of rolls that MudaeFarm will perform every hour at minimum if it was not able to determine the roll's result.
    - `daily_kakera_wait_hours`: Number of hours that MudaeFarm will wait for between each $dk. (set it to 10 for premium members, 20 for regular)
    - `pokeroll_enabled`: Whether autorolling of pokemon is enabled.
    - `pokeroll_command`: Command to use for rolling pokemon.

### `#wished-characters`

This channel contains a list of characters that should be claimed. Rules:

- Each message contains one or more character names, separated by lines.
- Character names are case-insensitive.
- If a character name contains information in brackets for disambiguation, this must be included.
- Basic glob expressions are supported. `?` for matching any single character. `*` for matching any zero-or-more characters.

JSON objects are also accepted:

- `name`: a regular expression (not glob expression) that matches character name.

For example,

- `goku` matches Goku and Goku only.
- `* kurosaki` matches any character with name ending with "kurosaki" (Ichigo Kurosaki).
- `kazuya (planetarian)` matches Kazuya from the anime Planetarian.
- `{"name": "^goku$"}` matches Goku and Goku only.
- `{"name": "^.*\s+kurosaki$"}` matches any character with name ending with "kurosaki" (Ichigo Kurosaki).
- `{"name": "^kazuya\s+\(planetarian\)$"}` matches Kazuya from the anime Planetarian.

### `#wished-anime`

This channel contains a list of anime from which characters should be claimed. Rules are the same as `#wished-characters`.

JSON objects are also accepted:

- `name`: a regular expression (not glob expression) that matches anime name.
- `excluding`: an array of JSON objects specified in `#wished-characters`. This acts like a blacklist of characters from a specific anime.

For example,

- `is the order a rabbit?` matches all characters from the anime Is the Order a Rabbit?
- `{"name": "^is the order a rabbit\?$", "excluding": [{"name": "^chino\s+kafuu$}]}` matches all characters from the anime Is the Order a Rabbit? except Chino Kafuu.

Note: Legacy versions of MudaeFarm supported the excluding bracket notation like `is the order a rabbit? (excluding: chino kafuu)`. This was not very flexible, and is not supported by current versions of MudaeFarm, so you must use JSON objects instead.

### `#command-channels`

This channel contains a list of channels in which MudaeFarm should **do rolls and other commands**. Rules:

- Send channel ID only. This can be retrieved by enabling Discord developer mode and right-clicking on a channel.

If MudaeFarm recognizes the ID, it will indicate success by replacing the message with a tagged channel.

### `#claim-channels`

This channel contains a list of channels in which MudaeFarm should **watch for claims and reacts**. Rules:

- Send channel ID only. This can be retrieved by enabling Discord developer mode and right-clicking on a channel.

If MudaeFarm recognizes the ID, it will indicate success by replacing the message with a tagged channel.

- Generally, any channel ID put into #command-channels should be repeated here but not all channels here should be repeated in #command-channels.

### `#claim-replies`

This channel contains a list of messages that will be automatically sent after successfully claiming a character. This can make MudaeFarm look more human-like when used effectively. Rules:

- Each message literally represents a message that will be sent.
- One message in the entire channel history will be selected at random. All messages have a weight of `1` by default, which means all messages are equally likely to be selected.
- Duplicate messages are accepted, increasing the probability of selecting such message.
- A message containing just a dot `.` represents NOT sending anything.
- A message containing `\n` will be splitted and sent separately, sequentially.
- Basic variable substitution is supported, in the format `*variable*`.

JSON objects are also accepted:

- `content`: Content of the message to send. The same formatting rules outlined above apply.
- `event`: Specifies exactly when the message will be sent. All non-JSON messages in this channel have `ClaimSucceeded` by default. It is not possible to change this field without using JSON. Accepted values: `ClaimSucceeded`, `ClaimFailed`, `BeforeClaim`, `KakeraSucceeded`, `KakeraFailed`, `BeforeKakera`.
- `weight`: Changes the probability of this message being selected, with a higher value indicating higher probability. This is `1` by default.

There are different variable substitutions available for events:

- ClaimSucceeded, ClaimFailed, BeforeClaim
    - `character`: Character's lowercase first name.
    - `Character`: Character's first name.
    - `character_full`: Character's lowercase full name.
    - `Character_full`: Character's full name.
    - `anime`: Character's lowercase anime.
    - `Anime`: Character's anime.
- KakeraSucceeded, KakeraFailed, BeforeKakera
    - `kakera`: kakera color e.g. purple, blue, teal

For example,

- `I love *Character_full* in *anime*!` is converted to `I love Chino Kafuu in is the order a rabbit?!` and sent after successfully claiming a character.
- `Wow!\n*Character* is really cute.` is converted to `Wow!\nChino is really cute.` and sent in two messages, `Wow!` and `Chino is really cute.`, after successfully claiming a character.
- `{"content": "I hate *character*.", "weight": 0}` is converted to `{"content": "I hate chino.", "weight": 0}` but will never be sent because weight is zero.
- `{"content": "I will claim a *kakera* kakera now.", "event": "BeforeKakera"}` is converted to `{"content": "I will claim a rainbow kakera now.", "event": "BeforeKakera"}` and sent before claiming a kakera.

### `#wishlist-users`

This channel contains a list of users whose wishlists will be used for claiming characters. Rules:

- Send user ID only. This can be retrieved by enabling Discord developer mode and right-clicking on a user.
- The target user's wishlist must be public for this to work. This feature relies on Mudae pinging users after a roll, like "Wished by @user1, @user2".

It is possible to add your own ID in this channel. This effectively allows MudaeFarm use your public Mudae wishlist for claiming. MudaeFarm's own wishlist behavior will not be affected.

### Configuration profiles

MudaeFarm supports "profiles" that can be used for authenticating as a different user or to add multiple isolated configuration servers. This is useful for having different configurations for certain servers. Profiles are located at `%localappdata%\MudaeFarm\profiles.json`.

You can configure profiles for two different accounts.

```json
{
  "user A": "<user A's token>",
  "user B": "<user B's token>"
}
```

You can make aliased profiles, which adds multiple configuration servers to an account. Aliased profiles will inherit the referenced profile's token.

```json
{
  "claimer (general)": "<user token>",
  "claimer (only fav)": "claimer (general)"
}
```

Some notes about profiles:

- When using custom profiles, you should remove the `default` profile, because it will always be selected automatically.
- When you are renaming a profile, you must also edit `#information` channel's topic to match the profile's name.
- It is possible to start multiple instances of MudaeFarm with different profiles, as they will never interfere with each other.
- If an account has multiple configurations, it is your responsibility to ensure that the configured servers and characters are mutually exclusive. Otherwise, it may result in duplicate claims, race conditions or other undefined behavior.

## Contributors

- [phosphene47](https://github.com/phosphene47) Maintainer
- [zhaobenny](https://github.com/zhaobenny) [#201](https://github.com/chiyadev/MudaeFarm/pull/201)

## Reporting bugs

0. Before you decide to report a bug, try reading this guide again. You may have missed some critical steps while configuring this bot.

1. Run this bot with verbose logging and collect logs. Open the folder that contains `MudaeFarm.exe`, click Windows Explorer navigation bar, and enter `MudaeFarm.exe --verbose`. MudaeFarm will print much more logs than normal, and these logs are saved in the same folder.

2. MudaeFarm does not generally log sensitive data, but if you consider your username#0000 or user/channel/guild IDs to be "sensitive" data, you can delete them.

3. [Create a GitHub issue](https://github.com/chiyadev/MudaeFarm/issues/new) with the relevant log file attached. Please do NOT post a screenshot of the log file or the console window. A screenshot of the configuration channel can be helpful, however. Describe the buggy behavior, the expected behavior, and (if you know) what can be done to fix it. This can make fixing the bug a lot easier for the developers.
