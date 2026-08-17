//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2020 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.IO;

namespace UGF.EditorTools.Data.DataTable
{
    public sealed partial class DataTableProcessor
    {
        private sealed class StringIntPairArrayProcessor : GenericDataProcessor<StringIntPair[]>
        {
            public override bool IsSystem
            {
                get
                {
                    return false;
                }
            }

            public override string LanguageKeyword
            {
                get
                {
                    return "StringIntPair[]";
                }
            }

            public override int ShowOrder => 90;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "stringintpair[]",
                };
            }

            public override StringIntPair[] Parse(string value)
            {
                return DataTableExtension.ParseStringIntPairArray(value);
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
                    binaryWriter.Write(itm.str ?? string.Empty);
                    binaryWriter.Write7BitEncodedInt32(itm.num);
                }
            }
        }
    }
}
