namespace CoherentPredictions.Models;

public class AdminSession
{
    public AdminState State { get; set; }

    public string? Team1 { get; set; }

    public string? Team2 { get; set; }

    public DateTime? DateTime { get; set; }
}