namespace DieCutCatalog.Infrastructure.JustCut;

public sealed class JustCutOptions
{
    public string BaseUrl { get; set; } = "http://api1c.justcut.ru:8081/jctest/hs/jcexch/";
    public string UidContragent { get; set; } = string.Empty;
    public string Environment { get; set; } = "Test";
    public int TimeoutSeconds { get; set; } = 60;
}
