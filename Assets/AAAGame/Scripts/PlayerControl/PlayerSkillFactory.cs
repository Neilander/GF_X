using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameFramework.Resource;

[CreateAssetMenu(fileName = "PlayerSkillFactory", menuName = "Skill Factory/PlayerSkill")]
public class PlayerSkillFactory : SkillCompFactory
{
    [Header("主动技能")]
    public List<ActiveSkillSO> skills;
    [Header("被动技能")]
    public List<PassiveSkillSO> passiveSkills;

    public override void PrepareRuntimeDependencies()
    {
        PrepareRuntimeSkillAssets(skills, passiveSkills);
    }

    public override ISkillComp CreateSkillComp(IEntityContext gmo)
    {
        if (gmo is not ISkillCompHost host)
            throw new System.InvalidOperationException($"PlayerSkillFactory requires ISkillCompHost. entity={gmo?.GetType().Name}");

        PlayerSkillComp comp = new PlayerSkillComp();

        host.SetSkillComp(comp);
        comp.Init(gmo, RuntimeActiveSkills, RuntimePassiveSkills);
        //BasicAction[] actionSet = {atkAction1, atkAction2, atkAction3};
        //comp.actions = actionSet;
        return comp;
    }
}

public abstract class SkillCompFactory : ScriptableObject
{
    private List<ActiveSkillSO> m_RuntimeActiveSkills;
    private List<PassiveSkillSO> m_RuntimePassiveSkills;

    protected List<ActiveSkillSO> RuntimeActiveSkills => m_RuntimeActiveSkills
        ?? throw new System.InvalidOperationException($"{GetType().Name} runtime active skills were not prepared.");
    protected List<PassiveSkillSO> RuntimePassiveSkills => m_RuntimePassiveSkills
        ?? throw new System.InvalidOperationException($"{GetType().Name} runtime passive skills were not prepared.");

    public abstract void PrepareRuntimeDependencies();
    public abstract ISkillComp CreateSkillComp(IEntityContext gmo);

    protected void PrepareRuntimeSkillAssets(
        IReadOnlyList<ActiveSkillSO> activeSkills,
        IReadOnlyList<PassiveSkillSO> passiveSkills)
    {
        if (LogicFrameRuntime.IsExecutingFrame)
            throw new System.InvalidOperationException("Skill factory runtime assets cannot be prepared during a logic frame.");
        if (m_RuntimeActiveSkills != null || m_RuntimePassiveSkills != null)
            return;
        if (activeSkills == null)
            throw new System.InvalidOperationException($"{GetType().Name} has a null active skill list.");

        m_RuntimeActiveSkills = new List<ActiveSkillSO>(activeSkills.Count);
        for (int i = 0; i < activeSkills.Count; i++)
        {
            ActiveSkillSO skill = activeSkills[i]
                                  ?? throw new System.InvalidOperationException(
                                      $"{GetType().Name} has a null active skill. index={i}.");
            m_RuntimeActiveSkills.Add(skill.CreateRuntimeSnapshot());
        }

        int passiveCount = passiveSkills?.Count ?? 0;
        m_RuntimePassiveSkills = new List<PassiveSkillSO>(passiveCount);
        for (int i = 0; i < passiveCount; i++)
        {
            PassiveSkillSO skill = passiveSkills[i]
                                   ?? throw new System.InvalidOperationException(
                                       $"{GetType().Name} has a null passive skill. index={i}.");
            m_RuntimePassiveSkills.Add(skill.CreateRuntimeSnapshot());
        }
    }

    public static LoadAssetCallbacks SkillFactoryCallBack = new LoadAssetCallbacks(
        (assetName, asset, duration, userData) =>
        {
            SkillCompFactory factory = asset as SkillCompFactory
                                       ?? throw new System.InvalidOperationException(
                                           $"Loaded skill factory has invalid type. asset={assetName}.");
            IEntityContext entity = userData as IEntityContext
                                    ?? throw new System.InvalidOperationException(
                                        $"Loaded skill factory requires an entity context. asset={assetName}.");
            factory.PrepareRuntimeDependencies();
            factory.CreateSkillComp(entity);
        });
}
