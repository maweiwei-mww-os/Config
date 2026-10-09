using GameJson.Examples.Generated;
using UnityEngine;

namespace GameJson.Examples
{
    public sealed class TypedSnapshotExample : MonoBehaviour
    {
        public TextAsset InitialConfig;
        public TextAsset UpdatedConfig;
        ConfigStore<ExampleConfig> store;
        ConfigSnapshot<ExampleConfig> capturedTurn;
        void Start()
        {
            store=new ConfigStore<ExampleConfig>(d=>new ExampleConfig(d),ConfigVersionPolicy.Increasing);
            if(Publish(InitialConfig))capturedTurn=store.Capture();
        }
        [ContextMenu("Publish updated configuration")]
        public void PublishUpdate()
        {
            if(store==null||!Publish(UpdatedConfig))return;
            Debug.Log("Active reward="+store.Current.Tile.Get(5).RewardRecord.Coins+
                "; captured turn reward="+(capturedTurn==null?"released":capturedTurn.Value.Tile.Get(5).RewardRecord.Coins.ToString()));
        }
        [ContextMenu("Release captured turn")]
        public void ReleaseTurn(){capturedTurn=null;}
        bool Publish(TextAsset asset)
        {
            if(asset==null){Debug.LogWarning("Assign a compiled .bytes configuration.");return false;}
            byte[] bytes=asset.bytes;
            if(!store.TryPrepare(()=>CompactConfig.Load(bytes),out var candidate,out var error)||!store.TryCommit(candidate,out error))
            {Debug.LogWarning(error.Code+": "+error.Message);return false;}
            Debug.Log("Published configuration v"+store.Capture().Version);return true;
        }
    }
}
