using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class InputActionConfigurationTests : InputTestFixture
{
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    [Test]
    public void Space_DoesNotSubmitUi_ButEnterDoes()
    {
        InputActionAsset actions = LoadActionsClone();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

        try
        {
            InputAction submit = actions.FindAction("UI/Submit", true);
            submit.Enable();

            Press(keyboard.spaceKey);
            Assert.That(submit.WasPressedThisFrame(), Is.False);
            Release(keyboard.spaceKey);

            Press(keyboard.enterKey);
            Assert.That(submit.WasPressedThisFrame(), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(actions);
        }
    }

    [Test]
    public void NumberKeys_TriggerMatchingCardActions()
    {
        InputActionAsset actions = LoadActionsClone();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        KeyControl[] keys =
        {
            keyboard.digit1Key,
            keyboard.digit2Key,
            keyboard.digit3Key,
            keyboard.digit4Key
        };

        try
        {
            for (int i = 0; i < keys.Length; i++)
            {
                InputAction action = actions.FindAction($"Player/Card{i + 1}", true);
                action.Enable();

                Press(keys[i]);
                Assert.That(action.WasPressedThisFrame(), Is.True, action.name);
                Release(keys[i]);

                action.Disable();
            }
        }
        finally
        {
            Object.DestroyImmediate(actions);
        }
    }

    [Test]
    public void ObsoleteCardToggleAction_IsAbsent()
    {
        InputActionAsset actions = LoadActionsClone();

        try
        {
            Assert.That(actions.FindAction("Player/CardToggle", false), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(actions);
        }
    }

    private static InputActionAsset LoadActionsClone()
    {
        InputActionAsset source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        Assert.That(source, Is.Not.Null, InputActionsPath);
        return InputActionAsset.FromJson(source.ToJson());
    }
}
