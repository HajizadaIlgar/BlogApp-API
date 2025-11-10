namespace BlogApp.Core.Entities;

// ========== OKEY DAŞI ==========

public class OkeyTile
{
    public string Id { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty; // Red, Black, Blue, Yellow
    public int Number { get; set; } // 1-13 (0 = Joker)
    public bool IsFakeJoker { get; set; }
}
public class OkeyPlayer
{
    public string ConnectionId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<OkeyTile> Hand { get; set; } = new();
    public int Score { get; set; }
    public bool IsReady { get; set; }

    public void SortHand()
    {
        Hand = Hand.OrderBy(t => t.Color).ThenBy(t => t.Number).ToList();
    }
}

public class OkeyRoom
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string CreatorName { get; set; } = string.Empty;
    public int CreatorUserId { get; set; }
    public decimal EntryFee { get; set; }
    public List<OkeyPlayer> Players { get; set; } = new();
    public List<OkeyTile> Stock { get; set; } = new();
    public List<OkeyTile> DiscardPile { get; set; } = new();
    public OkeyTile? Indicator { get; set; }
    public int CurrentPlayerIndex { get; set; }
    public bool IsGameStarted { get; set; }
    public bool IsGameFinished { get; set; }
    public OkeyPlayer? Winner { get; set; }
    public object StateLock { get; } = new();

    public bool IsFull => Players.Count >= 4;
    public bool CanStartGame => Players.Count == 4 && !IsGameStarted;
    public string CurrentPlayerId => Players.Count > 0 ? Players[CurrentPlayerIndex].ConnectionId : "";

    public OkeyPlayer? GetPlayer(string connectionId) =>
        Players.FirstOrDefault(p => p.ConnectionId == connectionId);

    public OkeyPlayer? GetCurrentPlayer() =>
        Players.Count > 0 ? Players[CurrentPlayerIndex] : null;

    public void NextTurn()
    {
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
    }
}
public class OkeyRoomListItem
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string CreatorName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public decimal EntryFee { get; set; }
    public bool IsGameStarted { get; set; }
}
public class OkeyGameState
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public List<OkeyPlayer> Players { get; set; } = new();
    public List<OkeyTile> MyHand { get; set; } = new();
    public OkeyTile? Indicator { get; set; }
    public OkeyTile? LastDiscarded { get; set; }
    public int StockCount { get; set; }
    public bool IsMyTurn { get; set; }
    public string? CurrentPlayerName { get; set; }
    public bool CanDrawFromDiscard { get; set; }
}
public class OkeyMoveResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
