using BlogApp.Core.Entities;
using BlogApp.DAL.DALs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace BlogApp.Api.Hubs
{
    public class DominoHub : Hub
    {

        private readonly BlogAppDbContext _db;
        private readonly DominoRoomManager _roomManager;
        private static readonly ConcurrentDictionary<string, string> _userRooms = new();

        public DominoHub(BlogAppDbContext db, DominoRoomManager roomManager)
        {
            _db = db;
            _roomManager = roomManager;
        }

        // ========== CONNECTION EVENTS ==========

        public override async Task OnConnectedAsync()
        {
            if (Context.User?.Identity?.IsAuthenticated != true)
            {
                Context.Abort();
                return;
            }

            var userId = GetUserId();
            if (userId == 0)
            {
                Context.Abort();
                return;
            }

            try
            {
                var user = await _db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Id, u.UserName, u.Name, u.Surname, u.Balance })
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    Context.Abort();
                    return;
                }

                string fullName = $"{user.Name} {user.Surname}".Trim();
                if (string.IsNullOrEmpty(fullName)) fullName = user.UserName;

                await Clients.Caller.SendAsync("UserData", new
                {
                    userId = user.Id,
                    username = user.UserName,
                    fullName,
                    balance = user.Balance
                });

                Console.WriteLine($"✅ Domino connected: {fullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ OnConnectedAsync error: {ex.Message}");
                Context.Abort();
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            string connId = Context.ConnectionId;

            if (_userRooms.TryRemove(connId, out var roomId))
            {
                var room = _roomManager.GetRoom(roomId);
                if (room != null)
                {
                    lock (room.StateLock)
                    {
                        var player = room.Players.FirstOrDefault(p => p.ConnectionId == connId);
                        if (player != null)
                        {
                            room.Players.Remove(player);
                            Console.WriteLine($"❌ Player disconnected: {player.Name}");

                            Clients.Group(roomId).SendAsync("PlayerLeft", player.Name);
                            BroadcastRoomPlayers(roomId);
                        }
                    }

                    if (room.Players.Count == 0)
                    {
                        _roomManager.DeleteRoom(roomId);
                        await Clients.All.SendAsync("RoomDeleted", roomId);
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ========== ROOM ƏMƏLİYYATLARI ==========

        public async Task<object> CreateRoom(
            string roomName,
            string gameTypeStr = "Classic101",
            decimal entryFee = 10,
            int maxPlayers = 4,
            bool isPrivate = false,
            string? password = null)
        {
            var userId = GetUserId();
            if (userId == 0) return new { success = false, message = "İstifadəçi tapılmadı" };

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return new { success = false, message = "İstifadəçi tapılmadı" };
            if (user.Balance < entryFee) return new { success = false, message = $"Kifayət qədər balans yoxdur (lazım: {entryFee})" };

            string fullName = $"{user.Name} {user.Surname}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = user.UserName;

            if (!Enum.TryParse<DominoGameType>(gameTypeStr, out var gameType))
            {
                gameType = DominoGameType.Classic101;
            }

            var room = _roomManager.CreateRoom(roomName, fullName, userId, gameType, entryFee, maxPlayers, isPrivate, password);
            if (room == null) return new { success = false, message = "Room yaratmaq alınmadı" };

            await Clients.All.SendAsync("DominoRoomCreated", new DominoRoomListItem
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                CreatorName = room.CreatorName,
                PlayerCount = 0,
                MaxPlayers = room.MaxPlayers,
                EntryFee = room.EntryFee,
                IsPrivate = room.IsPrivate,
                GameTypeName = gameTypeStr
            });

            return new { success = true, roomId = room.RoomId };
        }

        public async Task<List<DominoRoomListItem>> GetRoomList()
        {
            return _roomManager.GetAvailableRooms();
        }

        public async Task JoinRoom(string roomId, string? password = null)
        {
            var userId = GetUserId();
            if (userId == 0)
            {
                await Clients.Caller.SendAsync("JoinError", "İstifadəçi tapılmadı");
                return;
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                await Clients.Caller.SendAsync("JoinError", "İstifadəçi tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("JoinError", "Room tapılmadı");
                return;
            }

            string fullName = $"{user.Name} {user.Surname}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = user.UserName;

            DominoPlayer? existingPlayer = null;
            lock (room.StateLock)
            {
                existingPlayer = room.Players.FirstOrDefault(p => p.UserId == userId);
            }

            if (existingPlayer != null)
            {
                existingPlayer.ConnectionId = Context.ConnectionId;
                await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
                _userRooms[Context.ConnectionId] = roomId;
                await SendGameState(roomId, Context.ConnectionId);
                return;
            }

            if (user.Balance < room.EntryFee)
            {
                await Clients.Caller.SendAsync("JoinError", $"Kifayət qədər balans yoxdur (lazım: {room.EntryFee})");
                return;
            }

            var player = new DominoPlayer
            {
                ConnectionId = Context.ConnectionId,
                UserId = user.Id,
                Name = fullName
            };

            if (!_roomManager.AddPlayerToRoom(roomId, player, password))
            {
                await Clients.Caller.SendAsync("JoinError", "Room-a qoşulmaq alınmadı");
                return;
            }

            user.Balance -= room.EntryFee;
            await _db.SaveChangesAsync();

            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            _userRooms[Context.ConnectionId] = roomId;

            await Clients.Caller.SendAsync("JoinedRoom", new
            {
                roomId,
                roomName = room.RoomName,
                gameType = room.GameType.ToString(),
                balance = user.Balance
            });

            await Clients.Group(roomId).SendAsync("PlayerJoined", fullName);
            await BroadcastRoomPlayers(roomId);
        }

        public async Task LeaveRoom()
        {
            var connId = Context.ConnectionId;
            if (!_userRooms.TryGetValue(connId, out var roomId)) return;

            var userId = GetUserId();
            if (userId == 0) return;

            _roomManager.RemovePlayerFromRoom(roomId, userId);
            await Groups.RemoveFromGroupAsync(connId, roomId);
            _userRooms.TryRemove(connId, out _);

            await Clients.Caller.SendAsync("LeftRoom");
            await BroadcastRoomPlayers(roomId);
        }

        // ========== OYUN BAŞLATMA (FIXED) ==========

        public async Task StartGame()
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId))
            {
                await Clients.Caller.SendAsync("GameError", "Room tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("GameError", "Room tapılmadı");
                return;
            }

            lock (room.StateLock)
            {
                if (room.IsGameStarted)
                {
                    Clients.Caller.SendAsync("GameError", "Oyun artıq başlayıb");
                    return;
                }

                if (room.Players.Count < 2)
                {
                    Clients.Caller.SendAsync("GameError", "Minimum 2 oyunçu lazımdır");
                    return;
                }

                room.IsGameStarted = true;
                room.IsRoundFinished = false;

                var (stock, hands) = DominoGameGenerator.DealTiles(room.Players.Count, room.TilesPerPlayer);

                room.Stock = stock;

                for (int i = 0; i < room.Players.Count; i++)
                {
                    room.Players[i].Hand = hands[i];
                    room.Players[i].Status = PlayerStatus.Waiting;
                    room.Players[i].HasPassed = false;
                }

                // ✅ EN KİÇİK DUPLET
                int startPlayerIndex = DominoGameGenerator.FindPlayerWithSmallestDouble(room.Players);
                room.CurrentPlayerIndex = startPlayerIndex;
                room.Players[startPlayerIndex].Status = PlayerStatus.Playing;

                Console.WriteLine($"🎮 Game started in {room.RoomName} - Round {room.CurrentRound}");
            }

            await Clients.Group(roomId).SendAsync("GameStarted", new
            {
                startPlayerName = room.GetCurrentPlayer()?.Name,
                message = $"{room.GetCurrentPlayer()?.Name} ən kiçik dupletlə başlayır",
                round = room.CurrentRound
            });

            foreach (var player in room.Players)
            {
                await SendGameState(roomId, player.ConnectionId);
            }
        }

        // ========== DAŞ QOYMA ==========

        public async Task PlaceTile(string tileId, string side)
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId))
            {
                await Clients.Caller.SendAsync("MoveError", "Room tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("MoveError", "Room tapılmadı");
                return;
            }

            DominoPlayer? player;
            DominoTile? tile;
            bool moveSuccess = false;
            string? errorMessage = null;

            lock (room.StateLock)
            {
                if (!room.IsGameStarted)
                {
                    errorMessage = "Oyun hələ başlamayıb";
                    goto SendError;
                }

                player = room.GetPlayer(Context.ConnectionId);
                if (player == null)
                {
                    errorMessage = "Oyunçu tapılmadı";
                    goto SendError;
                }

                if (room.CurrentPlayerId != Context.ConnectionId)
                {
                    errorMessage = "Sizin növbəniz deyil";
                    goto SendError;
                }

                tile = player.Hand.FirstOrDefault(t => t.Id == tileId);
                if (tile == null)
                {
                    errorMessage = "Bu daş əlinizdə yoxdur";
                    goto SendError;
                }

                if (room.Chain.Tiles.Count == 0)
                {
                    room.Chain.AddRight(tile, false);
                    player.RemoveTile(tileId);
                    moveSuccess = true;
                }
                else
                {
                    var (canLeft, canRight) = room.Chain.CanPlace(tile);

                    if (!canLeft && !canRight)
                    {
                        errorMessage = "Bu daş qoyula bilməz";
                        goto SendError;
                    }

                    if (side == "left" && canLeft)
                    {
                        bool needFlip = tile.Left == room.Chain.LeftEnd;
                        room.Chain.AddLeft(tile, needFlip);
                        player.RemoveTile(tileId);
                        moveSuccess = true;
                    }
                    else if (side == "right" && canRight)
                    {
                        bool needFlip = tile.Right == room.Chain.RightEnd;
                        room.Chain.AddRight(tile, needFlip);
                        player.RemoveTile(tileId);
                        moveSuccess = true;
                    }
                    else
                    {
                        errorMessage = "Bu daş bu tərəfə qoyula bilməz";
                        goto SendError;
                    }
                }

                // ✅ RAUND SONU YOXLAMASI
                if (player.Hand.Count == 0)
                {
                    room.IsRoundFinished = true;
                    room.RoundWinner = player;
                }
                else
                {
                    player.Status = PlayerStatus.Waiting;
                    player.HasPassed = false;
                    room.NextTurn();
                    var nextPlayer = room.GetCurrentPlayer();
                    if (nextPlayer != null)
                    {
                        nextPlayer.Status = PlayerStatus.Playing;
                    }
                }
            }

            if (moveSuccess)
            {
                await Clients.Group(roomId).SendAsync("TilePlaced", new
                {
                    playerName = player.Name,
                    tile = new { tile.Left, tile.Right, tile.Id },
                    side,
                    leftEnd = room.Chain.LeftEnd,
                    rightEnd = room.Chain.RightEnd
                });

                foreach (var p in room.Players)
                {
                    await SendGameState(roomId, p.ConnectionId);
                }

                if (room.IsRoundFinished && room.RoundWinner != null)
                {
                    await HandleRoundEnd(roomId, room.RoundWinner);
                }
                return;
            }

        SendError:
            if (!string.IsNullOrEmpty(errorMessage))
            {
                await Clients.Caller.SendAsync("MoveError", errorMessage);
            }
        }

        // ========== BAZARDAN DAŞ GÖTÜR ==========

        public async Task TakeFromStock()
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId)) return;

            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            DominoPlayer? player;
            bool shouldPass = false;

            lock (room.StateLock)
            {
                player = room.GetPlayer(Context.ConnectionId);
                if (player == null) return;

                if (room.CurrentPlayerId != Context.ConnectionId)
                {
                    Clients.Caller.SendAsync("MoveError", "Sizin növbəniz deyil");
                    return;
                }

                if (room.Chain.Tiles.Count > 0)
                {
                    bool hasPlayableTile = player.Hand.Any(t =>
                    {
                        var (canLeft, canRight) = room.Chain.CanPlace(t);
                        return canLeft || canRight;
                    });

                    if (hasPlayableTile)
                    {
                        Clients.Caller.SendAsync("MoveError", "Oynaya biləcəyiniz daş var!");
                        return;
                    }
                }

                if (room.Stock.Count == 0)
                {
                    player.HasPassed = true;
                    player.Status = PlayerStatus.Passed;
                    shouldPass = true;

                    if (room.Players.All(p => p.HasPassed))
                    {
                        room.IsRoundFinished = true;
                        room.RoundWinner = room.Players.OrderBy(p => p.GetHandValue()).First();
                    }
                    else
                    {
                        room.NextTurn();
                        var nextPlayer = room.GetCurrentPlayer();
                        if (nextPlayer != null)
                        {
                            nextPlayer.Status = PlayerStatus.Playing;
                        }
                    }
                }
                else
                {
                    var takenTile = room.Stock.First();
                    room.Stock.RemoveAt(0);
                    player.Hand.Add(takenTile);
                }
            }

            await Clients.Group(roomId).SendAsync("TileDrawn", new
            {
                playerName = player.Name,
                stockCount = room.Stock.Count,
                passed = shouldPass
            });

            foreach (var p in room.Players)
            {
                await SendGameState(roomId, p.ConnectionId);
            }

            if (room.IsRoundFinished && room.RoundWinner != null)
            {
                await HandleRoundEnd(roomId, room.RoundWinner);
            }
        }

        // ========== RAUND SONU (FIXED - 101 QAYDALARI) ==========

        private async Task HandleRoundEnd(string roomId, DominoPlayer roundWinner)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            int totalPoints = 0;
            var playerScores = new List<object>();

            lock (room.StateLock)
            {
                // ✅ BÜTÜN RƏQIBLƏRIN EL XALLARI
                foreach (var player in room.Players)
                {
                    if (player.UserId != roundWinner.UserId)
                    {
                        int handValue = player.GetHandValue();
                        totalPoints += handValue;
                        playerScores.Add(new
                        {
                            name = player.Name,
                            handValue,
                            tiles = player.Hand.Select(t => $"[{t.Left}|{t.Right}]").ToList()
                        });
                    }
                }

                // ✅ QALIB XALLARI ALIYR
                roundWinner.Score += totalPoints;

                Console.WriteLine($"🏆 Round {room.CurrentRound} winner: {roundWinner.Name} (+{totalPoints} xal)");
            }

            await Clients.Group(roomId).SendAsync("RoundFinished", new
            {
                winnerName = roundWinner.Name,
                pointsEarned = totalPoints,
                currentScore = roundWinner.Score,
                playerScores,
                round = room.CurrentRound
            });

            // ✅ OYUN BİTDİ? (101 və ya daha çox)
            if (roundWinner.Score >= 101)
            {
                await HandleGameEnd(roomId, roundWinner);
            }
            else
            {
                await Task.Delay(5000);
                await StartNewRound(roomId);
            }
        }

        // ========== YENİ RAUND ==========

        private async Task StartNewRound(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            lock (room.StateLock)
            {
                room.CurrentRound++;
                room.IsRoundFinished = false;
                room.Chain.Tiles.Clear();
                room.Stock.Clear();
                room.RoundWinner = null;

                var (stock, hands) = DominoGameGenerator.DealTiles(room.Players.Count, room.TilesPerPlayer);
                room.Stock = stock;

                for (int i = 0; i < room.Players.Count; i++)
                {
                    room.Players[i].Hand = hands[i];
                    room.Players[i].Status = PlayerStatus.Waiting;
                    room.Players[i].HasPassed = false;
                }

                int startPlayerIndex = DominoGameGenerator.FindPlayerWithSmallestDouble(room.Players);
                room.CurrentPlayerIndex = startPlayerIndex;
                room.Players[startPlayerIndex].Status = PlayerStatus.Playing;
            }

            await Clients.Group(roomId).SendAsync("NewRoundStarted", new
            {
                round = room.CurrentRound,
                startPlayerName = room.GetCurrentPlayer()?.Name,
                scores = room.Players.Select(p => new { p.Name, p.Score }).ToList()
            });

            foreach (var player in room.Players)
            {
                await SendGameState(roomId, player.ConnectionId);
            }
        }

        // ========== OYUN SONU (101+ XALLA) ==========

        private async Task HandleGameEnd(string roomId, DominoPlayer winner)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            decimal reward = room.EntryFee * room.Players.Count;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == winner.UserId);
            if (user != null)
            {
                user.Balance += reward;
                await _db.SaveChangesAsync();
            }

            await Clients.Group(roomId).SendAsync("GameFinished", new
            {
                winnerName = winner.Name,
                finalScore = winner.Score,
                reward,
                allScores = room.Players.Select(p => new { p.Name, p.Score }).ToList(),
                message = $"🏆 {winner.Name} qalib oldu! {winner.Score} xalla (+{reward} coin)"
            });

            await Task.Delay(8000);
            await ResetRoom(roomId);
        }

        private async Task ResetRoom(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            lock (room.StateLock)
            {
                room.IsGameStarted = false;
                room.IsRoundFinished = false;
                room.Chain.Tiles.Clear();
                room.Stock.Clear();
                room.RoundWinner = null;
                room.Winner = null;
                room.CurrentPlayerIndex = 0;
                room.CurrentRound = 1;

                foreach (var player in room.Players)
                {
                    player.Hand.Clear();
                    player.Status = PlayerStatus.Waiting;
                    player.HasPassed = false;
                    player.Score = 0; // ✅ Reset scores
                }
            }

            await Clients.Group(roomId).SendAsync("GameReset");
        }

        // ========== HELPER METHODS ==========

        private int GetUserId()
        {
            var userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out int userId) ? userId : 0;
        }

        private string? GetCurrentRoom()
        {
            _userRooms.TryGetValue(Context.ConnectionId, out var roomId);
            return roomId;
        }

        private async Task BroadcastRoomPlayers(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            List<object> playerInfos;
            lock (room.StateLock)
            {
                playerInfos = room.Players.Select(p => new
                {
                    name = p.Name,
                    tileCount = p.Hand.Count,
                    score = p.Score,
                    isCurrentTurn = p.ConnectionId == room.CurrentPlayerId
                }).Cast<object>().ToList();
            }

            await Clients.Group(roomId).SendAsync("PlayersList", playerInfos);
        }

        private async Task SendGameState(string roomId, string connectionId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            var player = room.GetPlayer(connectionId);
            if (player == null) return;

            var state = new DominoGameState
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                CurrentRound = room.CurrentRound,
                Players = room.Players.Select(p => new DominoPlayerInfo
                {
                    Name = p.Name,
                    TileCount = p.Hand.Count,
                    Score = p.Score,
                    IsCurrentTurn = p.ConnectionId == room.CurrentPlayerId,
                    Status = p.Status
                }).ToList(),
                MyHand = player.Hand,
                ChainTiles = room.Chain.Tiles,
                LeftEnd = room.Chain.LeftEnd,
                RightEnd = room.Chain.RightEnd,
                StockCount = room.Stock.Count,
                IsMyTurn = player.ConnectionId == room.CurrentPlayerId,
                CurrentPlayerName = room.GetCurrentPlayer()?.Name
            };

            await Clients.Client(connectionId).SendAsync("GameState", state);
        }
    }
}
