public sealed class MoveAtkComp : DirectAtkComp
{
    protected override bool ShouldLockMoveDuringAttack()
    {
        return false;
    }
}
