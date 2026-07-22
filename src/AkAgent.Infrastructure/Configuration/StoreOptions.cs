namespace AkAgent.Infrastructure.Configuration;

public sealed class StoreOptions
{
    public string DataPath { get; set; } = "./data";
    public double MinScore { get; set; } = 0.15;
    public int MaxResults { get; set; } = 5;
}
