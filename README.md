# Territory

A turn-based territory expansion board game built with C# and Avalonia UI.

## Overview

Territory is a 2–5 player strategy game played on a configurable grid. Players take turns claiming cells, expanding their territory outward from their starting position. The player who controls the most cells when no moves remain wins.

## Screenshots & UI

The game uses a dark-themed UI with:
- A configuration panel to set board size and player names/colors
- A dynamic board grid where players click to claim cells
- A live scoreboard and persistent leaderboard in the sidebar

## Rules

1. **First move**: A player may claim any empty cell on the board.
2. **Subsequent moves**: A player may only claim an empty cell that is **8-directionally adjacent** (including diagonals) to a cell they already own.
3. **Elimination**: If a player has no legal moves on their turn, they are eliminated and skipped.
4. **Game end**: The game ends when the board is full or no remaining player has a legal move.
5. **Winner**: The player with the most cells wins. If multiple players tie, the game is declared a draw.

## Features

- Configurable board size: 4×4 to 10×10
- 2 to 5 players with custom names and colors
- Save and load game state to/from `save.txt`
- Persistent leaderboard stored in `leaderboard.json`
- Live per-player cell count during the game
- Player elimination tracking
- Dark-themed Avalonia UI with Fluent design

## Project Structure

```
Territory/
├── Territory.sln
└── Territory/
    ├── Territory.csproj
    ├── Program.cs              # Entry point
    ├── App.axaml / App.axaml.cs
    ├── MainWindow.axaml        # Main UI layout
    ├── MainWindow.axaml.cs     # Game controller + ViewModel
    ├── Board.cs                # Board state and move validation
    ├── Cell.cs                 # Grid cell model
    ├── Player.cs               # Player model
    ├── ColourConverter.cs      # IValueConverter for XAML color binding
    ├── LeaderboardManager.cs   # Leaderboard read/write utility
    ├── LeaderboardEntry.cs     # Leaderboard data model
    ├── save.txt                # Save file (generated at runtime)
    └── leaderboard.json        # Leaderboard file (generated at runtime)
```

## Architecture

The project follows the **MVVM pattern** with Avalonia UI.

### Core Classes

#### `Board.cs`
Holds the grid of `Cell` objects and implements move validation.

- `Cells` — flat list of all cells in row-major order
- `LegalMoves(Player)` — returns all empty cells adjacent (8-directional) to the player's existing cells; returns all empty cells if the player has no cells yet (first move)

#### `Cell.cs`
Represents a single grid cell. Implements `INotifyPropertyChanged` so the UI updates automatically when `Owner` changes.

- `Row`, `Column` — position
- `Owner` — the `Player` who owns this cell, or `null` if empty
- `OwnerId` — string version of owner ID used in XAML bindings

#### `Player.cs`
Represents a player. Implements `INotifyPropertyChanged`.

- `Id` — unique string identifier (`"1"`–`"5"`)
- `Name` — display name (editable by the user before the game starts)
- `Color` — hex color string (e.g. `#FF4500`)

#### `MainWindow.axaml.cs`
Central game controller and ViewModel. Handles:
- Game setup and input validation
- Cell click handling and move enforcement
- Turn advancement and player elimination
- Game-over detection and winner determination
- Save/load logic
- Leaderboard updates
- All UI state properties (`StatusMessage`, `CurrentPlayerBrush`, `ScoreBoard`, etc.)

#### `ColourConverter.cs`
Implements Avalonia's `IValueConverter`. Maps a `Player` object (or player ID string) to a `SolidColorBrush` for XAML color bindings. Falls back to a built-in 8-color palette if the player's custom color is invalid.

#### `LeaderboardManager.cs`
Static utility for reading and writing `leaderboard.json` using `System.Text.Json`. Returns an empty list silently on missing file or parse error.

### Game Flow

```
[Configuration] → [Start Game] → [Player Takes Turn]
                                        │
                               [Validate Move via Board.LegalMoves]
                                        │
                              ┌─────────┴──────────┐
                           Legal                 Illegal
                              │                     │
                     [Assign Cell Owner]     [Show "Illegal move"]
                              │
                     [Check: Board Full?]
                     [Check: No moves left?]
                              │
                      ┌───────┴───────┐
                    Yes               No
                      │               │
                [Game Over]    [Advance Turn]
                [Leaderboard]  [Skip eliminated players]
```

### Turn System

- `_currentTurnIndex` tracks whose turn it is (index into `_players` list).
- `AdvanceTurn()` increments the index and checks if the next player has legal moves.
- Players with no legal moves are added to `_eliminatedPlayers` and skipped.
- The game ends if all remaining active players have no moves, or if the board is full.

## Save/Load Format

Games are saved to `save.txt` in plain text:

```
<rows> <columns> <playerCount> <currentTurnIndex>
<name1>,<color1>|<name2>,<color2>|...
<ownerId> <ownerId> ... (space-separated, one per cell, 0 = empty)
```

**Example:**
```
5 5 2 1
Alice,#E63946|Bob,#457B9D
0 1 1 0 0 1 1 2 0 0 0 1 2 2 0 0 0 2 0 0 0 0 0 0 0
```

Load validates:
- Board dimensions are in range (4–10)
- Player count is in range (2–5)
- Cell count matches `rows × columns`
- All owner IDs are valid (0 to playerCount)

## Leaderboard Format

`leaderboard.json` stores an array of game results:

```json
[
  {
    "PlayerName": "Alice",
    "Score": 14,
    "Date": "2026-03-29T12:00:00+01:00"
  }
]
```

A new entry is appended only when a single player wins. Draws are not recorded.

## Technology Stack

| Component        | Technology                           |
|------------------|--------------------------------------|
| Language         | C# (.NET 10)                         |
| UI Framework     | Avalonia UI 11.3.12                  |
| UI Theme         | Avalonia Fluent                      |
| Font             | Inter (via Avalonia.Fonts.Inter)     |
| Serialization    | System.Text.Json                     |
| UI Pattern       | MVVM (manual INotifyPropertyChanged) |

## Build & Run

**Prerequisites**: .NET 10 SDK

```bash
# Run the application
dotnet run --project Territory/Territory.csproj

# Build only
dotnet build

# Release build
dotnet run -c Release --project Territory/Territory.csproj
```

## Key Design Decisions

- **`MainWindow` as ViewModel**: The window itself implements `INotifyPropertyChanged` and acts as its own DataContext, keeping the project structure simple without a separate ViewModel layer.
- **`ColourConverter` fallback palette**: Players can pick any hex color, but if the input is invalid the converter falls back to a predefined 8-color palette indexed by player ID.
- **Static `LeaderboardManager`**: Since leaderboard operations are pure file I/O with no shared state, a static utility class avoids unnecessary dependency injection overhead.
- **HashSet in `LegalMoves`**: Ensures no duplicate cells are returned when multiple owned cells are adjacent to the same empty cell.
- **Player color uniqueness**: `Player_PropertyChanged` enforces that no two players share the same color by reallocating on conflict.
