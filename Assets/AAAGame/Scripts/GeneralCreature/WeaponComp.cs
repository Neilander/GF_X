/// <summary>
/// 武器组件：纯信息提供，不执行攻击逻辑。
/// Brain 和 AtkComp 从这里查询攻击距离等武器属性。
/// 支持运行时切换武器。
/// </summary>
public class WeaponComp : ICapability
{
    public Weapon Data { get; private set; }

    /// <summary>武器攻击距离（已转换为游戏单位）</summary>
    public Fix64 AttackRange => Data != null ? DistanceUnitConverter.ConvertToWorld(Data.Range) : Fix64.Zero;

    public WeaponComp(Weapon data)
    {
        Data = data;
    }

    public void SwapWeapon(Weapon newData)
    {
        Data = newData;
    }

    public void ShutDown() { }
    public void Resume() { }
}
