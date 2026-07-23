using AAAGame.MiniMap.FOG3;
using NUnit.Framework;
using UnityEngine;

public sealed class Fog3StageCheckpointTests
{
    [Test]
    public void ExplorationCheckpoint_RestoresExploredBitsButNotTransientVisibility()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });
        map.MarkExplored(1, 0);
        map.MarkExplored(2, 1);
        map.AddVisibility(1, 0, 1f);
        map.AddVisibility(2, 1, 0.5f);
        Fog3ExplorationCheckpoint checkpoint = map.CaptureExplorationCheckpoint();

        map.ResetExploration();
        map.MarkExplored(0, 1);
        map.AddVisibility(0, 1, 1f);
        map.RestoreExplorationCheckpoint(checkpoint);

        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(1, 0));
        Assert.AreEqual(Fog3CellState.Explored, map.GetCellState(2, 1));
        Assert.AreEqual(Fog3CellState.Hidden, map.GetCellState(0, 1));
        Assert.AreEqual(0f, map.GetVisibility(1, 0));
        Assert.IsTrue(map.IsDirty);
    }

    [Test]
    public void RenderVisibility_DoesNotMutateLogicExploration()
    {
        Fog3MapData map = CreateMap(new[] { true, true, true, true, true, true });

        map.AddVisibility(1, 0, 1f);

        Assert.AreEqual(Fog3CellState.Visible, map.GetCellState(1, 0));
        Assert.IsFalse(map.IsExplored(1, 0));
        Assert.AreEqual(0, map.ExploredCellCount);
    }

    [Test]
    public void ExplorationCheckpoint_RejectsDifferentTerrainTopology()
    {
        Fog3ExplorationCheckpoint checkpoint = CreateMap(
            new[] { true, true, true, true, true, true }).CaptureExplorationCheckpoint();
        Fog3MapData different = CreateMap(new[] { true, true, false, true, true, true });

        Assert.Throws<System.InvalidOperationException>(() =>
            different.RestoreExplorationCheckpoint(checkpoint));
    }

    private static Fog3MapData CreateMap(bool[] walkable)
    {
        return new Fog3MapData(new Fog3TerrainInfo(
            3,
            2,
            1f,
            Vector3.zero,
            walkable,
            "StageCheckpointTest"));
    }
}
