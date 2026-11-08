namespace ServiceLib.Models.Configs;

[Serializable]
public sealed class ProfileFailoverSettings
{
    public bool Enabled { get; set; }
    public int ConsecutiveFailureThreshold { get; set; } = 3;
    public int FailureHysteresisSeconds { get; set; } = 30;
    public int CooldownSeconds { get; set; } = 60;
}
