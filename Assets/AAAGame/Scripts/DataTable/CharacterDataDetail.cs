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
/// CharacterDataDetail
/// </summary>
public class CharacterDataDetail : DataRowBase
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
        /// 代码内标识符
        /// </summary>
        public string CharacterKey
        {
            get;
            private set;
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
        /// prefab路径
        /// </summary>
        public string PrefabPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 单位名称（多语言）
        /// </summary>
        public string NameKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 单位描述（多语言）
        /// </summary>
        public string DescKey
        {
            get;
            private set;
        }

        /// <summary>
        /// 单位标签
        /// </summary>
        public UnitTag[] UnitTags
        {
            get;
            private set;
        }

        /// <summary>
        /// 占用人口
        /// </summary>
        public int Supply
        {
            get;
            private set;
        }

        /// <summary>
        /// 体型
        /// </summary>
        public UnitSize Size
        {
            get;
            private set;
        }

        /// <summary>
        /// 转身速率
        /// </summary>
        public Fix64 TurnRate
        {
            get;
            private set;
        }

        /// <summary>
        /// 视野半径
        /// </summary>
        public Fix64 Sight
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级护甲
        /// </summary>
        public Fix64 Lv1Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级血量
        /// </summary>
        public Fix64 Lv1HP
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级移速
        /// </summary>
        public Fix64 Lv1MoveSpeed
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1攻击力
        /// </summary>
        public Fix64 Lv1Weapon1Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1攻击距离
        /// </summary>
        public Fix64 Lv1Weapon1Range
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级护甲
        /// </summary>
        public Fix64 Lv2Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级血量
        /// </summary>
        public Fix64 Lv2HP
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级移速
        /// </summary>
        public Fix64 Lv2MoveSpeed
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1攻击力
        /// </summary>
        public Fix64 Lv2Weapon1Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1攻击距离
        /// </summary>
        public Fix64 Lv2Weapon1Range
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级护甲
        /// </summary>
        public Fix64 Lv3Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级血量
        /// </summary>
        public Fix64 Lv3HP
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级移速
        /// </summary>
        public Fix64 Lv3MoveSpeed
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1攻击力
        /// </summary>
        public Fix64 Lv3Weapon1Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1攻击距离
        /// </summary>
        public Fix64 Lv3Weapon1Range
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1攻击间隔
        /// </summary>
        public Fix64 Lv1Weapon1Interval
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1攻击类型
        /// </summary>
        public WeaponType Lv1Weapon1Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1弹道速度
        /// </summary>
        public Fix64 Lv1Weapon1ProjectileSpeed
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1前摇
        /// </summary>
        public Fix64 Lv1Weapon1WindUp
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1后摇
        /// </summary>
        public Fix64 Lv1Weapon1WindDown
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1溅射半径
        /// </summary>
        public Fix64 Lv1Weapon1SplashRadius
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1分裂角度
        /// </summary>
        public Fix64 Lv1Weapon1SplitAngle
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1分裂距离
        /// </summary>
        public Fix64 Lv1Weapon1SplitDist
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1弹道数量
        /// </summary>
        public Fix64 Lv1Weapon1ProjectileCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1弹药量
        /// </summary>
        public Fix64 Lv1Weapon1AmmunitionCapacity
        {
            get;
            private set;
        }

        /// <summary>
        /// 1级武器1其他数值
        /// </summary>
        public Fix64[] Lv1Weapon1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1攻击间隔
        /// </summary>
        public Fix64 Lv2Weapon1Interval
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1攻击类型
        /// </summary>
        public WeaponType Lv2Weapon1Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1弹道速度
        /// </summary>
        public Fix64 Lv2Weapon1ProjectileSpeed
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1前摇
        /// </summary>
        public Fix64 Lv2Weapon1WindUp
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1后摇
        /// </summary>
        public Fix64 Lv2Weapon1WindDown
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1溅射半径
        /// </summary>
        public Fix64 Lv2Weapon1SplashRadius
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1分裂角度
        /// </summary>
        public Fix64 Lv2Weapon1SplitAngle
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1分裂距离
        /// </summary>
        public Fix64 Lv2Weapon1SplitDist
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1弹道数量
        /// </summary>
        public Fix64 Lv2Weapon1ProjectileCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1弹药量
        /// </summary>
        public Fix64 Lv2Weapon1AmmunitionCapacity
        {
            get;
            private set;
        }

        /// <summary>
        /// 2级武器1其他数值
        /// </summary>
        public Fix64[] Lv2Weapon1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1攻击间隔
        /// </summary>
        public Fix64 Lv3Weapon1Interval
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1攻击类型
        /// </summary>
        public WeaponType Lv3Weapon1Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1弹道速度
        /// </summary>
        public Fix64 Lv3Weapon1ProjectileSpeed
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1前摇
        /// </summary>
        public Fix64 Lv3Weapon1WindUp
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1后摇
        /// </summary>
        public Fix64 Lv3Weapon1WindDown
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1溅射半径
        /// </summary>
        public Fix64 Lv3Weapon1SplashRadius
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1分裂角度
        /// </summary>
        public Fix64 Lv3Weapon1SplitAngle
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1分裂距离
        /// </summary>
        public Fix64 Lv3Weapon1SplitDist
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1弹道数量
        /// </summary>
        public Fix64 Lv3Weapon1ProjectileCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1弹药量
        /// </summary>
        public Fix64 Lv3Weapon1AmmunitionCapacity
        {
            get;
            private set;
        }

        /// <summary>
        /// 3级武器1其他数值
        /// </summary>
        public Fix64[] Lv3Weapon1UniqueValues
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
            CharacterKey = columnStrings[index++];
            index++;
            index++;
            UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            PrefabPath = columnStrings[index++];
            NameKey = columnStrings[index++];
            DescKey = columnStrings[index++];
            UnitTags = DataTableExtension.ParseArray<UnitTag>(columnStrings[index++]);
            Supply = DataTableExtension.ParseInt32(columnStrings[index++]);
            Size = DataTableExtension.ParseEnum<UnitSize>(columnStrings[index++]);
            TurnRate = DataTableExtension.ParseFix64(columnStrings[index++]);
            Sight = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1HP = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1MoveSpeed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1Range = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2HP = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2MoveSpeed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1Range = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3HP = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3MoveSpeed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1Range = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1Interval = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1Type = DataTableExtension.ParseEnum<WeaponType>(columnStrings[index++]);
            Lv1Weapon1ProjectileSpeed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1WindUp = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1WindDown = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1SplashRadius = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1SplitAngle = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1SplitDist = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1ProjectileCount = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1AmmunitionCapacity = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv1Weapon1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Lv2Weapon1Interval = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1Type = DataTableExtension.ParseEnum<WeaponType>(columnStrings[index++]);
            Lv2Weapon1ProjectileSpeed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1WindUp = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1WindDown = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1SplashRadius = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1SplitAngle = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1SplitDist = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1ProjectileCount = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1AmmunitionCapacity = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv2Weapon1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Lv3Weapon1Interval = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1Type = DataTableExtension.ParseEnum<WeaponType>(columnStrings[index++]);
            Lv3Weapon1ProjectileSpeed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1WindUp = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1WindDown = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1SplashRadius = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1SplitAngle = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1SplitDist = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1ProjectileCount = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1AmmunitionCapacity = DataTableExtension.ParseFix64(columnStrings[index++]);
            Lv3Weapon1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);

            return true;
        }

        public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            using (MemoryStream memoryStream = new MemoryStream(dataRowBytes, startIndex, length, false))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8))
                {
                    m_Id = binaryReader.Read7BitEncodedInt32();
                    CharacterKey = binaryReader.ReadString();
                    UniqueValues = binaryReader.ReadFix64Array();
                    PrefabPath = binaryReader.ReadString();
                    NameKey = binaryReader.ReadString();
                    DescKey = binaryReader.ReadString();
                    UnitTags = binaryReader.ReadArray<UnitTag>();
                    Supply = binaryReader.Read7BitEncodedInt32();
                    Size = binaryReader.ReadEnum<UnitSize>();
                    TurnRate = binaryReader.ReadFix64();
                    Sight = binaryReader.ReadFix64();
                    Lv1Def = binaryReader.ReadFix64();
                    Lv1HP = binaryReader.ReadFix64();
                    Lv1MoveSpeed = binaryReader.ReadFix64();
                    Lv1Weapon1Atk = binaryReader.ReadFix64();
                    Lv1Weapon1Range = binaryReader.ReadFix64();
                    Lv2Def = binaryReader.ReadFix64();
                    Lv2HP = binaryReader.ReadFix64();
                    Lv2MoveSpeed = binaryReader.ReadFix64();
                    Lv2Weapon1Atk = binaryReader.ReadFix64();
                    Lv2Weapon1Range = binaryReader.ReadFix64();
                    Lv3Def = binaryReader.ReadFix64();
                    Lv3HP = binaryReader.ReadFix64();
                    Lv3MoveSpeed = binaryReader.ReadFix64();
                    Lv3Weapon1Atk = binaryReader.ReadFix64();
                    Lv3Weapon1Range = binaryReader.ReadFix64();
                    Lv1Weapon1Interval = binaryReader.ReadFix64();
                    Lv1Weapon1Type = binaryReader.ReadEnum<WeaponType>();
                    Lv1Weapon1ProjectileSpeed = binaryReader.ReadFix64();
                    Lv1Weapon1WindUp = binaryReader.ReadFix64();
                    Lv1Weapon1WindDown = binaryReader.ReadFix64();
                    Lv1Weapon1SplashRadius = binaryReader.ReadFix64();
                    Lv1Weapon1SplitAngle = binaryReader.ReadFix64();
                    Lv1Weapon1SplitDist = binaryReader.ReadFix64();
                    Lv1Weapon1ProjectileCount = binaryReader.ReadFix64();
                    Lv1Weapon1AmmunitionCapacity = binaryReader.ReadFix64();
                    Lv1Weapon1UniqueValues = binaryReader.ReadFix64Array();
                    Lv2Weapon1Interval = binaryReader.ReadFix64();
                    Lv2Weapon1Type = binaryReader.ReadEnum<WeaponType>();
                    Lv2Weapon1ProjectileSpeed = binaryReader.ReadFix64();
                    Lv2Weapon1WindUp = binaryReader.ReadFix64();
                    Lv2Weapon1WindDown = binaryReader.ReadFix64();
                    Lv2Weapon1SplashRadius = binaryReader.ReadFix64();
                    Lv2Weapon1SplitAngle = binaryReader.ReadFix64();
                    Lv2Weapon1SplitDist = binaryReader.ReadFix64();
                    Lv2Weapon1ProjectileCount = binaryReader.ReadFix64();
                    Lv2Weapon1AmmunitionCapacity = binaryReader.ReadFix64();
                    Lv2Weapon1UniqueValues = binaryReader.ReadFix64Array();
                    Lv3Weapon1Interval = binaryReader.ReadFix64();
                    Lv3Weapon1Type = binaryReader.ReadEnum<WeaponType>();
                    Lv3Weapon1ProjectileSpeed = binaryReader.ReadFix64();
                    Lv3Weapon1WindUp = binaryReader.ReadFix64();
                    Lv3Weapon1WindDown = binaryReader.ReadFix64();
                    Lv3Weapon1SplashRadius = binaryReader.ReadFix64();
                    Lv3Weapon1SplitAngle = binaryReader.ReadFix64();
                    Lv3Weapon1SplitDist = binaryReader.ReadFix64();
                    Lv3Weapon1ProjectileCount = binaryReader.ReadFix64();
                    Lv3Weapon1AmmunitionCapacity = binaryReader.ReadFix64();
                    Lv3Weapon1UniqueValues = binaryReader.ReadFix64Array();
                }
            }

            return true;
        }
}
