using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Game.Config.Build")]
[assembly: InternalsVisibleTo("Game.Config.Json")]
namespace GameJson
{
    internal static class ConfigFormat
    {
        internal const int LegacyMagic = 0x31434a47;
        internal const int Magic = 0x32434a47;
        internal const int MaxBinaryBytes = 64 * 1024 * 1024;
    }
}
