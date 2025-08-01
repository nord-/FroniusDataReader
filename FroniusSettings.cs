namespace FroniusDataReader;

public class FroniusSettings
{
    public required string InverterIp { get; init; }
    public int MaxDays { get; init; } = 15;
}
