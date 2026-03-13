using AAAGame.Scripts.Entity;
using UnityEngine;
using UnityGameFramework.Runtime;

public static class BrainFactory
{
    public static IControlBrain Create(BrainType tp, SkillEntity entity, EntityParams p)
    {
        switch (tp)
        {
            case BrainType.Player:
                return new PlayerBrain();

            case BrainType.EnemyAI:
                return new EnemyAIBrain();

            case BrainType.FriendlyAI:
                return new FriendlyAIBrain(); 

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