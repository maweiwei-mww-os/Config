using UnityEngine;
using GameJson;

public sealed class ReadConfigExample : MonoBehaviour
{
    public TextAsset PackedRewards;
    void Start()
    {
        var config=CompactConfig.Load(PackedRewards.bytes);
        var table=config.Table("Reward");
        int coins=table.Bind("coins",FieldKind.Int);
        int row=table.Find(1);
        if(row>=0)Debug.Log("Reward 1: "+table.Int(row,coins));
    }
}
