using GameFramework;
using System;
using System.Runtime.InteropServices;


/// <summary>
/// 类型和名称的组合值。
/// </summary>
public struct StringFix64Pair : IEquatable<StringFix64Pair>
{
    public string str;
    public Fix64 num;

    public StringFix64Pair(string str, Fix64 num)
    {
        this.str = str;
        this.num = num;
    }

    /// <summary>
    /// 获取对象的哈希值。
    /// </summary>
    /// <returns>对象的哈希值。</returns>
    public override int GetHashCode()
    {
        return str.GetHashCode() ^ num.GetHashCode();
    }

    /// <summary>
    /// 比较对象是否与自身相等。
    /// </summary>
    /// <param name="obj">要比较的对象。</param>
    /// <returns>被比较的对象是否与自身相等。</returns>
    public override bool Equals(object obj)
    {
        return obj is StringFix64Pair && Equals((StringFix64Pair)obj);
    }

    /// <summary>
    /// 比较对象是否与自身相等。
    /// </summary>
    /// <param name="value">要比较的对象。</param>
    /// <returns>被比较的对象是否与自身相等。</returns>
    public bool Equals(StringFix64Pair value)
    {
        return str == value.str && num == value.num;
    }

    /// <summary>
    /// 判断两个对象是否相等。
    /// </summary>
    /// <param name="a">值 a。</param>
    /// <param name="b">值 b。</param>
    /// <returns>两个对象是否相等。</returns>
    public static bool operator ==(StringFix64Pair a, StringFix64Pair b)
    {
        return a.Equals(b);
    }

    /// <summary>
    /// 判断两个对象是否不相等。
    /// </summary>
    /// <param name="a">值 a。</param>
    /// <param name="b">值 b。</param>
    /// <returns>两个对象是否不相等。</returns>
    public static bool operator !=(StringFix64Pair a, StringFix64Pair b)
    {
        return !(a == b);
    }
}
