using GameJson;
using UnityEngine;

namespace GameConfigSamples
{
    public sealed class SourceSampleReader : MonoBehaviour
    {
        public TextAsset PackedConfig;
        void Start()
        {
            if (PackedConfig == null) { Debug.LogError("Assign SourceConfig.bytes.", this); return; }
            var config = new SourceConfig(CompactConfig.Load(PackedConfig.bytes));
            var reward = config.Tile.Get(5).RewardRecord;
            Debug.Log("Tile 5 grants " + reward.Coins + " coins: " + reward.Name, this);
        }
    }
}
