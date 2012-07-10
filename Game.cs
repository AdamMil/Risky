using System;
using System.Linq;
using System.Collections.ObjectModel;

namespace Risky
{

#region GameStage
/// <summary>Represents a stage of the game.</summary>
public enum GameStage
{
  /// <summary>The game is not yet started. This value will never be returned from <see cref="Game.Stage"/>.</summary>
  Initializing=0,
  /// <summary>The current player is expected to claim a territory.</summary>
  Claim,
  /// <summary>The current player is expected to select a territory where an army will be placed.</summary>
  Populate,
  /// <summary>The current player is expected to select territories where armies will be drafted, and optionally trade
  /// in cards to acquire bonus armies.
  /// </summary>
  Draft,
  /// <summary>The current player is expected to attack other territories or skip the stage.</summary>
  Attack,
  /// <summary>The current player is expected to move additional troops into a newly-conquered territory, or skip the
  /// invasion (to return to the attack stage).
  /// </summary>
  Invade,
  /// <summary>The current player is expected to manuever one or more armies from one territory to another, or skip the
  /// stage.
  /// </summary>
  Maneuver,
  /// <summary>The game is finished.</summary>
  Finished
}
#endregion

#region Player
/// <summary>Represents a player in a game, and that player's current state.</summary>
public sealed class Player
{
  internal Player(int index)
  {
    Index = index;
  }

  /// <summary>Gets or sets the <see cref="AI"/> of the player. If null, the player is human-controlled.</summary>
  public AI AI
  {
    get; set;
  }

  /// <summary>Gets whether the player has been defeated.</summary>
  public bool Defeated
  {
    get; internal set;
  }

  /// <summary>Gets the number of remaining armies that the player can draft, in the claim, populate, or draft stage.</summary>
  public int DraftArmies
  {
    get; internal set;
  }

  /// <summary>Gets the index of the player within the <see cref="Game.Players"/> collection, and therefore, within
  /// the turn order.
  /// </summary>
  public int Index
  {
    get; private set;
  }

  /// <summary>Gets the number of territories owned by the player.</summary>
  public int OwnedTerritories
  {
    get; internal set;
  }

  /// <summary>Gets the player's name.</summary>
  public string Name
  {
    get; set;
  }

  /// <summary>Gets the total number of stars from single and double star cards that have been won.</summary>
  public int Stars
  {
    get { return SingleStarCards + DoubleStarCards*2; }
  }

  /// <summary>Gets the number of double star cards that have been won. Each card is worth two stars.</summary>
  public int DoubleStarCards
  {
    get; internal set;
  }

  /// <summary>Gets the number of single star cards that have been won. Each card is worth one star, and you cannot
  /// trade in an odd number of stars unless you have at least one single star card.
  /// </summary>
  public int SingleStarCards
  {
    get; internal set;
  }

  /// <summary>Stores the number of territories the player has captured on the current turn.</summary>
  internal int CapturesThisTurn
  {
    get; set;
  }

  /// <summary>Gets the player's total continent draft bonus.</summary>
  internal int ContinentBonus
  {
    get; set;
  }

  /// <summary>Resets the player's per-turn data.</summary>
  internal void ResetTurnData()
  {
    CapturesThisTurn = 0;
  }
}
#endregion

#region TerritoryInfo
/// <summary>Represents the current state of a territory during a game.</summary>
public struct TerritoryInfo
{
  /// <summary>Gets the number of armies stationed in the territory.</summary>
  public int Armies
  {
    get; internal set;
  }

  /// <summary>Gets the player who owns this territory, or null if the territory is unowned. (Territories can only be
  /// unowned during the <see cref="GameStage.Claim"/> stage.)
  /// </summary>
  public Player Owner
  {
    get; internal set;
  }
}
#endregion

#region Game
/// <summary>Represents the current state of a game.</summary>
public sealed class Game
{
  /// <summary>Initializes a new <see cref="Game"/> given a map and the number of players.</summary>
  public Game(Map map, int numPlayers)
  {
    if(map == null) throw new ArgumentNullException("map");
    if(numPlayers < 2 || numPlayers > 6) throw new ArgumentOutOfRangeException("numPlayers");
    this.map = map;

    // initialize the territory state and players
    territories = new TerritoryInfo[map.Territories.Count];
    _players = new Player[numPlayers];
    for(int i=0; i<_players.Length; i++) _players[i] = new Player(i);
    Players = new ReadOnlyCollection<Player>(_players);

    // set up the initial deck of star cards
    singleStarCards = 30;
    doubleStarCards = 12;
    
    // start the game in the claim stage
    Stage = GameStage.Claim;
  }

  /// <summary>Gets the players whose turn it currently is (assuming <see cref="Stage"/> is not
  /// <see cref="GameStage.Finished"/>).
  /// </summary>
  public Player CurrentPlayer
  {
    get { return _players[_currentPlayer]; }
  }

  public ReadOnlyCollection<Player> Players
  {
    get; private set;
  }

  /// <summary>Gets the current stage of the game, as a <see cref="GameStage"/> value. This determines which actions
  /// are legal to take, as embodied in the methods that are allowed to be called.
  /// </summary>
  public GameStage Stage
  {
    get { return _stage; }
    private set
    {
      if(value != _stage)
      {
        _stage = value;
        OnStageChange();
      }
    }
  }

  /// <summary>In the <see cref="GameStage.Attack"/> stage, simulates an attack from <paramref name="fromTerritory"/>
  /// to <paramref name="toTerritory"/>, using <paramref name="attackers"/> as the number of attacking armies (from
  /// 1-3, but not more than the number of available armies minus one) and <paramref name="defenders"/> as the number
  /// of defending armies (from 1-2, but not more than the number of available armies). True is returned if the
  /// territory was captured and false if it was not. If the territory is captured and there are additional armies that
  /// can be moved into the territory, the game transitions to the <see cref="GameStage.Invade"/> stage.
  /// </summary>
  public bool Attack(Territory fromTerritory, Territory toTerritory, int attackers, int defenders)
  {
    AssertStage(GameStage.Attack);
    int attackIndex = GetTerritoryIndex(fromTerritory), defendIndex = GetTerritoryIndex(toTerritory);
    Player defender = territories[defendIndex].Owner;
    AssertOwned(attackIndex);
    AssertAdjacent(fromTerritory, toTerritory);
    if(CurrentPlayer == defender) throw new ArgumentException("You can't attack your own territory!");

    if(attackers < 1 || attackers > 3 || territories[attackIndex].Armies <= attackers)
    {
      throw new ArgumentOutOfRangeException("attackers");
    }
    if(defenders < 1 || defenders > 2 || territories[defendIndex].Armies < defenders)
    {
      throw new ArgumentOutOfRangeException("defenders");
    }

    // rather than rolling the dice, we'll use a precomputed table of win chances, which should be equivalent
    if(defenders == 1)
    {
      territories[rand.NextDouble() < singleWinChances[attackers-1] ? defendIndex : attackIndex].Armies--;
    }
    else
    {
      double n = rand.NextDouble();
      if(n < bothChances[attackers-1]) // both belligerents lose an army
      {
        territories[attackIndex].Armies--;
        territories[defendIndex].Armies--;
        attackers--; // keep track of the number of attackers so we know how many to move into the new territory
      }
      else // one belligerent loses
      {
        territories[n < doubleWinChances[attackers-1] ? defendIndex : attackIndex].Armies -= (attackers == 1 ? 1 : 2);
      }
    }

    if(territories[defendIndex].Armies != 0) // if the defender hasn't lost the territory...
    {
      return false; // just return false
    }
    else // otherwise, the attacker claimed the territory...
    {
      // so move the attacking armies into it
      territories[attackIndex].Armies -= attackers;
      territories[defendIndex].Armies += attackers;

      // change territory ownership
      territories[defendIndex].Owner  = CurrentPlayer;
      OnTerritoryGained(CurrentPlayer);
      OnTerritoryLost(defender); // this may change the stage to Finished

      // if the game isn't over, and the attacker has armies he can move into the territory, go to the invade stage
      if(Stage != GameStage.Finished && territories[attackIndex].Armies > 1)
      {
        moveFrom = attackIndex;
        moveTo   = defendIndex;
        Stage    = GameStage.Invade;
      }

      // if this is the first territory the player has captured this turn, give him a card
      if(CurrentPlayer.CapturesThisTurn++ == 0) GiveCard();

      // if the defender was defeated by losing this territory, give all his cards to the current player
      if(defender.Defeated)
      {
        CurrentPlayer.SingleStarCards += defender.SingleStarCards;
        CurrentPlayer.DoubleStarCards += defender.DoubleStarCards;
        defender.SingleStarCards = defender.DoubleStarCards = 0;
      }

      return true;
    }
  }

  /// <summary>In the <see cref="GameStage.Claim"/> stage, claims an unowned territory for the current player and
  /// advances to the next player. The stage is advanced to the <see cref="GameStage.Populate"/> stage when the last
  /// territory is claimed.
  /// </summary>
  public void Claim(Territory territory)
  {
    AssertStage(GameStage.Claim);
    int territoryIndex = GetTerritoryIndex(territory);
    if(territories[territoryIndex].Owner != null)
    {
      throw new InvalidOperationException("The territory is already claimed.");
    }

    territories[territoryIndex].Owner = CurrentPlayer;
    territories[territoryIndex].Armies  = 1;
    CurrentPlayer.DraftArmies--;
    OnTerritoryGained(CurrentPlayer);
    AdvancePlayer();
    if(--unclaimedTerritories == 0) Stage = GameStage.Populate;
  }

  /// <summary>In the <see cref="GameStage.Draft"/> stage, adds the given number of armies from the available draft
  /// pool (see <see cref="Player.DraftArmies"/>) to the given territory. If the player uses all his draft armies,
  /// the game transitions to the <see cref="GameStage.Attack"/> stage.
  /// </summary>
  public void Draft(Territory territory, int numArmies)
  {
    AssertStage(GameStage.Draft);
    int territoryIndex = GetTerritoryIndex(territory);
    AssertOwned(territoryIndex);
    if(numArmies < 0 || numArmies > CurrentPlayer.DraftArmies) throw new ArgumentOutOfRangeException();

    territories[territoryIndex].Armies += numArmies;
    CurrentPlayer.DraftArmies -= numArmies;
    if(CurrentPlayer.DraftArmies == 0) Stage = GameStage.Attack;
  }

  /// <summary>Returns the current state of the given territory as a <see cref="TerritoryInfo"/> object.</summary>
  public TerritoryInfo GetTerritoryInfo(Territory territory)
  {
    return territories[GetTerritoryIndex(territory)];
  }

  /// <summary>In the <see cref="GameStage.Invade"/> stage, moves the given number of additional armies from the 
  /// attacking territory to the newly captured territory and transitions the game back to the
  /// <see cref="GameStage.Attack"/> stage.
  /// </summary>
  public void Invade(int numArmies)
  {
    AssertStage(GameStage.Invade);
    if(numArmies < 0 || numArmies >= territories[moveFrom].Armies) throw new ArgumentOutOfRangeException();

    territories[moveFrom].Armies -= numArmies;
    territories[moveTo].Armies   += numArmies;
    Stage = GameStage.Attack;
  }

  /// <summary>In the <see cref="GameStage.Maneuver"/> stage, moves the given number of armies from one territory
  /// owned by the player to another and advances to the <see cref="GameStage.Draft"/> for the next player.
  /// </summary>
  public void Maneuver(Territory from, Territory to, int numArmies)
  {
    AssertStage(GameStage.Maneuver);
    int fromIndex = GetTerritoryIndex(from), toIndex = GetTerritoryIndex(to);
    AssertOwned(fromIndex);
    AssertOwned(toIndex);
    if(numArmies < 0 || numArmies >= territories[fromIndex].Armies) throw new ArgumentOutOfRangeException();
    AssertAdjacent(from, to);

    territories[fromIndex].Armies -= numArmies;
    territories[toIndex].Armies   += numArmies;
    AdvancePlayer();
    Stage = GameStage.Draft;
  }

  /// <summary>In the <see cref="GameStage.Populate"/> stage, adds an army to a territory owned by the current player
  /// and advances to the next player. The stage is advanced to the <see cref="GameStage.Draft"/> stage when all of
  /// the initial armies have been placed.
  /// </summary>
  public void Populate(Territory territory)
  {
    AssertStage(GameStage.Populate);
    int territoryIndex = GetTerritoryIndex(territory);
    AssertOwned(territoryIndex);

    territories[territoryIndex].Armies++;
    CurrentPlayer.DraftArmies--;

    if(!AdvancePlayer(p => p.DraftArmies != 0)) // try to move to the next player with armies remaining
    {
      AdvancePlayer(); // if there are none, just move to the next player
      Stage = GameStage.Draft; // and we're done with this stage
    }
  }

  /// <summary>Skips the current stage. This is valid in the <see cref="GameStage.Attack"/>,
  /// <see cref="GameStage.Maneuver"/>, and <see cref="GameStage.Invade"/> stages.
  /// </summary>
  public void Skip()
  {
    if(Stage == GameStage.Attack)
    {
      Stage = GameStage.Maneuver;
    }
    else if(Stage == GameStage.Maneuver)
    {
      AdvancePlayer();
      Stage = GameStage.Draft;
    }
    else if(Stage == GameStage.Invade)
    {
      Stage = GameStage.Attack;
    }
    else
    {
      throw new InvalidOperationException("The game is not in a stage that can be skipped.");
    }
  }

  /// <summary>In the <see cref="GameStage.Attack"/> stage, trades in the given number of stars for bonus armies. The
  /// number of stars must be a valid number that could be formed from the cards the current player has been given. For
  /// instance, if the current player has only double star cards, he cannot trade in an odd number of stars.
  /// </summary>
  public void TradeInCards(int stars)
  {
    AssertStage(GameStage.Draft);

    int maxStars = Math.Min(10, CurrentPlayer.Stars);
    // to simulate players trading in physical cards, we make sure that they can't trade in an odd number of stars
    // if they only have double star cards
    if(stars < 2 || stars > maxStars || CurrentPlayer.SingleStarCards == 0 && (stars & 1) != 0)
    {
      throw new ArgumentOutOfRangeException();
    }

    // TODO: should we preferentially take single cards instead, to return more cards to the deck?
    int doubleCardsTradedIn = Math.Min(stars/2, CurrentPlayer.DoubleStarCards); // preferentially take double cards
    int singleCardsTradedIn = stars - doubleCardsTradedIn*2; // then take the rest from single cards

    CurrentPlayer.DoubleStarCards -= doubleCardsTradedIn;
    CurrentPlayer.SingleStarCards -= singleCardsTradedIn;
    discardedDoubleStarCards += doubleCardsTradedIn;
    discardedSingleStarCards += singleCardsTradedIn;

    CurrentPlayer.DraftArmies += GetArmiesForStars(stars);
  }

  /// <summary>Gets the number of bonus armies that would be granted if the player traded in the given number of stars,
  /// from 2 to 10.
  /// </summary>
  public static int GetArmiesForStars(int stars)
  {
    if(stars < 2 || stars > 10) throw new ArgumentOutOfRangeException();
    return armiesForStars[stars-2];
  }

  /// <summary>Advances to the next undefeated player.</summary>
  void AdvancePlayer()
  {
    AdvancePlayer(null);
  }

  /// <summary>Advances to the next undefeated player that satisfies the given predicate. Returns true if the game
  /// advanced to the next player and false if no valid players could be found.
  /// </summary>
  bool AdvancePlayer(Predicate<Player> isValid)
  {
    int nextPlayer = _currentPlayer;
    do
    {
      if(++nextPlayer == _players.Length) nextPlayer = 0;
      if(nextPlayer == _currentPlayer) return false; // if we've returned to the starting point, then no players match
    } while(_players[nextPlayer].Defeated || isValid != null && !isValid(_players[nextPlayer]));

    CurrentPlayer.ResetTurnData(); // reset the old player's per-turn info
    _currentPlayer = nextPlayer;   // and move to the new player
    return true;
  }

  /// <summary>Asserts that two territories are adjacent for the purposes of attacking and moving.</summary>
  void AssertAdjacent(Territory t1, Territory t2)
  {
    if(!t1.Neighbors.Contains(t2)) throw new ArgumentException("The territories are not adjacent.");
  }

  /// <summary>Asserts that the territory at the given index is owned by the current player.</summary>
  void AssertOwned(int territoryIndex)
  {
    if(territories[territoryIndex].Owner != CurrentPlayer)
    {
      throw new InvalidOperationException("The territory belongs to another player.");
    }
  }

  /// <summary>Asserts that the game is currently in the given stage.</summary>
  void AssertStage(GameStage stage)
  {
    if(Stage != stage) throw new InvalidOperationException("The game is not in the " + stage.ToString() + " stage.");
  }

  /// <summary>Recalculates the given player's continent bonus by checking all continents for ownership.</summary>
  void CalculateContinentBonus(Player player)
  {
    int bonus = 0;
    foreach(Continent continent in map.Continents)
    {
      if(player.OwnedTerritories >= continent.Territories.Count)
      {
        if(continent.Territories.All(t => territories[GetTerritoryIndex(t)].Owner == player))
        {
          bonus += continent.DraftBonus;
        }
      }
    }
    player.ContinentBonus = bonus;
  }

  /// <summary>Gets the number of initial armies granted to each player.</summary>
  int GetInitialArmies()
  {
    return 40 - (_players.Length-2)*5; // 40 for 2 players, 35 for 3, 30 for 4, 25 for 5, and 20 for 6
  }

  /// <summary>Gets the index of the given territory within the <see cref="Map.Territories"/> collection (and thus the
  /// index of the corresponding <see cref="TerritoryInfo"/> object within the <see cref="territories"/> array).
  /// </summary>
  int GetTerritoryIndex(Territory territory)
  {
    int territoryIndex = map.Territories.IndexOf(territory);
    if(territoryIndex == -1) throw new ArgumentException();
    return territoryIndex;
  }

  /// <summary>Draws a card from the deck and gives it to the current player, if possible.</summary>
  void GiveCard()
  {
    int totalCards = singleStarCards + doubleStarCards;
    if(totalCards == 0) // if the deck is empty, try to make a new deck from the discard pile
    {
      singleStarCards = discardedSingleStarCards;
      doubleStarCards = discardedDoubleStarCards;
      totalCards = singleStarCards + doubleStarCards;
      if(totalCards == 0) return; // if there are no cards in the discard pile either, then we can't give a card
      else discardedSingleStarCards = discardedDoubleStarCards = 0; // otherwise, reset the discard pile
    }

    int n = rand.Next(totalCards);
    if(n < singleStarCards)
    {
      singleStarCards--;
      CurrentPlayer.SingleStarCards++;
    }
    else
    {
      doubleStarCards--;
      CurrentPlayer.DoubleStarCards++;
    }
  }
  
  /// <summary>Called after the game changes from one stage to another.</summary>
  void OnStageChange()
  {
    switch(Stage)
    {
      case GameStage.Claim: // in the claim stage, set the number of unclaimed territories and the initial armies
        unclaimedTerritories = territories.Length;
        foreach(Player player in _players) player.DraftArmies = GetInitialArmies();
        break;

      case GameStage.Draft: // in the draft stage, grant the new armies to the current player
      {
        // give the player one new army for every three territories owned, with a minimum of three, plus bonuses
        int newArmies = Math.Max(3, CurrentPlayer.OwnedTerritories / 3) + CurrentPlayer.ContinentBonus;
        CurrentPlayer.DraftArmies += newArmies;
        break;
      }
    }
  }

  /// <summary>Called when the given player gains a territory.</summary>
  void OnTerritoryGained(Player player)
  {
    player.OwnedTerritories++;
    CalculateContinentBonus(player);
  }

  /// <summary>Called when the given player loses a territory.</summary>
  void OnTerritoryLost(Player player)
  {
    if(--player.OwnedTerritories == 0) // if a player lost all his territories
    {
      player.Defeated = true; // then he was defeated
      if(_players.Count(p => !p.Defeated) == 1) Stage = GameStage.Finished; // if only one player remains, he wins
    }
    CalculateContinentBonus(player);
  }

  readonly Map map;
  readonly Player[] _players;
  readonly Random rand = new Random();
  /// <summary>Information about the state of the game's territories. Each element corresponds to a territory in the
  /// <see cref="Map.Territories"/> collection.
  /// </summary>
  TerritoryInfo[] territories;
  /// <summary>The index of the current player.</summary>
  int _currentPlayer;
  /// <summary>The number of unclaimed territories, in the <see cref="GameStage.Claim"/> stage.</summary>
  int unclaimedTerritories;
  /// <summary>The indices of the from/to territories used in the corresponding attack during the
  /// <see cref="GameStage.Invade"/> stage.
  /// </summary>
  int moveFrom, moveTo;
  /// <summary>The number of single and double star cards in the draw deck.</summary>
  int singleStarCards, doubleStarCards;
  /// <summary>The number of single and double star cards in the discard deck.</summary>
  int discardedSingleStarCards, discardedDoubleStarCards;
  /// <summary>The current game stage.</summary>
  GameStage _stage;

  // the values below were taken from http://www4.stat.ncsu.edu/~jaosborn/research/RISK.pdf

  /// <summary>The chances of the attacker winning while attacking with 1, 2, or 3 dice, against one defender.</summary>
  static readonly double[] singleWinChances = new double[] { 15d/36, 125d/216, 855d/1296 };

  /// <summary>The chances of the attacker winning while attacking with 1, 2, or 3 dice, against two defenders,
  /// assuming that it's not the case that both lose an army.
  /// </summary>
  static readonly double[] doubleWinChances = new double[] { 55d/216, 715d/1296, 5501d/7776 };

  /// <summary>The chance that both the attacker and defender armies lose one army, when the attacker attacks with 1,
  /// 2, or 3 dice.
  /// </summary>
  static readonly double[] bothChances = new double[] { 0, 420d/1296, 2611d/7776 };

  /// <summary>The number of armies given for trading in 2-10 stars.</summary>
  static readonly int[] armiesForStars = new int[] { 2, 4, 7, 10, 13, 17, 21, 25, 30 };
}
#endregion

} // namespace Risky
