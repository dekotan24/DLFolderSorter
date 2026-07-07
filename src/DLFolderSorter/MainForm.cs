using DLFolderSorter.Core;

namespace DLFolderSorter;

public sealed class MainForm : Form
{
    private static readonly (string Key, string Label)[] Categories =
    {
        (Category.Game, "ゲーム"),
        (Category.Voice, "音声"),
        (Category.Movie, "動画"),
        (Category.Book, "マンガ・CG"),
        (Category.Av, "AV"),
        (Category.Unknown, "不明"),
    };

    private static readonly Dictionary<string, string> CategoryLabels =
        Categories.ToDictionary(c => c.Key, c => c.Label);

    private readonly AppConfig _config = AppConfig.Load();
    private List<SortItem> _items = new();
    private List<SortItem> _view = new();
    private CancellationTokenSource? _cts;

    // コントロール
    private readonly TextBox _txtSource = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _chkIncludeFiles = new() { Text = "ファイルを含める", AutoSize = true, Checked = true };
    private readonly CheckBox _chkRename = new() { Text = "IDのみの名前をリネーム", AutoSize = true, Checked = true };
    private readonly CheckBox _chkMoveUnknown = new() { Text = "種別不明を「不明」へ移動", AutoSize = true };
    private readonly NumericUpDown _numRate = new() { Minimum = 1.0m, Maximum = 30m, DecimalPlaces = 1, Increment = 0.5m, Value = 2.5m, Width = 60 };
    private readonly CheckBox _chkSeparateSites = new() { Text = "販売元で分ける", AutoSize = true };
    private readonly CheckBox _chkSeparateAi = new() { Text = "AI作品を分ける", AutoSize = true };
    private readonly DataGridView _destGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        AllowUserToResizeRows = false,
        ColumnHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
    };
    private readonly Button _btnScan = new() { Text = "スキャン+照会", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _btnExecute = new() { Text = "実行", AutoSize = true, Padding = new Padding(16, 2, 16, 2), Enabled = false };
    private readonly Button _btnCancel = new() { Text = "中断", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Enabled = false };
    private readonly Button _btnTheme = new() { Text = "🌙", AutoSize = true };
    private readonly ComboBox _cmbFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        VirtualMode = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AllowUserToResizeRows = false,
    };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 200 };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        Text = "DLFolderSorter";
        Width = 1180;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception)
        {
            // アイコンが取れなくても起動は続ける
        }
        BuildLayout();
        BuildGridColumns();
        LoadConfigToUi();
        ApplyTheme();

        FormClosing += (_, _) => SaveUiToConfig();
    }

    // ---------- レイアウト ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 仕分け元
        var srcRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        srcRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        srcRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        srcRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        srcRow.Controls.Add(new Label { Text = "仕分け元:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        srcRow.Controls.Add(_txtSource, 1, 0);
        var btnBrowseSrc = new Button { Text = "参照...", AutoSize = true };
        btnBrowseSrc.Click += (_, _) => BrowseFolder(_txtSource);
        srcRow.Controls.Add(btnBrowseSrc, 2, 0);
        root.Controls.Add(srcRow, 0, 0);

        // オプション
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        options.Controls.Add(_chkIncludeFiles);
        options.Controls.Add(_chkRename);
        options.Controls.Add(_chkMoveUnknown);
        options.Controls.Add(new Label { Text = "  API間隔:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 3, 0, 0) });
        options.Controls.Add(_numRate);
        options.Controls.Add(new Label { Text = "秒   ", AutoSize = true, Padding = new Padding(0, 3, 0, 0) });
        options.Controls.Add(new Label { Text = "ソート軸:", AutoSize = true, Padding = new Padding(0, 3, 0, 0) });
        options.Controls.Add(_chkSeparateSites);
        options.Controls.Add(_chkSeparateAi);
        options.Controls.Add(_btnTheme);
        _chkSeparateSites.CheckedChanged += (_, _) => RegenerateDestRows();
        _chkSeparateAi.CheckedChanged += (_, _) => RegenerateDestRows();
        _btnTheme.Click += (_, _) => { _config.DarkMode = !_config.DarkMode; ApplyTheme(); };
        root.Controls.Add(options, 0, 1);

        // 仕分け先（ソート軸に応じた区分だけを動的に並べる）
        var destGroup = new GroupBox { Text = "仕分け先フォルダ（空欄の区分は動かさない）", Dock = DockStyle.Top, Height = 210, Padding = new Padding(8) };
        var destPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        destPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        destPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        BuildDestGridColumns();
        destPanel.Controls.Add(_destGrid, 0, 0);
        var bulkRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var btnBulk = new Button { Text = "ベースフォルダから一括入力...", AutoSize = true };
        btnBulk.Click += (_, _) => BulkFillDestinations();
        var btnClearDest = new Button { Text = "全てクリア", AutoSize = true };
        btnClearDest.Click += (_, _) => { foreach (DataGridViewRow r in _destGrid.Rows) r.Cells["Path"].Value = ""; };
        bulkRow.Controls.Add(btnBulk);
        bulkRow.Controls.Add(btnClearDest);
        destPanel.Controls.Add(bulkRow, 0, 1);
        destGroup.Controls.Add(destPanel);
        root.Controls.Add(destGroup, 0, 2);

        // アクション行
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
        actions.Controls.Add(_btnScan);
        actions.Controls.Add(_btnExecute);
        actions.Controls.Add(_btnCancel);
        actions.Controls.Add(new Label { Text = "   表示:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        actions.Controls.Add(_cmbFilter);
        var btnRetryNotFound = new Button { Text = "not found を再試行", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
        btnRetryNotFound.Click += (_, _) =>
        {
            new ClassifyService(AppConfig.AppDataDirectory).ClearNotFound();
            SetStatus("not found のキャッシュを消去しました。次回スキャンで再照会されます。");
        };
        actions.Controls.Add(btnRetryNotFound);
        _btnScan.Click += async (_, _) => await ScanAndClassifyAsync();
        _btnExecute.Click += async (_, _) => await ExecuteAsync();
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        _cmbFilter.Items.AddRange(new object[] { "全て", "移動・リネームあり", "スキップ・不明のみ", "IDなしのみ" });
        _cmbFilter.SelectedIndex = 0;
        _cmbFilter.SelectedIndexChanged += (_, _) => RefreshView();
        root.Controls.Add(actions, 0, 3);

        root.Controls.Add(_grid, 0, 4);
        Controls.Add(root);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_progress);
        Controls.Add(statusStrip);
    }

    private void BuildGridColumns()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "✓", Width = 30, Name = "Enabled" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "名前", Width = 320, Name = "Name", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Width = 100, Name = "WorkId", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "種別", Width = 90, Name = "WorkType", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "カテゴリ", Width = 75, Name = "Category", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "アクション", Width = 85, Name = "Action", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "新しい名前", Width = 300, Name = "NewName", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "備考", Width = 170, Name = "Flag", ReadOnly = true });

        _grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            var item = _view[e.RowIndex];
            e.Value = _grid.Columns[e.ColumnIndex].Name switch
            {
                "Enabled" => item.Enabled,
                "Name" => item.Name,
                "WorkId" => item.WorkId,
                "WorkType" => item.WorkTypeString,
                "Category" => CategoryLabels.GetValueOrDefault(item.Category, item.Category),
                "Action" => ActionLabel(item.Action),
                "NewName" => item.NewName,
                "Flag" => item.Result != "" ? $"{item.Flag} [{item.Result}]".TrimStart() : item.Flag,
                _ => "",
            };
        };
        _grid.CellValuePushed += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name == "Enabled")
            {
                _view[e.RowIndex].Enabled = e.Value is true;
            }
        };
        // チェック変更を即座にコミットする（実行直前の最後のクリックが未反映になる取りこぼし防止）
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
    }

    private static string ActionLabel(PlanAction action) => action switch
    {
        PlanAction.Stay => "そのまま",
        PlanAction.Rename => "リネーム",
        PlanAction.Move => "移動",
        PlanAction.MoveRename => "移動+リネーム",
        PlanAction.Skip => "スキップ",
        _ => action.ToString(),
    };

    private void BuildDestGridColumns()
    {
        _destGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", Visible = false });
        _destGrid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            Width = 80,
            ReadOnly = true,
            ImageLayout = DataGridViewImageCellLayout.Normal,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleLeft, NullValue = null },
        });
        _destGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Label", Width = 160, ReadOnly = true });
        _destGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = false });
        _destGrid.Columns.Add(new DataGridViewButtonColumn { Name = "Browse", Width = 40, Text = "...", UseColumnTextForButtonValue = true });
        _destGrid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _destGrid.Columns[e.ColumnIndex].Name != "Browse") return;
            using var dialog = new FolderBrowserDialog();
            var current = _destGrid.Rows[e.RowIndex].Cells["Path"].Value as string ?? "";
            if (Directory.Exists(current)) dialog.SelectedPath = current;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _destGrid.Rows[e.RowIndex].Cells["Path"].Value = dialog.SelectedPath;
            }
        };
    }

    /// <summary>ソート軸の変更に合わせて宛先グリッドの行を組み直す。入力済みパスは区分キー単位で引き継ぐ。</summary>
    private void RegenerateDestRows()
    {
        CollectDestGridToConfig();
        _destGrid.Rows.Clear();
        foreach (var (key, label, category, isAi, site) in DestKey.EnumerateRows(_chkSeparateSites.Checked, _chkSeparateAi.Checked))
        {
            var path = _config.Sort.DestinationMap.GetValueOrDefault(key, "");
            _destGrid.Rows.Add(key, DestIcons.Get(category, isAi, site), label, path, "...");
        }
    }

    /// <summary>グリッドの入力値をconfigのDestinationMapへ反映する（非表示区分の値は保持）。</summary>
    private void CollectDestGridToConfig()
    {
        foreach (DataGridViewRow row in _destGrid.Rows)
        {
            var key = row.Cells["Key"].Value as string;
            if (string.IsNullOrEmpty(key)) continue;
            _config.Sort.DestinationMap[key] = (row.Cells["Path"].Value as string ?? "").Trim();
        }
    }

    /// <summary>ベースフォルダ配下に区分名のサブフォルダを割り当てる（フォルダは実行時に自動作成）。</summary>
    private void BulkFillDestinations()
    {
        using var dialog = new FolderBrowserDialog { Description = "仕分け先のベースフォルダを選択（区分名のサブフォルダを割り当てます）" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        foreach (DataGridViewRow row in _destGrid.Rows)
        {
            var label = row.Cells["Label"].Value as string ?? "";
            row.Cells["Path"].Value = Path.Combine(dialog.SelectedPath, NameBuilder.Sanitize(label));
        }
    }

    private static void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog();
        if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    // ---------- 設定 ----------

    private void LoadConfigToUi()
    {
        var s = _config.Sort;
        _txtSource.Text = s.SourceFolder;
        _chkIncludeFiles.Checked = s.IncludeFiles;
        _chkRename.Checked = s.RenameIdOnly;
        _chkMoveUnknown.Checked = s.MoveUnknown;
        _numRate.Value = Math.Clamp((decimal)s.RateSeconds, _numRate.Minimum, _numRate.Maximum);
        _chkSeparateSites.Checked = s.SeparateSites;
        _chkSeparateAi.Checked = s.SeparateAi;
        RegenerateDestRows();
    }

    private void SaveUiToConfig()
    {
        var s = _config.Sort;
        s.SourceFolder = _txtSource.Text.Trim();
        s.IncludeFiles = _chkIncludeFiles.Checked;
        s.RenameIdOnly = _chkRename.Checked;
        s.MoveUnknown = _chkMoveUnknown.Checked;
        s.RateSeconds = (double)_numRate.Value;
        s.SeparateSites = _chkSeparateSites.Checked;
        s.SeparateAi = _chkSeparateAi.Checked;
        CollectDestGridToConfig();
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            SetStatus("設定の保存に失敗: " + ex.Message);
        }
    }

    private void ApplyTheme()
    {
        DarkTheme.Apply(this, _config.DarkMode);
        _btnTheme.Text = _config.DarkMode ? "☀" : "🌙";
    }

    // ---------- 処理 ----------

    private async Task ScanAndClassifyAsync()
    {
        SaveUiToConfig();
        var config = _config.Sort;
        if (!Directory.Exists(config.SourceFolder))
        {
            MessageBox.Show("仕分け元フォルダが存在しません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("スキャン中...");
            _items = await Task.Run(() => Planner.Scan(config));

            var classify = new ClassifyService(AppConfig.AppDataDirectory);
            var session = new SortSession(classify);
            var uniqueIds = _items.Where(i => i.IdKind != IdKind.None).Select(i => i.WorkId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var cachedCount = _items.Where(i => i.IdKind != IdKind.None)
                .Select(i => i.WorkId).Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(id => classify.TryGetCached(id, out _));
            _progress.Visible = true;
            _progress.Maximum = Math.Max(1, uniqueIds);
            session.ClassifyProgress += (done, total, id) => Invoke(() =>
            {
                _progress.Value = Math.Min(done, _progress.Maximum);
                SetStatus($"照会中 {done}/{total}: {id}（キャッシュ済み{cachedCount}件は即時）");
            });

            var transient = await Task.Run(() => session.ClassifyAllAsync(_items, config, _cts.Token));

            SetStatus("計画作成中...");
            await Task.Run(() => Planner.BuildPlan(_items, config));
            RefreshView();
            var summary = BuildSummary();
            SetStatus(transient > 0
                ? $"{summary} / 一時失敗{transient}件（再スキャンで再試行できます）"
                : summary);
            _btnExecute.Enabled = _items.Any(i => i.Action is PlanAction.Rename or PlanAction.Move or PlanAction.MoveRename);
        }
        catch (OperationCanceledException)
        {
            SetStatus("中断しました（照会済み分はキャッシュに保存済み。再スキャンで続きから再開します）");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("エラー: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _progress.Visible = false;
        }
    }

    private async Task ExecuteAsync()
    {
        var targets = _items.Where(i => i.Enabled && i.Action is PlanAction.Rename or PlanAction.Move or PlanAction.MoveRename).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("実行対象がありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetStatus("実行前チェック中...");
        var warnings = await Task.Run(() => Executor.PreflightCheck(_items));
        var message = $"{targets.Count}件を処理します。\n" +
                      $"移動: {targets.Count(t => t.Action is PlanAction.Move or PlanAction.MoveRename)}件 / " +
                      $"リネームのみ: {targets.Count(t => t.Action == PlanAction.Rename)}件";
        if (warnings.Count > 0)
        {
            message += "\n\n⚠ 警告:\n" + string.Join("\n", warnings);
        }
        message += "\n\n実行しますか？";
        if (MessageBox.Show(message, Text, MessageBoxButtons.OKCancel,
                warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
        {
            SetStatus("キャンセルしました");
            return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var logPath = Path.Combine(AppConfig.AppDataDirectory, "logs", $"run_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            var executor = new Executor();
            _progress.Visible = true;
            _progress.Maximum = targets.Count;
            executor.Progress += (done, total, item) => Invoke(() =>
            {
                _progress.Value = Math.Min(done, _progress.Maximum);
                SetStatus($"実行中 {done}/{total}: {item.Name}");
            });

            var (ok, skipped, errors) = await executor.ExecuteAsync(_items, logPath, _cts.Token);

            RefreshView();
            SetStatus($"完了: 成功{ok}件 / スキップ{skipped}件 / エラー{errors}件 / ログ: {logPath}");
            if (errors > 0)
            {
                MessageBox.Show($"エラーが{errors}件ありました。詳細はログを確認してください。\n{logPath}",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            RefreshView();
            SetStatus($"中断しました（処理済み分のログ: {logPath}）");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("エラー: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _progress.Visible = false;
        }
    }

    private string BuildSummary()
    {
        var move = _items.Count(i => i.Action is PlanAction.Move or PlanAction.MoveRename);
        var rename = _items.Count(i => i.Action == PlanAction.Rename);
        var skip = _items.Count(i => i.Action == PlanAction.Skip);
        var stay = _items.Count(i => i.Action == PlanAction.Stay);
        return $"計画完了: 全{_items.Count}件 → 移動{move} / リネームのみ{rename} / スキップ{skip} / そのまま{stay}";
    }

    private void RefreshView()
    {
        _view = _cmbFilter.SelectedIndex switch
        {
            1 => _items.Where(i => i.Action is PlanAction.Rename or PlanAction.Move or PlanAction.MoveRename).ToList(),
            2 => _items.Where(i => i.Action == PlanAction.Skip || i.Category == Category.Unknown).ToList(),
            3 => _items.Where(i => i.IdKind == IdKind.None).ToList(),
            _ => _items,
        };
        _grid.RowCount = 0;
        _grid.RowCount = _view.Count;
        _grid.Invalidate();
    }

    private void SetBusy(bool busy)
    {
        _btnScan.Enabled = !busy;
        _btnExecute.Enabled = !busy && _items.Any(i => i.Action is PlanAction.Rename or PlanAction.Move or PlanAction.MoveRename);
        _btnCancel.Enabled = busy;
    }

    private void SetStatus(string text) => _status.Text = text;
}
