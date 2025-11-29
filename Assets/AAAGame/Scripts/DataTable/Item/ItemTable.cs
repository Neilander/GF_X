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
public class ItemTable : DataRowBase
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
        /// 在代码中的命名标识符(下划线前与表名一致)
        /// </summary>
        public string Identifier
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
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// 物品描述(多语言)
        /// </summary>
        public string Description
        {
            get;
            private set;
        }

        /// <summary>
        /// 物品类型标签
        /// </summary>
        public int[] Tags
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
            Identifier = columnStrings[index++];
            SpriteName = columnStrings[index++];
            Name = columnStrings[index++];
            Description = columnStrings[index++];
            Tags = DataTableExtension.ParseArray<int>(columnStrings[index++]);
            MaxStack = int.Parse(columnStrings[index++]);

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
                    SpriteName = binaryReader.ReadString();
                    Name = binaryReader.ReadString();
                    Description = binaryReader.ReadString();
                    Tags = binaryReader.ReadArray<int>();
                    MaxStack = binaryReader.Read7BitEncodedInt32();
                }
            }

            return true;
        }
}
