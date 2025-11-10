using BlogApp.Core.Entities;
using System.Collections.Concurrent;

namespace BlogApp.Api.Hubs
{

    public class DominoRoomManager
    {
        private readonly ConcurrentDictionary<string, DominoRoom> _rooms = new();

        // ========== ROOM YARATMA ==========
        public DominoRoom? CreateRoom(
            string roomName,
            string creatorName,
            int creatorUserId,
            DominoGameType gameType,
            decimal entryFee = 10m,
            int maxPlayers = 4,
            bool isPrivate = false,
            string? password = null)
        {
            try
            {
                var room = new DominoRoom
                {
                    RoomId = Guid.NewGuid().ToString(),
                    RoomName = roomName,
                    CreatorName = creatorName,
                    CreatorUserId = creatorUserId,
                    GameType = gameType,
                    EntryFee = entryFee,
                    MaxPlayers = Math.Clamp(maxPlayers, 2, 4), // 2-4 oyunçu
                    IsPrivate = isPrivate,
                    Password = isPrivate ? password : null
                };

                if (_rooms.TryAdd(room.RoomId, room))
                {
                    Console.WriteLine($"✅ Domino room created: {roomName} (Type: {gameType})");
                    return room;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CreateRoom error: {ex.Message}");
                return null;
            }
        }

        // ========== ROOM ƏLDƏ ET ==========
        public DominoRoom? GetRoom(string roomId)
        {
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        // ========== ROOM SİL ==========
        public bool DeleteRoom(string roomId)
        {
            if (_rooms.TryRemove(roomId, out var room))
            {
                Console.WriteLine($"🗑️ Domino room deleted: {room.RoomName}");
                return true;
            }
            return false;
        }

        // ========== OYUNÇU ƏLAVƏ ET ==========
        public bool AddPlayerToRoom(string roomId, DominoPlayer player, string? password = null)
        {
            var room = GetRoom(roomId);
            if (room == null)
            {
                Console.WriteLine($"❌ Room not found: {roomId}");
                return false;
            }

            lock (room.StateLock)
            {
                // Room dolu?
                if (room.IsFull)
                {
                    Console.WriteLine($"❌ Room is full: {room.RoomName}");
                    return false;
                }

                // Oyun başlayıb?
                if (room.IsGameStarted)
                {
                    Console.WriteLine($"❌ Game already started in: {room.RoomName}");
                    return false;
                }

                // Parol yoxla
                if (room.IsPrivate && room.Password != password)
                {
                    Console.WriteLine($"❌ Wrong password for: {room.RoomName}");
                    return false;
                }

                // Oyunçu artıq room-dadır?
                if (room.Players.Any(p => p.UserId == player.UserId))
                {
                    Console.WriteLine($"⚠️ Player already in room: {player.Name}");
                    return false;
                }

                room.Players.Add(player);
                Console.WriteLine($"✅ Player joined: {player.Name} → {room.RoomName} ({room.Players.Count}/{room.MaxPlayers})");
                return true;
            }
        }

        // ========== OYUNÇU ÇIX ==========
        public bool RemovePlayerFromRoom(string roomId, int userId)
        {
            var room = GetRoom(roomId);
            if (room == null) return false;

            lock (room.StateLock)
            {
                var player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null) return false;

                room.Players.Remove(player);
                Console.WriteLine($"❌ Player left: {player.Name} from {room.RoomName}");

                // Room boşaldı?
                if (room.Players.Count == 0)
                {
                    DeleteRoom(roomId);
                }

                return true;
            }
        }

        // ========== MÖVCUD ROOM-LAR ==========
        public List<DominoRoomListItem> GetAvailableRooms()
        {
            return _rooms.Values
                .Where(r => !r.IsGameStarted && !r.IsFull) // Yalnız açıq və dolu olmayan
                .Select(r => new DominoRoomListItem
                {
                    RoomId = r.RoomId,
                    RoomName = r.RoomName,
                    CreatorName = r.CreatorName,
                    PlayerCount = r.Players.Count,
                    MaxPlayers = r.MaxPlayers,
                    EntryFee = r.EntryFee,
                    IsPrivate = r.IsPrivate,
                    IsGameStarted = r.IsGameStarted,
                    GameTypeName = GetGameTypeName(r.GameType)
                })
                .OrderByDescending(r => r.PlayerCount)
                .ToList();
        }

        // ========== BÜTÜN ROOM-LAR (admin üçün) ==========
        public List<DominoRoomListItem> GetAllRooms()
        {
            return _rooms.Values
                .Select(r => new DominoRoomListItem
                {
                    RoomId = r.RoomId,
                    RoomName = r.RoomName,
                    CreatorName = r.CreatorName,
                    PlayerCount = r.Players.Count,
                    MaxPlayers = r.MaxPlayers,
                    EntryFee = r.EntryFee,
                    IsPrivate = r.IsPrivate,
                    IsGameStarted = r.IsGameStarted,
                    GameTypeName = GetGameTypeName(r.GameType)
                })
                .OrderByDescending(r => r.PlayerCount)
                .ToList();
        }

        // ========== HELPER ==========
        private string GetGameTypeName(DominoGameType type)
        {
            return type switch
            {
                DominoGameType.Classic101 => "101 (7 daş)",
                DominoGameType.Quick5 => "Sürətli (5 daş)",
                DominoGameType.PhoneDomino => "Telefon Domino",
                _ => "Klassik"
            };
        }

        // ========== STATİSTİKA ==========
        public int GetActiveRoomCount() => _rooms.Count;
        public int GetActivePlayers() => _rooms.Values.Sum(r => r.Players.Count);
    }
}
