using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class StorySystemTests
{
    [Test]
    public void GeneratedStoryTables_ContainOrderedL1AndL2WinScripts()
    {
        StoryScriptTable[] scripts = LoadRows<StoryScriptTable>(
            "DataTable/Story/StoryScriptTable.txt",
            line =>
            {
                var row = new StoryScriptTable();
                row.ParseDataRow(line, null);
                return row;
            });
        StoryTriggerTable[] triggers = LoadRows<StoryTriggerTable>(
            "DataTable/Story/StoryTriggerTable.txt",
            line =>
            {
                var row = new StoryTriggerTable();
                row.ParseDataRow(line, null);
                return row;
            });

        StoryScriptTable[] l1 = StoryTableQuery.GetScriptRows(scripts, "Story_L1");
        StoryScriptTable[] l2 = StoryTableQuery.GetScriptRows(scripts, "Story_L2");
        StoryTriggerTable l1Trigger = StoryTableQuery.FindTrigger(triggers, "Lv_1", StoryTiming.AfterLevelWin);
        StoryTriggerTable l2Trigger = StoryTableQuery.FindTrigger(triggers, "Lv_2", StoryTiming.AfterLevelWin);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, l1.Select(row => row.Order));
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, l2.Select(row => row.Order));
        Assert.AreEqual(StoryStyle.Terminal, l1[0].Style);
        Assert.AreEqual(StoryStyle.Document, l2[0].Style);
        Assert.AreEqual("Story_L1", l1Trigger.ScriptID);
        Assert.AreEqual("Story_L2", l2Trigger.ScriptID);
        Assert.IsTrue(l1Trigger.OnceOnly);
        Assert.IsTrue(l2Trigger.OnceOnly);
        Assert.IsNull(StoryTableQuery.FindTrigger(triggers, "Lv_1", StoryTiming.AfterLevelFail));
    }

    [Test]
    public void DuplicateTrigger_ThrowsInsteadOfChoosingArbitrarily()
    {
        StoryTriggerTable first = ParseTrigger("\t91\tfirst\tLv_Test\tStoryTiming.AfterLevelWin\tStory_A\tTrue");
        StoryTriggerTable second = ParseTrigger("\t92\tsecond\tLv_Test\tStoryTiming.AfterLevelWin\tStory_B\tTrue");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            StoryTableQuery.FindTrigger(
                new[] { first, second },
                "Lv_Test",
                StoryTiming.AfterLevelWin));

        StringAssert.Contains("Multiple story triggers", exception.Message);
    }

    [Test]
    public void StoryProgress_JsonRoundTripRetainsReadScriptIds()
    {
        const string json = "{\"m_ReadScriptIds\":[\"Story_L1\",\"Story_L2\"]}";
        StoryProgressDataModel model = UtilityBuiltin.Json.ToObject<StoryProgressDataModel>(json);

        Assert.IsNotNull(model);
        Assert.IsTrue(model.HasRead("Story_L1"));
        Assert.IsTrue(model.HasRead("Story_L2"));
        Assert.IsFalse(model.HasRead("Story_L3"));

        string roundTripJson = UtilityBuiltin.Json.ToJson(model);
        StoryProgressDataModel roundTrip = UtilityBuiltin.Json.ToObject<StoryProgressDataModel>(roundTripJson);
        Assert.IsTrue(roundTrip.HasRead("Story_L1"));
        Assert.IsTrue(roundTrip.HasRead("Story_L2"));
    }

    private static StoryTriggerTable ParseTrigger(string line)
    {
        var row = new StoryTriggerTable();
        row.ParseDataRow(line, null);
        return row;
    }

    private static T[] LoadRows<T>(string assetRelativePath, Func<string, T> parser)
    {
        string path = Path.Combine(Application.dataPath, "AAAGame", assetRelativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Generated story table was not found.", path);

        var rows = new List<T>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            rows.Add(parser(line));
        }
        return rows.ToArray();
    }
}
