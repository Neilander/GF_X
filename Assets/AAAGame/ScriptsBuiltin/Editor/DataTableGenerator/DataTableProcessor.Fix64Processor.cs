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
        private sealed class Fix64Processor : GenericDataProcessor<Fix64>
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
                    return "Fix64";
                }
            }

            public override int ShowOrder => 20;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "fix64",
                };
            }

            public override Fix64 Parse(string value)
            {
                return DataTableExtension.ParseFix64(value);
            }

            public override void WriteToStream(DataTableProcessor dataTableProcessor, BinaryWriter binaryWriter, string value)
            {
                var v = Parse(value);
                binaryWriter.Write(v.RawValue);
            }
        }
    }
}
