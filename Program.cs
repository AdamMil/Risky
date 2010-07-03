using System.Drawing;
using GameLib.Events;
using GameLib.Fonts;
using GameLib.Input;
using GameLib.Video;
using System;

namespace Risky
{

class Program
{
  enum StageProgress
  {
    None, FromSelected, ToSelected, AttackersSelected, TradingCards
  }

  static void Main()
  {
    Events.Initialize();
    Input.Initialize();
    Video.Initialize();

    string dataDir = "d:/adammil/code/risky/data/";
    map = new Map(dataDir + "map.xml");
    game = new Game(map, 2);
    font = new TrueTypeFont("c:/windows/fonts/arial.ttf", 16) { RenderStyle = RenderStyle.Blended };

    Video.SetMode(1005, 660, 0);
    WM.WindowTitle = "Risky!";

    Events.PumpEvents(EventProcedure);

    Video.Deinitialize();
    Input.Deinitialize();
    Events.Deinitialize();
  }

  static bool EventProcedure(Event e)
  {
    switch(e.Type)
    {
      case EventType.Keyboard:
      {
        KeyboardEvent ke = (KeyboardEvent)e;
        if(!ke.Down && ke.KeyMods == KeyMod.None)
        {
          if(ke.Key == Key.Escape) // if escape is pressed, reset the current stage if we're partially through it,
          {                        // or skip it if we're at the beginning of it
            if(progress != StageProgress.None) // if we're partially through the stage
            {
              progress = StageProgress.None; // go back to the beginning
              Repaint();
            }
            else if(game.Stage == GameStage.Attack || game.Stage == GameStage.Maneuver)
            {
              game.Skip();  // otherwise skip the stage if we're allowed
              Repaint();
            }
          }
          else if(ke.Key == Key.Enter) // if Enter is pressed...
          {
            if(progress == StageProgress.TradingCards) // trade in cards if the user has selected the number to trade
            {
              game.TradeInCards(starsToTrade);
              progress = StageProgress.None;
              Repaint();
            }
          }
        }
        break;
      }

      case EventType.MouseClick:
      {
        MouseClickEvent me = (MouseClickEvent)e;
        if(me.Down)
        {
          if(me.IsMouseWheel) // if the user rolls the mouse wheel...
          {
            if(game.Stage == GameStage.Draft) // in the draft stage...
            {
              // increase the number of armies in the territory if it's owned by the player on mouse wheel up
              // (there's no way to decrease it because the armies are placed immediately)
              if(me.Button == MouseButton.WheelUp && overTerritory != null &&
                 game.GetTerritoryInfo(overTerritory).Owner == game.CurrentPlayer)
              {
                game.Draft(overTerritory, 1);
              }
            }
            // in the attack stage, when selecting attackers, increase or decrease the number of attackers
            else if(game.Stage == GameStage.Attack && progress == StageProgress.ToSelected)
            {
              int maxArmies = Math.Min(3, game.GetTerritoryInfo(fromTerritory).Armies-1);
              if(me.Button == MouseButton.WheelUp && armiesCommitted < maxArmies) armiesCommitted++;
              else if(me.Button == MouseButton.WheelDown && armiesCommitted > 1) armiesCommitted--;
              else break;
            }
            // in the attack stage, when selecting defenders, increase or decrease the number of defenders
            else if(game.Stage == GameStage.Attack && progress == StageProgress.AttackersSelected)
            {
              int maxArmies = Math.Min(2, game.GetTerritoryInfo(toTerritory).Armies);
              if(me.Button == MouseButton.WheelUp && defenders < maxArmies) defenders++;
              else if(me.Button == MouseButton.WheelDown && defenders > 1) defenders--;
              else break;
            }
            // in the invade stage, or when maneuvering to a selected country, increase or decrease the number of
            // armies to move from one country to the other
            else if(game.Stage == GameStage.Invade ||
                    game.Stage == GameStage.Maneuver && progress == StageProgress.ToSelected)
            {
              int maxArmies = game.GetTerritoryInfo(fromTerritory).Armies-1;
              if(me.Button == MouseButton.WheelUp && armiesCommitted < maxArmies) armiesCommitted++;
              else if(me.Button == MouseButton.WheelDown && armiesCommitted > 0) armiesCommitted--;
              else break;
            }
            // in the draft stage, while trading cards, increase or decrease the number of cards to trade in
            else if(game.Stage == GameStage.Draft && progress == StageProgress.TradingCards)
            {
              // the player can only select an even number of cards to trade if he possesses no single star cards
              if(me.Button == MouseButton.WheelUp && starsToTrade < game.CurrentPlayer.Stars)
              {
                starsToTrade += game.CurrentPlayer.SingleStarCards == 0 ? 2 : 1;
              }
              else if(me.Button == MouseButton.WheelDown && starsToTrade > 2)
              {
                starsToTrade -= game.CurrentPlayer.SingleStarCards == 0 ? 2 : 1;
              }
              else break;
            }
          }
          else if(overTerritory != null && me.Button == MouseButton.Left) // when left-clicking on a territory...
          {
            switch(game.Stage)
            {
              case GameStage.Claim: // in the claim stage, claim the territory
                if(game.GetTerritoryInfo(overTerritory).Owner == null) // if it's unowned...
                {
                  game.Claim(overTerritory);
                }
                break;

              case GameStage.Populate: case GameStage.Draft: // in the populate or draft stages, add an army
                if(game.GetTerritoryInfo(overTerritory).Owner == game.CurrentPlayer) // if it's owned by the player
                {
                  if(game.Stage == GameStage.Populate) game.Populate(overTerritory);
                  else game.Draft(overTerritory, 1);
                }
                break;

              case GameStage.Attack:
                switch(progress)
                {
                  case StageProgress.None: // before an attack has begun, clicking selects the territory to attack from
                  {
                    TerritoryInfo info = game.GetTerritoryInfo(overTerritory);
                    if(info.Owner == game.CurrentPlayer && info.Armies > 1) // it must be owned by the player and have
                    {                                                       // a free army
                      fromTerritory = overTerritory;
                      progress = StageProgress.FromSelected;
                    }
                    break;
                  }
                  case StageProgress.FromSelected: // the player selected the destination territory
                    if(game.GetTerritoryInfo(overTerritory).Owner != game.CurrentPlayer && // if it's owned and
                       fromTerritory.Neighbors.Contains(overTerritory))                    // adjacent
                    {
                      toTerritory = overTerritory;
                      // default to committing the maximum number of armies. if there's no choice about the number of
                      // armies, proceed directly to choosing the number of defenders
                      armiesCommitted = Math.Min(3, game.GetTerritoryInfo(fromTerritory).Armies-1);
                      progress = armiesCommitted == 1 ? StageProgress.AttackersSelected : StageProgress.ToSelected;
                    }
                    break;
                  case StageProgress.ToSelected: // the player has selected the number of attackers
                    // so progress to selecting the number of defenders, defaulting to the maximum
                    defenders = Math.Min(2, game.GetTerritoryInfo(toTerritory).Armies);
                    progress  = StageProgress.AttackersSelected;
                    break;
                  case StageProgress.AttackersSelected: // the player has selected the number of defenders
                    // so we can begin the attack
                    if(game.Attack(fromTerritory, toTerritory, armiesCommitted, defenders))
                    {
                      // if the territory was claimed, we'll either be in the attack or invade stage. in case it's
                      // invade, we'll commit the maximum number of armies that could be moved into the new territory
                      armiesCommitted = game.GetTerritoryInfo(fromTerritory).Armies-1;
                      progress = StageProgress.None;
                    }
                    else
                    {
                      // the territory wasn't claimed, but armies were lost, so reduce the attackers and defenders to
                      // the new maximums as necessary. if no more armies are available, call off the attack
                      armiesCommitted = Math.Min(armiesCommitted, game.GetTerritoryInfo(fromTerritory).Armies-1);
                      defenders       = Math.Min(defenders, game.GetTerritoryInfo(toTerritory).Armies);
                      if(armiesCommitted == 0) progress = StageProgress.None;
                    }
                    break;
                }
                break;

              case GameStage.Invade: // in the invade stage, clicking sends in the armies
                if(overTerritory == toTerritory) game.Invade(armiesCommitted);
                break;

              case GameStage.Maneuver:
                switch(progress)
                {
                  case StageProgress.None: // the player selected the territory to move from 
                    if(game.GetTerritoryInfo(overTerritory).Owner == game.CurrentPlayer)
                    {
                      fromTerritory = overTerritory;
                      armiesCommitted = 0; // default to moving zero armies
                      progress = StageProgress.FromSelected;
                    }
                    break;
                  case StageProgress.FromSelected: // the player selected the destination territory
                    if(game.GetTerritoryInfo(overTerritory).Owner == game.CurrentPlayer &&
                       fromTerritory.Neighbors.Contains(overTerritory)) // it must be owned by the player and adjacent
                    {
                      toTerritory = overTerritory;
                      progress = StageProgress.ToSelected;
                    }
                    break;
                  case StageProgress.ToSelected: // the player selected the number of armies and finishes the move
                    game.Maneuver(fromTerritory, toTerritory, armiesCommitted);
                    progress = StageProgress.None;
                    break;
                }
                break;
            }
          }
          else if(me.Button == MouseButton.Right) // right click resets the current stage back to the beginning
          {
            if(progress != StageProgress.None) progress = StageProgress.None;
          }
          // left clicking outside all territories begins trading in stars, if the player has enough to trade in
          else if(game.Stage == GameStage.Draft && me.Button == MouseButton.Left && game.CurrentPlayer.Stars > 1)
          {
            if(progress == StageProgress.None) // the player hasn't started trading in stars yet
            {
              starsToTrade = Math.Min(10, game.CurrentPlayer.Stars); // default to the maximum allowed
              progress     = StageProgress.TradingCards;
            }
            else if(progress == StageProgress.TradingCards) // the player selected the number of stars
            {
              game.TradeInCards(starsToTrade); // so trade them in
              progress = StageProgress.None;
            }
          }

          Repaint();
        }
        break;
      }

      case EventType.MouseMove: // on mouse move, keep track of the territory under the mouse
      {
        MouseMoveEvent me = (MouseMoveEvent)e;
        if(overTerritory == null || !overTerritory.Contains(me.X, me.Y))
        {
          Territory newTerritory = map.GetTerritory(me.X, me.Y);
          if(newTerritory != overTerritory) // if the player mouses over a different territory...
          {
            overTerritory = newTerritory;
            Repaint();
          }
        }
        break;
      }

      case EventType.Repaint: Repaint(); break;
      case EventType.Quit: return false;
    }

    return true;
  }

  static void HighlightTerritory(Territory territory)
  {
    if(territory != null) territory.Image.Blit(Video.DisplaySurface, territory.Bounds.Location);
  }

  static void Repaint()
  {
    map.Image.Blit(Video.DisplaySurface);

    // highlight the moused-over territory and the selected territories
    HighlightTerritory(overTerritory);
    HighlightTerritory(fromTerritory);
    HighlightTerritory(toTerritory);

    // draw the army counts on all the territories
    foreach(Territory territory in map.Territories)
    {
      TerritoryInfo info = game.GetTerritoryInfo(territory);
      if(info.Owner != null) // if the territory is owned by somebody...
      {
        int armies = info.Armies;
        // in the invade and maneuver stages, alter the counts to reflect the number of armies that may be moved
        if(game.Stage == GameStage.Invade || game.Stage == GameStage.Maneuver && progress == StageProgress.ToSelected)
        {
          if(territory == fromTerritory) armies -= armiesCommitted;
          else if(territory == toTerritory) armies += armiesCommitted;
        }
        // draw the number in the player's color
        font.Color = playerColors[info.Owner.Index];
        font.Center(Video.DisplaySurface, armies.ToString(), territory.Center);
      }
    }

    // in the attack stage, draw a red line between the territories, and the allocated army count, if applicable
    if(game.Stage == GameStage.Attack && progress != StageProgress.None) // the 'from' territory has been selected
    {
      // if the player has selected the 'to' territory or is mousing over a valid candidate...
      if(progress != StageProgress.FromSelected ||
         overTerritory != null && fromTerritory.Neighbors.Contains(overTerritory) &&
         game.GetTerritoryInfo(overTerritory).Owner != game.CurrentPlayer)
      {
        // draw a red line from the 'from' territory to the potentially or actually selected 'to' territory
        Territory destination = progress == StageProgress.FromSelected ? overTerritory : toTerritory;
        Primitives.Line(Video.DisplaySurface, fromTerritory.Center, destination.Center, Color.Red);
      }

      // draw the number of attackers and defenders
      if(progress == StageProgress.ToSelected || progress == StageProgress.AttackersSelected)
      {
        // draw the number of attackers and, if selected, the number of defenders, halfway between the territories
        string text = armiesCommitted.ToString() +
                      (progress == StageProgress.AttackersSelected ? ":" + defenders.ToString() : null);
        font.Color = Color.Black;
        font.Render(Video.DisplaySurface, text, new Point((fromTerritory.Center.X + toTerritory.Center.X) / 2,
                                                          (fromTerritory.Center.Y + toTerritory.Center.Y) / 2));
      }
    }
    // in the invade or maneuver stages (when preparing to move troops), draw a green line between the territories
    else if(game.Stage == GameStage.Invade || game.Stage == GameStage.Maneuver && progress != StageProgress.None)
    {
      // similarly to the logic for the attack stage, draw a line if the 'to' territory is selected or the player is
      // mousing over a valid candidate for the 'to' territory
      if(game.Stage == GameStage.Invade || progress != StageProgress.FromSelected ||
         overTerritory != null && fromTerritory.Neighbors.Contains(overTerritory) &&
         game.GetTerritoryInfo(overTerritory).Owner == game.CurrentPlayer)
      {
        Territory destination = progress == StageProgress.FromSelected ? overTerritory : toTerritory;
        Primitives.Line(Video.DisplaySurface, fromTerritory.Center, destination.Center, Color.Green);
      }
    }
    else if(game.Stage == GameStage.Finished) // if the game is over, say so
    {
      font.Color = Color.White;
      font.Center(Video.DisplaySurface, "Game over!");
    }

    // draw a colored square indicating the current player
    Rectangle playerRect = new Rectangle(15, Video.Height-35, 20, 20);
    Video.DisplaySurface.Fill(playerRect, playerColors[game.CurrentPlayer.Index]);

    // and the number of armies the player has left to allocate, if any
    if(game.CurrentPlayer.DraftArmies != 0)
    {
      font.Color = Color.Black;
      font.Center(Video.DisplaySurface, game.CurrentPlayer.DraftArmies.ToString(), playerRect);
    }

    // and the number of stars the player has
    font.Color = playerColors[game.CurrentPlayer.Index];
    int x = playerRect.Right+5, y = playerRect.Top + playerRect.Height/2 - font.Height/2;
    if(game.CurrentPlayer.Stars != 0)
    {
      x += font.Render(Video.DisplaySurface, game.CurrentPlayer.Stars.ToString() + " stars", x, y) + 10;
    }
    // and the player's current continent bonus
    if(game.CurrentPlayer.ContinentBonus != 0)
    {
      x += font.Render(Video.DisplaySurface, "+" + game.CurrentPlayer.ContinentBonus + " bonus", x, y);
    }

    // draw the name of the game stage (with an asterisk if the player is partially through it)
    font.Color = Color.White;
    font.Render(Video.DisplaySurface, game.Stage.ToString() + (progress == StageProgress.None ? null : "*"), 10, 10);
    
    // if the player is in the middle of trading cards, draw the number of cards traded and the number they'd get back
    if(progress == StageProgress.TradingCards)
    {
      font.Render(Video.DisplaySurface, "Trading " + starsToTrade + " stars for " +
                  Game.GetArmiesForStars(starsToTrade).ToString() + " armies", 10, 10 + font.LineHeight);
    }

    Video.Flip();
  }
  
  static Map map;
  static Game game;
  /// <summary>The territory currently under the mouse, or null if the mouse is not over a territory.</summary>
  static Territory overTerritory;
  /// <summary>The 'from' and 'to' territories for the current stage. These are only valid when <see cref="progress"/>
  /// and <see cref="Game.Stage"/> are equal to appropriate values.
  /// </summary>
  static Territory fromTerritory, toTerritory;
  static TrueTypeFont font;
  /// <summary>The number of armies we'll use for invading, maneuvering, or attacking.</summary>
  static int armiesCommitted;
  /// <summary>The number of armies we'll use for defense.</summary>
  static int defenders;
  /// <summary>The number of stars to trade in.</summary>
  static int starsToTrade;
  /// <summary>The progress within the current stage, if applicable.</summary>
  static StageProgress progress;

  static readonly Color[] playerColors =
    new Color[] { Color.Red, Color.Blue, Color.Green, Color.Yellow, Color.Orange, Color.DarkGray };
}

} // namespace Risky
