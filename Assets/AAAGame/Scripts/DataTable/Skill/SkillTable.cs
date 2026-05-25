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
/// 建筑表
/// </summary>
public class SkillTable : DataRowBase
{
	private int m_Id = 0;
	/// <summary>
    /// 
    /// </summary>
    public override int Id
    {
        get { return m_Id; }
    }

        /// <summary>
        /// 代码内标识符
        /// </summary>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级数值
        /// </summary>
        public Fix64[] Lv1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级使用次数
        /// </summary>
        public int Lv1UsageCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 升级增加数值
        /// </summary>
        public Fix64[] UpgradeIncrementUniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 升级增加使用次数
        /// </summary>
        public int UpgradeIncrementUsageCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 技能类型
        /// </summary>
        public SkillType Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 技能名称（多语言）
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 技能描述（多语言）
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// Sprite路径
        /// </summary>
        public string SpritePath
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
            Identifier = columnStrings[index++];
            index++;
            index++;
            Lv1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Lv1UsageCount = DataTableExtension.ParseInt32(columnStrings[index++]);
            UpgradeIncrementUniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            UpgradeIncrementUsageCount = DataTableExtension.ParseInt32(columnStrings[index++]);
            index++;
            Type = DataTableExtension.ParseEnum<SkillType>(columnStrings[index++]);
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            SpritePath = columnStrings[index++];

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
                    Lv1UniqueValues = binaryReader.ReadFix64Array();
                    Lv1UsageCount = binaryReader.Read7BitEncodedInt32();
                    UpgradeIncrementUniqueValues = binaryReader.ReadFix64Array();
                    UpgradeIncrementUsageCount = binaryReader.Read7BitEncodedInt32();
                    Type = binaryReader.ReadEnum<SkillType>();
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    SpritePath = binaryReader.ReadString();
                }
            }

            return true;
        }
}
