using System;
using UnityEngine;

public static class NavigationGridFixedMath
{
    public const int AuthorityPayloadVersion = 1;
    public const int EncodedFixVector2Size = sizeof(long) * 2;

    private const int GridFractionalPlaces = 32;
    private const int GridToFixShift = GridFractionalPlaces - Fix64.FRACTIONAL_PLACES;
    private const long GridOne = 1L << GridFractionalPlaces;

    public static long FloatToGridRaw(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Navigation grid value must be finite.");

        double scaled = (double)value * GridOne;
        if (scaled < long.MinValue || scaled > long.MaxValue)
            throw new OverflowException($"Navigation grid value is outside Q32 range: {value}.");
        return checked((long)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }

    public static Fix64 GridRawToFix64(long value)
    {
        const long half = 1L << (GridToFixShift - 1);
        long fixRaw = value >= 0
            ? checked(value + half) >> GridToFixShift
            : -checked((-value + half) >> GridToFixShift);
        return Fix64.FromRaw(fixRaw);
    }

    public static long Fix64ToGridRaw(Fix64 value)
    {
        return checked(value.RawValue << GridToFixShift);
    }

    public static int WorldToGridCell(Fix64 worldPosition, long originGridRaw, long cellSizeGridRaw)
    {
        return GridRawToCell(Fix64ToGridRaw(worldPosition), originGridRaw, cellSizeGridRaw);
    }

    public static int WorldToGridCell(float worldPosition, long originGridRaw, long cellSizeGridRaw)
    {
        return GridRawToCell(FloatToGridRaw(worldPosition), originGridRaw, cellSizeGridRaw);
    }

    private static int GridRawToCell(long worldGridRaw, long originGridRaw, long cellSizeGridRaw)
    {
        if (cellSizeGridRaw <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeGridRaw));

        long offset = checked(worldGridRaw - originGridRaw);
        long result = offset / cellSizeGridRaw;
        if (offset < 0 && offset % cellSizeGridRaw != 0)
            result--;
        return checked((int)result);
    }

    public static int DivideCeilingByCellSize(Fix64 distance, long cellSizeGridRaw)
    {
        if (distance < Fix64.Zero)
            throw new ArgumentOutOfRangeException(nameof(distance));
        if (cellSizeGridRaw <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeGridRaw));

        long distanceGridRaw = Fix64ToGridRaw(distance);
        return checked((int)((distanceGridRaw + cellSizeGridRaw - 1) / cellSizeGridRaw));
    }

    public static FixVector2 GridCellCenterFixed(
        long cellSizeGridRaw,
        long originXGridRaw,
        long originZGridRaw,
        int x,
        int y)
    {
        if (cellSizeGridRaw <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeGridRaw));

        long centerXRaw = checked(originXGridRaw + checked((long)x * cellSizeGridRaw) + cellSizeGridRaw / 2);
        long centerZRaw = checked(originZGridRaw + checked((long)y * cellSizeGridRaw) + cellSizeGridRaw / 2);
        return new FixVector2(GridRawToFix64(centerXRaw), GridRawToFix64(centerZRaw));
    }

    public static byte[] EncodeFixVector2XZ(Vector3[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        byte[] result = new byte[checked(values.Length * EncodedFixVector2Size)];
        for (int i = 0; i < values.Length; i++)
        {
            Vector3 value = values[i];
            if (!IsFinite(value))
                throw new InvalidOperationException($"Cannot encode a non-finite navigation anchor. index={i} value={value}.");

            int offset = i * EncodedFixVector2Size;
            WriteInt64LittleEndian(result, offset, ((Fix64)value.x).RawValue);
            WriteInt64LittleEndian(result, offset + sizeof(long), ((Fix64)value.z).RawValue);
        }

        return result;
    }

    public static FixVector2 DecodeFixVector2XZ(byte[] payload, int index)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.Length % EncodedFixVector2Size != 0)
            throw new InvalidOperationException($"Fixed navigation anchor payload length is invalid. length={payload.Length}.");
        if (index < 0 || index >= payload.Length / EncodedFixVector2Size)
            throw new ArgumentOutOfRangeException(nameof(index));

        int offset = index * EncodedFixVector2Size;
        return new FixVector2(
            Fix64.FromRaw(ReadInt64LittleEndian(payload, offset)),
            Fix64.FromRaw(ReadInt64LittleEndian(payload, offset + sizeof(long))));
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
               && !float.IsNaN(value.y)
               && !float.IsNaN(value.z)
               && !float.IsInfinity(value.x)
               && !float.IsInfinity(value.y)
               && !float.IsInfinity(value.z);
    }

    private static void WriteInt64LittleEndian(byte[] destination, int offset, long value)
    {
        ulong bits = unchecked((ulong)value);
        for (int i = 0; i < sizeof(long); i++)
            destination[offset + i] = (byte)(bits >> (i * 8));
    }

    private static long ReadInt64LittleEndian(byte[] source, int offset)
    {
        ulong bits = 0;
        for (int i = 0; i < sizeof(long); i++)
            bits |= (ulong)source[offset + i] << (i * 8);
        return unchecked((long)bits);
    }
}
