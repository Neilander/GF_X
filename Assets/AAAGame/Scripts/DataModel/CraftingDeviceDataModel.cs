using GameFramework;
using GameFramework.Event;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 合成装置数据模型类, 预读各装置合成公式
/// </summary>
public class CraftingDeviceDataModel : DataModelBase
{
    private Dictionary<string, List<CraftingFormula>> craftingDeviceDataDic;

    protected override void OnCreate(RefParams userdata)
    {
        craftingDeviceDataDic = new();
        var craftingFormulaTb = GF.DataTable.GetDataTable<CraftingFormulaTable>();
        foreach (var row in craftingFormulaTb.GetAllDataRows())
        {
            CraftingFormula formula = ImportCraftingFormulaDataRow(row);
            var key = row.CraftingPlace;
            if (craftingDeviceDataDic.TryGetValue(key, out var list))
            {
                list.Add(formula);
            }
            else
            {
                craftingDeviceDataDic[key] = new List<CraftingFormula>() { formula };
            }
        }
    }
    protected override void OnRelease() { }

    public static List<CraftingFormula> GetCraftingDeviceData(string craftingDeviceIdentifier)
    {
        var craftingDeviceDataModel = GF.DataModel.GetDataModel<CraftingDeviceDataModel>();
        if (craftingDeviceDataModel.craftingDeviceDataDic.TryGetValue(craftingDeviceIdentifier, out var list)) return list;
        return null;
    }

    private CraftingFormula ImportCraftingFormulaDataRow(CraftingFormulaTable row)
    {
        CraftingFormula formula = new(row.RequiredItems, row.ProducedItems, row.Workload);
        return formula;
    }
}
