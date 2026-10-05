namespace StoreOS.Domain.Common;

public static class Money
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
