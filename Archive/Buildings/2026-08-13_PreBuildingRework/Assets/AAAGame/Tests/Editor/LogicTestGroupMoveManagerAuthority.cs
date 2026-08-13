using System;
using System.Reflection;
using UnityEngine;

internal sealed class LogicTestGroupMoveManagerAuthority : IDisposable
{
    private readonly GameObject m_ManagerObject;
    private readonly GroupMoveConfig m_Config;
    private bool m_IsAttached;

    public GroupMoveManager Manager { get; }

    private LogicTestGroupMoveManagerAuthority(
        GameObject managerObject,
        GroupMoveConfig config,
        GroupMoveManager manager)
    {
        m_ManagerObject = managerObject;
        m_Config = config;
        Manager = manager;
        m_IsAttached = true;
    }

    public static LogicTestGroupMoveManagerAuthority Create(string ownerName)
    {
        if (GroupMoveManager.HasInstance)
            throw new InvalidOperationException($"{ownerName} requires no pre-existing GroupMoveManager.");

        GroupMoveConfig config = ScriptableObject.CreateInstance<GroupMoveConfig>();
        var managerObject = new GameObject($"{ownerName}_GroupMoveManager");
        managerObject.SetActive(false);
        GroupMoveManager manager = managerObject.AddComponent<GroupMoveManager>();

        SetPrivateField(manager, "_config", config);
        InvokePrivate(manager, "CaptureGroupMoveConfig");
        SetInstance(manager);
        return new LogicTestGroupMoveManagerAuthority(managerObject, config, manager);
    }

    public void Detach()
    {
        if (!m_IsAttached)
            return;
        if (!ReferenceEquals(GroupMoveManager.Instance, Manager))
            throw new InvalidOperationException("GroupMoveManager test authority binding is inconsistent.");

        SetInstance(null);
        m_IsAttached = false;
    }

    public void Dispose()
    {
        Detach();
        UnityEngine.Object.DestroyImmediate(m_ManagerObject);
        UnityEngine.Object.DestroyImmediate(m_Config);
    }

    private static void SetInstance(GroupMoveManager manager)
    {
        PropertyInfo property = typeof(GroupMoveManager).GetProperty(
            nameof(GroupMoveManager.Instance),
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("GroupMoveManager.Instance property was not found.");
        property.SetValue(null, manager);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{instance.GetType().Name}.{fieldName} field was not found.");
        field.SetValue(instance, value);
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{instance.GetType().Name}.{methodName} method was not found.");
        method.Invoke(instance, null);
    }
}
