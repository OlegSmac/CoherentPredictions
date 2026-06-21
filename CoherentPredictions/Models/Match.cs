namespace CoherentPredictions.Models;

public class Match
{
    public int MatchId { get; set; }
    public string Team1 { get; set; }
    public string Team2 { get; set; }
    public string? Score { get; set; }
    public DateTime DateTime { get; set; }
}