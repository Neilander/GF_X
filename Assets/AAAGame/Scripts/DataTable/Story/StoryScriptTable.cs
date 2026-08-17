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
/// 剧情脚本表
/// </summary>
public class StoryScriptTable : DataRowBase
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
        /// 剧情段落标识，同一段使用相同值
        /// </summary>
        public string ScriptID
        {
            get;
            private set;
        }

        /// <summary>
        /// 段落内播放顺序，从1开始
        /// </summary>
        public int Order
        {
            get;
            private set;
        }

        /// <summary>
        /// 背景图路径，留空沿用上一屏
        /// </summary>
        public string BgSpritePath
        {
            get;
            private set;
        }

        /// <summary>
        /// 左侧立绘路径，留空或-清除
        /// </summary>
        public string PortraitLeft
        {
            get;
            private set;
        }

        /// <summary>
        /// 中间立绘路径，留空或-清除
        /// </summary>
        public string PortraitCenter
        {
            get;
            private set;
        }

        /// <summary>
        /// 右侧立绘路径，留空或-清除
        /// </summary>
        public string PortraitRight
        {
            get;
            private set;
        }

        /// <summary>
        /// 立绘差分标识（预留）
        /// </summary>
        public string PortraitEmotion
        {
            get;
            private set;
        }

        /// <summary>
        /// 说话人名称（多语言），留空为旁白
        /// </summary>
        public string SpeakerKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 剧情正文（多语言）
        /// </summary>
        public string TextKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 剧情显示样式
        /// </summary>
        public StoryStyle Style
        {
            get;
            private set;
        }

        /// <summary>
        /// 入场或转场特效，可留空
        /// </summary>
        public string Effect
        {
            get;
            private set;
        }

        /// <summary>
        /// 动态文本引用，默认None
        /// </summary>
        public StoryDynamicRef DynamicRef
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
            ScriptID = columnStrings[index++];
            Order = DataTableExtension.ParseInt(columnStrings[index++]);
            BgSpritePath = columnStrings[index++];
            PortraitLeft = columnStrings[index++];
            PortraitCenter = columnStrings[index++];
            PortraitRight = columnStrings[index++];
            PortraitEmotion = columnStrings[index++];
            SpeakerKey = columnStrings[index++];
            TextKey = columnStrings[index++];
            Style = DataTableExtension.ParseEnum<StoryStyle>(columnStrings[index++]);
            Effect = columnStrings[index++];
            DynamicRef = DataTableExtension.ParseEnum<StoryDynamicRef>(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    ScriptID = binaryReader.ReadString();
                    Order = binaryReader.Read7BitEncodedInt32();
                    BgSpritePath = binaryReader.ReadString();
                    PortraitLeft = binaryReader.ReadString();
                    PortraitCenter = binaryReader.ReadString();
                    PortraitRight = binaryReader.ReadString();
                    PortraitEmotion = binaryReader.ReadString();
                    SpeakerKey = binaryReader.ReadString();
                    TextKey = binaryReader.ReadString();
                    Style = binaryReader.ReadEnum<StoryStyle>();
                    Effect = binaryReader.ReadString();
                    DynamicRef = binaryReader.ReadEnum<StoryDynamicRef>();
                }
            }

            return true;
        }
}
