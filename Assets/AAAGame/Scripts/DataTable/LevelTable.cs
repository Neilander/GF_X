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

        /// <summary>
        /// 防御阶段1出怪
        /// </summary>
        public StringIntPair[] Def1Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段2出怪
        /// </summary>
        public StringIntPair[] Def2Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段3出怪
        /// </summary>
        public StringIntPair[] Def3Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段4出怪
        /// </summary>
        public StringIntPair[] Def4Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段5出怪
        /// </summary>
        public StringIntPair[] Def5Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段6出怪
        /// </summary>
        public StringIntPair[] Def6Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段7出怪
        /// </summary>
        public StringIntPair[] Def7Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段8出怪
        /// </summary>
        public StringIntPair[] Def8Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段9出怪
        /// </summary>
        public StringIntPair[] Def9Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 防御阶段10出怪
        /// </summary>
        public StringIntPair[] Def10Enemies
        {
            get;
            private set;
        }

        /// <summary>
        /// 首通解锁行业
        /// </summary>
        public Archetype[] UnlockArchetype
        {
            get;
            private set;
        }

        /// <summary>
        /// 默认初始行业
        /// </summary>
        public Archetype DefaultArchetype
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
            Def1Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def2Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def3Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def4Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def5Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def6Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def7Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def8Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def9Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            Def10Enemies = DataTableExtension.ParseStringIntPairArray(columnStrings[index++]);
            UnlockArchetype = DataTableExtension.ParseArray<Archetype>(columnStrings[index++]);
            DefaultArchetype = DataTableExtension.ParseEnum<Archetype>(columnStrings[index++]);

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
                    Def1Enemies = binaryReader.ReadStringIntPairArray();
                    Def2Enemies = binaryReader.ReadStringIntPairArray();
                    Def3Enemies = binaryReader.ReadStringIntPairArray();
                    Def4Enemies = binaryReader.ReadStringIntPairArray();
                    Def5Enemies = binaryReader.ReadStringIntPairArray();
                    Def6Enemies = binaryReader.ReadStringIntPairArray();
                    Def7Enemies = binaryReader.ReadStringIntPairArray();
                    Def8Enemies = binaryReader.ReadStringIntPairArray();
                    Def9Enemies = binaryReader.ReadStringIntPairArray();
                    Def10Enemies = binaryReader.ReadStringIntPairArray();
                    UnlockArchetype = binaryReader.ReadArray<Archetype>();
                    DefaultArchetype = binaryReader.ReadEnum<Archetype>();
                }
            }

            return true;
        }
}
