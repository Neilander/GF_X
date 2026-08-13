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
/// 工具表
/// </summary>
public class ItemWithEffectTable : DataRowBase
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
        /// 品质
        /// </summary>
        public ItemRarity Rarity
        {
            get;
            private set;
        }

        /// <summary>
        /// 最大堆叠数(-1表示无限)
        /// </summary>
        public int MaxStack
        {
            get;
            private set;
        }

        /// <summary>
        /// 属性数值
        /// </summary>
        public StringFix64Pair[] PropertyNumerals
        {
            get;
            private set;
        }

        /// <summary>
        /// 效果数值
        /// </summary>
        public Fix64[] EffectNumerals
        {
            get;
            private set;
        }

        /// <summary>
        /// 效果简介(仅供速查)
        /// </summary>
        public string EffectIntroduction
        {
            get;
            private set;
        }

        /// <summary>
        /// Sprite名
        /// </summary>
        public string SpriteName
        {
            get;
            private set;
        }

        /// <summary>
        /// 物品名(多语言)
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 物品描述(多语言)
        /// </summary>
        public string DescriptionKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 物品类型标签
        /// </summary>
        public ItemTag[] Tags
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
            Rarity = DataTableExtension.ParseEnum<ItemRarity>(columnStrings[index++]);
            MaxStack = DataTableExtension.ParseInt32(columnStrings[index++]);
            PropertyNumerals = DataTableExtension.ParseStringFix64PairArray(columnStrings[index++]);
            EffectNumerals = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            EffectIntroduction = columnStrings[index++];
            SpriteName = columnStrings[index++];
            NameKey = columnStrings[index++];
            DescriptionKey = columnStrings[index++];
            Tags = DataTableExtension.ParseArray<ItemTag>(columnStrings[index++]);

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
                    Rarity = binaryReader.ReadEnum<ItemRarity>();
                    MaxStack = binaryReader.Read7BitEncodedInt32();
                    PropertyNumerals = binaryReader.ReadStringFix64PairArray();
                    EffectNumerals = binaryReader.ReadFix64Array();
                    EffectIntroduction = binaryReader.ReadString();
                    SpriteName = binaryReader.ReadString();
                    NameKey = binaryReader.ReadString();
                    DescriptionKey = binaryReader.ReadString();
                    Tags = binaryReader.ReadArray<ItemTag>();
                }
            }

            return true;
        }
}
