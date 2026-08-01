using NUnit.Framework;

public sealed class CreaturePropertyManagerTests
{
    [Test]
    public void Managers_KeepMutablePropertyGraphsIndependent()
    {
        CreaturePropertyManager first = CreateManager();
        CreaturePropertyManager second = CreateManager();
        PropertyDirectAdditiveModifier modifier =
            PropertyDirectAdditiveModifier.Create((Fix64)25);

        try
        {
            Assert.AreEqual((Fix64)100, first.GetProperty(CreatureMainProperty.Health));
            Assert.AreEqual((Fix64)100, second.GetProperty(CreatureMainProperty.Health));

            first.ModifyMainPropertyValueBuff(
                CreatureMainProperty.Health,
                modifier);

            Assert.AreEqual((Fix64)125, first.GetProperty(CreatureMainProperty.Health));
            Assert.AreEqual((Fix64)100, second.GetProperty(CreatureMainProperty.Health));

            first.ModifyMainPropertyValueBuff(
                CreatureMainProperty.Health,
                modifier,
                false);
            modifier = null;
        }
        finally
        {
            if (modifier != null)
            {
                first.ModifyMainPropertyValueBuff(
                    CreatureMainProperty.Health,
                    modifier,
                    false);
            }

            first.Dispose();
            second.Dispose();
        }
    }

    [Test]
    public void ReusedPropertyNodes_ReregisterDirtyPropagation()
    {
        CreaturePropertyManager first = CreateManager();
        Assert.AreEqual((Fix64)100, first.GetProperty(CreatureMainProperty.Health));
        first.Dispose();

        CreaturePropertyManager reused = CreateManager();
        PropertyDirectAdditiveModifier modifier =
            PropertyDirectAdditiveModifier.Create((Fix64)25);
        try
        {
            Assert.AreEqual((Fix64)100, reused.GetProperty(CreatureMainProperty.Health));
            reused.ModifyMainPropertyValueBuff(
                CreatureMainProperty.Health,
                modifier);
            Assert.AreEqual((Fix64)125, reused.GetProperty(CreatureMainProperty.Health));
            reused.ModifyMainPropertyValueBuff(
                CreatureMainProperty.Health,
                modifier,
                false);
            modifier = null;
        }
        finally
        {
            if (modifier != null)
            {
                reused.ModifyMainPropertyValueBuff(
                    CreatureMainProperty.Health,
                    modifier,
                    false);
            }

            reused.Dispose();
        }
    }

    [Test]
    public void Dispose_ReleasesPropertyGraphAndRejectsDoubleRelease()
    {
        CreaturePropertyManager manager = CreateManager();
        PropertyManager propertyManager = manager.propertyManager;

        manager.Dispose();

        Assert.IsNull(manager.propertyManager);
        Assert.IsNull(propertyManager.GetProperty(nameof(CreatureMainProperty.Health)));
        Assert.Throws<System.InvalidOperationException>(() => manager.Dispose());
    }

    private static CreaturePropertyManager CreateManager()
    {
        return new CreaturePropertyManager(
            property => property == CreatureMainProperty.Health
                ? (Fix64)100
                : Fix64.One);
    }
}
