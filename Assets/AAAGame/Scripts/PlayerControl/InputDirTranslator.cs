using System;
using UnityEngine;

public static class InputDirTranslator
{
    public static FixVector2 TranslateAndQuantize(Vector2 inputData, Camera camera)
    {
        if (!float.IsFinite(inputData.x) || !float.IsFinite(inputData.y))
            throw new ArgumentOutOfRangeException(nameof(inputData), inputData, "Move input must be finite.");

        if (inputData.sqrMagnitude <= 0.0001f)
            return FixVector2.Zero;
        if (camera == null)
            throw new ArgumentNullException(nameof(camera), "Non-zero move input requires a camera at the input sampling boundary.");

        Vector3 worldDirection = new Vector3(inputData.x, 0f, inputData.y);
        float yaw = camera.transform.eulerAngles.y;
        worldDirection = Quaternion.Euler(0f, yaw, 0f) * worldDirection;

        worldDirection = Vector3.ClampMagnitude(worldDirection, 1f);
        return new FixVector2((Fix64)worldDirection.x, (Fix64)worldDirection.z);
    }

    public static Vector2 Translate(Vector2 inputData)
    {
        return TranslateAndQuantize(inputData, Camera.main);
    }
}
