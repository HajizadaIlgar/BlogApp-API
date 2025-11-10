// OkeyHub.cs
using BlogApp.Core.Entities;
using BlogApp.DAL.DALs;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;
using Task = System.Threading.Tasks.Task;

namespace BlogApp.Api.Hubs
{
    public class OkeyHub : Hub
    {
        private readonly BlogAppDbContext _db;
        private readonly OkeyRoomManager _roomManager;
        private static readonly ConcurrentDictionary<string, string> _userRooms = new();

        public OkeyHub(BlogAppDbContext db, OkeyRoomManager roomManager)
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
                var user = _db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Id, u.UserName, u.Name, u.Surname, u.Balance }).FirstOrDefault();
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

                Console.WriteLine($"✅ Okey connected: {fullName}");
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

                            // Oyun başlamışsa və oyunçu çıxıbsa
                            if (room.IsGameStarted && !room.IsGameFinished)
                            {
                                HandlePlayerDisconnectDuringGame(room, player.Name);
                            }
                        }
                    }

                    // Otaq boşdursa sil
                    if (room.Players.Count == 0)
                    {
                        _roomManager.DeleteRoom(roomId);
                        await Clients.All.SendAsync("RoomDeleted", roomId);
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        private void HandlePlayerDisconnectDuringGame(OkeyRoom room, string playerName)
        {
            // Oyunu dayandır və bildiriş göndər
            Clients.Group(room.RoomId).SendAsync("GameCancelled", new
            {
                message = $"{playerName} oyundan çıxdı. Oyun ləğv edildi.",
                refund = true
            });

            // Balansı geri qaytar
            foreach (var player in room.Players)
            {
                var user = _db.Users.FirstOrDefault(u => u.Id == player.UserId);
                if (user != null)
                {
                    user.Balance += room.EntryFee;
                }
            }
            _db.SaveChanges();

            // Otağı sıfırla
            ResetRoomSync(room);
        }

        // ========== ROOM ƏMƏLİYYATLARI ==========

        public async Task<object> CreateRoom(string roomName, decimal entryFee = 50)
        {
            var userId = GetUserId();
            if (userId == 0) return new { success = false, message = "İstifadəçi tapılmadı" };

            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return new { success = false, message = "İstifadəçi tapılmadı" };
            if (user.Balance < entryFee) return new { success = false, message = $"Kifayət qədər balans yoxdur (lazım: {entryFee})" };

            string fullName = $"{user.Name} {user.Surname}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = user.UserName;

            var room = _roomManager.CreateRoom(roomName, fullName, userId, entryFee);
            if (room == null) return new { success = false, message = "Otaq yaratmaq alınmadı" };

            await Clients.All.SendAsync("OkeyRoomCreated", new OkeyRoomListItem
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                CreatorName = room.CreatorName,
                PlayerCount = 0,
                EntryFee = room.EntryFee,
                IsGameStarted = false
            });

            return new { success = true, roomId = room.RoomId };
        }

        public async Task<List<OkeyRoomListItem>> GetRoomList()
        {
            return _roomManager.GetAvailableRooms();
        }

        public async Task JoinRoom(string roomId)
        {
            var userId = GetUserId();
            if (userId == 0)
            {
                await Clients.Caller.SendAsync("JoinError", "İstifadəçi tapılmadı");
                return;
            }

            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                await Clients.Caller.SendAsync("JoinError", "İstifadəçi tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("JoinError", "Otaq tapılmadı");
                return;
            }

            string fullName = $"{user.Name} {user.Surname}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = user.UserName;

            // Artıq otaqdadırsa
            OkeyPlayer? existingPlayer = null;
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

            var player = new OkeyPlayer
            {
                ConnectionId = Context.ConnectionId,
                UserId = user.Id,
                Name = fullName
            };

            if (!_roomManager.AddPlayerToRoom(roomId, player))
            {
                await Clients.Caller.SendAsync("JoinError", "Otağa qoşulmaq alınmadı");
                return;
            }

            // Balansdan çıx
            user.Balance -= room.EntryFee;
            await _db.SaveChangesAsync();

            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            _userRooms[Context.ConnectionId] = roomId;

            await Clients.Caller.SendAsync("JoinedRoom", new
            {
                roomId,
                roomName = room.RoomName,
                balance = user.Balance
            });

            await Clients.Group(roomId).SendAsync("PlayerJoined", fullName);
            await BroadcastRoomPlayers(roomId);

            // 4 oyunçu toplanıbsa oyunu avtomatik başlat
            if (room.Players.Count == 4 && !room.IsGameStarted)
            {
                await Task.Delay(2000);
                await StartGame(roomId);
            }
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

        // ========== OYUN BAŞLATMA ==========

        private async Task StartGame(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            lock (room.StateLock)
            {
                if (room.IsGameStarted) return;
                if (room.Players.Count != 4)
                {
                    Console.WriteLine($"⚠️ Cannot start - only {room.Players.Count} players");
                    return;
                }

                room.IsGameStarted = true;
                room.IsGameFinished = false;

                // Daşları paylamaq
                var (stock, hands, startPlayerIndex) = OkeyGameGenerator.DealTiles();

                room.Stock = stock;
                room.CurrentPlayerIndex = startPlayerIndex;

                // Göstərici təyin et
                room.Indicator = OkeyGameGenerator.SelectIndicator(stock);

                // Hər oyunçuya daşları ver
                for (int i = 0; i < room.Players.Count; i++)
                {
                    room.Players[i].Hand = hands[i];
                    room.Players[i].SortHand();
                }

                Console.WriteLine($"🎮 Okey game started in {room.RoomName}");
                //Console.WriteLine($"📍 Indicator: [{room.Indicator.Color[0]}{room.Indicator.Number}]");
                Console.WriteLine($"👤 Starting player: {room.Players[startPlayerIndex].Name}");
            }

            await Clients.Group(roomId).SendAsync("GameStarted", new
            {
                startPlayerName = room.GetCurrentPlayer()?.Name,
                message = $"🎮 Oyun başladı! {room.GetCurrentPlayer()?.Name} başlayır"
            });

            // Hər oyunçuya ayrıca state göndər
            foreach (var player in room.Players)
            {
                await SendGameState(roomId, player.ConnectionId);
            }
        }

        // ========== DAŞ ÇƏKMƏ ==========

        public async Task DrawFromStock()
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId))
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            OkeyPlayer? player;
            OkeyTile? drawnTile = null;

            lock (room.StateLock)
            {
                if (!room.IsGameStarted)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyun hələ başlamayıb");
                    return;
                }

                player = room.GetPlayer(Context.ConnectionId);
                if (player == null)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyunçu tapılmadı");
                    return;
                }

                if (room.CurrentPlayerId != Context.ConnectionId)
                {
                    Clients.Caller.SendAsync("MoveError", "Sizin növbəniz deyil");
                    return;
                }

                if (room.Stock.Count == 0)
                {
                    Clients.Caller.SendAsync("MoveError", "Yığın boşdur");
                    return;
                }

                // Daş çək
                drawnTile = room.Stock[0];
                room.Stock.RemoveAt(0);
                player.Hand.Add(drawnTile);
                player.SortHand();
            }

            await Clients.Group(roomId).SendAsync("TileDrawn", new
            {
                playerName = player.Name,
                stockCount = room.Stock.Count
            });

            // Yalnız çəkən oyunçuya state göndər
            await SendGameState(roomId, Context.ConnectionId);
        }

        public async Task DrawFromDiscard()
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId))
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            OkeyPlayer? player;
            OkeyTile? drawnTile = null;

            lock (room.StateLock)
            {
                if (!room.IsGameStarted)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyun hələ başlamayıb");
                    return;
                }

                player = room.GetPlayer(Context.ConnectionId);
                if (player == null)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyunçu tapılmadı");
                    return;
                }

                if (room.CurrentPlayerId != Context.ConnectionId)
                {
                    Clients.Caller.SendAsync("MoveError", "Sizin növbəniz deyil");
                    return;
                }

                if (room.DiscardPile.Count == 0)
                {
                    Clients.Caller.SendAsync("MoveError", "Atılmış daş yoxdur");
                    return;
                }

                // Son atılmış daşı götür
                drawnTile = room.DiscardPile[^1];
                room.DiscardPile.RemoveAt(room.DiscardPile.Count - 1);
                player.Hand.Add(drawnTile);
                player.SortHand();
            }

            await Clients.Group(roomId).SendAsync("TileDrawnFromDiscard", new
            {
                playerName = player.Name,
                tile = new { drawnTile.Color, drawnTile.Number, drawnTile.IsFakeJoker }
            });

            await SendGameState(roomId, Context.ConnectionId);
        }

        // ========== DAŞ ATMAQ ==========

        public async Task DiscardTile(string tileId)
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId))
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            OkeyPlayer? player;
            OkeyTile? tile;

            lock (room.StateLock)
            {
                if (!room.IsGameStarted)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyun hələ başlamayıb");
                    return;
                }

                player = room.GetPlayer(Context.ConnectionId);
                if (player == null)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyunçu tapılmadı");
                    return;
                }

                if (room.CurrentPlayerId != Context.ConnectionId)
                {
                    Clients.Caller.SendAsync("MoveError", "Sizin növbəniz deyil");
                    return;
                }

                tile = player.Hand.FirstOrDefault(t => t.Id == tileId);
                if (tile == null)
                {
                    Clients.Caller.SendAsync("MoveError", "Bu daş əlinizdə yoxdur");
                    return;
                }

                // Daşı at
                player.Hand.Remove(tile);
                room.DiscardPile.Add(tile);

                // Növbəti oyunçuya keç
                room.NextTurn();
                var nextPlayer = room.GetCurrentPlayer();
            }

            await Clients.Group(roomId).SendAsync("TileDiscarded", new
            {
                playerName = player.Name,
                tile = new { tile.Color, tile.Number, tile.IsFakeJoker, tile.Id }
            });

            // Hamıya yeni state göndər
            foreach (var p in room.Players)
            {
                await SendGameState(roomId, p.ConnectionId);
            }
        }

        // ========== OKEY BİLDİRMƏK ==========

        public async Task DeclareWin()
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId))
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("MoveError", "Otaq tapılmadı");
                return;
            }

            OkeyPlayer? player;
            bool isValidWin = false;

            lock (room.StateLock)
            {
                player = room.GetPlayer(Context.ConnectionId);
                if (player == null)
                {
                    Clients.Caller.SendAsync("MoveError", "Oyunçu tapılmadı");
                    return;
                }

                if (room.CurrentPlayerId != Context.ConnectionId)
                {
                    Clients.Caller.SendAsync("MoveError", "Sizin növbəniz deyil");
                    return;
                }

                // Qalib yoxlaması
                isValidWin = OkeyCombinationValidator.CheckWin(player.Hand, room.Indicator);

                if (isValidWin)
                {
                    room.IsGameFinished = true;
                    room.Winner = player;
                }
            }

            if (!isValidWin)
            {
                await Clients.Caller.SendAsync("WinError", "Əlinizdə düzgün kombinasiya yoxdur!");
                return;
            }

            // Qalib elan et
            await HandleGameEnd(roomId, player);
        }

        // ========== OYUN SONU ==========

        private async Task HandleGameEnd(string roomId, OkeyPlayer winner)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            decimal reward = room.EntryFee * 4; // 4 oyunçunun giriş haqqı

            var user = _db.Users.FirstOrDefault(u => u.Id == winner.UserId);
            if (user != null)
            {
                user.Balance += reward;
                await _db.SaveChangesAsync();
            }

            await Clients.Group(roomId).SendAsync("GameFinished", new
            {
                winnerName = winner.Name,
                reward,
                winnerBalance = user?.Balance ?? 0,
                message = $"🏆 {winner.Name} qalib oldu! (+{reward} coin)"
            });

            Console.WriteLine($"🏆 Game finished: {winner.Name} won {reward} coins");

            await Task.Delay(8000);
            await ResetRoom(roomId);
        }

        private async Task ResetRoom(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            lock (room.StateLock)
            {
                ResetRoomSync(room);
            }

            await Clients.Group(roomId).SendAsync("GameReset", new
            {
                message = "Otaq sıfırlandı. Yeni oyun üçün 4 oyunçu gözlənilir."
            });
        }

        private void ResetRoomSync(OkeyRoom room)
        {
            room.IsGameStarted = false;
            room.IsGameFinished = false;
            room.Stock.Clear();
            room.DiscardPile.Clear();
            room.Indicator = null;
            room.Winner = null;
            room.CurrentPlayerIndex = 0;

            foreach (var player in room.Players)
            {
                player.Hand.Clear();
                player.Score = 0;
                player.IsReady = false;
            }
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

            var state = new OkeyGameState
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                Players = room.Players.Select(p => new OkeyPlayer
                {
                    Name = p.Name,
                    //TileCount = p.Hand.Count,
                    Score = p.Score,
                    //IsCurrentTurn = p.ConnectionId == room.CurrentPlayerId
                }).ToList(),
                MyHand = player.Hand,
                Indicator = room.Indicator,
                LastDiscarded = room.DiscardPile.Count > 0 ? room.DiscardPile[^1] : null,
                StockCount = room.Stock.Count,
                IsMyTurn = player.ConnectionId == room.CurrentPlayerId,
                CurrentPlayerName = room.GetCurrentPlayer()?.Name,
                CanDrawFromDiscard = room.DiscardPile.Count > 0
            };

            await Clients.Client(connectionId).SendAsync("GameState", state);
        }
    }

    public static class OkeyCombinationValidator
    {
        public static bool CheckWin(List<OkeyTile> hand, OkeyTile? indicator)
        {
            if (hand.Count != 14) return false;

            // Sadələşdirilmiş qələbə yoxlaması
            // Real Okey oyununda: 4 set (3-3-3-3) + 1 cüt (2)
            // Və ya 7 cüt (2-2-2-2-2-2-2)

            // Burada sadə yoxlama: rəqəmlərə görə qruplaşdırma
            var groups = hand.GroupBy(t => t.Number).OrderByDescending(g => g.Count());

            // 7 cüt kombinasiyası
            if (groups.Count() == 7 && groups.All(g => g.Count() == 2))
                return true;

            // 4 üçlük + 1 cüt kombinasiyası
            int triplets = groups.Count(g => g.Count() >= 3);
            int pairs = groups.Count(g => g.Count() >= 2);

            if (triplets >= 4 && pairs >= 1)
                return true;

            // Real oyunda daha mürəkkəb kombinasiya yoxlamaları ola bilər
            // Run (ardıcıllıq): 3-4-5, 6-7-8-9 və s.

            return false;
        }

        public static bool IsValidSet(List<OkeyTile> tiles)
        {
            if (tiles.Count < 3) return false;

            // Eyni rəqəm, fərqli rənglər
            var number = tiles.First().Number;
            return tiles.All(t => t.Number == number) &&
                   tiles.Select(t => t.Color).Distinct().Count() == tiles.Count;
        }

        public static bool IsValidRun(List<OkeyTile> tiles)
        {
            if (tiles.Count < 3) return false;

            // Eyni rəng, ardıcıl rəqəmlər
            var color = tiles.First().Color;
            if (!tiles.All(t => t.Color == color)) return false;

            var numbers = tiles.Select(t => t.Number).OrderBy(n => n).ToList();
            for (int i = 1; i < numbers.Count; i++)
            {
                if (numbers[i] != numbers[i - 1] + 1)
                    return false;
            }

            return true;
        }
    }

    public static class OkeyGameGenerator
    {
        public static (List<OkeyTile> stock, List<OkeyTile>[] hands, int startIndex) DealTiles()
        {
            var allTiles = new List<OkeyTile>();
            var colors = new[] { "Red", "Black", "Blue", "Yellow" };

            // Her rəng və rəqəm üçün 2 dəst (106 daş)
            for (int set = 0; set < 2; set++)
            {
                foreach (var color in colors)
                {
                    for (int num = 1; num <= 13; num++)
                    {
                        allTiles.Add(new OkeyTile
                        {
                            Id = Guid.NewGuid().ToString(),
                            Color = color,
                            Number = num,
                            IsFakeJoker = false
                        });
                    }
                }
            }

            // 2 Fake Joker (sahte joker)
            allTiles.Add(new OkeyTile { Id = Guid.NewGuid().ToString(), Color = "Red", Number = 0, IsFakeJoker = true });
            allTiles.Add(new OkeyTile { Id = Guid.NewGuid().ToString(), Color = "Black", Number = 0, IsFakeJoker = true });

            // Qarışdır
            var random = new Random();
            allTiles = allTiles.OrderBy(x => random.Next()).ToList();

            // Hər oyunçuya 14 daş ver
            var hands = new List<OkeyTile>[4];
            for (int i = 0; i < 4; i++)
            {
                hands[i] = allTiles.Skip(i * 14).Take(14).ToList();
            }

            // Qalan daşlar yığında
            var stock = allTiles.Skip(56).ToList();

            // Təsadüfi başlanğıc oyunçu
            int startIndex = random.Next(0, 4);

            return (stock, hands, startIndex);
        }

        public static OkeyTile SelectIndicator(List<OkeyTile> stock)
        {
            if (stock.Count == 0)
                return new OkeyTile { Color = "Red", Number = 1 };

            var random = new Random();
            int index = random.Next(0, Math.Min(10, stock.Count));
            return stock[index];
        }
    }
}

