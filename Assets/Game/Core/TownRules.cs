using System;
using System.IO;
using GameJson;

namespace DiceTown
{
    public sealed class TownRules
    {
        public const int TileCount = 24;
        public const int TownCount = 120;
        readonly IConfigTable tiles, rewards, buildings;
        readonly int tileReward, tileKind, tileName, coins, dice, buildingCost, buildingName;
        public int Version { get; }
        public TownRules(IConfigData data)
        {
            Version = data.Version;
            tiles = data.Table("Tile"); rewards = data.Table("Reward"); buildings = data.Table("Building");
            tileReward = tiles.Bind("reward", FieldKind.Int); tileKind = tiles.Bind("kind", FieldKind.Int); tileName = tiles.Bind("name", FieldKind.String);
            coins = rewards.Bind("coins", FieldKind.Int); dice = rewards.Bind("dice", FieldKind.Int);
            buildingCost = buildings.Bind("cost", FieldKind.Int); buildingName = buildings.Bind("name", FieldKind.String);
            for (int town = 0; town < TownCount; town++)
            {
                for (int tile = 0; tile < TileCount; tile++)
                {
                    int r = Required(tiles, town * TileCount + tile + 1);
                    Required(rewards, tiles.Int(r, tileReward));
                    int kind = tiles.Int(r, tileKind);
                    if (kind < 0 || kind > 3) throw new InvalidDataException("Tile kind out of range.");
                }
                for (int b = 0; b < 3; b++) for (int l = 1; l <= 5; l++)
                    if (Cost(town, b, l) <= 0) throw new InvalidDataException("Building cost must be positive.");
            }
        }
        static int Required(IConfigTable table, int id) { int row = table.Find(id); if (row < 0) throw new InvalidDataException("Missing game row " + id); return row; }
        public Reward Reward(int town, int tile)
        {
            int t = Required(tiles, town * TileCount + tile + 1);
            int r = Required(rewards, tiles.Int(t, tileReward));
            return new Reward(tiles.Int(t, tileKind), rewards.Int(r, coins), rewards.Int(r, dice), tiles.Text(t, tileName));
        }
        public int Cost(int town, int building, int nextLevel) => buildings.Int(Required(buildings, town * 15 + building * 5 + nextLevel), buildingCost);
        public string BuildingName(int town, int building) => buildings.Text(Required(buildings, town * 15 + building * 5 + 1), buildingName);
    }
    public readonly struct Reward
    {
        public readonly int Kind, Coins, Dice;
        public readonly string Name;
        public Reward(int kind, int coins, int dice, string name) { Kind = kind; Coins = coins; Dice = dice; Name = name; }
    }
}
