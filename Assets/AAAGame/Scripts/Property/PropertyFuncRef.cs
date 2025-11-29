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
}
