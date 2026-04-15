/// <summary>
/// 统一无敌状态 Buff（被动标记型）。
/// 真实的授予/移除由 MAEntity 的来源注册机制维护。
/// </summary>
public sealed class InvincibleStateBuff : BuffCallback
{
    public const string BuffId = "state_invincible";
}