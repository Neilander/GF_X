using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

public static class PropertyFuncRef 
{
    // 累加所有 func[i]()
    public static Func<Func<Fix64>[], Func<Fix64>> SumAll =
        funcArray => () =>
        {
            Fix64 total = Fix64.Zero;
            foreach (var f in funcArray)
                total += f();
            return total;
        };

    // 累乘所有 func[i]()
    public static Func<Func<Fix64>[], Func<Fix64>> MultAll =
        funcArray => () =>
        {
            Fix64 total = Fix64.One;
            foreach (var f in funcArray)
                total *= f();
            return total;
        };
    
    public static Func<Func<Fix64>[], Func<Fix64>> GetAbilityWithConfigAndLevel =
        funcArray => () =>
        {
            // 1. 获取等级
            Fix64 level = funcArray[0]();

            // 2. 获取种族值
            Fix64 race = funcArray[1]();

            // 3. 分解等级 level = 10x + y
            Fix64 xFix = Fix64.Floor(level / (Fix64)10);  // Fix64 格式的 x
            Fix64 y = level - xFix * (Fix64)10;

            // 转 int（Fix64.Floor 保证它是整数）
            int x = (int)xFix;

            // 4. 计算能力
            Fix64 pow = Fix64.Pow((Fix64)2, x);        // 2^x
            Fix64 scale = Fix64.One + y / (Fix64)10;   // 1 + y/10

            Fix64 ability = pow * scale * race / (Fix64)18.5m;

            return ability;
        };
    
    public static Func<Func<Fix64>[], Func<Fix64>> GetHealthWithConfigAndLevel =
        funcArray => () =>
        {
            // 1. 获取等级
            Fix64 level = funcArray[0]();

            // 2. 获取种族值
            Fix64 race = funcArray[1]();

            // 3. 分解等级 level = 10x + y
            Fix64 xFix = Fix64.Floor(level / (Fix64)10);  // Fix64 格式的 x
            Fix64 y = level - xFix * (Fix64)10;

            // 转 int（Fix64.Floor 保证它是整数）
            int x = (int)xFix;

            // 4. 计算能力
            Fix64 pow = Fix64.Pow((Fix64)2, x);        // 2^x
            Fix64 scale = Fix64.One + y / (Fix64)10;   // 1 + y/10

            // 新公式：3 + 2^x * (1+y/10) * race / 18.5
            Fix64 ability = (Fix64)3 + pow * scale * race / (Fix64)18.5m;

            return ability;
        };
    
    public static Func<Func<Fix64>[], Func<Fix64>> GetSpeedWithConfigAndLevel =
        funcArray => () =>
        {
            // 0. 移速不再依赖等级，等级仍然是 funcArray[0]（留着但不使用）
            // Fix64 level = funcArray[0]();

            // 1. 种族值
            Fix64 race = funcArray[1]();

            // 2. 公式：移速 = 200 + 种族
            return (Fix64)200 + race;
        };
    
    public static Func<Func<Fix64>[], Func<Fix64>> GetManaWithConfigAndLevel =
        funcArray => () =>
        {
            // 1. 等级
            Fix64 level = funcArray[0]();

            // 2. 种族值
            Fix64 race = funcArray[1]();

            // 3. 分解 level = 10x + y
            Fix64 xFix = Fix64.Floor(level / (Fix64)10);
            Fix64 y = level - xFix * (Fix64)10;
            int x = (int)xFix;

            // 4. 计算蓝量
            // pow = (1.3)^x
            Fix64 pow = Fix64.Pow((Fix64)1.3m, x);

            // scale = 1 + 3y / 100
            Fix64 scale = Fix64.One + (Fix64)3 * y / (Fix64)100;

            // 最终公式：1 + (1.3^x) * (1 + 3y/100) * 种族 / 18.5
            Fix64 mana = (Fix64)1 + pow * scale * race / (Fix64)18.5m;

            return mana;
        };
}
