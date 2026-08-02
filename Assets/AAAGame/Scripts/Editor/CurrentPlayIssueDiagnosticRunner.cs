using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class CurrentPlayIssueDiagnosticRunner
{
    private const string RequestRelativePath = "Logs/RunCurrentPlayIssueDiagnostic.request";
    private const string ResultRelativePath = "Logs/CurrentPlayIssueDiagnosticResult.txt";
    private const double SampleDurationSeconds = 3.0;

    private static readonly string[] PrefabPaths =
    {
        "Assets/AAAGame/Prefabs/Entity/Building/Buil_Def_Lv0.prefab",
        "Assets/AAAGame/Prefabs/Entity/Building/Buil_Army_Lv0.prefab",
        "Assets/AAAGame/Prefabs/Entity/Soldier/超时骑手.prefab",
        "Assets/AAAGame/Prefabs/Entity/Soldier/灭火员.prefab",
        "Assets/AAAGame/Prefabs/Entity/Soldier/剔骨狂魔.prefab",
        "Assets/AAAGame/Prefabs/Entity/Soldier/英雄.prefab",
    };

    private static readonly Dictionary<int, RuntimeSample> InitialSamples = new();
    private static double s_SampleStartTime;
    private static bool s_Sampling;

    static CurrentPlayIssueDiagnosticRunner()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        string requestPath = GetProjectPath(RequestRelativePath);
        if (!File.Exists(requestPath))
            return;

        File.Delete(requestPath);
        EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/AAAGame/Diagnostics/Capture Current Play Issues")]
    public static void Run()
    {
        var report = new StringBuilder(16384);
        report.AppendLine("RESULT=RUNNING");
        report.Append("capturedUtc=").AppendLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        AppendPrefabDiagnostics(report);

        if (!EditorApplication.isPlaying || !LogicEntityStateStore.IsActive)
        {
            report.AppendLine("runtime=not-playing");
            WriteResult(report.ToString().Replace("RESULT=RUNNING", "RESULT=PASS"));
            return;
        }

        InitialSamples.Clear();
        CaptureRuntimeSamples(InitialSamples);
        report.Append("runtimeInitialCount=").AppendLine(InitialSamples.Count.ToString(CultureInfo.InvariantCulture));
        WriteResult(report.ToString());
        s_SampleStartTime = EditorApplication.timeSinceStartup;
        s_Sampling = true;
    }

    private static void Update()
    {
        if (!s_Sampling || EditorApplication.timeSinceStartup - s_SampleStartTime < SampleDurationSeconds)
            return;

        s_Sampling = false;
        var finalSamples = new Dictionary<int, RuntimeSample>();
        CaptureRuntimeSamples(finalSamples);
        string existing = File.ReadAllText(GetProjectPath(ResultRelativePath));
        var report = new StringBuilder(existing.Length + 16384);
        report.Append(existing);
        report.Append("runtimeFinalCount=").AppendLine(finalSamples.Count.ToString(CultureInfo.InvariantCulture));

        foreach (KeyValuePair<int, RuntimeSample> pair in finalSamples)
        {
            RuntimeSample current = pair.Value;
            InitialSamples.TryGetValue(pair.Key, out RuntimeSample initial);
            report.Append("ENEMY ").Append(current.Description)
                .Append(" sampleAttackDelta=").Append(current.AttackCount - initial.AttackCount)
                .Append(" sampleMoveDistance=").Append(Vector3.Distance(current.Position, initial.Position).ToString("F3", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        report.Replace("RESULT=RUNNING", "RESULT=PASS");
        WriteResult(report.ToString());
        Debug.Log("AVENGE_CURRENT_PLAY_ISSUE_DIAGNOSTIC_PASS\n" + report);
    }

    private static void AppendPrefabDiagnostics(StringBuilder report)
    {
        for (int i = 0; i < PrefabPaths.Length; i++)
        {
            string path = PrefabPaths[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                                ?? throw new FileNotFoundException("Diagnostic prefab is missing.", path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate diagnostic prefab. path={path}.");

            try
            {
                Transform display = instance.transform.Find(EntityPresentationBindings.DisplayObjectName)
                                    ?? throw new InvalidOperationException($"Prefab has no direct Display child. path={path}.");
                Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
                bool hasBounds = false;
                Bounds bounds = default;
                int visualRendererCount = 0;
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                        continue;
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                    visualRendererCount++;
                }
                if (!hasBounds)
                    throw new InvalidOperationException($"Prefab Display has no mesh renderer bounds. path={path}.");

                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                int displayColliderCount = 0;
                int solidColliderCount = 0;
                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    if (collider.transform.IsChildOf(display))
                        displayColliderCount++;
                    if (!collider.isTrigger)
                        solidColliderCount++;
                }

                report.Append("PREFAB path=").Append(path)
                    .Append(" displayScale=").Append(display.localScale)
                    .Append(" visualRenderers=").Append(visualRendererCount)
                    .Append(" boundsSize=").Append(bounds.size)
                    .Append(" boundsCenter=").Append(bounds.center)
                    .Append(" colliders=").Append(colliders.Length)
                    .Append(" displayColliders=").Append(displayColliderCount)
                    .Append(" solidColliders=").Append(solidColliderCount)
                    .AppendLine();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }

    private static void CaptureRuntimeSamples(Dictionary<int, RuntimeSample> samples)
    {
        if (!EditorApplication.isPlaying || !LogicEntityStateStore.IsActive)
            return;

        IList<IEntityContext> entities = EntityRegistry.AllEntities;
        for (int i = 0; i < entities.Count; i++)
        {
            IEntityContext entity = entities[i];
            if (entity == null || !entity.Alive || entity.Side != SideType.EnemySide || entity.CharacterData == null)
                continue;

            IEntityContext target = entity.TargetComp?.CurrentTarget;
            DirectAtkComp directAttack = entity.AtkComp as DirectAtkComp;
            int attackCount = directAttack?.AttackCount ?? -1;
            string attackState = directAttack != null ? directAttack.State.ToString() : entity.AtkComp?.GetType().Name ?? "null";
            string brainState = entity.Brain is SoldierAIBrain soldierBrain
                ? soldierBrain.State.ToString()
                : entity.Brain?.GetType().Name ?? "null";
            string targetDescription = target == null
                ? "null"
                : target.LogicEntityId.Value + ":" + target.CharacterKey + ":alive=" + target.Alive;
            string distance = target == null
                ? "n/a"
                : ((float)entity.LogicFrameDistanceToTargetSurfaceFixed(target)).ToString("F3", CultureInfo.InvariantCulture);
            Fix64 speed = entity.GetProperty(CreatureMainProperty.Speed);
            bool hasDefendSpeed = entity.BuffComp != null && entity.BuffComp.HasBuff(LogicUnitConfigurator.DefendSpeedBuffId);
            bool renderVisible = false;
            if (LogicEntityLifecycleService.TryGetBoundView(entity.LogicEntityId, out MAEntity view) && view != null)
            {
                Renderer[] renderers = view.PresentationBindings.DisplayRoot.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    renderVisible |= renderers[rendererIndex].enabled;
            }

            string description = new StringBuilder(512)
                .Append("id=").Append(entity.LogicEntityId.Value)
                .Append(" key=").Append(entity.CharacterKey)
                .Append(" brain=").Append(brainState)
                .Append(" brainAttack=").Append(entity.Brain?.Attack ?? false)
                .Append(" target=").Append(targetDescription)
                .Append(" distance=").Append(distance)
                .Append(" atkState=").Append(attackState)
                .Append(" attackCount=").Append(attackCount)
                .Append(" moving=").Append(entity.MoveComp?.IsMoving ?? false)
                .Append(" speed=").Append(((float)speed).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" speedRaw=").Append(speed.RawValue)
                .Append(" defendSpeedBuff=").Append(hasDefendSpeed)
                .Append(" renderVisible=").Append(renderVisible)
                .ToString();
            samples.Add(entity.LogicEntityId.Value, new RuntimeSample(entity.Position, attackCount, description));
        }
    }

    private static void WriteResult(string content)
    {
        string resultPath = GetProjectPath(ResultRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        File.WriteAllText(resultPath, content, new UTF8Encoding(false));
    }

    private static string GetProjectPath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
    }

    private readonly struct RuntimeSample
    {
        public RuntimeSample(Vector3 position, int attackCount, string description)
        {
            Position = position;
            AttackCount = attackCount;
            Description = description;
        }

        public Vector3 Position { get; }
        public int AttackCount { get; }
        public string Description { get; }
    }
}
