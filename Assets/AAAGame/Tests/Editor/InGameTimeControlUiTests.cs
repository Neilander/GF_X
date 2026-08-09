using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class InGameTimeControlUiTests
{
    [Test]
    public void InGamePrefab_HasBoundPauseAndSpeedButtonsWithIcons()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/InGameUIForm.prefab");
        Assert.NotNull(prefab);
        Transform controls = prefab.transform.Find("TimeControls");
        Assert.NotNull(controls);
        Assert.NotNull(controls.Find("PauseButton").GetComponent<Button>());
        Assert.NotNull(controls.Find("PauseButton/Icon").GetComponent<TextMeshProUGUI>());
        Assert.NotNull(controls.Find("SpeedButton").GetComponent<Button>());
        Assert.NotNull(controls.Find("SpeedButton/Icon").GetComponent<TextMeshProUGUI>());

        var serialized = new SerializedObject(prefab.GetComponent<InGameUIForm>());
        Assert.NotNull(serialized.FindProperty("varPauseButton").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("varSpeedButton").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("varPauseIcon").objectReferenceValue);
        Assert.NotNull(serialized.FindProperty("varSpeedIcon").objectReferenceValue);
    }

    [Test]
    public void InGamePrefab_TimeControlsArePlacedLeftOfMinimap()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/AAAGame/Prefabs/UI/InGameUIForm.prefab");
        GameObject instance = Object.Instantiate(prefab);

        try
        {
            var root = (RectTransform)instance.transform;
            root.sizeDelta = new Vector2(1920f, 1080f);
            Canvas.ForceUpdateCanvases();

            var controls = (RectTransform)root.Find("TimeControls");
            var serialized = new SerializedObject(instance.GetComponent<InGameUIForm>());
            var minimapMask = (RectTransform)serialized.FindProperty("varMiniMapMask").objectReferenceValue;
            var minimapArea = (RectTransform)minimapMask.parent;
            Bounds controlsBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, controls);
            Bounds minimapBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, minimapArea);

            Assert.That(
                minimapBounds.min.x - controlsBounds.max.x,
                Is.GreaterThanOrEqualTo(12f),
                "Time controls must remain fully left of the minimap.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void InputActions_HavePauseAndSpeedKeyboardShortcuts()
    {
        string json = File.ReadAllText("Assets/InputSystem_Actions.inputactions");
        InputActionAsset actions = InputActionAsset.FromJson(json);
        InputAction pause = actions.FindAction("Player/Pause", true);
        InputAction speed = actions.FindAction("Player/Speed", true);

        Assert.That(pause.bindings, Has.Some.Matches<InputBinding>(binding => binding.path == "<Keyboard>/p"));
        Assert.That(speed.bindings, Has.Some.Matches<InputBinding>(binding => binding.path == "<Keyboard>/tab"));
    }

    [Test]
    public void CardUiAnimations_DoNotBindToBattleTimeScaleOrUnscaledEscTime()
    {
        string[] files = Directory.GetFiles(
            "Assets/AAAGame/Scripts/Card/UI",
            "*.cs",
            SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            StringAssert.DoesNotContain("AnimationRatePresenter", source, file);
            StringAssert.DoesNotContain("LogicTimeControlService.AnimationScale", source, file);
            StringAssert.DoesNotContain("AnimatorUpdateMode.UnscaledTime", source, file);
        }
    }
}
