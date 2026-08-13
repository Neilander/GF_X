using GameFramework;
using GameFramework.Fsm;
using GameFramework.Procedure;
using Obfuz;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
/// <summary>
/// 热更逻辑入口
/// </summary>
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName | Obfuz.ObfuzScope.MethodName)]
public class HotfixEntry
{
    public static async void StartHotfixLogic(bool enableHotfix)
    {
        Log.Info<bool>("Hotfix Enable:{0}", enableHotfix);
        AwaitExtension.SubscribeEvent();


        GFBuiltin.Fsm.DestroyFsm<IProcedureManager>();
        var fsmManager = GameFrameworkEntry.GetModule<IFsmManager>();
        var procManager = GameFrameworkEntry.GetModule<IProcedureManager>();
        var appConfig = await AppConfigs.GetInstanceSync();

        var procedureNames = appConfig.Procedures ?? Array.Empty<string>();
        var resolvedProcedures = new List<ProcedureBase>(procedureNames.Length);
        var assemblies = Utility.Assembly.GetAssemblies();

        System.Type ResolveTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            var tp = Type.GetType(fullName, throwOnError: false);
            if (tp != null) return tp;
            foreach (var asm in assemblies)
            {
                try
                {
                    tp = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (tp != null) return tp;
                }
                catch
                {
                    // 忽略无法读取类型的程序集
                }
            }
            return null;
        }

        foreach (var name in procedureNames)
        {
            var tp = ResolveTypeByFullName(name);
            if (tp == null)
            {
                Log.Warning("Procedure type not found: {0}", name);
                continue;
            }
            var instance = Activator.CreateInstance(tp) as ProcedureBase;
            if (instance == null)
            {
                Log.Warning("Failed to instantiate Procedure: {0}", name);
                continue;
            }
            resolvedProcedures.Add(instance);
        }

        procManager.Initialize(fsmManager, resolvedProcedures.ToArray());
        procManager.StartProcedure<PreloadProcedure>();
    }
}
