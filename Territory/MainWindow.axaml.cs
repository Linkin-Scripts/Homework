using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia; // for Thickness

namespace Territory;

public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    // configuration from UI
    public int Rows { get; set; } = 5;
    public int Columns { get; set; } = 5;
    private int _playerCount = 2;
    public int PlayerCount
    {
        get => _playerCount;
        set
        {
            if (_playerCount != value)
            {
                // clamp between 2 and 5
                var v = Math.Max(2, Math.Min(5, value));
                _playerCount = v;
                OnPropertyChanged(nameof(PlayerCount));
                RebuildPlayers(_playerCount);
            }
        }
    }

    private const string SaveFileName = "save.txt";

    // players
    private Player _currentPlayer = new Player("1");
    public ObservableCollection<Player> Players { get; set; } = new ObservableCollection<Player>();
    public List<string> ColorOptions { get; set; } = new List<string>();
    public List<int> PlayerCountOptions { get; set; } = new List<int> { 2, 3, 4, 5 };

    // track last known colors to enforce uniqueness
    private readonly Dictionary<Player, string> _lastColor = new();

    private Board _board = new Board(4,4);
    public Board Board
    {
        get => _board;
        set
        {
            if (_board != value)
            {
                _board = value;
                OnPropertyChanged(nameof(Board));
                OnPropertyChanged(nameof(Cells));
            }
        }
    }

    public IEnumerable<Cell> Cells => Board?.Cells;

    public ColourConverter colourConverter { get; set; }

    // index into Players
    public int Turn { get; set; } = 0;

    public string StatusMessage { get; set; } = "";
    private bool _gameOver;
    // public bool GameStarted { get; set; } // no longer needed
    public IBrush CurrentPlayerBrush { get; set; } = Brushes.LightGray;
    public bool StatusVisible { get; set; } = false;

    public MainWindow()
    {
        InitializeComponent();

        RebuildPlayers(PlayerCount);

        colourConverter = new ColourConverter();

        DataContext = this;

        // initialize text boxes with default sizes
        RowsBox.Text = Rows.ToString(); // Initialize RowsBox with default Rows
        ColsBox.Text = Columns.ToString(); // Initialize ColsBox with default Columns
        PlayersBox.Text = PlayerCount.ToString(); // Initialize PlayersBox with default players

        // start a new game with default size
        NewGame(null, null);
        
        // hide status on initial load; it will be shown when Play is pressed
        StatusVisible = false;
        StatusMessage = "";
        OnPropertyChanged(nameof(StatusVisible));
        OnPropertyChanged(nameof(StatusMessage));

        // initialize color options
        ColorOptions = new List<string> { "#FF0000", "#0000FF", "#00AA00", "#FFA500", "#FFD700", "#9370DB", "#FF1493", "#008080" };
        OnPropertyChanged(nameof(ColorOptions));
    }

    private void RebuildPlayers(int count)
    {
        var defaults = new[] { "#FF0000", "#0000FF", "#00AA00", "#FFA500", "#FFD700", "#9370DB", "#FF1493", "#008080" };

        // preserve existing players where possible
        var newList = new List<Player>();
        for (int i = 1; i <= count; i++)
        {
            if (i - 1 < Players.Count)
            {
                newList.Add(Players[i - 1]);
            }
            else
            {
                var p = new Player(i.ToString()) { Name = $"Player {i}", Color = defaults[(i - 1) % defaults.Length] };
                newList.Add(p);
            }
        }

        Players = new ObservableCollection<Player>(newList);
        // subscribe to property changes for uniqueness enforcement
        _lastColor.Clear();
        foreach (var p in Players)
        {
            p.PropertyChanged += Player_PropertyChanged;
            _lastColor[p] = p.Color;
        }

        OnPropertyChanged(nameof(Players));
    }

    private void Player_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not Player p) return;
        if (e.PropertyName == nameof(Player.Color))
        {
            var newColor = p.Color ?? string.Empty;
            // disallow empty
            if (string.IsNullOrWhiteSpace(newColor))
            {
                p.Color = _lastColor.ContainsKey(p) ? _lastColor[p] : string.Empty;
                return;
            }

            // check duplicates
            if (Players.Any(other => other != p && string.Equals(other.Color, newColor, StringComparison.OrdinalIgnoreCase)))
            {
                // revert
                SetStatus("Color already in use", Brushes.OrangeRed);
                p.Color = _lastColor.ContainsKey(p) ? _lastColor[p] : string.Empty;
            }
            else
            {
                _lastColor[p] = newColor;
                OnPropertyChanged(nameof(Players));
            }
        }
    }

    private void UpdateStatus()
    {
        // determine current player from Turn
        _currentPlayer = Players[Turn];
        var brush = GetPlayerBrush(_currentPlayer);
        SetStatus($"{_currentPlayer.Name}'s turn", brush);
    }

    private IBrush GetPlayerBrush(Player player)
    {
        if (!string.IsNullOrWhiteSpace(player.Color))
        {
            var brush = (IBrush?)colourConverter.Convert(player.Color, typeof(IBrush), null, null);
            if (brush != null) return brush;
        }

        return (IBrush?)colourConverter.Convert(player.Id, typeof(IBrush), null, null) ?? Brushes.LightGray;
    }

    private void SetStatus(string message, IBrush? brush = null)
    {
        StatusMessage = message;
        CurrentPlayerBrush = brush ?? Brushes.LightGray;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(CurrentPlayerBrush));
    }

    private bool AdvanceTurn()
    {
        for (int i = 0; i < Players.Count; i++)
        {
            Turn = (Turn + 1) % Players.Count;
            if (Board.LegalMoves(Players[Turn]).Any())
            {
                UpdateStatus();
                return true;
            }
        }

        return false;
    }

    private bool GameOver(out string message, out IBrush brush)
    {
        // draw if board full
        if (Board.Cells.All(c => c.Owner != null))
        {
            var winnerStatus = BuildWinnerStatus();
            message = winnerStatus.Message;
            brush = winnerStatus.Brush;
            return true;
        }

        if (!Players.Any(p => Board.LegalMoves(p).Any()))
        {
            var winnerStatus = BuildWinnerStatus();
            message = winnerStatus.Message;
            brush = winnerStatus.Brush;
            return true;
        }

        message = string.Empty;
        brush = Brushes.LightGray;
        return false;
    }

    private (string Message, IBrush Brush) BuildWinnerStatus()
    {
        var counts = Players.ToDictionary(p => p, p => Board.Cells.Count(c => c.Owner == p));
        var max = counts.Values.Max();
        var winners = counts.Where(kv => kv.Value == max).Select(kv => kv.Key.Name).ToList();
        if (winners.Count == 1)
        {
            var winner = counts.First(kv => kv.Value == max).Key;
            return ($"{winners[0]} wins with {max} cells!", GetPlayerBrush(winner));
        }

        return ($"Draw between {string.Join(", ", winners)} with {max} cells each.", Brushes.LightGray);
    }

    public void SaveGame(object? sender, RoutedEventArgs? e)
    {
        // no dialog; always write to predefined save file
        try
        {
            using var writer = new System.IO.StreamWriter(SaveFileName, false);
            writer.WriteLine($"{Board.Rows} {Board.Columns} {Players.Count} {Turn}");
            // write player name,color pairs
            var pairs = Players.Select(p => $"{p.Name.Replace('|',' ')},{p.Color}");
            writer.WriteLine(string.Join("|", pairs));
            var values = Board.Cells.Select(c => c.Owner == null ? "0" : c.Owner.Id);
            writer.WriteLine(string.Join(" ", values));
            SetStatus("Game saved.");
        }
        catch
        {
            SetStatus("Failed to save.");
        }
    }

    public void LoadGame(object? sender, RoutedEventArgs? e)
    {
        // always load from fixed file
        var path = SaveFileName;
        if (!System.IO.File.Exists(path))
        {
            SetStatus("Save file not found.");
            return;
        }

        try
        {
            var lines = System.IO.File.ReadAllLines(path);
            if (lines.Length < 2) return;
            var dims = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (dims.Length < 2) return;
            if (!int.TryParse(dims[0], out var rows) || !int.TryParse(dims[1], out var cols))
                return;
            int players = 2;
            int turn = 0;
            if (dims.Length >= 3 && !int.TryParse(dims[2], out players))
                return;
            if (dims.Length >= 4 && !int.TryParse(dims[3], out turn))
                return;
            // enforce min/max
            if (rows < 4 || cols < 4 || rows > 10 || cols > 10)
            {
                SetStatus("Saved board size out of allowed range (4-10).");
                return;
            }

            var tokens = lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != rows * cols)
                return;

            Rows = rows;
            Columns = cols;
            // parse players line if present
            if (lines.Length >= 3)
            {
                var playerLine = lines[1];
                var tokensP = playerLine.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (players < 2 || players > 8)
                {
                    SetStatus("Saved player count out of allowed range (2-8).");
                    return;
                }
                PlayerCount = players;
                var list = new List<Player>();
                for (int i = 0; i < tokensP.Length; i++)
                {
                    var parts = tokensP[i].Split(',', 2);
                    var name = parts.Length > 0 ? parts[0] : $"Player {i+1}";
                    var color = parts.Length > 1 ? parts[1] : string.Empty;
                    var p = new Player((i+1).ToString()) { Name = name, Color = color };
                    list.Add(p);
                }
                // ensure we have desired count
                while (list.Count < players)
                    list.Add(new Player((list.Count+1).ToString()));

                Players = new System.Collections.ObjectModel.ObservableCollection<Player>(list);
                foreach (var p in Players) p.PropertyChanged += Player_PropertyChanged;
                OnPropertyChanged(nameof(Players));
            }

            // push values into boxes as well
            RowsBox.Text = Rows.ToString();
            ColsBox.Text = Columns.ToString();
            PlayersBox.SelectedItem = PlayerCount;

            Board = new Board(rows, cols);
            // will rebuild grid at end
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!int.TryParse(tokens[i], out var ownerId) || ownerId < 0 || ownerId > Players.Count)
                {
                    return;
                }
                Board.Cells[i].Owner = ownerId == 0 ? null : Players[ownerId - 1];
            }

            Turn = ((turn % Players.Count) + Players.Count) % Players.Count;

            _gameOver = false;
            // no need to touch BoardGrid; binding will pick up new Board
            BuildBoardGrid();
            UpdateStatus();
            if (GameOver(out var msg, out var brush))
            {
                SetStatus(msg, brush);
                _gameOver = true;
            }
        }
        catch
        {
            // ignore errors for now
        }
    }

    public void NewGame(object? sender, RoutedEventArgs? e)
    {
        // read and parse from text boxes explicitly; this avoids binding timing issues
        int r, c;
        if (!int.TryParse(RowsBox.Text, out r) || r < 1 ||
            !int.TryParse(ColsBox.Text, out c) || c < 1)
        {
            SetStatus("Invalid setup values");
            return;
        }

        // use currently selected player count (ComboBox binds to PlayerCount)
        int p = PlayerCount;

        // enforce min/max limits
        if (r < 4 || c < 4 || r > 10 || c > 10)
        {
            SetStatus("Board size must be between 4 and 10.");
            return;
        }
        if (p < 2 || p > 5)
        {
            SetStatus("Player count must be between 2 and 5.");
            return;
        }

        Rows = r;
        Columns = c;
        PlayerCount = p;
        // ensure Players collection matches PlayerCount (preserve existing values)
        RebuildPlayers(PlayerCount);

        // keep boxes in sync
        RowsBox.Text = Rows.ToString();
        ColsBox.Text = Columns.ToString();
        PlayersBox.SelectedItem = PlayerCount;

        Board = new Board(Rows, Columns);
        Turn = 0;
        _gameOver = false;
        BuildBoardGrid();
        UpdateStatus();
        // ensure status becomes visible when a new game is started via Play
        StatusVisible = true;
        OnPropertyChanged(nameof(StatusVisible));
    }

    public void Play_Click(object? sender, RoutedEventArgs? e)
    {
        NewGame(sender, e);
        // show/hide UI pieces
        ConfigPanel.IsVisible = false;
        RestartButton.IsVisible = true;
        // show the board area
        BoardGrid.IsVisible = true;
        // ensure status visible when game starts
        StatusVisible = true;
        OnPropertyChanged(nameof(StatusVisible));
    }
    // This method is executed when a cell button is clicked
    public void CellCLick(object? sender, RoutedEventArgs e)
    {
        if (_gameOver)
            return; // ignore clicks once game has ended

        // determine current player object
        _currentPlayer = Players[Turn];

        if (sender is Button clickedButton && clickedButton.Tag is Cell clickedCell)
        {
            // ignore occupied cells
            if (clickedCell.Owner != null) return;

            // check legality
            var legal = Board.LegalMoves(_currentPlayer);
            if (!legal.Contains(clickedCell))
            {
                SetStatus("Illegal move", GetPlayerBrush(_currentPlayer));
                return; // illegal move
            }

            // make the move
            clickedCell.Owner = _currentPlayer;
            // update button visually immediately using the player's custom color
            clickedButton.Background = (IBrush?)colourConverter.Convert(clickedCell.Owner, typeof(IBrush), null, null);

            // advance turn, then check end condition
            var advanced = AdvanceTurn();
            var hasEnded = GameOver(out var msg, out var brush);
            if (!advanced || hasEnded)
            {
                if (string.IsNullOrWhiteSpace(msg))
                {
                    var winnerStatus = BuildWinnerStatus();
                    msg = winnerStatus.Message;
                    brush = winnerStatus.Brush;
                }
                SetStatus(msg, brush);
                _gameOver = true;
            }
        }
    }

    // rebuild the grid based on current board dimensions and ownership
    private void BuildBoardGrid()
    {
        BoardGrid.Children.Clear();
        BoardGrid.RowDefinitions.Clear();
        BoardGrid.ColumnDefinitions.Clear();

        for (int r = 0; r < Board.Rows; r++)
            BoardGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < Board.Columns; c++)
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        // compute dynamic cell size so boards larger than 6x6 shrink to fit
        // use the fixed windowed board dimensions
        double area = Math.Min(BoardGrid.Width, BoardGrid.Height);
        if (area <= 0) area = 300;
        double baseArea = area;
        double cellW = baseArea / Math.Max(Board.Columns, 1);
        double cellH = baseArea / Math.Max(Board.Rows, 1);
        double computed = Math.Floor(Math.Min(cellW, cellH));
        double size = computed;
        if (size < 20) size = 20;
        if (size > 80) size = 80;
        int idx = 0;
        for (int r = 0; r < Board.Rows; r++)
        {
            for (int c = 0; c < Board.Columns; c++)
            {
                var cell = Board.Cells[idx++];
                var btn = new Button { Width = size, Height = size, Margin = new Thickness(1) };
                btn.Tag = cell;
                btn.Click += CellCLick;
                // use Owner (Player) to get the player's custom color; null owner defaults to gray
                btn.Background = (IBrush?)colourConverter.Convert(cell.Owner ?? (object)cell.OwnerId, typeof(IBrush), null, null);
                Grid.SetRow(btn, r);
                Grid.SetColumn(btn, c);
                BoardGrid.Children.Add(btn);
            }
        }
    }

    private void RefreshGrid()
    {
        foreach (var child in BoardGrid.Children)
        {
            if (child is Button b && b.Tag is Cell cel)
            {
                // use Owner (Player) to get the player's custom color; null owner defaults to gray
                b.Background = (IBrush?)colourConverter.Convert(cel.Owner ?? (object)cel.OwnerId, typeof(IBrush), null, null);
            }
        }
    }

    // restart button handler shows config panel again
    public void Restart_Click(object? sender, RoutedEventArgs? e)
    {
        //GameStarted = false;
        SetStatus("");
        // hide status when restarting
        StatusVisible = false;
        OnPropertyChanged(nameof(StatusVisible));
        ConfigPanel.IsVisible = true;
        RestartButton.IsVisible = false;
        // hide board until Play pressed again
        BoardGrid.IsVisible = false;
    }

    // INotifyPropertyChanged to support bindings on this class
    public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));
}
