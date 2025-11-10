using BlogApp.Core.Entities;
using System.Collections.Concurrent;

namespace BlogApp.Api.Hubs
{
    public class OkeyRoomManager
    {
        private readonly ConcurrentDictionary<string, OkeyRoom> _rooms = new();

        public OkeyRoom? CreateRoom(string roomName, string creatorName, int creatorUserId, decimal entryFee)
        {
            var room = new OkeyRoom
            {
                RoomId = Guid.NewGuid().ToString(),
                RoomName = roomName.Trim(),
                CreatorName = creatorName,
                CreatorUserId = creatorUserId,
                EntryFee = entryFee
            };

            if (_rooms.TryAdd(room.RoomId, room))
            {
                Console.WriteLine($"✅ Okey room created: {room.RoomName} (ID: {room.RoomId})");
                return room;
            }

            return null;
        }

        public OkeyRoom? GetRoom(string roomId)
        {
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        public List<OkeyRoomListItem> GetAvailableRooms()
        {
            return _rooms.Values
                .Where(r => !r.IsGameStarted && !r.IsFull)
                .Select(r => new OkeyRoomListItem
                {
                    RoomId = r.RoomId,
                    RoomName = r.RoomName,
                    CreatorName = r.CreatorName,
                    PlayerCount = r.Players.Count,
                    EntryFee = r.EntryFee,
                    IsGameStarted = r.IsGameStarted
                })
                .OrderByDescending(r => r.PlayerCount)
                .ToList();
        }

        public bool AddPlayerToRoom(string roomId, OkeyPlayer player)
        {
            var room = GetRoom(roomId);
            if (room == null) return false;

            lock (room.StateLock)
            {
                if (room.IsFull)
                {
                    Console.WriteLine($"⚠️ Room {room.RoomName} is full");
                    return false;
                }

                if (room.IsGameStarted)
                {
                    Console.WriteLine($"⚠️ Game already started in {room.RoomName}");
                    return false;
                }

                // Artıq otaqdadırsa
                if (room.Players.Any(p => p.UserId == player.UserId))
                {
                    Console.WriteLine($"⚠️ Player {player.Name} already in room");
                    return false;
                }

                room.Players.Add(player);
                Console.WriteLine($"✅ Player {player.Name} joined {room.RoomName} ({room.Players.Count}/4)");
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
                if (player == null) return false;

                room.Players.Remove(player);
                Console.WriteLine($"❌ Player {player.Name} left {room.RoomName}");

                // Otaq boşdursa sil
                if (room.Players.Count == 0)
                {
                    DeleteRoom(roomId);
                }

                return true;
            }
        }

        public void DeleteRoom(string roomId)
        {
            if (_rooms.TryRemove(roomId, out var room))
            {
                Console.WriteLine($"🗑️ Room deleted: {room.RoomName}");
            }
        }

        public int GetActiveRoomCount()
        {
            return _rooms.Count;
        }

        public int GetActivePlayers()
        {
            return _rooms.Values.Sum(r => r.Players.Count);
        }
    }
}
