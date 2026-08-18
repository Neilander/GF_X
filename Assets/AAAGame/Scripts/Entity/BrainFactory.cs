using AAAGame.Scripts.Entity;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class BrainFactory
{
    public static IControlBrain Create(BrainType tp, IEntityContext entity, EntityParams p)
    {
        switch (tp)
        {
            case BrainType.Player:
                return new PlayerBrain();

            case BrainType.EnemyAI:
            case BrainType.FriendlyAI:
                throw new System.InvalidOperationException(
                    $"BrainFactory.Create failed: legacy BrainType {tp} is unsupported; use SoldierAI.");

            case BrainType.SoldierAI:
            case BrainType.DefendEnemyAI:
                var soldierBrain = new SoldierAIBrain();
                soldierBrain.Inject();
                return soldierBrain;

            default:
                throw new System.InvalidOperationException($"BrainFactory.Create failed: unsupported BrainType {tp}.");
        }
    }
    
    public static class EntityControlKeys
    {
        public const string Side = "Side";                 // VarInt32
        public const string BrainType = "BrainType";       // VarInt32
    }
    
    
}
