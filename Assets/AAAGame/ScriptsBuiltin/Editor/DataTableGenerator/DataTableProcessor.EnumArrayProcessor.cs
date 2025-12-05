//------------------------------------------------------------
// Game Framework
//------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;

namespace GameFramework.Editor.DataTableTools
{
    public sealed partial class DataTableProcessor
    {
        private sealed class EnumArrayProcessor : GenericDataProcessor<int[]>
        {
            public override bool IsSystem
            {
                get
                {
                    return true;
                }
            }

            public override string LanguageKeyword
            {
                get
                {
                    return "enum[]";
                }
            }

            public override int ShowOrder => 10;

            public override string[] GetTypeStrings()
            {
                return new string[]
                {
                    "enum[]",
                    "system.enum[]"
                };
            }

            public override int[] Parse(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                string[] parts = value.Split(',');
                int[] result = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    var s = parts[i].Trim();
                    if (DataTableExtension.TryParseEnum(s, out Type enumType, out int enumValue))
                    {
                        result[i] = enumValue;
                    }
                    else
                    {
                        throw new GameFrameworkException(Utility.Text.Format("解析枚举类型失败:{0}, 配置枚举格式为: EnumType.Item1", s));
                    }
                }
                return result;
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
                    binaryWriter.Write7BitEncodedInt32(v[i]);
                }
            }
        }
    }
}
