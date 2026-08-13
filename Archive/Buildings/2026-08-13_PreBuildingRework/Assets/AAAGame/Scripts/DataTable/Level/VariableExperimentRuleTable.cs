//------------------------------------------------------------
//------------------------------------------------------------
// 此文件由工具自动生成，请勿直接修改。
// 生成时间：__DATA_TABLE_CREATE_TIME__
//------------------------------------------------------------

using GameFramework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityGameFramework.Runtime;
#if ENABLE_OBFUZ
[Obfuz.ObfuzIgnore(Obfuz.ObfuzScope.TypeName | Obfuz.ObfuzScope.MethodName)]
#endif
/// <summary>
/// 变量试验规则配置表
/// </summary>
public class VariableExperimentRuleTable : DataRowBase
{
	private int m_Id = 0;
	/// <summary>
    /// 行号
    /// </summary>
    public override int Id
    {
        get { return m_Id; }
    }

        /// <summary>
        /// 规则标识符
        /// </summary>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 本地化名称键
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 本地化描述键
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 强制初始行业或None
        /// </summary>
        public Archetype ForcedArchetype
        {
            get;
            private set;
        }

        /// <summary>
        /// 规则数值（索引含义由规则实现约定）
        /// </summary>
        public Fix64[] UniqueValues
        {
            get;
            private set;
        }

        public override bool ParseDataRow(string dataRowString, object userData)
        {
            string[] columnStrings = dataRowString.Split(DataTableExtension.DataSplitSeparators);
            for (int i = 0; i < columnStrings.Length; i++)
            {
                columnStrings[i] = columnStrings[i].Trim(DataTableExtension.DataTrimSeparators);
            }

            int index = 0;
            index++;
            m_Id = DataTableExtension.ParseInt32(columnStrings[index++]);
            index++;
            Identifier = columnStrings[index++];
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            ForcedArchetype = DataTableExtension.ParseEnum<Archetype>(columnStrings[index++]);
            UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    Identifier = binaryReader.ReadString();
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    ForcedArchetype = binaryReader.ReadEnum<Archetype>();
                    UniqueValues = binaryReader.ReadFix64Array();
                }
            }

            return true;
        }
}
