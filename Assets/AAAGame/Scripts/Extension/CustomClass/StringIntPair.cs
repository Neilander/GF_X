using GameFramework;
using System;


/// <summary>
/// 字符串与整数的组合值。
/// </summary>
public struct StringIntPair : IEquatable<StringIntPair>
{
    public string str;
    public int num;

    public StringIntPair(string str, int num)
    {
        this.str = str;
        this.num = num;
    }

    public override int GetHashCode()
    {
        return (str?.GetHashCode() ?? 0) ^ num.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        return obj is StringIntPair && Equals((StringIntPair)obj);
    }

    public bool Equals(StringIntPair value)
    {
        return str == value.str && num == value.num;
    }

    public static bool operator ==(StringIntPair a, StringIntPair b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(StringIntPair a, StringIntPair b)
    {
        return !(a == b);
    }
}

