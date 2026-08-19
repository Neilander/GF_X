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
/// 防御路线表
/// </summary>
public class DefendRouteTable : DataRowBase
{
	private int m_Id = 0;
	/// <summary>
    /// 行号
    /// </summary>
    public override int Id
    {
        get { return m_Id; }
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
        /// 所属关卡标识
        /// </summary>
        public string LevelIdentifier
        {
            get;
            private set;
        }

        /// <summary>
        /// 固定出兵来源传送点ID
        /// </summary>
        public string SourceTeleportationId
        {
            get;
            private set;
        }

        /// <summary>
        /// 固定途经传送点ID，按行军顺序填写；最终目标由运行时当前我方GameEnd建筑动态决定
        /// </summary>
        public string[] WaypointTeleportationIds
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
            Identifier = columnStrings[index++];
            LevelIdentifier = columnStrings[index++];
            SourceTeleportationId = columnStrings[index++];
            WaypointTeleportationIds = DataTableExtension.ParseArray<string>(columnStrings[index++]);

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
                    LevelIdentifier = binaryReader.ReadString();
                    SourceTeleportationId = binaryReader.ReadString();
                    WaypointTeleportationIds = binaryReader.ReadArray<string>();
                }
            }

            return true;
        }
}
