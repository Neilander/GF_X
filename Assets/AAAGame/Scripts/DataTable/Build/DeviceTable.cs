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
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 设备描述(多语言)
        /// </summary>
        public string DescriptionKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 建造条件
        /// </summary>
        public UnlockCondition BuildCondition
        {
            get;
            private set;
        }

        /// <summary>
        /// 升级设备ID
        /// </summary>
        public string UpgradeID
        {
            get;
            private set;
        }

        /// <summary>
        /// 交互面板UIViews
        /// </summary>
        public UIViews? InteractionPanelID
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
            NameKey = columnStrings[index++];
            DescriptionKey = columnStrings[index++];
            BuildCondition = DataTableExtension.ParseUnlockCondition(columnStrings[index++]);
            UpgradeID = columnStrings[index++];
            InteractionPanelID = DataTableExtension.ParseNullableEnum<UIViews>(columnStrings[index++]);

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
                    NameKey = binaryReader.ReadString();
                    DescriptionKey = binaryReader.ReadString();
                    BuildCondition = binaryReader.ReadUnlockCondition();
                    UpgradeID = binaryReader.ReadString();
                    InteractionPanelID = binaryReader.ReadNullableEnum<UIViews>();
                }
            }

            return true;
        }
}
