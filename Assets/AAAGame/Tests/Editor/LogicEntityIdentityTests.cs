using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class LogicEntityIdentityTests
{
    [SetUp]
    public void SetUp()
    {
        EntityRegistry.Clear();
        LogicTimeControlService.BeginTimeline();
        LogicEntityLifecycleService.BeginTimeline();
    }

    [TearDown]
    public void TearDown()
    {
        EntityRegistry.Clear();
        LogicEntityLifecycleService.EndTimeline();
        LogicTimeControlService.EndTimeline();
    }

    [Test]
    public void AllocatorSnapshotRestore_ReplaysSameNextId()
    {
        LogicEntityId first = LogicEntityIdAllocator.Allocate();
        LogicEntityIdAllocatorSnapshot snapshot = LogicEntityIdAllocator.CaptureSnapshot();
        LogicEntityId second = LogicEntityIdAllocator.Allocate();

        LogicEntityIdAllocator.RestoreSnapshot(snapshot);
        LogicEntityId replayedSecond = LogicEntityIdAllocator.Allocate();

        Assert.AreEqual(1, first.Value);
        Assert.AreEqual(2, second.Value);
        Assert.AreEqual(second, replayedSecond);
    }

    [Test]
    public void SpawnRequestOrder_DefinesIds_WhenViewsBindOutOfOrder()
    {
        LogicEntityId first = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityId second = LogicEntityLifecycleService.RequestSpawn();

        LogicEntityLifecycleService.BindView(second, 202);
        LogicEntityLifecycleService.BindView(first, 101);

        Assert.AreEqual(1, first.Value);
        Assert.AreEqual(2, second.Value);
        Assert.AreEqual(LogicEntityLifecycleCommandKind.ViewBound, LogicEntityLifecycleService.Commands[2].Kind);
        Assert.AreEqual(second, LogicEntityLifecycleService.Commands[2].EntityId);
        Assert.AreEqual(first, LogicEntityLifecycleService.Commands[3].EntityId);

        LogicEntityLifecycleService.UnbindView(second, 202);
        LogicEntityLifecycleService.UnbindView(first, 101);
    }

    [Test]
    public void LifecycleCommands_AreSequencedForNextLogicFrame()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);
        LogicEntityLifecycleService.UnbindView(entityId, 1001);

        Assert.AreEqual(3, LogicEntityLifecycleService.Commands.Count);
        for (int i = 0; i < LogicEntityLifecycleService.Commands.Count; i++)
        {
            Assert.AreEqual(1UL, LogicEntityLifecycleService.Commands[i].EffectiveFrame);
            Assert.AreEqual((ulong)(i + 1), LogicEntityLifecycleService.Commands[i].Sequence);
        }
    }

    [Test]
    public void EndTimeline_RejectsStillBoundViews()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);

        Assert.Throws<InvalidOperationException>(() => LogicEntityLifecycleService.EndTimeline());
        Assert.IsTrue(LogicEntityLifecycleService.IsActive);

        LogicEntityLifecycleService.UnbindView(entityId, 1001);
    }

    [Test]
    public void WorldTransitionReset_RejectsStillBoundViews()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(entityId, 1001);
        const int pauseSource = 9001;
        LogicTimeControlService.AcquirePause(pauseSource);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicEntityLifecycleService.ResetForWorldTransition());

        StringAssert.Contains("1 entity views are still bound", exception.Message);
        LogicEntityLifecycleService.UnbindView(entityId, 1001);
        LogicTimeControlService.ReleasePause(pauseSource);
    }

    [Test]
    public void WorldTransitionReset_ReopensDeterministicIdentitySpace()
    {
        LogicEntityId oldEntity = LogicEntityLifecycleService.RequestSpawn();
        LogicEntityLifecycleService.BindView(oldEntity, 1001);
        LogicEntityLifecycleService.UnbindView(oldEntity, 1001);
        Assert.AreEqual("building-0000000001", LogicPersistentIdAllocator.AllocateBuildingInstanceId());

        const int pauseSource = 9001;
        LogicTimeControlService.AcquirePause(pauseSource);
        LogicEntityLifecycleService.ResetForWorldTransition();
        LogicTimeControlService.ReleasePause(pauseSource);

        LogicEntityId newEntity = LogicEntityLifecycleService.RequestSpawn();
        Assert.AreEqual(1, newEntity.Value);
        Assert.AreEqual("building-0000000001", LogicPersistentIdAllocator.AllocateBuildingInstanceId());
        Assert.AreEqual(1, LogicEntityLifecycleService.RequestedEntityCount);
        Assert.AreEqual(0, LogicEntityLifecycleService.BoundViewCount);
        Assert.AreEqual(0, LogicEntityLifecycleService.ActiveEntityCount);
        Assert.AreEqual(1, LogicEntityLifecycleService.Commands.Count);
    }

    [Test]
    public void SpawnFrameWithoutBoundView_FailsInsteadOfDelayingSpawn()
    {
        LogicEntityId entityId = LogicEntityLifecycleService.RequestSpawn();
        LogicTimeControlService.BeginFrame(1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LogicEntityLifecycleService.ApplyFrame(1));

        StringAssert.Contains($"entity={entityId.Value}", exception.Message);
        StringAssert.Contains("spawn view is not ready", exception.Message);
    }

    [Test]
    public void DespawnRequest_RejectsNullView()
    {
        Assert.Throws<ArgumentNullException>(() => LogicEntityLifecycleService.RequestDespawn(null));
    }

    [Test]
    public void EntityRegistry_SortsByLogicId_AndRejectsDuplicateId()
    {
        var entity30 = new SimEntityContext { LogicEntityId = new LogicEntityId(30) };
        var entity10 = new SimEntityContext { LogicEntityId = new LogicEntityId(10) };
        var entity20 = new SimEntityContext { LogicEntityId = new LogicEntityId(20) };

        EntityRegistry.Register(entity30);
        EntityRegistry.Register(entity10);
        EntityRegistry.Register(entity20);

        CollectionAssert.AreEqual(
            new[] { 10, 20, 30 },
            new[]
            {
                EntityRegistry.AllEntities[0].LogicEntityId.Value,
                EntityRegistry.AllEntities[1].LogicEntityId.Value,
                EntityRegistry.AllEntities[2].LogicEntityId.Value,
            });

        var duplicate = new SimEntityContext { LogicEntityId = new LogicEntityId(20) };
        Assert.Throws<InvalidOperationException>(() => EntityRegistry.Register(duplicate));
    }

    [Test]
    public void StableColliderOrder_IsIndependentOfDiscoveryOrder()
    {
        GameObject root = new GameObject("Root");
        GameObject firstChild = new GameObject("SameName");
        GameObject secondChild = new GameObject("SameName");
        try
        {
            firstChild.transform.SetParent(root.transform, false);
            secondChild.transform.SetParent(root.transform, false);
            BoxCollider first = firstChild.AddComponent<BoxCollider>();
            SphereCollider second = secondChild.AddComponent<SphereCollider>();

            List<Collider> forward = StableColliderOrder.CollectEnabledBlockingColliders(
                root.transform,
                new Collider[] { first, second });
            List<Collider> reversed = StableColliderOrder.CollectEnabledBlockingColliders(
                root.transform,
                new Collider[] { second, first });

            Assert.AreSame(forward[0], reversed[0]);
            Assert.AreSame(forward[1], reversed[1]);
            Assert.AreEqual(
                LogicEntityObstacleId.FromBuildingCollider(new LogicEntityId(7), 0),
                LogicEntityObstacleId.FromBuildingCollider(new LogicEntityId(7), reversed.IndexOf(forward[0])));
            Assert.Less(LogicEntityObstacleId.FromBuildingCollider(new LogicEntityId(7), 0), 0);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(firstChild);
            UnityEngine.Object.DestroyImmediate(secondChild);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
