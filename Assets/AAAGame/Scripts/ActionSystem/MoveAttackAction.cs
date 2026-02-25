using UnityEngine;


[CreateAssetMenu(fileName = "MoveAttackAction", menuName = "Actions/MoveAttack")]
public class MoveAttackAction : NormalAttackAction
{
    [Header("攻击中移动")] [SerializeField] protected bool useInputMove = false;
    [SerializeField] protected float moveSpeed = 0f;
    
    [Header("速度线性改变modifier")]
    [SerializeField] protected float speedChangeMultiplier = 1f;

    protected override void OnStart(ActionInfo info)
    {
        base.OnStart(info);
        //如果use input move，就按照开始时的方向按键来进行一段位移
        if (useInputMove)
        {
            InputModel _inputModel = info.inputs;
            Vector2 translated = InputDirTranslator.Translate(
                new FixVector2(_inputModel.MoveX, _inputModel.MoveY)
            );

            Vector3 move = new Vector3(translated.x, 0f, translated.y);
            (info.selfBody as MAEntity).durationMoveEffectComp.StartDurationAdditionalMove(duration
                , move * moveSpeed,
                x =>
                {
                    return x * speedChangeMultiplier;
                });
        }
    }
}