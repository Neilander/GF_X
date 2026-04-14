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
/// 建筑表
/// </summary>
public class BuildingTable : DataRowBase
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
        /// 独有数值
        /// </summary>
        public Fix64[] UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 建筑类型
        /// </summary>
        public BuilType Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 所属种族
        /// </summary>
        public Archetype Archetype
        {
            get;
            private set;
        }

        /// <summary>
        /// 建筑名称（多语言）
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 建筑描述（多语言）
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级预制体路径
        /// </summary>
        public string Lv1PrefabPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级预制体路径
        /// </summary>
        public string Lv2PrefabPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级预制体路径
        /// </summary>
        public string Lv3PrefabPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级造价
        /// </summary>
        public int Lv1Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 1升2价格
        /// </summary>
        public int Lv2Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 2升3价格
        /// </summary>
        public int Lv3Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级血量
        /// </summary>
        public Fix64 Lv1HP
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级攻击力
        /// </summary>
        public Fix64 Lv1Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级护甲
        /// </summary>
        public Fix64 Lv1Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级血量
        /// </summary>
        public Fix64 Lv2HP
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级攻击力
        /// </summary>
        public Fix64 Lv2Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级护甲
        /// </summary>
        public Fix64 Lv2Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级血量
        /// </summary>
        public Fix64 Lv3HP
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级攻击力
        /// </summary>
        public Fix64 Lv3Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级护甲
        /// </summary>
        public Fix64 Lv3Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 生产单位id
        /// </summary>
        public string UnitID
        {
            get;
            private set;
        }

        /// <summary>
        /// 资源日产出/初始兵力
        /// </summary>
        public int Production
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1id
        /// </summary>
        public string Tech1ID
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1独有数值
        /// </summary>
        public Fix64[] Tech1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1全局是否可存在多个
        /// </summary>
        public bool Tech1Stackable
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1名称（多语言）
        /// </summary>
        public string Tech1NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1描述（多语言）
        /// </summary>
        public string Tech1DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1价格
        /// </summary>
        public int Tech1Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技1sprite路径
        /// </summary>
        public string Tech1SpritePath
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2id
        /// </summary>
        public string Tech2ID
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2独有数值
        /// </summary>
        public Fix64[] Tech2UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2全局是否可存在多个
        /// </summary>
        public bool Tech2Stackable
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2名称（多语言）
        /// </summary>
        public string Tech2NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2描述（多语言）
        /// </summary>
        public string Tech2DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2价格
        /// </summary>
        public int Tech2Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技2sprite路径
        /// </summary>
        public string Tech2SpritePath
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3id
        /// </summary>
        public string Tech3ID
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3独有数值
        /// </summary>
        public Fix64[] Tech3UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3全局是否可存在多个
        /// </summary>
        public bool Tech3Stackable
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3名称（多语言）
        /// </summary>
        public string Tech3NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3描述（多语言）
        /// </summary>
        public string Tech3DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3价格
        /// </summary>
        public int Tech3Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技3sprite路径
        /// </summary>
        public string Tech3SpritePath
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4id
        /// </summary>
        public string Tech4ID
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4独有数值
        /// </summary>
        public Fix64[] Tech4UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4全局是否可存在多个
        /// </summary>
        public bool Tech4Stackable
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4名称（多语言）
        /// </summary>
        public string Tech4NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4描述（多语言）
        /// </summary>
        public string Tech4DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4价格
        /// </summary>
        public int Tech4Cost
        {
            get;
            private set;
        }

        /// <summary>
        /// 科技4sprite路径
        /// </summary>
        public string Tech4SpritePath
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
            Identifier = columnStrings[index++];
            index++;
            index++;
            UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Type = DataTableExtension.ParseEnum<BuilType>(columnStrings[index++]);
            Archetype = DataTableExtension.ParseEnum<Archetype>(columnStrings[index++]);
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            Lv1PrefabPath = columnStrings[index++];
            Lv2PrefabPath = columnStrings[index++];
            Lv3PrefabPath = columnStrings[index++];
            Lv1Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Lv2Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Lv3Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Lv1HP = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2HP = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3HP = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            UnitID = columnStrings[index++];
            Production = DataTableExtension.ParseInt32(columnStrings[index++]);
            Tech1ID = columnStrings[index++];
            index++;
            Tech1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Tech1Stackable = DataTableExtension.ParseBoolean(columnStrings[index++]);
            Tech1NameKey = columnStrings[index++];
            Tech1DescKey = columnStrings[index++];
            Tech1Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Tech1SpritePath = columnStrings[index++];
            Tech2ID = columnStrings[index++];
            index++;
            Tech2UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Tech2Stackable = DataTableExtension.ParseBoolean(columnStrings[index++]);
            Tech2NameKey = columnStrings[index++];
            Tech2DescKey = columnStrings[index++];
            Tech2Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Tech2SpritePath = columnStrings[index++];
            Tech3ID = columnStrings[index++];
            index++;
            Tech3UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Tech3Stackable = DataTableExtension.ParseBoolean(columnStrings[index++]);
            Tech3NameKey = columnStrings[index++];
            Tech3DescKey = columnStrings[index++];
            Tech3Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Tech3SpritePath = columnStrings[index++];
            Tech4ID = columnStrings[index++];
            index++;
            Tech4UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Tech4Stackable = DataTableExtension.ParseBoolean(columnStrings[index++]);
            Tech4NameKey = columnStrings[index++];
            Tech4DescKey = columnStrings[index++];
            Tech4Cost = DataTableExtension.ParseInt32(columnStrings[index++]);
            Tech4SpritePath = columnStrings[index++];

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
                    UniqueValues = binaryReader.ReadFix64Array();
                    Type = binaryReader.ReadEnum<BuilType>();
                    Archetype = binaryReader.ReadEnum<Archetype>();
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    Lv1PrefabPath = binaryReader.ReadString();
                    Lv2PrefabPath = binaryReader.ReadString();
                    Lv3PrefabPath = binaryReader.ReadString();
                    Lv1Cost = binaryReader.Read7BitEncodedInt32();
                    Lv2Cost = binaryReader.Read7BitEncodedInt32();
                    Lv3Cost = binaryReader.Read7BitEncodedInt32();
                    Lv1HP = binaryReader.ReadFix64();
                    Lv1Atk = binaryReader.ReadFix64();
                    Lv1Def = binaryReader.ReadFix64();
                    Lv2HP = binaryReader.ReadFix64();
                    Lv2Atk = binaryReader.ReadFix64();
                    Lv2Def = binaryReader.ReadFix64();
                    Lv3HP = binaryReader.ReadFix64();
                    Lv3Atk = binaryReader.ReadFix64();
                    Lv3Def = binaryReader.ReadFix64();
                    UnitID = binaryReader.ReadString();
                    Production = binaryReader.Read7BitEncodedInt32();
                    Tech1ID = binaryReader.ReadString();
                    Tech1UniqueValues = binaryReader.ReadFix64Array();
                    Tech1Stackable = binaryReader.ReadBoolean();
                    Tech1NameKey = binaryReader.ReadString();
                    Tech1DescKey = binaryReader.ReadString();
                    Tech1Cost = binaryReader.Read7BitEncodedInt32();
                    Tech1SpritePath = binaryReader.ReadString();
                    Tech2ID = binaryReader.ReadString();
                    Tech2UniqueValues = binaryReader.ReadFix64Array();
                    Tech2Stackable = binaryReader.ReadBoolean();
                    Tech2NameKey = binaryReader.ReadString();
                    Tech2DescKey = binaryReader.ReadString();
                    Tech2Cost = binaryReader.Read7BitEncodedInt32();
                    Tech2SpritePath = binaryReader.ReadString();
                    Tech3ID = binaryReader.ReadString();
                    Tech3UniqueValues = binaryReader.ReadFix64Array();
                    Tech3Stackable = binaryReader.ReadBoolean();
                    Tech3NameKey = binaryReader.ReadString();
                    Tech3DescKey = binaryReader.ReadString();
                    Tech3Cost = binaryReader.Read7BitEncodedInt32();
                    Tech3SpritePath = binaryReader.ReadString();
                    Tech4ID = binaryReader.ReadString();
                    Tech4UniqueValues = binaryReader.ReadFix64Array();
                    Tech4Stackable = binaryReader.ReadBoolean();
                    Tech4NameKey = binaryReader.ReadString();
                    Tech4DescKey = binaryReader.ReadString();
                    Tech4Cost = binaryReader.Read7BitEncodedInt32();
                    Tech4SpritePath = binaryReader.ReadString();
                }
            }

            return true;
        }
}
