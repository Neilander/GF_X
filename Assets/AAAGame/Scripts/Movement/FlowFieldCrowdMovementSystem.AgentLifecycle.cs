using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AAAGame.FlowPath;
using AAAGame.MiniMap.FOG3;
using GameFramework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MainThreadFrameProfiler = UnityGameFramework.Runtime.MainThreadFrameProfiler;
using MainThreadPerfScope = UnityGameFramework.Runtime.MainThreadPerfScope;

public static partial class FlowFieldCrowdMovementSystem
{
    public static void RegisterAgent(IEntityContext entity)
    {
        Fix64 radiusFixed = ResolveCollisionRadiusFixed(entity);
        RegisterAgentInternal(entity, (float)radiusFixed, ResolveExplicitAgentTypeId(entity, nameof(RegisterAgent)));
    }

}
