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
/// 关卡表
/// </summary>
public class LevelTable : DataRowBase
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
        /// 关卡prefab路径
        /// </summary>
        public string PrefabPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 关卡名(多语言)
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 关卡简介(多语言)
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 初始资源
        /// </summary>
        public int InitResource
        {
            get;
            private set;
        }

        /// <summary>
        /// 初始阶段
        /// </summary>
        public GamePhase StartPhase
        {
            get;
            private set;
        }

        /// <summary>
        /// 胜利条件
        /// </summary>
        public VictoryConditionType[] VictoryConditions
        {
            get;
            private set;
        }

        /// <summary>
        /// 胜利条件设定数值
        /// </summary>
        public int VictoryValue
        {
            get;
            private set;
        }

        /// <summary>
        /// 失败条件
        /// </summary>
        public FailConditionType[] LoseConditions
        {
            get;
            private set;
        }

        /// <summary>
        /// 失败条件设定数值
        /// </summary>
        public int LoseValue
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
            PrefabPath = columnStrings[index++];
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            InitResource = DataTableExtension.ParseInt32(columnStrings[index++]);
            StartPhase = DataTableExtension.ParseEnum<GamePhase>(columnStrings[index++]);
            VictoryConditions = DataTableExtension.ParseArray<VictoryConditionType>(columnStrings[index++]);
            VictoryValue = DataTableExtension.ParseInt32(columnStrings[index++]);
            LoseConditions = DataTableExtension.ParseArray<FailConditionType>(columnStrings[index++]);
            LoseValue = DataTableExtension.ParseInt32(columnStrings[index++]);

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
                    PrefabPath = binaryReader.ReadString();
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    InitResource = binaryReader.Read7BitEncodedInt32();
                    StartPhase = binaryReader.ReadEnum<GamePhase>();
                    VictoryConditions = binaryReader.ReadArray<VictoryConditionType>();
                    VictoryValue = binaryReader.Read7BitEncodedInt32();
                    LoseConditions = binaryReader.ReadArray<FailConditionType>();
                    LoseValue = binaryReader.Read7BitEncodedInt32();
                }
            }

            return true;
        }
}
