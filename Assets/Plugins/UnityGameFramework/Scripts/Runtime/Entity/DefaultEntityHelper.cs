//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Entity;
using UnityEngine;

namespace UnityGameFramework.Runtime
{
    /// <summary>
    /// 默认实体辅助器。
    /// </summary>
    public class DefaultEntityHelper : EntityHelperBase
    {
        private ResourceComponent m_ResourceComponent = null;

        /// <summary>
        /// 实例化实体。
        /// </summary>
        /// <param name="entityAsset">要实例化的实体资源。</param>
        /// <returns>实例化后的实体。</returns>
        public override object InstantiateEntity(object entityAsset)
        {
            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                return Instantiate((Object)entityAsset);
            }
            finally
            {
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.EntityInstantiate,
                    System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
            }
        }

        /// <summary>
        /// 创建实体。
        /// </summary>
        /// <param name="entityInstance">实体实例。</param>
        /// <param name="entityGroup">实体所属的实体组。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>实体。</returns>
        public override IEntity CreateEntity(object entityInstance, IEntityGroup entityGroup, object userData)
        {
            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            GameObject gameObject = entityInstance as GameObject;
            if (gameObject == null)
            {
                Log.Error("Entity instance is invalid.");
                MainThreadFrameProfiler.Record(
                    MainThreadPerfScope.EntityCreate,
                    System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
                return null;
            }

            Transform transform = gameObject.transform;
            transform.SetParent(((MonoBehaviour)entityGroup.Helper).transform);

            IEntity entity = gameObject.GetOrAddComponent<Entity>();
            MainThreadFrameProfiler.Record(
                MainThreadPerfScope.EntityCreate,
                System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
            return entity;
        }

        /// <summary>
        /// 释放实体。
        /// </summary>
        /// <param name="entityAsset">要释放的实体资源。</param>
        /// <param name="entityInstance">要释放的实体实例。</param>
        public override void ReleaseEntity(object entityAsset, object entityInstance)
        {
            m_ResourceComponent.UnloadAsset(entityAsset);
            Destroy((Object)entityInstance);
        }

        private void Start()
        {
            m_ResourceComponent = GameEntry.GetComponent<ResourceComponent>();
            if (m_ResourceComponent == null)
            {
                Log.Fatal("Resource component is invalid.");
                return;
            }
        }
    }
}
