using Newtonsoft.Json.Linq;

namespace DiceTown
{
    public static class TownContent
    {
        public static string Create(int version)
        {
            var tiles = new JArray(); var rewards = new JArray(); var buildings = new JArray();
            string[] names = { "START", "MARKET", "BAKERY", "POST", "PARK", "SUPPLY", "STATION", "CAFE", "ARCADE", "PORT", "GARDEN", "CHANCE", "PLAZA", "SHOP", "STUDIO", "MUSEUM", "PIER", "SUPPLY", "STATION", "HOTEL", "CINEMA", "FESTIVAL", "LIBRARY", "CHANCE" };
            for (int town = 0; town < TownRules.TownCount; town++)
            {
                for (int tile = 0; tile < TownRules.TileCount; tile++)
                {
                    int id = town * TownRules.TileCount + tile + 1;
                    int kind = tile == 5 || tile == 17 ? 1 : tile == 6 || tile == 18 ? 2 : tile == 11 || tile == 23 ? 3 : 0;
                    tiles.Add(new JArray(id, names[tile], kind, id));
                    int cash = kind == 3 ? -65 : kind == 2 ? 170 : 50 + (tile % 5) * 12;
                    if (version == 2 && cash > 0) cash *= 2;
                    rewards.Add(new JArray(id, cash, kind == 1 ? 4 : 0, "Town " + (town + 1), new JArray(tile % 4, town % 8)));
                }
                string[] places = { "CORNER CAFE", "CITY GARDEN", "GRAND HOTEL" };
                for (int b = 0; b < 3; b++) for (int level = 1; level <= 5; level++)
                    buildings.Add(new JArray(town * 15 + b * 5 + level, places[b], 80 + level * 30 + b * 10, level));
            }
            var root = new JObject { ["version"] = version, ["tables"] = new JArray(
                Table("Tile", new JArray(Field("id","int"), Field("name","string"), Field("kind","int"), Field("reward","int","Reward")), tiles),
                Table("Reward", new JArray(Field("id","int"), Field("coins","int"), Field("dice","int"), Field("district","string"), Field("tags","int[]")), rewards),
                Table("Building", new JArray(Field("id","int"), Field("name","string"), Field("cost","int"), Field("level","int")), buildings)) };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }
        static JObject Table(string name, JArray fields, JArray rows) => new JObject { ["name"] = name, ["fields"] = fields, ["rows"] = rows };
        static JObject Field(string name, string type, string reference = null)
        { var f = new JObject { ["name"] = name, ["type"] = type }; if (reference != null) f["ref"] = reference; return f; }
    }
}
