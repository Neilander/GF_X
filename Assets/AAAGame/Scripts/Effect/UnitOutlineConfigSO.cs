using UnityEngine;

namespace AAAGame.Effect
{
    [CreateAssetMenu(fileName = "UnitOutlineConfig", menuName = "AAAGame/Effect/UnitOutlineConfig")]
    public class UnitOutlineConfigSO : ScriptableObject
    {
        [Header("我方描边配置")]
        public Color FriendlyOutlineColor = Color.white;
        [Range(0, 0.1f)] public float FriendlyOutlineWidth = 0.05f;

        [Header("敌方描边配置 (多层)")]
        public Color EnemyOutlineColor1 = new Color(0.7f, 0.1f, 0.9f, 0.8f);
        [Range(0, 0.1f)] public float EnemyOutlineWidth1 = 0.05f;

        public Color EnemyOutlineColor2 = new Color(0.7f, 0.1f, 0.9f, 0.3f);
        [Range(0, 0.1f)] public float EnemyOutlineWidth2 = 0.1f;
    }
}
