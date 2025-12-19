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
/// 设备表
/// </summary>
public class DeviceTable : DataRowBase
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
        /// 材料消耗
        /// </summary>
        public StringIntPair[] CostMaterial
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

        /// <summary>
        /// Prefab路径
        /// </summary>
        public string PrefabName
        {
            get;
            private set;
        }

        /// <summary>
        /// 设备名(多语言)
        /// </summary>
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// 设备描述(多语言)
        /// </summary>
        public string Description
        {
            get;
            private set;
        }

        /// <summary>
        /// 建造条件
        /// </summary>
        public string BuildCapability
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
            CostMaterial = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Workload = DataTableExtension.ParseFix64(columnStrings[index++]);
            PrefabName = columnStrings[index++];
            Name = columnStrings[index++];
            Description = columnStrings[index++];
            BuildCapability = columnStrings[index++];

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
                    CostMaterial = binaryReader.ReadStringIntPairArray();
                    Workload = binaryReader.ReadFix64();
                    PrefabName = binaryReader.ReadString();
                    Name = binaryReader.ReadString();
                    Description = binaryReader.ReadString();
                    BuildCapability = binaryReader.ReadString();
                }
            }

            return true;
        }
}
