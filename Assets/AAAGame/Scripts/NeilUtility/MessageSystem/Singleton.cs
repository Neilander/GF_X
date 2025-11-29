using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BaseUtility
{
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        public static bool HasInstance => _instance != null;
        public static T Instance
        {
            get
            {
                // 如果已经存在，直接返回
                if (_instance != null) return _instance;

                // 场景中寻找已经存在的
                _instance = FindFirstObjectByType<T>();
                if (_instance != null) return _instance;
                // 如果还没有，就创建一个新的空对象并挂载
                var obj = new GameObject(typeof(T).Name);
                _instance = obj.AddComponent<T>();
                DontDestroyOnLoad(obj);

                return _instance;
            }
        }

        // 防止重建/重复
        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject); 
            }
        }
    }
}

