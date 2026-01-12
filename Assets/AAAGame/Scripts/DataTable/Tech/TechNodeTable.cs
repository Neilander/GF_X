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
/// 科技表
/// </summary>
public class TechNodeTable : DataRowBase
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
        /// 在代码中的命名标识符
        /// </summary>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 前置科技标识符
        /// </summary>
        public string[] PrereqTechIds
        {
            get;
            private set;
        }

        /// <summary>
        /// 对应科技等级
        /// </summary>
        public int Level
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技类型
        /// </summary>
        public TechCategory Category
        {
            get;
            private set;
        }

        /// <summary>
        /// 材料消耗
        /// </summary>
        public StringIntPair[] CostMaterial
        {
            get;
            private set;
        }

        /// <summary>
        /// AND 条件串
        /// </summary>
        public UnlockCondition[] AllConditions
        {
            get;
            private set;
        }

        /// <summary>
        /// OR 条件串
        /// </summary>
        public UnlockCondition[] AnyConditions
        {
            get;
            private set;
        }

        /// <summary>
        /// Sprite路径
        /// </summary>
        public string SpriteName
        {
            get;
            private set;
        }

        /// <summary>
        /// 节点名(多语言)
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 节点描述(多语言)
        /// </summary>
        public string DescriptionKey
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
            m_Id = int.Parse(columnStrings[index++]);
            index++;
            Identifier = columnStrings[index++];
            PrereqTechIds = DataTableExtension.ParseArray<string>(columnStrings[index++]);
            Level = int.Parse(columnStrings[index++]);
            Category = DataTableExtension.ParseEnum<TechCategory>(columnStrings[index++]);
            CostMaterial = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            AllConditions = DataTableExtension.ParseUnlockConditionArray(columnStrings[index++]);
            AnyConditions = DataTableExtension.ParseUnlockConditionArray(columnStrings[index++]);
            SpriteName = columnStrings[index++];
            NameKey = columnStrings[index++];
            DescriptionKey = columnStrings[index++];

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
                    PrereqTechIds = binaryReader.ReadArray<string>();
                    Level = binaryReader.Read7BitEncodedInt32();
                    Category = binaryReader.ReadEnum<TechCategory>();
                    CostMaterial = binaryReader.ReadStringIntPairArray();
                    AllConditions = binaryReader.ReadUnlockConditionArray();
                    AnyConditions = binaryReader.ReadUnlockConditionArray();
                    SpriteName = binaryReader.ReadString();
                    NameKey = binaryReader.ReadString();
                    DescriptionKey = binaryReader.ReadString();
                }
            }

            return true;
        }
}
