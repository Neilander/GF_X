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
        /// 独有数值
        /// </summary>
        public Fix64[] UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// prefab路径
        /// </summary>
        public string PrefabPath
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
            index++;
            UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            PrefabPath = columnStrings[index++];
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
                UniqueValues = binaryReader.ReadFix64Array();
                PrefabPath = binaryReader.ReadString();
            }
            }

            return true;
        }
}
