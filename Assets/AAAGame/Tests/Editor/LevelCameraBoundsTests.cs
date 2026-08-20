using NUnit.Framework;
using UnityEngine;

public sealed class LevelCameraBoundsTests
{
    [Test]
    public void ClampWorldPoint_MissingCorner_UsesNearestAuthoredArm()
    {
        var gameObject = new GameObject(nameof(LevelCameraBoundsTests));
        LevelCameraBounds bounds = gameObject.AddComponent<LevelCameraBounds>();
        bounds.SetData(
            1f,
            new[]
            {
                new WallGridCell(0, 0),
                new WallGridCell(1, 0),
                new WallGridCell(0, 1),
            });

        try
        {
            Vector3 clamped = bounds.ClampWorldPoint(new Vector3(1.25f, 3f, 1.25f));

            Assert.That(clamped.y, Is.EqualTo(3f));
            Assert.That(clamped.x, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(clamped.z, Is.EqualTo(0.5f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void SetData_ContiguousCells_AreCollapsedIntoOneSpan()
    {
        var gameObject = new GameObject(nameof(LevelCameraBoundsTests));
        LevelCameraBounds bounds = gameObject.AddComponent<LevelCameraBounds>();

        try
        {
            bounds.SetData(
                1f,
                new[]
                {
                    new WallGridCell(2, 4),
                    new WallGridCell(0, 4),
                    new WallGridCell(1, 4),
                });

            Assert.That(bounds.Spans, Has.Length.EqualTo(1));
            Assert.That(bounds.Spans[0].Row, Is.EqualTo(4));
            Assert.That(bounds.Spans[0].MinColumn, Is.EqualTo(0));
            Assert.That(bounds.Spans[0].MaxColumn, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }
}
