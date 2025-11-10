using BlogApp.Core.Entities;
using System.Collections.Concurrent;

namespace BlogApp.Api.Hubs.Services
{
    public class LotoRoomManager
    {
        private readonly ConcurrentDictionary<string, LotoRoom> _rooms = new();
        private readonly object _lock = new();

        public LotoRoom? CreateRoom(string roomName, string creatorName, int creatorUserId,
            decimal entryFee = 10, int maxPlayers = 10, bool isPrivate = false, string? password = null)
        {
            var room = new LotoRoom
            {
                RoomName = roomName,
                CreatorName = creatorName,
                CreatorUserId = creatorUserId,
                EntryFee = entryFee,
                MaxPlayers = maxPlayers,
                IsPrivate = isPrivate,
                Password = password,
                LineReward = entryFee,
                WinReward = entryFee * 5
            };

            if (_rooms.TryAdd(room.RoomId, room))
            {
                Console.WriteLine($"? Room yarad?ld?: {roomName} (ID: {room.RoomId})");
                return room;
            }

            return null;
        }

        public LotoRoom? GetRoom(string roomId)
        {
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        public bool DeleteRoom(string roomId)
        {
            if (_rooms.TryRemove(roomId, out var room))
            {
                // Cleanup
                room.AutoDrawCts?.Cancel();
                room.AutoDrawCts?.Dispose();
                room.AutoDrawTimer?.Dispose();

                Console.WriteLine($"??? Room silindi: {room.RoomName}");
                return true;
            }
            return false;
        }

        public List<RoomListItem> GetAvailableRooms()
        {
            return _rooms.Values
                .Where(r => !r.IsPrivate && !r.IsGameFinished)
                .Select(r => new RoomListItem
                {
                    RoomId = r.RoomId,
                    RoomName = r.RoomName,
                    CreatorName = r.CreatorName,
                    PlayerCount = r.Players.Count,
                    MaxPlayers = r.MaxPlayers,
                    EntryFee = r.EntryFee,
                    IsGameStarted = r.IsGameStarted,
                    IsPrivate = r.IsPrivate
                })
                .OrderByDescending(r => r.PlayerCount)
                .ToList();
        }

        public bool AddPlayerToRoom(string roomId, RoomPlayer player, string? password = null)
        {
            var room = GetRoom(roomId);
            if (room == null) return false;

            lock (room.StateLock)
            {
                // Validasiyalar
                if (room.Players.Count >= room.MaxPlayers)
                {
                    Console.WriteLine($"? Room dolu: {room.RoomName}");
                    return false;
                }

                if (room.IsGameStarted)
                {
                    Console.WriteLine($"? Oyun art?q ba?lay?b: {room.RoomName}");
                    return false;
                }

                if (room.IsPrivate && room.Password != password)
                {
                    Console.WriteLine($"? Yanl?? parol: {room.RoomName}");
                    return false;
                }

                if (room.Players.Any(p => p.UserId == player.UserId))
                {
                    Console.WriteLine($"?? Oyunçu art?q room-dad?r: {player.Name}");
                    return false;
                }

                room.Players.Add(player);
                Console.WriteLine($"? Oyunçu room-a qo?uldu: {player.Name} ? {room.RoomName}");
                return true;
            }
        }

        public bool RemovePlayerFromRoom(string roomId, int userId)
        {
            var room = GetRoom(roomId);
            if (room == null) return false;

            lock (room.StateLock)
            {
                var player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player != null)
                {
                    room.Players.Remove(player);
                    Console.WriteLine($"? Oyunçu room-dan ç?xd?: {player.Name}");

                    // Room bo?dursa v? oyun ba?lamay?bsa, sil
                    if (room.Players.Count == 0 && !room.IsGameStarted)
                    {
                        DeleteRoom(roomId);
                    }

                    return true;
                }
            }

            return false;
        }

        public int GetRoomCount() => _rooms.Count;

        public int GetTotalPlayers() => _rooms.Values.Sum(r => r.Players.Count);

        public void CleanupEmptyRooms()
        {
            var emptyRooms = _rooms.Values
                .Where(r => r.Players.Count == 0 && !r.IsGameStarted)
                .Select(r => r.RoomId)
                .ToList();

            foreach (var roomId in emptyRooms)
            {
                DeleteRoom(roomId);
            }
        }
    }
}