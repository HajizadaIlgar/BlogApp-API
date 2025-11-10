using BlogApp.Core.Entities;

namespace BlogApp.Api.Hubs
{

    public class OkeyGameService
    {
        public OkeyMoveResult StartGame(OkeyRoom room)
        {
            lock (room.StateLock)
            {
                if (!room.CanStartGame)
                    return Fail("Oyun başlamaq üçün 4 oyunçu lazımdır.");

                // Daş paylanması
                var (stock, hands, startIndex) = OkeyGameGenerator.DealTiles();
                room.Stock = stock;
                room.CurrentPlayerIndex = startIndex;

                for (int i = 0; i < 4; i++)
                {
                    room.Players[i].Hand = hands[i];
                    room.Players[i].SortHand();
                }

                room.Indicator = OkeyGameGenerator.SelectIndicator(room.Stock);
                room.IsGameStarted = true;

                return Success(room, "Oyun başladı");
            }
        }

        public OkeyMoveResult DrawFromStock(OkeyRoom room, string connectionId)
        {
            lock (room.StateLock)
            {
                var player = room.GetPlayer(connectionId);
                if (player == null) return Fail("Oyunçu tapılmadı.");
                if (room.CurrentPlayerId != connectionId) return Fail("Sənin növbən deyil.");
                if (room.Stock.Count == 0) return Fail("Yığın boşdur.");

                var tile = room.Stock.Last();
                room.Stock.RemoveAt(room.Stock.Count - 1);
                player.Hand.Add(tile);
                player.SortHand();

                return Success(room);
            }
        }

        public OkeyMoveResult DrawFromDiscard(OkeyRoom room, string connectionId)
        {
            lock (room.StateLock)
            {
                var player = room.GetPlayer(connectionId);
                if (player == null) return Fail("Oyunçu tapılmadı.");
                if (room.CurrentPlayerId != connectionId) return Fail("Sənin növbən deyil.");
                if (room.DiscardPile.Count == 0) return Fail("Atılmış daş yoxdur.");

                var tile = room.DiscardPile.Last();
                room.DiscardPile.RemoveAt(room.DiscardPile.Count - 1);
                player.Hand.Add(tile);
                player.SortHand();

                return Success(room);
            }
        }

        public OkeyMoveResult DiscardTile(OkeyRoom room, string connectionId, string tileId)
        {
            lock (room.StateLock)
            {
                var player = room.GetPlayer(connectionId);
                if (player == null) return Fail("Oyunçu tapılmadı.");
                if (room.CurrentPlayerId != connectionId) return Fail("Sənin növbən deyil.");

                var tile = player.Hand.FirstOrDefault(x => x.Id == tileId);
                if (tile == null) return Fail("Bu daş səndə yoxdur.");

                player.Hand.Remove(tile);
                room.DiscardPile.Add(tile);

                // Qələbə yoxlanır
                if (OkeyCombinationValidator.CheckWin(player.Hand, room.Indicator))
                {
                    room.IsGameFinished = true;
                    room.Winner = player;
                    return Win(room, player);
                }

                room.NextTurn();
                return Success(room);
            }
        }

        // Helpers -------------------------
        private OkeyMoveResult Success(OkeyRoom room, string? msg = null)
            => new OkeyMoveResult { Success = true, Message = msg ?? "OK" };

        private OkeyMoveResult Fail(string msg)
            => new OkeyMoveResult { Success = false, Message = msg };

        private OkeyMoveResult Win(OkeyRoom room, OkeyPlayer winner)
            => new OkeyMoveResult
            {
                Success = true,
                Message = $"{winner.Name} qalib gəldi 🎉"
            };
    }
}
