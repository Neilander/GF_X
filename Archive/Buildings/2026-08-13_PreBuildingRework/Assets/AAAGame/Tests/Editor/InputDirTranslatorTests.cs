using NUnit.Framework;
using UnityEngine;

public sealed class InputDirTranslatorTests
{
    [Test]
    public void TranslateAndQuantize_FreezesCameraYawIntoFixedWorldDirection()
    {
        var gameObject = new GameObject("InputDirTranslatorTests_Camera");
        try
        {
            Camera camera = gameObject.AddComponent<Camera>();
            camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            FixVector2 direction = InputDirTranslator.TranslateAndQuantize(Vector2.up, camera);

            Assert.AreEqual(Fix64.One.RawValue, direction.x.RawValue);
            Assert.LessOrEqual(System.Math.Abs(direction.y.RawValue), 1L);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void TranslateAndQuantize_NonZeroInputWithoutCameraFailsExplicitly()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            InputDirTranslator.TranslateAndQuantize(Vector2.up, null));
        Assert.AreEqual(FixVector2.Zero, InputDirTranslator.TranslateAndQuantize(Vector2.zero, null));
    }
}
