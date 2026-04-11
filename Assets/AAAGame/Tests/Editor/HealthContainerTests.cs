using NUnit.Framework;

public class HealthContainerTests
{
    [Test]
    public void 初始化后血量等于最大血量()
    {
        var hc = new HealthContainer();
        hc.Init((Fix64)100f);

        Assert.AreEqual((Fix64)100f, hc.maxHealth);
        Assert.AreEqual((Fix64)100f, hc.currentHealth);
    }

    [Test]
    public void 扣血后血量减少()
    {
        var hc = new HealthContainer();
        hc.Init((Fix64)100f);

        hc.ModifyHealth(HealthModifyType.reduce, (Fix64)30f, false);

        Assert.AreEqual(70f, (float)hc.currentHealth, 0.01f);
    }

    [Test]
    public void 血量不会低于零()
    {
        var hc = new HealthContainer();
        hc.Init((Fix64)50f);

        hc.ModifyHealth(HealthModifyType.reduce, (Fix64)999f, false);

        Assert.AreEqual(0f, (float)hc.currentHealth, 0.01f);
    }

    [Test]
    public void 血量不会超过最大值()
    {
        var hc = new HealthContainer();
        hc.Init((Fix64)100f);
        hc.ModifyHealth(HealthModifyType.reduce, (Fix64)50f, false);

        hc.ModifyHealth(HealthModifyType.set, (Fix64)999f, false);

        Assert.AreEqual(100f, (float)hc.currentHealth, 0.01f);
    }

    [Test]
    public void 尝试扣血不改变实际血量()
    {
        var hc = new HealthContainer();
        hc.Init((Fix64)100f);

        Fix64 result = hc.ModifyHealth(HealthModifyType.reduce, (Fix64)30f, true);

        Assert.AreEqual(70f, (float)result, 0.01f);
        Assert.AreEqual(100f, (float)hc.currentHealth, 0.01f); // 实际血量不变
    }

    [Test]
    public void 乘法修改血量()
    {
        var hc = new HealthContainer();
        hc.Init((Fix64)100f);
        hc.ModifyHealth(HealthModifyType.reduce, (Fix64)40f, false); // 剩 60

        hc.ModifyHealth(HealthModifyType.mult, (Fix64)0.5f, false);

        Assert.AreEqual(30f, (float)hc.currentHealth, 0.01f); // 60 * 0.5 = 30
    }
}
