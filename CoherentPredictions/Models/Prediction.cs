namespace CoherentPredictions.Models;

public class Prediction
{
    public int UserId { get; set; }

    public int MatchId { get; set; }

    public string Score { get; set; } = null!;
}