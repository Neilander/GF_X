using NUnit.Framework;

public sealed class Fix64Tests
{
    private LogicAuthorityConfigTestScope m_ConfigScope;

    [SetUp]
    public void SetUp()
    {
        m_ConfigScope = new LogicAuthorityConfigTestScope();
    }

    [TearDown]
    public void TearDown()
    {
        m_ConfigScope.Dispose();
        m_ConfigScope = null;
    }

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
    public void Vector2Magnitude_DiagonalBelowSquaredRawResolution_RemainsNonZero()
    {
        var value = new FixVector2(Fix64.FromRaw(51), Fix64.FromRaw(50));

        Assert.AreEqual(71, FixVector2.Magnitude(value).RawValue);
    }

    [Test]
    public void RuntimeAuthorityNumericConstants_UseRawFixedValues()
    {
        string scriptsRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath, "AAAGame", "Scripts");
        string[] files = System.IO.Directory.GetFiles(scriptsRoot, "*.cs", System.IO.SearchOption.AllDirectories);
        System.Array.Sort(files, System.StringComparer.Ordinal);
        var pattern = new System.Text.RegularExpressions.Regex(
            @"\(Fix64\)\s*\(?-?\d+\.\d+(?:[fFdDmM])?\)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var violations = new System.Collections.Generic.List<string>();

        for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            string source = System.IO.File.ReadAllText(files[fileIndex]);
            System.Text.RegularExpressions.MatchCollection matches = pattern.Matches(source);
            for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                violations.Add(
                    files[fileIndex].Substring(scriptsRoot.Length + 1)
                    + ": "
                    + matches[matchIndex].Value);
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Runtime authority numeric constants must use Fix64.FromRaw; float variables remain allowed only at explicit boundaries:\n"
            + string.Join("\n", violations));

        Fix64[] legacyValues =
        {
            (Fix64)0.0001f,
            (Fix64)0.001f,
            (Fix64)0.01f,
            (Fix64)0.04f,
            (Fix64)0.08f,
            (Fix64)0.1f,
            (Fix64)0.15f,
            (Fix64)0.2f,
            (Fix64)0.25f,
            (Fix64)0.35f,
            (Fix64)0.37f,
            (Fix64)0.45f,
            (Fix64)0.5f,
            (Fix64)0.55f,
            (Fix64)0.6f,
            (Fix64)1.5f,
            (Fix64)1.6f,
            (Fix64)2.39996323f,
            (Fix64)0.0001d,
            (Fix64)0.01m,
            (Fix64)1.3m,
            (Fix64)18.5m,
        };
        long[] expectedRaw =
        {
            1, 5, 41, 164, 328, 410, 615, 820, 1024,
            1434, 1516, 1844, 2048, 2253, 2458, 6144, 6554, 9831,
            1, 41, 5325, 75776,
        };
        Assert.AreEqual(expectedRaw.Length, legacyValues.Length);
        for (int i = 0; i < expectedRaw.Length; i++)
            Assert.AreEqual(expectedRaw[i], legacyValues[i].RawValue, $"Legacy runtime Q12 raw mismatch at index {i}.");
    }

    [TestCase("0.015136718751", 63L)]
    [TestCase("-0.015136718751", -63L)]
    [TestCase("12", 49152L)]
    public void FixedConfigParsing_QuantizesInvariantTextDirectlyToRaw(string text, long expectedRaw)
    {
        Fix64 result = FixedConfigReader.ParseFixedConfigText("TestFixedConfig", text);

        Assert.AreEqual(expectedRaw, result.RawValue);
    }

    [Test]
    public void FixedConfigParsing_DoesNotRoundThroughFloat()
    {
        const string text = "0.015136718751";
        float oldFloatValue = float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Fix64 oldFloatRoundTrip = (Fix64)oldFloatValue;
        Fix64 direct = FixedConfigReader.ParseFixedConfigText("TestFixedConfig", text);

        Assert.AreEqual(62L, oldFloatRoundTrip.RawValue, "The regression sample must prove the old float boundary loses one raw unit.");
        Assert.AreEqual(63L, direct.RawValue);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("1e-3")]
    [TestCase(" 0.015")]
    public void FixedConfigParsing_RejectsMissingOrNonInvariantText(string text)
    {
        Assert.Throws<System.InvalidOperationException>(
            () => FixedConfigReader.ParseFixedConfigText("TestFixedConfig", text));
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
