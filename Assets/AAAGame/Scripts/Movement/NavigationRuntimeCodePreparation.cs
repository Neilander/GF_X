using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

internal static class NavigationRuntimeCodePreparation
{
#if !ENABLE_IL2CPP
    private static readonly HashSet<Type> PreparedTypes = new HashSet<Type>();
#endif

    public static void Prepare(Type root)
    {
#if !ENABLE_IL2CPP
        if (root == null || root.ContainsGenericParameters)
            throw new ArgumentException("Runtime code preparation requires a closed root type.", nameof(root));
        if (PreparedTypes.Contains(root))
            return;

        var watch = Stopwatch.StartNew();
        var pending = new Stack<Type>();
        pending.Push(root);
        int prepared = 0;
        int openGenericTypes = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        while (pending.Count != 0)
        {
            Type type = pending.Pop();
            if (type.ContainsGenericParameters)
            {
                openGenericTypes++;
                continue;
            }
            foreach (Type nested in type.GetNestedTypes(flags))
                pending.Push(nested);
            var methods = new List<MethodBase>(type.GetMethods(flags));
            methods.AddRange(type.GetConstructors(flags));
            foreach (MethodBase method in methods)
            {
                // Open generic and runtime-provided bodies need their actual closed call sites.
                if (method.ContainsGenericParameters || method.GetMethodBody() == null)
                    continue;
                if (method.MethodHandle.GetFunctionPointer() == IntPtr.Zero)
                    throw new InvalidOperationException($"Runtime code preparation failed: {type.FullName}.{method.Name}.");
                prepared++;
            }
        }
        PreparedTypes.Add(root);
        UnityEngine.Debug.Log($"[NavigationRuntimeCode] type={root.FullName} methods={prepared} openGenericTypes={openGenericTypes} elapsedMs={watch.Elapsed.TotalMilliseconds:F3}");
#endif
    }
}
