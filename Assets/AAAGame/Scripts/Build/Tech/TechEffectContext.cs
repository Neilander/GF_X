/// <summary>
/// 科技效果运行时上下文。
/// </summary>
public sealed class TechEffectContext
{
    public string TechId { get; set; }
    public int OwnerFactionId { get; set; }
    public TechData TechData { get; set; }
    public ResolvedTechUnitScope ResolvedScope { get; set; }
    public GlobalBuffManager GlobalBuffManager { get; set; }
}
