using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class FlowFieldManualBatchRunner
{
    public static void RunControllerCombatRegression()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("Cannot resolve project root.");

        string logsDir = Path.Combine(projectRoot, "Logs");
        Directory.CreateDirectory(logsDir);
        string resultPath = Path.Combine(logsDir, "FlowControllerCombatManualResult.txt");
        string started = DateTime.UtcNow.ToString("O");

        try
        {
            Type testType = typeof(FlowFieldCrowdMovementSystemTests);
            MethodInfo testMethod = testType.GetMethod(
                "Lv3真实SH13多路径Combat链路使用CharacterController和地形边界时不应卡住",
                BindingFlags.Instance | BindingFlags.Public);

            if (testMethod == null)
                throw new InvalidOperationException("Controller combat regression test method was not found.");

            object instance = Activator.CreateInstance(testType);
            MethodInfo setup = testType.GetMethod("SetUp", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo tearDown = testType.GetMethod("TearDown", BindingFlags.Instance | BindingFlags.Public);

            setup?.Invoke(instance, null);
            try
            {
                testMethod.Invoke(instance, null);
            }
            finally
            {
                tearDown?.Invoke(instance, null);
            }

            File.WriteAllText(
                resultPath,
                "RESULT=PASS" + Environment.NewLine +
                "startedUtc=" + started + Environment.NewLine +
                "finishedUtc=" + DateTime.UtcNow.ToString("O") + Environment.NewLine +
                "method=" + testMethod.Name + Environment.NewLine);
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Exception root = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            File.WriteAllText(
                resultPath,
                "RESULT=FAIL" + Environment.NewLine +
                "startedUtc=" + started + Environment.NewLine +
                "finishedUtc=" + DateTime.UtcNow.ToString("O") + Environment.NewLine +
                root + Environment.NewLine);
            Debug.LogException(root);
            EditorApplication.Exit(1);
        }
    }
}
