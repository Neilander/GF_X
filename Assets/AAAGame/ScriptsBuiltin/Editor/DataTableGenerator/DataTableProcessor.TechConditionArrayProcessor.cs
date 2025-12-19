//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2020 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.IO;

namespace GameFramework.Editor.DataTableTools
{
    public sealed partial class DataTableProcessor
    {
        private sealed class TechConditionArrayProcessor : GenericDataProcessor<TechCondition[]>
        {
            public override bool IsSystem => false;

            public override string LanguageKeyword => "TechCondition[]";

            public override int ShowOrder => 95;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "techcondition[]",
                };
            }

            public override TechCondition[] Parse(string value)
            {
                return DataTableExtension.ParseTechConditionArray(value);
            }

            public override void WriteToStream(DataTableProcessor dataTableProcessor, BinaryWriter binaryWriter, string value)
            {
                var v = Parse(value);
                if (v == null)
                {
                    binaryWriter.Write7BitEncodedInt32(0);
                    return;
                }

                binaryWriter.Write7BitEncodedInt32(v.Length);
                for (int i = 0; i < v.Length; i++)
                {
                    var itm = v[i];
                    binaryWriter.Write7BitEncodedInt32((int)itm.Type);
                    binaryWriter.Write(SerializeArgs(itm));
                }
            }

            private static string SerializeArgs(TechCondition itm)
            {
                switch (itm.Type)
                {
                    case TechConditionType.BaseLevelGE:
                        return itm.I1.ToString();
                    case TechConditionType.HasTech:
                    case TechConditionType.QuestDone:
                    case TechConditionType.PlotFlag:
                        return itm.S1 ?? string.Empty;
                    case TechConditionType.HasItem:
                        return string.IsNullOrEmpty(itm.S1) ? string.Empty : string.Concat(itm.S1, ",", itm.I1.ToString());
                    default:
                        return string.Empty;
                }
            }
        }
    }
}
