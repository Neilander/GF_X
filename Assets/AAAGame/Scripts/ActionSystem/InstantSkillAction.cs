using System;
using UnityEngine;

[CreateAssetMenu(fileName = "InstantSkillAction", menuName = "Actions/Instant Skill")]
public sealed class InstantSkillAction : BasicAction
{
    protected override ActionInfo CreateInfo(IEntityContext body)
    {
        return new InstantSkillActionInfo
        {
            selfBody = body,
        };
    }

    protected override void OnStart(ActionInfo info)
    {
        Advance(GetInfo(info));
    }

    protected override void OnUpdate(ActionInfo info, Fix64 deltaTime)
    {
        Advance(GetInfo(info));
    }

    private void Advance(InstantSkillActionInfo info)
    {
        if (info.windUp < Fix64.Zero || info.windDown < Fix64.Zero)
            throw new InvalidOperationException("Instant skill wind-up and wind-down must be non-negative.");

        if (!info.effectCommitted && info.elapsed >= info.windUp)
            info.effectCommitted = true;

        if (info.elapsed >= info.windUp + info.windDown)
            FinishAction(info);
    }

    private static InstantSkillActionInfo GetInfo(ActionInfo info)
    {
        return info as InstantSkillActionInfo
               ?? throw new InvalidOperationException("InstantSkillAction requires InstantSkillActionInfo.");
    }
}

public sealed class InstantSkillActionInfo : ActionInfo
{
    public Fix64 windUp;
    public Fix64 windDown;
    public bool effectCommitted;
    public bool effectApplied;
}
