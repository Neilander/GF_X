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
        /// 模型碰撞半径
        /// </summary>
        public Fix64 CollisionRadius
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
        /// 护甲
        /// </summary>
        public Fix64 Def
        {
            get;
            private set;
        }

        /// <summary>
        /// 血量
        /// </summary>
        public Fix64 Health
        {
            get;
            private set;
        }

        /// <summary>
        /// 蓝量
        /// </summary>
        public Fix64 Mana
        {
            get;
            private set;
        }

        /// <summary>
        /// 移速
        /// </summary>
        public Fix64 Speed
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
        /// 武器1攻击力
        /// </summary>
        public Fix64 Weapon1Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击间隔
        /// </summary>
        public Fix64 Weapon1Interval
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击类型
        /// </summary>
        public WeaponType Weapon1Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1攻击距离
        /// </summary>
        public Fix64 Weapon1Range
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1弹道速度
        /// </summary>
        public Fix64 Weapon1Speed
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1前摇
        /// </summary>
        public Fix64 Weapon1WindUp
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1后摇
        /// </summary>
        public Fix64 Weapon1WindDown
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1溅射半径
        /// </summary>
        public Fix64 Weapon1SplashRadius
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1分裂角度
        /// </summary>
        public Fix64 Weapon1SplitAngle
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1分裂距离
        /// </summary>
        public Fix64 Weapon1SplitDist
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1弹道数量
        /// </summary>
        public Fix64 Weapon1ProjectileCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1耗蓝
        /// </summary>
        public Fix64 Weapon1ManaCost
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器1其他数值
        /// </summary>
        public Fix64[] Weapon1UniqueValues
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2攻击力
        /// </summary>
        public Fix64 Weapon2Atk
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2攻击间隔
        /// </summary>
        public Fix64 Weapon2Interval
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2攻击类型
        /// </summary>
        public WeaponType Weapon2Type
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2攻击距离
        /// </summary>
        public Fix64 Weapon2Range
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2弹道速度
        /// </summary>
        public Fix64 Weapon2Speed
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2前摇
        /// </summary>
        public Fix64 Weapon2WindUp
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2后摇
        /// </summary>
        public Fix64 Weapon2WindDown
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2溅射半径
        /// </summary>
        public Fix64 Weapon2SplashRadius
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2分裂角度
        /// </summary>
        public Fix64 Weapon2SplitAngle
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2分裂距离
        /// </summary>
        public Fix64 Weapon2SplitDist
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2弹道数量
        /// </summary>
        public Fix64 Weapon2ProjectileCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2耗蓝
        /// </summary>
        public Fix64 Weapon2ManaCost
        {
            get;
            private set;
        }

        /// <summary>
        /// 武器2其他数值
        /// </summary>
        public Fix64[] Weapon2UniqueValues
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
            CollisionRadius = DataTableExtension.ParseFix64(columnStrings[index++]);
            TurnRate = DataTableExtension.ParseFix64(columnStrings[index++]);
            Def = DataTableExtension.ParseFix64(columnStrings[index++]);
            Health = DataTableExtension.ParseFix64(columnStrings[index++]);
            Mana = DataTableExtension.ParseFix64(columnStrings[index++]);
            Speed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Sight = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1Interval = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1Type = DataTableExtension.ParseEnum<WeaponType>(columnStrings[index++]);
            Weapon1Range = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1Speed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1WindUp = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1WindDown = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1SplashRadius = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1SplitAngle = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1SplitDist = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1ProjectileCount = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1ManaCost = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon1UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);
            Weapon2Atk = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2Interval = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2Type = DataTableExtension.ParseEnum<WeaponType>(columnStrings[index++]);
            Weapon2Range = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2Speed = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2WindUp = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2WindDown = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2SplashRadius = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2SplitAngle = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2SplitDist = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2ProjectileCount = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2ManaCost = DataTableExtension.ParseFix64(columnStrings[index++]);
            Weapon2UniqueValues = DataTableExtension.ParseFix64Array(columnStrings[index++]);

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
                    CollisionRadius = binaryReader.ReadFix64();
                    TurnRate = binaryReader.ReadFix64();
                    Def = binaryReader.ReadFix64();
                    Health = binaryReader.ReadFix64();
                    Mana = binaryReader.ReadFix64();
                    Speed = binaryReader.ReadFix64();
                    Sight = binaryReader.ReadFix64();
                    Weapon1Atk = binaryReader.ReadFix64();
                    Weapon1Interval = binaryReader.ReadFix64();
                    Weapon1Type = binaryReader.ReadEnum<WeaponType>();
                    Weapon1Range = binaryReader.ReadFix64();
                    Weapon1Speed = binaryReader.ReadFix64();
                    Weapon1WindUp = binaryReader.ReadFix64();
                    Weapon1WindDown = binaryReader.ReadFix64();
                    Weapon1SplashRadius = binaryReader.ReadFix64();
                    Weapon1SplitAngle = binaryReader.ReadFix64();
                    Weapon1SplitDist = binaryReader.ReadFix64();
                    Weapon1ProjectileCount = binaryReader.ReadFix64();
                    Weapon1ManaCost = binaryReader.ReadFix64();
                    Weapon1UniqueValues = binaryReader.ReadFix64Array();
                    Weapon2Atk = binaryReader.ReadFix64();
                    Weapon2Interval = binaryReader.ReadFix64();
                    Weapon2Type = binaryReader.ReadEnum<WeaponType>();
                    Weapon2Range = binaryReader.ReadFix64();
                    Weapon2Speed = binaryReader.ReadFix64();
                    Weapon2WindUp = binaryReader.ReadFix64();
                    Weapon2WindDown = binaryReader.ReadFix64();
                    Weapon2SplashRadius = binaryReader.ReadFix64();
                    Weapon2SplitAngle = binaryReader.ReadFix64();
                    Weapon2SplitDist = binaryReader.ReadFix64();
                    Weapon2ProjectileCount = binaryReader.ReadFix64();
                    Weapon2ManaCost = binaryReader.ReadFix64();
                    Weapon2UniqueValues = binaryReader.ReadFix64Array();
                }
            }

            return true;
        }
}
