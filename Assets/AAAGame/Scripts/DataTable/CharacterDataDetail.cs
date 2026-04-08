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
/// CharacterDataDetail
/// </summary>
public class CharacterDataDetail : DataRowBase
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
        /// 
        /// </summary>
        public string CharacterKey
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
        /// 护甲
        /// </summary>
        public Fix64 PhysicalDef
        {
            get;
            private set;
        }

        /// <summary>
        /// 血量
        /// </summary>
        public Fix64 Health
        {
            get;
            private set;
        }

        /// <summary>
        /// 蓝量
        /// </summary>
        public Fix64 Mana
        {
            get;
            private set;
        }

        /// <summary>
        /// 移速
        /// </summary>
        public Fix64 Speed
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击力
        /// </summary>
        public Fix64 PhysicalAtk
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击间隔
        /// </summary>
        public Fix64 WeaponIntervalOne
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击类型
        /// </summary>
        public WeaponType WeaponTypeOne
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击距离
        /// </summary>
        public Fix64 WeaponRangeOne
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1弹道速度
        /// </summary>
        public Fix64 WeaponSpeedOne
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1前摇
        /// </summary>
        public Fix64 WeaponPreOne
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1后摇
        /// </summary>
        public Fix64 WeaponEndOne
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
            CharacterKey = columnStrings[index++];
            index++;
            index++;
            UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            PhysicalDef = DataTableExtension.ParseFix64(columnStrings[index++]);
            Health = DataTableExtension.ParseFix64(columnStrings[index++]);
            Mana = DataTableExtension.ParseFix64(columnStrings[index++]);
            Speed = DataTableExtension.ParseFix64(columnStrings[index++]);
            index++;
            PhysicalAtk = DataTableExtension.ParseFix64(columnStrings[index++]);
            WeaponIntervalOne = DataTableExtension.ParseFix64(columnStrings[index++]);
            WeaponTypeOne = DataTableExtension.ParseEnum<WeaponType>(columnStrings[index++]);
            WeaponRangeOne = DataTableExtension.ParseFix64(columnStrings[index++]);
            WeaponSpeedOne = DataTableExtension.ParseFix64(columnStrings[index++]);
            WeaponPreOne = DataTableExtension.ParseFix64(columnStrings[index++]);
            WeaponEndOne = DataTableExtension.ParseFix64(columnStrings[index++]);
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;
            index++;

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    CharacterKey = binaryReader.ReadString();
                    UniqueValues = binaryReader.ReadFix64Array();
                    PhysicalDef = binaryReader.ReadFix64();
                    Health = binaryReader.ReadFix64();
                    Mana = binaryReader.ReadFix64();
                    Speed = binaryReader.ReadFix64();
                    PhysicalAtk = binaryReader.ReadFix64();
                    WeaponIntervalOne = binaryReader.ReadFix64();
                    WeaponTypeOne = binaryReader.ReadEnum<WeaponType>();
                    WeaponRangeOne = binaryReader.ReadFix64();
                    WeaponSpeedOne = binaryReader.ReadFix64();
                    WeaponPreOne = binaryReader.ReadFix64();
                    WeaponEndOne = binaryReader.ReadFix64();
                }
            }

            return true;
        }
}
