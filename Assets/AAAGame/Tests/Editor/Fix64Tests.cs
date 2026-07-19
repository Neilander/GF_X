using NUnit.Framework;

public sealed class Fix64Tests
{
    [TestCase(5.5f, 2.25f, 3.25f)]
    [TestCase(-2.5f, 1.25f, -3.75f)]
    public void FloatMinusFix64_SubtractsRightOperand(float left, float right, float expected)
    {
        Fix64 result = left - (Fix64)right;

        Assert.AreEqual((Fix64)expected, result);
    }

    [Test]
    public void AdditionOverflow_CurrentlyWrapsUnchecked()
    {
        Fix64 maxRaw = Fix64.FromRaw(long.MaxValue);

        Fix64 result = maxRaw + Fix64.FromRaw(1L);

        Assert.AreEqual(long.MinValue, result.RawValue);
    }

    [Test]
    public void DistanceConversion_QuantizesOnlyFinalWorldValue()
    {
        Fix64 result = DistanceUnitConverter.ConvertToWorld((Fix64)10);

        Assert.AreEqual(((Fix64)0.5f).RawValue, result.RawValue);
    }

    [TestCase(1, 205L)]
    [TestCase(-1, -205L)]
    public void DistanceConversion_PreservesFix64OutwardQuantization(int tableValue, long expectedRaw)
    {
        Fix64 result = DistanceUnitConverter.ConvertToWorld((Fix64)tableValue);

        Assert.AreEqual(expectedRaw, result.RawValue);
    }

    [Test]
    public void FixVector3Equals_MatchingBoxedVector_ReturnsTrue()
    {
        var value = new FixVector3((Fix64)1, (Fix64)2, (Fix64)3);
        object sameValue = new FixVector3((Fix64)1, (Fix64)2, (Fix64)3);

        Assert.IsTrue(value.Equals(sameValue));
    }

    [Test]
    public void FixVector3Equals_DifferentOrWrongType_ReturnsFalse()
    {
        var value = new FixVector3((Fix64)1, (Fix64)2, (Fix64)3);
        object differentValue = new FixVector3((Fix64)1, (Fix64)2, (Fix64)4);
        object wrongType = new FixVector2((Fix64)1, (Fix64)2);

        Assert.IsFalse(value.Equals(differentValue));
        Assert.IsFalse(value.Equals(wrongType));
    }

    [Test]
    public void FixVector3Indexer_SetIndexTwo_UpdatesZOnly()
    {
        var value = new FixVector3((Fix64)1, (Fix64)2, (Fix64)3);

        value[2] = (Fix64)4;

        Assert.AreEqual((Fix64)1, value.x);
        Assert.AreEqual((Fix64)2, value.y);
        Assert.AreEqual((Fix64)4, value.z);
    }
}
