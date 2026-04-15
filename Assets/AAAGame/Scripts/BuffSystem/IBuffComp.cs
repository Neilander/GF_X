namespace AAAGame.Scripts.BuffSystem
{
    /// <summary>
    /// Buff组件接口
    /// </summary>
    public interface IBuffComp : ICapability
    {
        /// <summary>
        /// 初始化Buff组件
        /// </summary>
        void Init(MAEntity entity);

        /// <summary>
        /// 添加Buff
        /// </summary>
        bool AddBuff(BuffData buffData, MAEntity hostEntity);

        /// <summary>
        /// 更新Buff
        /// </summary>
        void UpdateBuff(float deltaTime);

        /// <summary>
        /// 移除指定 Buff。
        /// </summary>
        bool RemoveBuff(string buffId);

        /// <summary>
        /// 是否包含指定 Buff。
        /// </summary>
        bool HasBuff(string buffId);



        /// <summary>
        /// 宿主死亡时处理
        /// </summary>
        void OnHostDead();

        /// <summary>
        /// 宿主击杀目标时处理
        /// </summary>
        void OnKill(MAEntity target);
    }
}