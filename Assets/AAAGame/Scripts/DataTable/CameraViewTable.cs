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
/// 正交相机配置
/// </summary>
public class CameraViewTable : DataRowBase
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
        /// 相机欧拉角（X=俯角，Y=朝向）
        /// </summary>
        public Vector3 Rotation
        {
            get;
            private set;
        }

        /// <summary>
        /// 正交半屏高度（世界单位）
        /// </summary>
        public float OrthographicSize
        {
            get;
            private set;
        }

        /// <summary>
        /// 相机相对跟随点的竖直高度（世界单位）
        /// </summary>
        public float CameraHeight
        {
            get;
            private set;
        }

        /// <summary>
        /// 瞄准点偏移
        /// </summary>
        public Vector3 AimOffset
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
            Rotation = DataTableExtension.ParseVector3(columnStrings[index++]);
            OrthographicSize = DataTableExtension.ParseFloat(columnStrings[index++]);
            CameraHeight = DataTableExtension.ParseFloat(columnStrings[index++]);
            AimOffset = DataTableExtension.ParseVector3(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    Rotation = binaryReader.ReadVector3();
                    OrthographicSize = binaryReader.ReadSingle();
                    CameraHeight = binaryReader.ReadSingle();
                    AimOffset = binaryReader.ReadVector3();
                }
            }

            return true;
        }
}
