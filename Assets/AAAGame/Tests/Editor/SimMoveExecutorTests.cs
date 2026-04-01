using NUnit.Framework;
using UnityEngine;

public class SimMoveExecutorTests
{
    [Test]
    public void 向前移动10帧_位置正确()
    {
        var executor = new SimMoveExecutor();
        executor.Position = Vector3.zero;

        for (int i = 0; i < 10; i++)
        {
            executor.SetInput(Vector3.forward * 5f);
            executor.Execute(0.1f);
        }

        // 5m/s * 1s = 5m
        Assert.AreEqual(5f, executor.Position.z, 0.01f);
        Assert.AreEqual(0f, executor.Position.x, 0.01f);
    }

    [Test]
    public void 三个小人同时移动_互不干扰()
    {
        var units = new SimMoveExecutor[3];
        units[0] = new SimMoveExecutor { Position = new Vector3(0, 0, 0) };
        units[1] = new SimMoveExecutor { Position = new Vector3(5, 0, 0) };
        units[2] = new SimMoveExecutor { Position = new Vector3(10, 0, 0) };

        for (int i = 0; i < 10; i++)
        {
            foreach (var unit in units)
            {
                unit.SetInput(Vector3.forward * 5f);
                unit.Execute(0.1f);
            }
        }

        Assert.AreEqual(5f, units[0].Position.z, 0.01f);
        Assert.AreEqual(5f, units[1].Position.z, 0.01f);
        Assert.AreEqual(5f, units[2].Position.z, 0.01f);

        // 横向位置不变
        Assert.AreEqual(0f, units[0].Position.x, 0.01f);
        Assert.AreEqual(5f, units[1].Position.x, 0.01f);
        Assert.AreEqual(10f, units[2].Position.x, 0.01f);
    }

    [Test]
    public void Override覆盖普通移动()
    {
        var executor = new SimMoveExecutor();
        executor.Position = Vector3.zero;

        executor.SetInput(Vector3.forward * 10f);   // 正常向前
        executor.SetOverride(Vector3.back * 20f);     // 击飞向后
        executor.Execute(0.1f);

        // Override 生效，应该往后走
        Assert.Less(executor.Position.z, 0f);
    }

    [Test]
    public void External叠加到Input()
    {
        var executor = new SimMoveExecutor();
        executor.Position = Vector3.zero;

        executor.SetInput(Vector3.forward * 5f);
        executor.AddExternal(Vector3.right * 3f);
        executor.Execute(1f);

        Assert.AreEqual(5f, executor.Position.z, 0.01f);
        Assert.AreEqual(3f, executor.Position.x, 0.01f);
    }

    [Test]
    public void 每帧自动重置输入()
    {
        var executor = new SimMoveExecutor();
        executor.Position = Vector3.zero;

        executor.SetInput(Vector3.forward * 10f);
        executor.Execute(0.1f);

        // 第二帧不设输入
        executor.Execute(0.1f);

        // 应该只走了一帧的距离
        Assert.AreEqual(1f, executor.Position.z, 0.01f);
    }
}
