using System;
using System.Collections.Generic;
using Disqord;
using Newtonsoft.Json;

namespace MudaeFarm
{
    public class GeneralOptions
    {
        public const string Section = "General";

        [JsonProperty("fallback_status")]
        public UserStatus FallbackStatus { get; set; } = UserStatus.Idle;

        [JsonProperty("reply_typing_cpm")]
        public double ReplyTypingCpm { get; set; } = 190;

        [JsonProperty("auto_update")]
        public bool AutoUpdate { get; set; } = true;
    }

    public class ClaimingOptions
    {
        public const string Section = "Claiming";

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("delay_seconds")]
        public double DelaySeconds { get; set; } = 0.2;

        [JsonProperty("ignore_cooldown")]
        public bool IgnoreCooldown { get; set; }

        [JsonProperty("kakera_delay_seconds")]
        public double KakeraDelaySeconds { get; set; } = 0.2;

        [JsonProperty("kakera_ignore_cooldown")]
        public bool KakeraIgnoreCooldown { get; set; }

        [JsonProperty("kakera_targets")]
        public HashSet<KakeraType> KakeraTargets { get; set; } = new HashSet<KakeraType>();

        [JsonProperty("enable_custom_emotes")]
        public bool CustomEmotes { get; set; }

        [JsonProperty("notify_on_character_claim")]
        public bool NotifyOnCharacter { get; set; } = true;

        [JsonProperty("notify_on_kakera_claim")]
        public bool NotifyOnKakera { get; set; }

        /// <summary>
        /// https://github.com/chiyadev/MudaeFarm/issues/152
        /// </summary>
        [JsonProperty("bypass_im_check_bug_152")]
        public bool BypassImCheck { get; set; }
    }

    public class RollingOptions
    {
        public const string Section = "Rolling";

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("command")]
        public string Command { get; set; } = "$w";

        [JsonProperty("daily_kakera_enabled")]
        public bool DailyKakeraEnabled { get; set; }

        [JsonProperty("daily_kakera_command")]
        public string DailyKakeraCommand { get; set; } = "$dk";

        [JsonProperty("typing_delay_seconds")]
        public double TypingDelaySeconds { get; set; } = 0.3;

        [JsonProperty("interval_seconds")]
        public double IntervalSeconds { get; set; } = 0.5;

        [JsonProperty("default_per_hour")]
        public int DefaultPerHour { get; set; } = 5;

        [JsonProperty("daily_kakera_wait_hours")]
        public int DailyKakeraWaitHours { get; set; } = 20;

        [JsonProperty("pokeroll_enabled")]
        public bool PokerollEnabled { get; set; }

        [JsonProperty("pokeroll_command")]
        public string PokerollCommand { get; set; } = "$p";

        [JsonProperty("pokeroll_wait_hours")]
        public int PokerollWaitHours { get; set; } = 2;
    }

    public class CharacterWishlist
    {
        public const string Section = "Wished characters";

        public class Item
        {
            [JsonProperty("name")]
            public string Name { get; set; }
        }

        [JsonProperty("items")]
        public List<Item> Items { get; set; } = new List<Item>();
    }

    public class AnimeWishlist
    {
        public const string Section = "Wished anime";

        public class Item
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("excluding")]
            public CharacterWishlist.Item[] Excluding { get; set; }
        }

        [JsonProperty("items")]
        public List<Item> Items { get; set; } = new List<Item>();
    }

    public class BotChannelList
    {
        public const string Section = "Bot channels";

        public class Item : IEquatable<Item>
        {
            [JsonProperty("id")]
            public ulong Id { get; set; }

            public bool Equals(Item other) => other != null && Id == other.Id;
        }

        [JsonProperty("items")]
        public List<Item> Items { get; set; } = new List<Item>();
    }

    public enum ReplyEvent
    {
        ClaimSucceeded = 0,
        ClaimFailed = 1,
        BeforeClaim = 2,
        KakeraSucceeded = 3,
        KakeraFailed = 4,
        BeforeKakera = 5
    }

    public class ReplyList
    {
        public const string Section = "Replies";

        public class Item
        {
            [JsonProperty("content")]
            public string Content { get; set; }

            [JsonProperty("event")]
            public ReplyEvent Event { get; set; }

            [JsonProperty("weight")]
            public double Weight { get; set; } = 1;
        }

        [JsonProperty("items")]
        public List<Item> Items { get; set; } = new List<Item>();
    }

    public class UserWishlistList
    {
        public const string Section = "User wishlists";

        public class Item
        {
            [JsonProperty("id")]
            public ulong Id { get; set; }

            [JsonProperty("excluding")]
            public CharacterWishlist.Item[] ExcludingCharacters { get; set; }

            [JsonProperty("excluding_anime")]
            public AnimeWishlist.Item[] ExcludingAnime { get; set; }
        }

        [JsonProperty("items")]
        public List<Item> Items { get; set; } = new List<Item>();
    }
}
