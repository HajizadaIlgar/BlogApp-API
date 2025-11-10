using BlogApp.Api.Hubs.Services;
using BlogApp.Core.Entities;
using BlogApp.DAL.DALs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace BlogApp.Api.Hubs
{
    public class LotoHub : Hub
    {
        private readonly BlogAppDbContext _db;
        private readonly LotoRoomManager _roomManager;

        public LotoHub(BlogAppDbContext db, LotoRoomManager roomManager)
        {
            _db = db;
            _roomManager = roomManager;
        }

        // User connection ID → Room ID mapping
        private static readonly ConcurrentDictionary<string, string> _userRooms = new();

        public override async Task OnConnectedAsync()
        {
            if (Context.User?.Identity?.IsAuthenticated != true)
            {
                Console.WriteLine($"❌ Unauthorized connection attempt");
                Context.Abort();
                return;
            }

            string userIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out int userId))
            {
                Console.WriteLine($"❌ Invalid user ID");
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
                    Console.WriteLine($"⚠️ User not found: {userId}");
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

                Console.WriteLine($"✅ Connected: {fullName} (Balance: {user.Balance})");
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
                            Console.WriteLine($"❌ Player left: {player.Name} from {room.RoomName}");

                            Clients.Group(roomId).SendAsync("PlayerLeft", player.Name);
                            BroadcastRoomPlayers(roomId);
                        }
                    }

                    // Boş room-u sil
                    if (room.Players.Count == 0)
                    {
                        _roomManager.DeleteRoom(roomId);
                        await Clients.All.SendAsync("RoomDeleted", roomId);
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ========== ROOM ƏMƏLIYYATLARI ==========

        public async Task<object> CreateRoom(string roomName, decimal entryFee = 10,
            int maxPlayers = 10, bool isPrivate = false, string? password = null)
        {
            var userId = GetUserId();
            if (userId == 0)
            {
                await Clients.Caller.SendAsync("Error", "İstifadəçi ID-si tapılmadı");
                return new { success = false, message = "User not found" };
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return new { success = false, message = "User not found" };
            }

            string fullName = $"{user.Name} {user.Surname}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = user.UserName;

            var room = _roomManager.CreateRoom(roomName, fullName, userId,
                entryFee, maxPlayers, isPrivate, password);

            if (room == null)
            {
                return new { success = false, message = "Room yaratmaq alınmadı" };
            }

            await Clients.All.SendAsync("RoomCreated", new RoomListItem
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                CreatorName = room.CreatorName,
                PlayerCount = 0,
                MaxPlayers = room.MaxPlayers,
                EntryFee = room.EntryFee,
                IsPrivate = room.IsPrivate
            });

            return new { success = true, roomId = room.RoomId };
        }

        public async Task<List<RoomListItem>> GetRoomList()
        {
            var rooms = _roomManager.GetAvailableRooms();
            return rooms;
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

            RoomPlayer? existingPlayer = null;
            lock (room.StateLock)
            {
                existingPlayer = room.Players.FirstOrDefault(p => p.UserId == userId);
            }

            // ✅ Əgər artıq room-dadırsa, yenidən giriş haqqı çəkmə
            if (existingPlayer != null)
            {
                Console.WriteLine($"🔄 Player rejoining: {fullName} → {room.RoomName}");

                // Connection ID-ni yenilə
                existingPlayer.ConnectionId = Context.ConnectionId;

                await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
                _userRooms[Context.ConnectionId] = roomId;

                await Clients.Caller.SendAsync("JoinedRoom", new
                {
                    roomId,
                    roomName = room.RoomName,
                    card = existingPlayer.Card,
                    balance = user.Balance
                });

                await BroadcastRoomPlayers(roomId);
                return;
            }

            // ✅ Yeni oyunçu - giriş haqqı çək
            if (user.Balance < room.EntryFee)
            {
                await Clients.Caller.SendAsync("JoinError",
                    $"Kifayət qədər balans yoxdur (lazım: {room.EntryFee})");
                return;
            }

            var player = new RoomPlayer
            {
                ConnectionId = Context.ConnectionId,
                UserId = user.Id,
                Name = fullName,
                Balance = user.Balance,
                Card = LotoCardGenerator.GenerateCard()
            };

            if (!_roomManager.AddPlayerToRoom(roomId, player, password))
            {
                await Clients.Caller.SendAsync("JoinError", "Room-a qoşulmaq alınmadı");
                return;
            }

            // Balansdan giriş haqqını çıx
            user.Balance -= room.EntryFee;
            await _db.SaveChangesAsync();

            // SignalR group-a əlavə et
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            _userRooms[Context.ConnectionId] = roomId;

            await Clients.Caller.SendAsync("JoinedRoom", new
            {
                roomId,
                roomName = room.RoomName,
                card = player.Card,
                balance = user.Balance
            });

            await Clients.Group(roomId).SendAsync("PlayerJoined", fullName);
            await BroadcastRoomPlayers(roomId);

            Console.WriteLine($"✅ {fullName} joined {room.RoomName}");
        }

        public async Task LeaveRoom()
        {
            var connId = Context.ConnectionId;

            if (!_userRooms.TryGetValue(connId, out var roomId))
            {
                return;
            }

            var userId = GetUserId();
            if (userId == 0) return;

            _roomManager.RemovePlayerFromRoom(roomId, userId);
            await Groups.RemoveFromGroupAsync(connId, roomId);
            _userRooms.TryRemove(connId, out _);

            await Clients.Caller.SendAsync("LeftRoom");
            await BroadcastRoomPlayers(roomId);
        }

        public async Task StartGame(AutoLotoService _ser)
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId)) return;

            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            lock (room.StateLock)
            {
                if (room.IsGameStarted)
                {
                    Clients.Caller.SendAsync("GameError", "Oyun artıq başlayıb");
                    return;
                }

                if (room.Players.Count < 1)
                {
                    Clients.Caller.SendAsync("GameError", "Ən az 1 oyunçu lazımdır");
                    return;
                }

                room.IsGameStarted = true;
                room.IsGameFinished = false;
                room.DrawnNumbers.Clear();
                room.NumbersQueue = new Queue<int>(
                    Enumerable.Range(1, 90).OrderBy(x => Guid.NewGuid())
                );

                Console.WriteLine($"🎮 Game started in {room.RoomName}");
            }

            await Clients.Group(roomId).SendAsync("GameStarted");

            // Auto-draw başlat
            //_ = Task.Run(() => AutoDrawLoop(roomId, room.AutoDrawIntervalMs));
            _ser.Start();

        }

        //private async Task AutoDrawLoop(string roomId, int intervalMs)
        //{
        //    var room = _roomManager.GetRoom(roomId);
        //    if (room == null) return;

        //    room.AutoDrawCts = new CancellationTokenSource();
        //    var token = room.AutoDrawCts.Token;

        //    try
        //    {
        //        while (!token.IsCancellationRequested)
        //        {
        //            int? next = null;

        //            lock (room.StateLock)
        //            {
        //                if (!room.IsGameStarted || room.NumbersQueue == null ||
        //                    room.NumbersQueue.Count == 0)
        //                {
        //                    break;
        //                }

        //                next = room.NumbersQueue.Dequeue();
        //                room.DrawnNumbers.Add(next.Value);
        //            }

        //            if (next.HasValue)
        //            {
        //                Console.WriteLine($"🎱 [{room.RoomName}] Drawn: {next.Value}");
        //                await Clients.Group(roomId).SendAsync("NumberDrawn", next.Value);
        //            }

        //            await Task.Delay(intervalMs, token);
        //        }

        //        // Nömrələr bitdi
        //        if (room.NumbersQueue?.Count == 0)
        //        {
        //            await Clients.Group(roomId).SendAsync("GameOver", "Nömrələr qurtardı");
        //            await ResetGame(roomId);
        //        }
        //    }
        //    catch (TaskCanceledException)
        //    {
        //        Console.WriteLine($"🛑 AutoDraw stopped: {room.RoomName}");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"❌ AutoDrawLoop error: {ex}");
        //    }
        //}

        public async Task LineCompleted(int rowIndex)
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId)) return;

            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            var userId = GetUserId();
            if (userId == 0) return;

            RoomPlayer? player;
            lock (room.StateLock)
            {
                player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null || player.CompletedRows.Contains(rowIndex))
                {
                    return;
                }
                player.CompletedRows.Add(rowIndex);
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                user.Balance += room.LineReward;
                await _db.SaveChangesAsync();

                await Clients.Client(player.ConnectionId)
                    .SendAsync("BalanceUpdated", user.Balance);
            }

            await Clients.Group(roomId).SendAsync("LineCompleted", player.Name);
        }

        public async Task Bingo()
        {
            var roomId = GetCurrentRoom();
            if (string.IsNullOrEmpty(roomId)) return;

            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            var userId = GetUserId();
            if (userId == 0) return;

            RoomPlayer? player;
            bool isValid;

            lock (room.StateLock)
            {
                if (room.IsGameFinished)
                {
                    Clients.Caller.SendAsync("BingoError", "Oyun artıq bitib!");
                    return;
                }

                player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null) return;

                // ✅ BOŞ xanaları düzgün skip edən validatordan istifadə edirik
                isValid = LotoCardValidator.IsFullCardMarked(player.Card, room.DrawnNumbers);
            }

            if (!isValid)
            {
                await Clients.Caller.SendAsync("BingoError", "Yanlış LOTO iddiası!");
                return;
            }

            // ✅ Oyun bitir
            lock (room.StateLock)
            {
                room.IsGameFinished = true;
                room.IsGameStarted = false;
            }

            // ✅ Daş çəkmə prosesini dayandır
            room.AutoDrawCts?.Cancel();

            // ✅ Uduş verilir
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                user.Balance += room.WinReward;
                await _db.SaveChangesAsync();

                await Clients.Client(player.ConnectionId)
                    .SendAsync("BalanceUpdated", user.Balance);
            }

            // ✅ Qalib elan edilir
            await Clients.Group(roomId).SendAsync("GameOver",
                $"{player.Name} qalib oldu! 🎉 (+{room.WinReward} coin)");

            // ✅ 5 saniyə sonra oyun sıfırlanır
            await Task.Delay(5000);
            await ResetGame(roomId);

            Console.WriteLine("=== BINGO VALID ===");
        }

        private async Task ResetGame(string roomId)
        {
            var room = _roomManager.GetRoom(roomId);
            if (room == null) return;

            lock (room.StateLock)
            {
                room.Players.Clear();
                room.DrawnNumbers.Clear();
                room.NumbersQueue = null;
                room.IsGameStarted = false;
                room.IsGameFinished = false;
                room.AutoDrawCts?.Dispose();
                room.AutoDrawCts = null;
            }

            await Clients.Group(roomId).SendAsync("GameReset");
            Console.WriteLine($"🔄 Game reset: {room.RoomName}");
        }

        // ========== HELPER METODLAR ==========

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

            string[] playerNames;
            lock (room.StateLock)
            {
                playerNames = room.Players.Select(p => p.Name).ToArray();
            }

            await Clients.Group(roomId).SendAsync("PlayersList", playerNames);
        }

        internal static class LotoCardGenerator
        {
            public static int?[][] GenerateCard()
            {
                int?[][] card = new int?[3][];
                for (int r = 0; r < 3; r++)
                {
                    card[r] = new int?[9];
                }

                var numbers = Enumerable.Range(1, 90)
                    .OrderBy(x => Guid.NewGuid())
                    .Take(15)
                    .ToList();

                int idx = 0;
                for (int r = 0; r < 3; r++)
                {
                    var positions = Enumerable.Range(0, 9)
                        .OrderBy(x => Guid.NewGuid())
                        .Take(5)
                        .OrderBy(x => x)
                        .ToList();

                    foreach (var pos in positions)
                    {
                        card[r][pos] = numbers[idx++];
                    }
                }

                return card;
            }
        }

        internal static class LotoCardValidator
        {
            public static bool IsFullCardMarked(int?[][] card, IEnumerable<int> drawnNumbers)
            {
                var drawnSet = new HashSet<int>(drawnNumbers);

                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 9; c++)
                    {
                        if (card[r][c].HasValue && !drawnSet.Contains(card[r][c].Value))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }

    }
}