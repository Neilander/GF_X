using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class InputDirTranslator
{
    public static Vector2 Translate(Vector2 inputData)
    {
        if (inputData.sqrMagnitude <= 0.0001f)
        {
            return Vector2.zero;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return inputData;
        }

        float yaw = cam.transform.eulerAngles.y;
        Vector3 worldDir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(inputData.x, 0f, inputData.y);
        worldDir = Vector3.ClampMagnitude(worldDir, 1f);
        return new Vector2(worldDir.x, worldDir.z);
    }
}
