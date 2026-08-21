/// <summary>
/// 武器组件：纯信息提供，不执行攻击逻辑。
/// Brain 和 AtkComp 从这里查询攻击距离等武器属性。
/// 支持运行时切换武器。
/// </summary>
public class WeaponComp : ICapability
{
    public Weapon Data { get; private set; }
    public int CurrentAmmo { get; private set; }
    public int MaxAmmo { get; private set; }
    public bool HasAmmunition => MaxAmmo > 0;
    public bool HasAmmoToAttack => !HasAmmunition || CurrentAmmo > 0;

    /// <summary>武器攻击距离（已转换为游戏单位）</summary>
    public Fix64 AttackRange => Data != null ? (Data.Range) : Fix64.Zero;

    public WeaponComp(Weapon data)
    {
        SwapWeapon(data);
    }

    public void SwapWeapon(Weapon newData)
    {
        Data = newData;
        MaxAmmo = ResolveAmmoCapacity(newData);
        CurrentAmmo = MaxAmmo;
    }

    public bool TryConsumeAmmo(int amount)
    {
        if (amount <= 0)
            throw new System.InvalidOperationException($"WeaponComp.TryConsumeAmmo failed: amount must be positive. amount={amount}.");

        if (!HasAmmunition)
            return true;

        if (CurrentAmmo < amount)
            return false;

        CurrentAmmo -= amount;
        return true;
    }

    public void ReloadFull()
    {
        if (!HasAmmunition)
            return;

        CurrentAmmo = MaxAmmo;
    }

    public void RestoreAmmo(int currentAmmo, int maxAmmo)
    {
        int expectedMax = ResolveAmmoCapacity(Data);
        if (maxAmmo != expectedMax)
            throw new System.InvalidOperationException($"WeaponComp.RestoreAmmo failed: max ammo mismatch. expected={expectedMax}, snapshot={maxAmmo}.");
        if (currentAmmo < 0 || currentAmmo > maxAmmo)
            throw new System.InvalidOperationException($"WeaponComp.RestoreAmmo failed: current ammo is invalid. current={currentAmmo}, max={maxAmmo}.");

        MaxAmmo = maxAmmo;
        CurrentAmmo = currentAmmo;
    }

    private static int ResolveAmmoCapacity(Weapon weapon)
    {
        if (weapon == null || weapon.AmmunitionCapacity <= Fix64.Zero)
            return 0;

        return UnityEngine.Mathf.Max(0, (int)weapon.AmmunitionCapacity);
    }

    public void ShutDown() { }
    public void Resume() { }
}
