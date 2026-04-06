using UnityEngine;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌系统日志工具
    /// 替代 GameFramework 的 Log 类
    /// </summary>
    public static class CardLogger
    {
        private static bool enableLog = true;

        public static void SetLogEnabled(bool enabled)
        {
            enableLog = enabled;
        }

        public static void Log(string message)
        {
            if (!enableLog) return;
            Debug.Log($"<color=#2BD988>[Card] {message}</color>");
        }

        public static void LogWarning(string message)
        {
            if (!enableLog) return;
            Debug.LogWarning($"<color=#F2A20C>[Card] {message}</color>");
        }

        public static void LogError(string message)
        {
            Debug.LogError($"<color=#F22E2E>[Card] {message}</color>");
        }

        public static void Info(string message)
        {
            Log(message);
        }

        public static void Warning(string message)
        {
            LogWarning(message);
        }

        public static void Error(string message)
        {
            LogError(message);
        }
    }
}
