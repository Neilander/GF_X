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
/// 信物表
/// </summary>
public class KeepsakeTable : DataRowBase
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
        /// 稳定信物标识
        /// </summary>
        public string Identifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 信物名称（多语言）
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 信物描述（多语言）
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 图标路径，可留空
        /// </summary>
        public string IconPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 英雄角色配置标识，模型、动作、武器和属性读取CharacterDataDetail
        /// </summary>
        public string HeroCharacterKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 初始技能标识列表，按逗号分隔
        /// </summary>
        public string[] InitialSkillIdentifiers
        {
            get;
            private set;
        }

        /// <summary>
        /// 是否初始解锁
        /// </summary>
        public bool InitialUnlocked
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
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            IconPath = columnStrings[index++];
            HeroCharacterKey = columnStrings[index++];
            InitialSkillIdentifiers = DataTableExtension.ParseArray<string>(columnStrings[index++]);
            InitialUnlocked = DataTableExtension.ParseBoolean(columnStrings[index++]);

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
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    IconPath = binaryReader.ReadString();
                    HeroCharacterKey = binaryReader.ReadString();
                    InitialSkillIdentifiers = binaryReader.ReadArray<string>();
                    InitialUnlocked = binaryReader.ReadBoolean();
                }
            }

            return true;
        }
}
