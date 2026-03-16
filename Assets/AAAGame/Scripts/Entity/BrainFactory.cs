using AAAGame.Scripts.Entity;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class BrainFactory
{
    public static IControlBrain Create(BrainType tp, MAEntity entity, EntityParams p)
    {
        switch (tp)
        {
            case BrainType.Player:
                return new PlayerBrain();

            case BrainType.EnemyAI:
                return new EnemyAIBrain();

            case BrainType.FriendlyAI:
                return new FriendlyAIBrain();

            case BrainType.SoldierAI:
                var soldierBrain = new SoldierAIBrain();
                // 惰性注入：Inject 在这里调用，EntityRegistry 提供玩家和全局列表
                soldierBrain.Inject(EntityRegistry.Player, EntityRegistry.AllEntities);
                return soldierBrain;

            default:
                return new PlayerBrain();
        }
    }
    
    public static class EntityControlKeys
    {
        public const string Side = "Side";                 // VarInt32
        public const string BrainType = "BrainType";       // VarInt32
    }
    
    
}