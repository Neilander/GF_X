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
/// CharacterMainPropertyTable
/// </summary>
public class CharacterMainPropertyTable : DataRowBase
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
        /// 请添加字段, 字段名首字母大写
        /// </summary>
        public string CharacterKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int PhysicalAtk
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int SpecialAtk
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int PhysicalDef
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int SpecialDef
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int Health
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int Speed
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int Mana
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
            CharacterKey = columnStrings[index++];
            PhysicalAtk = int.Parse(columnStrings[index++]);
            SpecialAtk = int.Parse(columnStrings[index++]);
            PhysicalDef = int.Parse(columnStrings[index++]);
            SpecialDef = int.Parse(columnStrings[index++]);
            Health = int.Parse(columnStrings[index++]);
            Speed = int.Parse(columnStrings[index++]);
            Mana = int.Parse(columnStrings[index++]);

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
                    PhysicalAtk = binaryReader.Read7BitEncodedInt32();
                    SpecialAtk = binaryReader.Read7BitEncodedInt32();
                    PhysicalDef = binaryReader.Read7BitEncodedInt32();
                    SpecialDef = binaryReader.Read7BitEncodedInt32();
                    Health = binaryReader.Read7BitEncodedInt32();
                    Speed = binaryReader.Read7BitEncodedInt32();
                    Mana = binaryReader.Read7BitEncodedInt32();
                }
            }

            return true;
        }
}
