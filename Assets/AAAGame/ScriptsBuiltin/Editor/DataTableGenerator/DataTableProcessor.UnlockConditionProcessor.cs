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
        private sealed class UnlockConditionProcessor : GenericDataProcessor<UnlockCondition>
        {
            public override bool IsSystem => false;

            public override string LanguageKeyword => "UnlockCondition";

            public override int ShowOrder => 94;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "unlockcondition",
                    // 兼容迁移期：旧表头可能写 BuildCondition=Capability
                    "capability",
                };
            }

            public override UnlockCondition Parse(string value)
            {
                return DataTableExtension.ParseUnlockCondition(value);
            }

            public override void WriteToStream(DataTableProcessor dataTableProcessor, BinaryWriter binaryWriter, string value)
            {
                var v = Parse(value);
                binaryWriter.Write7BitEncodedInt32((int)v.Type);
                binaryWriter.Write(v.Identifier ?? string.Empty);
            }
        }
    }
}
