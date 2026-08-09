using NUnit.Framework;
using UnityEngine;
using UnityGameFramework.Runtime;

public sealed class EntityLogicLayerLifecycleTests
{
    private sealed class TestEntityLogic : EntityLogic
    {
        public void Initialize()
        {
            OnInit(null);
        }

        public void Show()
        {
            OnShow(null);
        }

        public void Hide()
        {
            OnHide(false, null);
        }
    }

    [Test]
    public void Hide_RestoresRootWithoutFlatteningAuthoredChildLayer()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Assert.GreaterOrEqual(groundLayer, 0, "Project must define the Ground layer.");

        var root = new GameObject("EntityLogicLayerLifecycle_Root");
        var ground = new GameObject("Ground");
        ground.transform.SetParent(root.transform, false);
        root.layer = 0;
        ground.layer = groundLayer;

        try
        {
            TestEntityLogic logic = root.AddComponent<TestEntityLogic>();
            logic.Initialize();
            logic.Show();

            root.layer = LayerMask.NameToLayer("Ignore Raycast");
            logic.Hide();

            Assert.AreEqual(0, root.layer);
            Assert.AreEqual(groundLayer, ground.layer);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
