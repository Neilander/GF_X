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
/// 防御出兵组表
/// </summary>
public class DefendAttackGroupTable : DataRowBase
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
        /// 代码内标识符，由路线名和后缀派生；编辑器中只编辑后缀
        /// </summary>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 所属关卡标识
        /// </summary>
        public string LevelIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 启用防御日；编辑器顶部选择天数后勾选，不生成按日实例
        /// </summary>
        public int[] ActiveDays
        {
            get;
            private set;
        }

        /// <summary>
        /// 路线标识符
        /// </summary>
        public string RouteIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 单一兵种标识，不包含等级
        /// </summary>
        public string UnitIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 初始橙髓等价战力；由兵种、数量和等级的非线性价值换算
        /// </summary>
        public Fix64 InitialStrengthValue
        {
            get;
            private set;
        }

        /// <summary>
        /// 新增强度在数量成长中的分配权重，0=全投等级，1=全投数量
        /// </summary>
        public Fix64 CountGrowthWeight
        {
            get;
            private set;
        }

        /// <summary>
        /// 前置小队；前置与本小队必须在同一天启用
        /// </summary>
        public string AfterGroupIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 前置小队固定出兵窗口结束后的延迟秒；无前置时为防御阶段开始延迟
        /// </summary>
        public Fix64 StartDelaySeconds
        {
            get;
            private set;
        }

        /// <summary>
        /// 从本小队计时基准到预计接战时刻的秒数
        /// </summary>
        public Fix64 ExpectedEngagementSeconds
        {
            get;
            private set;
        }

        /// <summary>
        /// 命名后缀，可空；同关卡同路线多小队时必须唯一
        /// </summary>
        public string Suffix
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
            m_Id = DataTableExtension.ParseInt(columnStrings[index++]);
            index++;
            Identifier = columnStrings[index++];
            LevelIdentifier = columnStrings[index++];
            ActiveDays = DataTableExtension.ParseArray<int>(columnStrings[index++]);
            RouteIdentifier = columnStrings[index++];
            UnitIdentifier = columnStrings[index++];
            InitialStrengthValue = DataTableExtension.ParseFix64(columnStrings[index++]);
            CountGrowthWeight = DataTableExtension.ParseFix64(columnStrings[index++]);
            AfterGroupIdentifier = columnStrings[index++];
            StartDelaySeconds = DataTableExtension.ParseFix64(columnStrings[index++]);
            ExpectedEngagementSeconds = DataTableExtension.ParseFix64(columnStrings[index++]);
            Suffix = columnStrings[index++];

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
                    LevelIdentifier = binaryReader.ReadString();
                    ActiveDays = binaryReader.ReadArray<int>();
                    RouteIdentifier = binaryReader.ReadString();
                    UnitIdentifier = binaryReader.ReadString();
                    InitialStrengthValue = binaryReader.ReadFix64();
                    CountGrowthWeight = binaryReader.ReadFix64();
                    AfterGroupIdentifier = binaryReader.ReadString();
                    StartDelaySeconds = binaryReader.ReadFix64();
                    ExpectedEngagementSeconds = binaryReader.ReadFix64();
                    Suffix = binaryReader.ReadString();
                }
            }

            return true;
        }
}
