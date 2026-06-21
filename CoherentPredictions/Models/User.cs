namespace CoherentPredictions.Models;

public class User
{
    public int UserId { get; set; }

    public long TelegramUserId { get; set; }

    public string Name { get; set; } = null!;
}