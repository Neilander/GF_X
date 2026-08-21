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
        /// 显式启用的防御波次；空缺波完整沿用最近上一波编排
        /// </summary>
        public int[] ActiveDefenseWaves
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
        /// 初始小队资源等价量，当前以橙髓计；按各等级单兵资源等价量乘数量后求和
        /// </summary>
        public Fix64 InitialResourceEquivalent
        {
            get;
            private set;
        }

        /// <summary>
        /// 新增资源等价量在数量成长中的分配权重，0=全投等级，1=全投数量
        /// </summary>
        public Fix64 CountGrowthWeight
        {
            get;
            private set;
        }

        /// <summary>
        /// 与启用防御波次一一对应的队首相对接战秒数；可为0，运行时不足则全波统一延后
        /// </summary>
        public Fix64[] RelativeLeaderEngagementSeconds
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
            ActiveDefenseWaves = DataTableExtension.ParseArray<int>(columnStrings[index++]);
            RouteIdentifier = columnStrings[index++];
            UnitIdentifier = columnStrings[index++];
            InitialResourceEquivalent = DataTableExtension.ParseFix64(columnStrings[index++]);
            CountGrowthWeight = DataTableExtension.ParseFix64(columnStrings[index++]);
            RelativeLeaderEngagementSeconds = DataTableExtension.ParseFix64Array(columnStrings[index++]);
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
                    ActiveDefenseWaves = binaryReader.ReadArray<int>();
                    RouteIdentifier = binaryReader.ReadString();
                    UnitIdentifier = binaryReader.ReadString();
                    InitialResourceEquivalent = binaryReader.ReadFix64();
                    CountGrowthWeight = binaryReader.ReadFix64();
                    RelativeLeaderEngagementSeconds = binaryReader.ReadFix64Array();
                    Suffix = binaryReader.ReadString();
                }
            }

            return true;
        }
}
