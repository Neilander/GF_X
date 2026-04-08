using GameFramework;
using GameFramework.DataTable;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Mathematics;
using UnityEngine;
using UnityGameFramework.Runtime;
public static class DataTableExtension
{
    internal static readonly char[] DataSplitSeparators = new char[] { '\t' };
    internal static readonly char[] DataTrimSeparators = new char[] { '\"' };

    /// <summary>
    /// 加载数据表, 支持A/B测试
    /// </summary>
    /// <param name="dataTableComponent"></param>
    /// <param name="dataTableName"></param>
    /// <param name="abTestGroupName"></param>
    /// <param name="userData"></param>
    public static void LoadDataTable(this DataTableComponent dataTableComponent, string dataTableName, string abTestGroupName, bool useBytes, object userData = null)
    {
        if (string.IsNullOrWhiteSpace(dataTableName))
        {
            Log.Warning("Data table name is invalid.");
            return;
        }

        string[] splitNames = dataTableName.Split('_');
        if (splitNames.Length > 2)
        {
            Log.Warning("Data table name is invalid.");
            return;
        }

        string dataRowClassName = System.IO.Path.GetFileName(splitNames[0]);

        Type dataRowType = Type.GetType(dataRowClassName);
        if (dataRowType == null)
        {
            Log.Warning("Can not get data row type with class name '{0}'.", dataRowClassName);
            return;
        }

        string name = splitNames.Length > 1 ? splitNames[1] : null;
        DataTableBase dataTable = dataTableComponent.CreateDataTable(dataRowType, name);

        string tableFileName = dataTableName;
        if (!string.IsNullOrWhiteSpace(abTestGroupName))
        {
            var abTableFileName = Utility.Text.Format("{0}{1}{2}", dataTableName, ConstBuiltin.AB_TEST_TAG, abTestGroupName);
            if (GFBuiltin.Resource.HasAsset(UtilityBuiltin.AssetsPath.GetDataTablePath(abTableFileName, useBytes)) != GameFramework.Resource.HasAssetResult.NotExist)
            {
                tableFileName = abTableFileName;
            }
        }

        string assetName = UtilityBuiltin.AssetsPath.GetDataTablePath(tableFileName, useBytes);
        try
        {
            dataTable.ReadData(assetName, userData);
        }
        catch (Exception e)
        {
            Log.Error("Load data table '{0}' failed, asset '{1}'. Error: {2}", dataTableName, assetName, e);
            dataTableComponent.DestroyDataTable(dataTable);
        }
    }

    /// <summary>
    /// 加载数据表
    /// 注意: 数据表类为热更新部分,所以需要从Hotfix程序集查找表类型
    /// </summary>
    /// <param name="dataTableComponent"></param>
    /// <param name="dataTableName"></param>
    /// <param name="userData"></param>
    public static void LoadDataTable(this DataTableComponent dataTableComponent, string dataTableName, bool useBytes, object userData = null)
    {
        string abTestGroup = GFBuiltin.Setting.GetABTestGroup();
        dataTableComponent.LoadDataTable(dataTableName, abTestGroup, useBytes, userData);
    }
    public static Color32 ParseColor32(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Color32(255, 255, 255, 255);
        string[] splitValue = value.Split(',');
        return new Color32(ParseByte(splitValue[0]), ParseByte(splitValue[1]), ParseByte(splitValue[2]), ParseByte(splitValue[3]));
    }
    public static Color32 ReadColor32(this BinaryReader binaryReader)
    {
        return new Color32(binaryReader.ReadByte(), binaryReader.ReadByte(), binaryReader.ReadByte(), binaryReader.ReadByte());
    }
    public static Color ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Color.white;
        string[] splitValue = value.Split(',');
        return new Color(ParseSingle(splitValue[0]), ParseSingle(splitValue[1]), ParseSingle(splitValue[2]), ParseSingle(splitValue[3]));
    }
    public static Color ReadColor(this BinaryReader binaryReader)
    {
        return new Color(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }
    public static Quaternion ParseQuaternion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Quaternion.identity;
        string[] splitValue = value.Split(',');
        return new Quaternion(ParseSingle(splitValue[0]), ParseSingle(splitValue[1]), ParseSingle(splitValue[2]), ParseSingle(splitValue[3]));
    }
    public static Quaternion ReadQuaternion(this BinaryReader binaryReader)
    {
        return new Quaternion(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }
    public static DateTime ParseDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;
        return DateTime.Parse(value);
    }

    public static bool ParseBoolean(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return bool.Parse(value);
    }

    public static byte ParseByte(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return byte.Parse(value);
    }

    public static sbyte ParseSByte(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return sbyte.Parse(value);
    }

    public static short ParseInt16(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return short.Parse(value);
    }

    public static ushort ParseUInt16(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return ushort.Parse(value);
    }

    public static int ParseInt32(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return int.Parse(value);
    }

    public static uint ParseUInt32(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return uint.Parse(value);
    }

    public static long ParseInt64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return long.Parse(value);
    }

    public static ulong ParseUInt64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return ulong.Parse(value);
    }

    public static float ParseSingle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return float.Parse(value);
    }

    public static double ParseDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return double.Parse(value);
    }

    public static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return decimal.Parse(value);
    }

    public static char ParseChar(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        return char.Parse(value);
    }

    public static DateTime ReadDateTime(this BinaryReader binaryReader)
    {
        return new DateTime(binaryReader.ReadInt64());
    }
    public static Rect ParseRect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Rect.zero;
        string[] splitValue = value.Split(',');
        return new Rect(ParseSingle(splitValue[0]), ParseSingle(splitValue[1]), ParseSingle(splitValue[2]), ParseSingle(splitValue[3]));
    }
    public static Rect ReadRect(this BinaryReader binaryReader)
    {
        return new Rect(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }
    public static Vector2 ParseVector2(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Vector2.zero;
        string[] splitValue = value.Split(',');
        return new Vector2(ParseSingle(splitValue[0]), ParseSingle(splitValue[1]));
    }
    public static Vector2 ReadVector2(this BinaryReader binaryReader)
    {
        return new Vector2(binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }

    public static Vector2[] ParseVector2Array(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<Vector2>();
        Vector2[] result = new Vector2[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = ParseVector2(arr[i]);
        }
        return result;
    }
    public static Vector2[] ReadVector2Array(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Vector2[] result = new Vector2[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.ReadVector2();
        }
        return result;
    }
    public static Vector2Int ParseVector2Int(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Vector2Int.zero;
        string[] splitValue = value.Split(',');
        return new Vector2Int(ParseInt32(splitValue[0]), ParseInt32(splitValue[1]));
    }
    public static Vector2Int ReadVector2Int(this BinaryReader binaryReader)
    {
        return new Vector2Int(binaryReader.Read7BitEncodedInt32(), binaryReader.Read7BitEncodedInt32());
    }
    public static Vector2Int[] ParseVector2IntArray(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<Vector2Int>();
        Vector2Int[] result = new Vector2Int[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = ParseVector2Int(arr[i]);
        }
        return result;
    }
    public static Vector2Int[] ReadVector2IntArray(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Vector2Int[] result = new Vector2Int[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.ReadVector2Int();
        }
        return result;
    }
    public static Vector3 ParseVector3(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Vector3.zero;
        string[] splitValue = value.Split(',');

        return new Vector3(ParseSingle(splitValue[0]), ParseSingle(splitValue[1]), ParseSingle(splitValue[2]));
    }
    public static Vector3 ReadVector3(this BinaryReader binaryReader)
    {
        return new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }
    public static Vector3[] ParseVector3Array(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<Vector3>();
        Vector3[] result = new Vector3[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = ParseVector3(arr[i]);
        }
        return result;
    }
    public static Vector3[] ReadVector3Array(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Vector3[] result = new Vector3[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.ReadVector3();
        }
        return result;
    }
    public static Vector3Int ParseVector3Int(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Vector3Int.zero;
        string[] splitValue = value.Split(',');
        return new Vector3Int(ParseInt32(splitValue[0]), ParseInt32(splitValue[1]), ParseInt32(splitValue[2]));
    }
    public static Vector3Int ReadVector3Int(this BinaryReader binaryReader)
    {
        return new Vector3Int(binaryReader.Read7BitEncodedInt32(), binaryReader.Read7BitEncodedInt32(), binaryReader.Read7BitEncodedInt32());
    }
    public static Vector3Int[] ParseVector3IntArray(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<Vector3Int>();
        Vector3Int[] result = new Vector3Int[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = ParseVector3Int(arr[i]);
        }
        return result;
    }
    public static Vector3Int[] ReadVector3IntArray(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Vector3Int[] result = new Vector3Int[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.ReadVector3Int();
        }
        return result;
    }
    public static Vector4 ParseVector4(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Vector4.zero;
        string[] splitValue = value.Split(',');
        return new Vector4(ParseSingle(splitValue[0]), ParseSingle(splitValue[1]), ParseSingle(splitValue[2]), ParseSingle(splitValue[3]));
    }
    public static Vector4 ReadVector4(this BinaryReader binaryReader)
    {
        return new Vector4(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
    }
    public static Vector4[] ParseVector4Array(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<Vector4>();

        Vector4[] result = new Vector4[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = ParseVector4(arr[i]);
        }
        return result;
    }
    public static Vector4[] ReadVector4Array(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Vector4[] result = new Vector4[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.ReadVector4();
        }
        return result;
    }
    public static Unity.Mathematics.int4 Parseint4(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return int4.zero;
        string[] splitValue = value.Split(',');
        return new Unity.Mathematics.int4(ParseInt32(splitValue[0]), ParseInt32(splitValue[1]), ParseInt32(splitValue[2]), ParseInt32(splitValue[3]));
    }
    public static Unity.Mathematics.int4 Readint4(this BinaryReader binaryReader)
    {
        return new Unity.Mathematics.int4(binaryReader.Read7BitEncodedInt32(), binaryReader.Read7BitEncodedInt32(), binaryReader.Read7BitEncodedInt32(), binaryReader.Read7BitEncodedInt32());
    }

    public static Unity.Mathematics.int4[] Parseint4Array(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<Unity.Mathematics.int4>();
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<Unity.Mathematics.int4>();
        Unity.Mathematics.int4[] result = new Unity.Mathematics.int4[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = Parseint4(arr[i]);
        }
        return result;
    }
    public static Unity.Mathematics.int4[] Readint4Array(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Unity.Mathematics.int4[] result = new Unity.Mathematics.int4[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.Readint4();
        }
        return result;
    }
    /// <summary>
    /// 解析枚举
    /// </summary>
    /// <typeparam name="TEnum"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    public static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum
    {
        if (!string.IsNullOrEmpty(value))
        {
            string[] splitValue = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (splitValue.Length == 1)
            {
                var valueStr = splitValue[0].Split('.')[1];
                if (Enum.TryParse<TEnum>(valueStr, out TEnum result))
                {
                    return result;
                }
            }
            else
            {
                int resultEnum = 0;
                foreach (string s in splitValue)
                {
                    var strTrim = s.Split('.')[1].Trim();
                    if (Enum.TryParse<TEnum>(strTrim, true, out TEnum result))
                    {
                        resultEnum |= Convert.ToInt32(result);
                    }
                }
                return (TEnum)Enum.ToObject(typeof(TEnum), resultEnum);
            }
        }
        return default(TEnum);
    }

    public static TEnum? ParseNullableEnum<TEnum>(string value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseEnum<TEnum>(value);
    }
    public static TEnum ReadEnum<TEnum>(this BinaryReader binaryReader) where TEnum : struct, Enum
    {
        int value = binaryReader.Read7BitEncodedInt32();
        if (Enum.IsDefined(typeof(TEnum), value))
        {
            return (TEnum)(object)value;
        }
        throw new GameFrameworkException(Utility.Text.Format("Value {0} is not defined in enum {1}.", value, typeof(TEnum).Name));
    }

    public static TEnum? ReadNullableEnum<TEnum>(this BinaryReader binaryReader) where TEnum : struct, Enum
    {
        bool hasValue = binaryReader.ReadBoolean();
        if (!hasValue)
        {
            return null;
        }

        return binaryReader.ReadEnum<TEnum>();
    }
    /// <summary>
    /// 解析数据表数组
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    public static T[] ParseArray<T>(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<T>();
        }
        string[] strs = value.Split(',');
        T[] arr = new T[strs.Length];
        Type t = typeof(T);
        for (int i = 0; i < strs.Length; i++)
        {
            try
            {
                string s = strs[i].Trim();
                if (t.IsEnum)
                {
                    int enumValue = 0;
                    var parts = s.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1)
                    {
                        var part = parts[0];
                        if (part.Contains('.')) part = part.Split('.')[1];
                        enumValue = (int)Enum.Parse(t, part, true);
                    }
                    else
                    {
                        foreach (var p in parts)
                        {
                            var part = p;
                            if (part.Contains('.')) part = part.Split('.')[1];
                            var tmp = (int)Enum.Parse(t, part, true);
                            enumValue |= tmp;
                        }
                    }
                    arr[i] = (T)Enum.ToObject(t, enumValue);
                }
                else if (t == typeof(Fix64))
                {
                    var f = Fix64.Parse(s);
                    arr[i] = (T)(object)f;
                }
                else
                {
                    arr[i] = (T)Convert.ChangeType(s, typeof(T));
                }
            }
            catch (Exception e)
            {
                Log.Error("解析失败数据失败! 格式有误:{0}\nError:{1}", strs[i], e.Message);
            }
        }
        return arr;
    }

    public static Fix64 ParseFix64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Fix64.Zero;
        return Fix64.Parse(value);
    }
    public static Fix64 ReadFix64(this BinaryReader binaryReader)
    {
        long raw = binaryReader.ReadInt64();
        return Fix64.FromRaw(raw);
    }
    public static Fix64[] ParseFix64Array(string value)
    {
        return ParseArray<Fix64>(value);
    }
    public static Fix64[] ReadFix64Array(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        Fix64[] result = new Fix64[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = binaryReader.ReadFix64();
        }
        return result;
    }

    public static StringFix64Pair[] ParseStringFix64PairArray(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<StringFix64Pair>();
        StringFix64Pair[] result = new StringFix64Pair[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            var parts = arr[i].Split(',', 2);
            var key = parts.Length > 0 ? parts[0] : string.Empty;
            var num = parts.Length > 1 ? Fix64.Parse(parts[1]) : Fix64.Zero;
            result[i] = new StringFix64Pair(key, num);
        }
        return result;
    }
    public static StringFix64Pair[] ReadStringFix64PairArray(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        StringFix64Pair[] result = new StringFix64Pair[length];
        for (int i = 0; i < length; i++)
        {
            string s = binaryReader.ReadString();
            long raw = binaryReader.ReadInt64();
            result[i] = new StringFix64Pair(s, Fix64.FromRaw(raw));
        }
        return result;
    }

    public static StringIntPair[] ParseStringIntPairArray(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<StringIntPair>();
        StringIntPair[] result = new StringIntPair[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            var parts = arr[i].Split(',', 2);
            var key = parts.Length > 0 ? parts[0] : string.Empty;
            int num = 0;
            if (parts.Length > 1)
            {
                int.TryParse(parts[1], out num);
            }
            result[i] = new StringIntPair(key, num);
        }
        return result;
    }

    public static UnlockCondition[] ParseUnlockConditionArray(string value)
    {
        string[] arr = ParseArrayElements(value);
        if (arr.Length == 0) return Array.Empty<UnlockCondition>();

        UnlockCondition[] result = new UnlockCondition[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            var parts = arr[i].Split(',', 2);
            var typeStr = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            var args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            if (!Enum.TryParse(typeStr, true, out UnlockConditionType type))
            {
                throw new GameFrameworkException(Utility.Text.Format("Invalid UnlockConditionType '{0}'.", typeStr));
            }

            result[i] = new UnlockCondition(type, args);
        }

        return result;
    }

    public static UnlockCondition ParseUnlockCondition(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = value.Trim();
        // 兼容可能的单元素数组写法：[Tech,TechNode_Map_1]
        if (s.Length >= 2 && s[0] == '[' && s[^1] == ']')
        {
            s = s.Substring(1, s.Length - 2).Trim();
        }

        var parts = s.Split(',', 2);
        var typeStr = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var id = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (!Enum.TryParse(typeStr, true, out UnlockConditionType type))
        {
            throw new GameFrameworkException(Utility.Text.Format("Invalid UnlockConditionType '{0}'.", typeStr));
        }

        return new UnlockCondition(type, id);
    }
    public static StringIntPair[] ReadStringIntPairArray(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        StringIntPair[] result = new StringIntPair[length];
        for (int i = 0; i < length; i++)
        {
            string s = binaryReader.ReadString();
            int num = binaryReader.Read7BitEncodedInt32();
            result[i] = new StringIntPair(s, num);
        }
        return result;
    }

    public static UnlockCondition[] ReadUnlockConditionArray(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        UnlockCondition[] result = new UnlockCondition[length];
        for (int i = 0; i < length; i++)
        {
            int typeValue = binaryReader.Read7BitEncodedInt32();
            string args = binaryReader.ReadString();
            result[i] = new UnlockCondition((UnlockConditionType)typeValue, args);
        }
        return result;
    }

    public static UnlockCondition ReadUnlockCondition(this BinaryReader binaryReader)
    {
        int typeValue = binaryReader.Read7BitEncodedInt32();
        string id = binaryReader.ReadString();
        return new UnlockCondition((UnlockConditionType)typeValue, id);
    }
    public static T[] ReadArray<T>(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        T[] arr = new T[length];
        Type type = typeof(T);
        if (type == typeof(int))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.Read7BitEncodedInt32();
            }
        }
        else if (type == typeof(float))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.ReadSingle();
            }
        }
        else if (type == typeof(double))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.Read();
            }
        }
        else if (type == typeof(long))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.Read();
            }
        }
        else if (type == typeof(bool))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.ReadBoolean();
            }
        }
        else if (type == typeof(string))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.ReadString();
            }
        }
        else if (type == typeof(byte))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.ReadByte();
            }
        }
        else if (type == typeof(char))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)binaryReader.ReadChar();
            }
        }
        else if (type.IsEnum)
        {
            for (int i = 0; i < length; i++)
            {
                int value = binaryReader.Read7BitEncodedInt32();
                if (Enum.IsDefined(type, value))
                {
                    arr[i] = (T)(object)value;
                }
                else
                {
                    throw new GameFrameworkException(Utility.Text.Format("Value {0} is not defined in enum {1}.", value, type.Name));
                }
            }
        }
        else if (type == typeof(DateTime))
        {
            for (int i = 0; i < length; i++)
            {
                arr[i] = (T)(object)new DateTime(binaryReader.ReadInt64());
            }
        }
        return arr;
    }
    /// <summary>
    /// 解析数据表2维数组
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    public static T[][] Parse2DArray<T>(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<T[]>();
        }
        var mats = Regex.Matches(value, "\\[.+?\\]");
        if (mats.Count > 0)
        {
            T[][] arr = new T[mats.Count][];
            for (int i = 0; i < mats.Count; i++)
            {
                string vstr = mats[i].Value;
                vstr = vstr[1..^1];
                arr[i] = ParseArray<T>(vstr);
            }
            return arr;
        }
        return Array.Empty<T[]>();
    }
    public static T[][] Read2DArray<T>(this BinaryReader binaryReader)
    {
        int length = binaryReader.Read7BitEncodedInt32();
        T[][] arr = new T[length][];
        for (int i = 0; i < length; i++)
        {
            arr[i] = ReadArray<T>(binaryReader);
        }
        return arr;
    }

    public static Type ParseType(string value)
    {
        return Type.GetType(value);
    }
    public static Type ReadType(this BinaryReader binaryReader)
    {
        return ParseType(binaryReader.ReadString());
    }
    private static string[] ParseArrayElements(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }
        var mats = Regex.Matches(value, "\\[(.*?)\\]");
        if (mats.Count > 0)
        {
            var arr = mats
                .Cast<Match>()
                .Select(m => m.Value.Length >= 2 ? m.Value[1..^1] : string.Empty)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToArray();
            return arr.Length == 0 ? Array.Empty<string>() : arr;
        }
        return Array.Empty<string>();
    }
    public static bool TryParseEnum(string enumValue, out Type enumType, out int value)
    {
        enumType = null;
        value = 0;
        var enumElements = enumValue.Split('.');
        if (enumElements.Length != 2)
        {
            return false;
        }
        var enumName = enumElements[0];
        enumType = Utility.Assembly.GetType(enumName);
        if (enumType == null)
        {
            enumType = Utility.Assembly.GetTypes().FirstOrDefault(t => t.IsEnum && (t.Name == enumName));
        }
        if (enumType != null)
        {
            value = (int)Enum.Parse(enumType, enumElements[1]);
        }
        return enumType != null && enumType.IsEnum;
    }
    public static bool TryParseEnum(string enumValue, out Type enumType)
    {
        return TryParseEnum(enumValue, out enumType, out _);
    }
}