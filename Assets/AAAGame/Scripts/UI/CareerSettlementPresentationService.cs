using System;

public static class CareerSettlementPresentationService
{
    private static CareerWinRecordResult? s_Latest;

    public static void SetLatest(CareerWinRecordResult? result)
    {
        s_Latest = result;
    }

    public static CareerWinRecordResult? ConsumeLatest()
    {
        CareerWinRecordResult? result = s_Latest;
        s_Latest = null;
        return result;
    }
}
