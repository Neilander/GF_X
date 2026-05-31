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
/// 关卡词条表
/// </summary>
public class LevelTagTable : DataRowBase
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
        /// 所属组（同组冲突）
        /// </summary>
        public int GroupID
        {
            get;
            private set;
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
        /// icon路径
        /// </summary>
        public string IconSpritePath
        {
            get;
            private set;
        }

        /// <summary>
        /// tag名(多语言)
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// tag描述(多语言)
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 所属关卡ID
        /// </summary>
        public string[] BelongLevelID
        {
            get;
            private set;
        }

        /// <summary>
        /// 排除关卡ID
        /// </summary>
        public string[] ExceptLevelID
        {
            get;
            private set;
        }

        /// <summary>
        /// 是否正面tag
        /// </summary>
        public bool IsPositiveTag
        {
            get;
            private set;
        }

        /// <summary>
        /// tag分值
        /// </summary>
        public int Score
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
            index++;
            UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            GroupID = DataTableExtension.ParseInt32(columnStrings[index++]);
            Identifier = columnStrings[index++];
            IconSpritePath = columnStrings[index++];
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            BelongLevelID = DataTableExtension.ParseArray<string>(columnStrings[index++]);
            ExceptLevelID = DataTableExtension.ParseArray<string>(columnStrings[index++]);
            IsPositiveTag = DataTableExtension.ParseBoolean(columnStrings[index++]);
            Score = DataTableExtension.ParseInt32(columnStrings[index++]);
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
                    GroupID = binaryReader.Read7BitEncodedInt32();
                    Identifier = binaryReader.ReadString();
                    IconSpritePath = binaryReader.ReadString();
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    BelongLevelID = binaryReader.ReadArray<string>();
                    ExceptLevelID = binaryReader.ReadArray<string>();
                    IsPositiveTag = binaryReader.ReadBoolean();
                    Score = binaryReader.Read7BitEncodedInt32();
                }
            }

            return true;
        }
}
