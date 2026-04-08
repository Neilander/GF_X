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
/// EntityGroup
/// </summary>
public class EntityGroupTable : DataRowBase
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
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public float ReleaseInterval
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int Capacity
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public float ExpireTime
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int Priority
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
            Name = columnStrings[index++];
            ReleaseInterval = DataTableExtension.ParseSingle(columnStrings[index++]);
            Capacity = DataTableExtension.ParseInt32(columnStrings[index++]);
            ExpireTime = DataTableExtension.ParseSingle(columnStrings[index++]);
            Priority = DataTableExtension.ParseInt32(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    Name = binaryReader.ReadString();
                    ReleaseInterval = binaryReader.ReadSingle();
                    Capacity = binaryReader.Read7BitEncodedInt32();
                    ExpireTime = binaryReader.ReadSingle();
                    Priority = binaryReader.Read7BitEncodedInt32();
                }
            }

            return true;
        }
}
