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
/// 关前通讯表
/// </summary>
public class StoryCommTable : DataRowBase
{
	private int m_Id = 0;
	/// <summary>
    /// 唯一编号
    /// </summary>
    public override int Id
    {
        get { return m_Id; }
    }

        /// <summary>
        /// 关卡标识
        /// </summary>
        public string LevelIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 通讯位置
        /// </summary>
        public StoryCommSlot Slot
        {
            get;
            private set;
        }

        /// <summary>
        /// 说话人名称（多语言）
        /// </summary>
        public string SpeakerKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 立绘路径，可留空
        /// </summary>
        public string PortraitPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 通讯正文（多语言）
        /// </summary>
        public string TextKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 信号状态
        /// </summary>
        public StorySignalState SignalState
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
            m_Id = DataTableExtension.ParseInt(columnStrings[index++]);
            index++;
            LevelIdentifier = columnStrings[index++];
            Slot = DataTableExtension.ParseEnum<StoryCommSlot>(columnStrings[index++]);
            SpeakerKey = columnStrings[index++];
            PortraitPath = columnStrings[index++];
            TextKey = columnStrings[index++];
            SignalState = DataTableExtension.ParseEnum<StorySignalState>(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    LevelIdentifier = binaryReader.ReadString();
                    Slot = binaryReader.ReadEnum<StoryCommSlot>();
                    SpeakerKey = binaryReader.ReadString();
                    PortraitPath = binaryReader.ReadString();
                    TextKey = binaryReader.ReadString();
                    SignalState = binaryReader.ReadEnum<StorySignalState>();
                }
            }

            return true;
        }
}
