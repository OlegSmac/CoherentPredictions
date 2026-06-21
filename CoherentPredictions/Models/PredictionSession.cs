namespace CoherentPredictions.Models;

public class PredictionSession
{
    public List<Match> Matches { get; set; } = [];
    public int CurrentIndex { get; set; }
}