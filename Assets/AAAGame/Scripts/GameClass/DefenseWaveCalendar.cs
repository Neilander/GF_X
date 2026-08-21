using System;

public static class DefenseWaveCalendar
{
    public static int GetDefenseDay(GamePhase startPhase, int defenseWaveIndex)
    {
        if (defenseWaveIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(defenseWaveIndex));

        int firstDefenseDay = StartsWithDefenseHalf(startPhase) ? 1 : 2;
        return checked(firstDefenseDay + (defenseWaveIndex - 1) * 2);
    }

    public static int GetDefenseWaveIndex(GamePhase startPhase, int defenseDay)
    {
        if (defenseDay <= 0)
            throw new ArgumentOutOfRangeException(nameof(defenseDay));

        int firstDefenseDay = StartsWithDefenseHalf(startPhase) ? 1 : 2;
        int offset = defenseDay - firstDefenseDay;
        if (offset < 0 || (offset & 1) != 0)
        {
            throw new InvalidOperationException(
                $"Day {defenseDay} is not a defense day for start phase {startPhase}.");
        }
        return checked(offset / 2 + 1);
    }

    public static int GetLastAuthorableDefenseWave(GamePhase startPhase, int expectedDays)
    {
        if (expectedDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDays));

        int wave = 1;
        while (GetDefenseDay(startPhase, wave) <= expectedDays)
            wave++;
        return wave;
    }

    private static bool StartsWithDefenseHalf(GamePhase startPhase)
    {
        switch (startPhase)
        {
            case GamePhase.BuildBeforeDefend:
            case GamePhase.Defend:
                return true;
            case GamePhase.BuildBeforeInvade:
            case GamePhase.Invade:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(startPhase), startPhase, "Unsupported start phase.");
        }
    }
}
