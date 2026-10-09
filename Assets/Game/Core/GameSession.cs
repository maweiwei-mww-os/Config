using System;
namespace DiceTown
{
    [Serializable] public sealed class TownSave
    {
        public int Format = 1, Town, Position, Coins = 180, Dice = 28, Shields, Turns;
        public int[] Levels = new int[3];
        public PendingTurn Pending;
    }
    [Serializable] public sealed class PendingTurn
    {
        public int Number, Start, Destination, Die1, Die2, Coins, Dice, ShieldDelta, Version;
        public string Name;
    }
    public sealed class GameSession
    {
        public TownSave State { get; }
        public GameSession(TownSave state = null)
        {
            State = state ?? new TownSave();
            if (State.Format != 1 || State.Town < 0 || State.Town >= TownRules.TownCount || State.Position < 0 || State.Position >= 24 ||
                State.Coins < 0 || State.Dice < 0 || State.Levels == null || State.Levels.Length != 3 || State.Shields < 0 || State.Shields > 3)
                throw new ArgumentException("Invalid save.");
            foreach (int l in State.Levels) if (l < 0 || l > 5) throw new ArgumentException("Invalid building level.");
            if (State.Pending != null)
            {
                var p = State.Pending;
                if (p.Destination < 0 || p.Destination >= 24 || p.Number != State.Turns + 1 || p.Dice < 0 || p.Dice > 100 || p.Coins < -10000 || p.Coins > 10000)
                    throw new ArgumentException("Invalid pending turn.");
                CompleteRoll(p);
            }
        }
        public PendingTurn BeginRoll(TownRules rules, int die1, int die2)
        {
            if (die1 < 1 || die1 > 6 || die2 < 1 || die2 > 6) throw new ArgumentOutOfRangeException();
            if (State.Pending != null || State.Dice <= 0) return null;
            int destination = (State.Position + die1 + die2) % 24;
            var reward = rules.Reward(State.Town, destination);
            int shieldDelta = reward.Kind == 1 && State.Shields < 3 ? 1 : reward.Kind == 3 && State.Shields > 0 ? -1 : 0;
            int cash = reward.Coins;
            if (cash < 0 && shieldDelta == -1) cash = 0;
            if (cash > 0) cash += (State.Levels[0] + State.Levels[1] + State.Levels[2]) * 5;
            if (State.Position + die1 + die2 >= 24) cash += 100;
            var turn = new PendingTurn { Number = State.Turns + 1, Start = State.Position, Destination = destination,
                Die1 = die1, Die2 = die2, Coins = cash, Dice = reward.Dice, ShieldDelta = shieldDelta, Version = rules.Version, Name = reward.Name };
            State.Dice--; State.Pending = turn; return turn;
        }
        public bool CompleteRoll(PendingTurn turn)
        {
            if (turn == null || !ReferenceEquals(State.Pending, turn)) return false;
            State.Position = turn.Destination; State.Coins = Math.Max(0, checked(State.Coins + turn.Coins));
            State.Dice = checked(State.Dice + turn.Dice); State.Shields = Math.Max(0, Math.Min(3, State.Shields + turn.ShieldDelta));
            State.Turns = turn.Number; State.Pending = null; return true;
        }
        public bool Upgrade(TownRules rules, int building)
        {
            if ((uint)building >= 3 || State.Pending != null || State.Levels[building] == 5) return false;
            int cost = rules.Cost(State.Town, building, State.Levels[building] + 1);
            if (State.Coins < cost) return false;
            State.Coins -= cost; State.Levels[building]++; return true;
        }
        public bool NextTown()
        {
            if (State.Pending != null || State.Levels[0] + State.Levels[1] + State.Levels[2] != 15 || State.Town == TownRules.TownCount - 1) return false;
            State.Town++; State.Position = 0; Array.Clear(State.Levels, 0, 3); State.Dice += 28; State.Coins += 180; return true;
        }
        public bool Supply()
        {
            if (State.Pending != null || State.Dice > 0) return false;
            State.Dice = 12; State.Coins = Math.Max(0, State.Coins - 100); return true;
        }
    }
}
