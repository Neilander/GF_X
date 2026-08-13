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
        /// 主要目标1规则标识
        /// </summary>
        public string PrimaryObjective1Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 主要目标1数值
        /// </summary>
        public Fix64[] PrimaryObjective1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 主要目标2规则标识
        /// </summary>
        public string PrimaryObjective2Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 主要目标2数值
        /// </summary>
        public Fix64[] PrimaryObjective2UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 主要目标3规则标识
        /// </summary>
        public string PrimaryObjective3Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 主要目标3数值
        /// </summary>
        public Fix64[] PrimaryObjective3UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标1规则标识
        /// </summary>
        public string OptionalObjective1Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标1数值
        /// </summary>
        public Fix64[] OptionalObjective1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标1完成经验
        /// </summary>
        public int OptionalObjective1Experience
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标2规则标识
        /// </summary>
        public string OptionalObjective2Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标2数值
        /// </summary>
        public Fix64[] OptionalObjective2UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标2完成经验
        /// </summary>
        public int OptionalObjective2Experience
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标3规则标识
        /// </summary>
        public string OptionalObjective3Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标3数值
        /// </summary>
        public Fix64[] OptionalObjective3UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标3完成经验
        /// </summary>
        public int OptionalObjective3Experience
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标4规则标识
        /// </summary>
        public string OptionalObjective4Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标4数值
        /// </summary>
        public Fix64[] OptionalObjective4UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标4完成经验
        /// </summary>
        public int OptionalObjective4Experience
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标5规则标识
        /// </summary>
        public string OptionalObjective5Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标5数值
        /// </summary>
        public Fix64[] OptionalObjective5UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 可选目标5完成经验
        /// </summary>
        public int OptionalObjective5Experience
        {
            get;
            private set;
        }

        /// <summary>
        /// 关卡首通经验
        /// </summary>
        public int FirstClearExperience
        {
            get;
            private set;
        }

        /// <summary>
        /// 关卡每次通关经验
        /// </summary>
        public int ClearExperience
        {
            get;
            private set;
        }

        /// <summary>
        /// 变量首通经验
        /// </summary>
        public int VariableFirstClearExperience
        {
            get;
            private set;
        }

        /// <summary>
        /// 变量每次通关经验
        /// </summary>
        public int VariableClearExperience
        {
            get;
            private set;
        }

        /// <summary>
        /// 变量试验可选关卡配置（空则使用本关）
        /// </summary>
        public string VariableLevelConfigIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 变量试验规则配置标识
        /// </summary>
        public string VariableRuleIdentifier
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
            PrimaryObjective1Identifier = columnStrings[index++];
            PrimaryObjective1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            PrimaryObjective2Identifier = columnStrings[index++];
            PrimaryObjective2UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            PrimaryObjective3Identifier = columnStrings[index++];
            PrimaryObjective3UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            OptionalObjective1Identifier = columnStrings[index++];
            OptionalObjective1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            OptionalObjective1Experience = DataTableExtension.ParseInt32(columnStrings[index++]);
            OptionalObjective2Identifier = columnStrings[index++];
            OptionalObjective2UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            OptionalObjective2Experience = DataTableExtension.ParseInt32(columnStrings[index++]);
            OptionalObjective3Identifier = columnStrings[index++];
            OptionalObjective3UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            OptionalObjective3Experience = DataTableExtension.ParseInt32(columnStrings[index++]);
            OptionalObjective4Identifier = columnStrings[index++];
            OptionalObjective4UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            OptionalObjective4Experience = DataTableExtension.ParseInt32(columnStrings[index++]);
            OptionalObjective5Identifier = columnStrings[index++];
            OptionalObjective5UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            OptionalObjective5Experience = DataTableExtension.ParseInt32(columnStrings[index++]);
            FirstClearExperience = DataTableExtension.ParseInt32(columnStrings[index++]);
            ClearExperience = DataTableExtension.ParseInt32(columnStrings[index++]);
            VariableFirstClearExperience = DataTableExtension.ParseInt32(columnStrings[index++]);
            VariableClearExperience = DataTableExtension.ParseInt32(columnStrings[index++]);
            VariableLevelConfigIdentifier = columnStrings[index++];
            VariableRuleIdentifier = columnStrings[index++];
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
                    PrimaryObjective1Identifier = binaryReader.ReadString();
                    PrimaryObjective1UniqueValues = binaryReader.ReadFix64Array();
                    PrimaryObjective2Identifier = binaryReader.ReadString();
                    PrimaryObjective2UniqueValues = binaryReader.ReadFix64Array();
                    PrimaryObjective3Identifier = binaryReader.ReadString();
                    PrimaryObjective3UniqueValues = binaryReader.ReadFix64Array();
                    OptionalObjective1Identifier = binaryReader.ReadString();
                    OptionalObjective1UniqueValues = binaryReader.ReadFix64Array();
                    OptionalObjective1Experience = binaryReader.Read7BitEncodedInt32();
                    OptionalObjective2Identifier = binaryReader.ReadString();
                    OptionalObjective2UniqueValues = binaryReader.ReadFix64Array();
                    OptionalObjective2Experience = binaryReader.Read7BitEncodedInt32();
                    OptionalObjective3Identifier = binaryReader.ReadString();
                    OptionalObjective3UniqueValues = binaryReader.ReadFix64Array();
                    OptionalObjective3Experience = binaryReader.Read7BitEncodedInt32();
                    OptionalObjective4Identifier = binaryReader.ReadString();
                    OptionalObjective4UniqueValues = binaryReader.ReadFix64Array();
                    OptionalObjective4Experience = binaryReader.Read7BitEncodedInt32();
                    OptionalObjective5Identifier = binaryReader.ReadString();
                    OptionalObjective5UniqueValues = binaryReader.ReadFix64Array();
                    OptionalObjective5Experience = binaryReader.Read7BitEncodedInt32();
                    FirstClearExperience = binaryReader.Read7BitEncodedInt32();
                    ClearExperience = binaryReader.Read7BitEncodedInt32();
                    VariableFirstClearExperience = binaryReader.Read7BitEncodedInt32();
                    VariableClearExperience = binaryReader.Read7BitEncodedInt32();
                    VariableLevelConfigIdentifier = binaryReader.ReadString();
                    VariableRuleIdentifier = binaryReader.ReadString();
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
