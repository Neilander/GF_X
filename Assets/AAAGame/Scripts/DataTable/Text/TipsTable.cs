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
/// 侧边提示表
/// </summary>
public class TipsTable : DataRowBase
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
        /// 代码内唯一标识
        /// </summary>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 标题本地化键
        /// </summary>
        public string TitleKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 内容本地化键
        /// </summary>
        public string ContentKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 讲话人图标，空值默认为Narrator
        /// </summary>
        public string Icon
        {
            get;
            private set;
        }

        /// <summary>
        /// 存在时间，-1表示条件结束
        /// </summary>
        public float Duration
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
            TitleKey = columnStrings[index++];
            ContentKey = columnStrings[index++];
            Icon = columnStrings[index++];
            Duration = DataTableExtension.ParseSingle(columnStrings[index++]);

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
                    TitleKey = binaryReader.ReadString();
                    ContentKey = binaryReader.ReadString();
                    Icon = binaryReader.ReadString();
                    Duration = binaryReader.ReadSingle();
                }
            }

            return true;
        }
}
