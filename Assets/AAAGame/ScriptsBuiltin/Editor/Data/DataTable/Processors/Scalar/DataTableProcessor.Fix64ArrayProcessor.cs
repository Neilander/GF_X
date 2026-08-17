//------------------------------------------------------------
// Game Framework
//------------------------------------------------------------

using System.IO;

namespace UGF.EditorTools.Data.DataTable
{
    public sealed partial class DataTableProcessor
    {
        private sealed class Fix64ArrayProcessor : GenericDataProcessor<Fix64[]>
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
                    return "Fix64[]";
                }
            }

            public override int ShowOrder => 90;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "fix64[]",
                };
            }

            public override Fix64[] Parse(string value)
            {
                return DataTableExtension.ParseFix64Array(value);
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
                    binaryWriter.Write(v[i].RawValue);
                }
            }
        }
    }
}

