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
/// 装备表
/// </summary>
public class CraftingFormulaTable : DataRowBase
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
        /// 制造地
        /// </summary>
        public string CraftingPlace
        {
            get;
            private set;
        }

        /// <summary>
        /// 产品
        /// </summary>
        public StringIntPair[] ProducedItems
        {
            get;
            private set;
        }

        /// <summary>
        /// 原材料
        /// </summary>
        public StringIntPair[] RequiredItems
        {
            get;
            private set;
        }

        /// <summary>
        /// 工作量
        /// </summary>
        public Fix64 Workload
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
            CraftingPlace = columnStrings[index++];
            ProducedItems = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            RequiredItems = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Workload = DataTableExtension.ParseFix64(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    CraftingPlace = binaryReader.ReadString();
                    ProducedItems = binaryReader.ReadStringIntPairArray();
                    RequiredItems = binaryReader.ReadStringIntPairArray();
                    Workload = binaryReader.ReadFix64();
                }
            }

            return true;
        }
}
