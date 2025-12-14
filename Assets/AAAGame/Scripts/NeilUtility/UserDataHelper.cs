using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IVector3ArrayData
{
    Vector3[] Vector3Array { get; }
}

public interface IFloatArrayData
{
    float[] FloatArray { get; }
}

public interface IIntArrayData
{
    int[] IntArray { get; }
}

public interface IBoolArrayData
{
    bool[] BoolArray { get; }
}

public class Vector3ArrayData : IVector3ArrayData
{
    public Vector3[] Vector3Array { get; private set; }

    public Vector3ArrayData(Vector3 value)
    {
        Vector3Array = new[] { value };
    }

    public Vector3ArrayData(params Vector3[] values)
    {
        Vector3Array = values;
    }
}

public class FloatArrayData : IFloatArrayData
{
    public float[] FloatArray { get; private set; }

    public FloatArrayData(float value)
    {
        FloatArray = new[] { value };
    }

    public FloatArrayData(params float[] values)
    {
        FloatArray = values;
    }
}

public class IntArrayData : IIntArrayData
{
    public int[] IntArray { get; private set; }

    public IntArrayData(int value)
    {
        IntArray = new[] { value };
    }

    public IntArrayData(params int[] values)
    {
        IntArray = values;
    }
}

public class BoolArrayData : IBoolArrayData
{
    public bool[] BoolArray { get; private set; }

    public BoolArrayData(bool value)
    {
        BoolArray = new[] { value };
    }

    public BoolArrayData(params bool[] values)
    {
        BoolArray = values;
    }
}

public class UniversalUserData : 
    IVector3ArrayData, 
    IFloatArrayData, 
    IIntArrayData, 
    IBoolArrayData
{
    public Vector3[] Vector3Array { get; set; }
    public float[] FloatArray { get; set; }
    public int[] IntArray { get; set; }
    public bool[] BoolArray { get; set; }
}