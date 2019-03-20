using System.Collections.Generic;

namespace MudaeFarm
{
    public class Config
    {
        public string AuthToken { get; set; }
        public double? AutoRollInterval { get; set; }
        public char AutoRollGender { get; set; } = 'w';

        public HashSet<ulong> BotChannels { get; set; } = new HashSet<ulong>();

        public HashSet<string> WishlistCharacters { get; set; } = new HashSet<string>();
        public HashSet<string> WishlistAnimes { get; set; } = new HashSet<string>();
    }
}
