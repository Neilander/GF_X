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
/// 剧情触发表
/// </summary>
public class StoryTriggerTable : DataRowBase
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
        /// 触发时机
        /// </summary>
        public StoryTiming Timing
        {
            get;
            private set;
        }

        /// <summary>
        /// 对应剧情段落标识
        /// </summary>
        public string ScriptID
        {
            get;
            private set;
        }

        /// <summary>
        /// 是否只播放一次
        /// </summary>
        public bool OnceOnly
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
            Timing = DataTableExtension.ParseEnum<StoryTiming>(columnStrings[index++]);
            ScriptID = columnStrings[index++];
            OnceOnly = DataTableExtension.ParseBool(columnStrings[index++]);

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
                    Timing = binaryReader.ReadEnum<StoryTiming>();
                    ScriptID = binaryReader.ReadString();
                    OnceOnly = binaryReader.ReadBoolean();
                }
            }

            return true;
        }
}
