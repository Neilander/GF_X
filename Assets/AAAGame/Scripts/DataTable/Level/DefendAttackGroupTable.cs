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
        /// 代码内标识符
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
        /// 防御日，从1开始；超过最大日时重复最后一日并按无尽倍率成长
        /// </summary>
        public int DefendRound
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
        /// 固定兵种与数量；来源据点被占领后整组不出兵且不向其他据点转移
        /// </summary>
        public StringIntPair[] Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 前置出兵组；空表示从防御阶段开始计时
        /// </summary>
        public string AfterGroupIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 前置组固定出兵窗口结束后的延迟秒；无前置时为防御阶段开始延迟
        /// </summary>
        public Fix64 StartDelaySeconds
        {
            get;
            private set;
        }

        /// <summary>
        /// 从本组计时基准到预计接战时刻的秒数；固定出兵后，关卡内按当前首个玩家中转据点的导航距离反推移速
        /// </summary>
        public Fix64 ExpectedEngagementSeconds
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
            DefendRound = DataTableExtension.ParseInt(columnStrings[index++]);
            RouteIdentifier = columnStrings[index++];
            Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            AfterGroupIdentifier = columnStrings[index++];
            StartDelaySeconds = DataTableExtension.ParseFix64(columnStrings[index++]);
            ExpectedEngagementSeconds = DataTableExtension.ParseFix64(columnStrings[index++]);

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
                    DefendRound = binaryReader.Read7BitEncodedInt32();
                    RouteIdentifier = binaryReader.ReadString();
                    Enemies = binaryReader.ReadStringIntPairArray();
                    AfterGroupIdentifier = binaryReader.ReadString();
                    StartDelaySeconds = binaryReader.ReadFix64();
                    ExpectedEngagementSeconds = binaryReader.ReadFix64();
                }
            }

            return true;
        }
}
