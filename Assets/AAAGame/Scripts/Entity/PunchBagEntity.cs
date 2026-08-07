public class PunchBagEntity : GeneralCreature
{
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        Side = SideType.EnemySide;
    }

}
