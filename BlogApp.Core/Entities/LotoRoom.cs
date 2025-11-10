namespace BlogApp.Core.Entities
{
    public class LotoRoom
    {
        public string RoomId { get; set; } = Guid.NewGuid().ToString();
        public string RoomName { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public int CreatorUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Oyun parametrləri
        public decimal EntryFee { get; set; } = 10;
        public decimal LineReward { get; set; } = 10;
        public decimal WinReward { get; set; } = 50;
        public int MaxPlayers { get; set; } = 10;
        public int AutoDrawIntervalMs { get; set; } = 3000;

        // Oyun vəziyyəti
        public bool IsGameStarted { get; set; } = false;
        public bool IsGameFinished { get; set; } = false;
        public bool IsPrivate { get; set; } = false;
        public string? Password { get; set; }

        // Oyunçular
        public List<RoomPlayer> Players { get; set; } = new();

        // Nömrələr
        public Queue<int>? NumbersQueue { get; set; }
        public List<int> DrawnNumbers { get; set; } = new();

        // Auto-draw üçün
        public CancellationTokenSource? AutoDrawCts { get; set; }
        public System.Threading.Timer? AutoDrawTimer { get; set; }

        // Lock object
        public readonly object StateLock = new();
    }

    public class RoomPlayer
    {
        public string ConnectionId { get; set; } = "";
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public decimal Balance { get; set; }
        public int?[][] Card { get; set; } = null!;
        public HashSet<int> CompletedRows { get; set; } = new();
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public class RoomListItem
    {
        public string RoomId { get; set; } = "";
        public string RoomName { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public decimal EntryFee { get; set; }
        public bool IsGameStarted { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsFull => PlayerCount >= MaxPlayers;
    }
}
