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

            //注意：这个现在没有使用！应使用SoliderAi
            case BrainType.EnemyAI:
                return new EnemyAIBrain();

            //注意：这个现在没有使用！应使用SoliderAi
            case BrainType.FriendlyAI:
                return new FriendlyAIBrain();

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
