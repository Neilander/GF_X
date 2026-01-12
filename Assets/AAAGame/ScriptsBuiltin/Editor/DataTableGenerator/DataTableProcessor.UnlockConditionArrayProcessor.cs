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
        private sealed class UnlockConditionArrayProcessor : GenericDataProcessor<UnlockCondition[]>
        {
            public override bool IsSystem => false;

            public override string LanguageKeyword => "UnlockCondition[]";

            public override int ShowOrder => 95;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "unlockcondition[]",
                    // 兼容迁移期：旧表头可能仍写成 Capability[]
                    "capability[]",
                };
            }

            public override UnlockCondition[] Parse(string value)
            {
                return DataTableExtension.ParseUnlockConditionArray(value);
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
                    binaryWriter.Write(itm.Identifier ?? string.Empty);
                }
            }
        }
    }
}
