using System.Diagnostics;

namespace MtsMerge;

internal sealed class MainForm : Form
{
    /// <summary>空き容量チェック時に必要サイズへ上乗せする余裕分。</summary>
    private const long SpaceMargin = 16L * 1024 * 1024;
    private const string MergeSuffix = "-merge";
    private static readonly string[] AllowedExtensions = [".mts", ".m2ts", ".ts", ".m2t"];

    private readonly List<InputFile> _files = [];

    private readonly ListView _list;
    private readonly Button _btnAdd, _btnRemove, _btnClear, _btnTop, _btnUp, _btnDown, _btnBottom, _btnSortName, _btnSortDate;
    private readonly Label _lblTotal;
    private readonly TextBox _txtDir, _txtName;
    private readonly Button _btnBrowse, _btnResetName;
    private readonly Label _lblFree;
    private readonly CheckBox _chkTimestamp, _chkOpenFolder;
    private readonly ProgressBar _progress;
    private readonly Label _lblStatus;
    private readonly Button _btnStart, _btnCancel;
    private readonly ToolTip _toolTip = new();

    private bool _dirEdited, _nameEdited, _suppressEditTracking;
    private CancellationTokenSource? _cts;
    private bool _closeAfterCancel;

    public MainForm(string[] initialPaths)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont ?? Font;
        Text = "MTS Merge";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 660);
        MinimumSize = new Size(760, 620);
        AllowDrop = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        // ---- 入力ファイル ----
        var inputGroup = new GroupBox { Text = "入力ファイル（追加ボタン または ドラッグ＆ドロップ）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var inputLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inputLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inputGroup.Controls.Add(inputLayout);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = true,
            AllowDrop = true,
        };
        // ListView の列幅は自動スケールされないので DPI に合わせて換算する
        _list.Columns.Add("#", LogicalToDeviceUnits(36), HorizontalAlignment.Right);
        _list.Columns.Add("ファイル名", LogicalToDeviceUnits(150));
        _list.Columns.Add("サイズ", LogicalToDeviceUnits(85), HorizontalAlignment.Right);
        _list.Columns.Add("更新日時", LogicalToDeviceUnits(135));
        _list.Columns.Add("形式", LogicalToDeviceUnits(85));
        _list.Columns.Add("フォルダ", LogicalToDeviceUnits(300));
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.KeyDown += List_KeyDown;
        _list.DragEnter += OnDragEnter;
        _list.DragDrop += OnDragDrop;
        inputLayout.Controls.Add(_list, 0, 0);

        var buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        _btnAdd = MakeButton("追加...", (_, _) => AddFromDialog());
        _btnRemove = MakeButton("削除", (_, _) => RemoveSelected());
        _btnClear = MakeButton("すべて削除", (_, _) => ClearAll());
        _btnTop = MakeButton("一番上へ", (_, _) => MoveSelectedToEdge(top: true));
        _btnUp = MakeButton("上へ ▲", (_, _) => MoveSelected(-1));
        _btnDown = MakeButton("下へ ▼", (_, _) => MoveSelected(+1));
        _btnBottom = MakeButton("一番下へ", (_, _) => MoveSelectedToEdge(top: false));
        _btnSortName = MakeButton("名前順", (_, _) => SortFiles(byDate: false));
        _btnSortDate = MakeButton("日時順", (_, _) => SortFiles(byDate: true));
        _btnClear.Margin = _btnClear.Margin with { Bottom = 14 };
        buttonPanel.Controls.AddRange([_btnAdd, _btnRemove, _btnClear, _btnTop, _btnUp, _btnDown, _btnBottom]);
        inputLayout.Controls.Add(buttonPanel, 1, 0);

        _toolTip.SetToolTip(_btnRemove, "選択したファイルをリストから外します（元ファイルは削除しません）\nDelete キー");
        _toolTip.SetToolTip(_btnUp, "Ctrl + ↑");
        _toolTip.SetToolTip(_btnDown, "Ctrl + ↓");
        _toolTip.SetToolTip(_btnSortName, "ファイル名の自然順（00002 → 00010）で並べ替えます");
        _toolTip.SetToolTip(_btnSortDate, "更新日時順で並べ替えます");

        // 下段: 左に並べ替えボタン、右下に合計サイズ
        var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sortPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var sortLabel = MakeLabel("並べ替え:");
        _btnSortName.MinimumSize = _btnSortDate.MinimumSize = new Size(80, 28);
        sortPanel.Controls.AddRange([sortLabel, _btnSortName, _btnSortDate]);
        bottomLayout.Controls.Add(sortPanel, 0, 0);
        _lblTotal = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Font = new Font(Font, FontStyle.Bold) };
        bottomLayout.Controls.Add(_lblTotal, 1, 0);
        inputLayout.Controls.Add(bottomLayout, 0, 1);
        inputLayout.SetColumnSpan(bottomLayout, 2);
        root.Controls.Add(inputGroup, 0, 0);

        // ---- 出力 ----
        var outputGroup = new GroupBox { Text = "出力", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
        var outputLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outputGroup.Controls.Add(outputLayout);

        _txtDir = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _txtDir.TextChanged += (_, _) => { if (!_suppressEditTracking) _dirEdited = _txtDir.Text.Length > 0; UpdateSummary(); };
        _btnBrowse = MakeButton("参照...", (_, _) => BrowseOutputDir());
        outputLayout.Controls.Add(MakeLabel("保存先フォルダ:"), 0, 0);
        outputLayout.Controls.Add(_txtDir, 1, 0);
        outputLayout.Controls.Add(_btnBrowse, 2, 0);

        _txtName = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _txtName.TextChanged += (_, _) => { if (!_suppressEditTracking) _nameEdited = _txtName.Text.Length > 0; UpdateSummary(); };
        _btnResetName = MakeButton("既定に戻す", (_, _) => ResetOutputDefaults());
        _toolTip.SetToolTip(_btnResetName, "保存先とファイル名を既定値（先頭ファイルと同じフォルダ・名前に「-merge」を付加）に戻します");
        outputLayout.Controls.Add(MakeLabel("ファイル名:"), 0, 1);
        outputLayout.Controls.Add(_txtName, 1, 1);
        outputLayout.Controls.Add(_btnResetName, 2, 1);

        _lblFree = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 4, 3, 4) };
        outputLayout.Controls.Add(_lblFree, 0, 2);
        outputLayout.SetColumnSpan(_lblFree, 3);

        var optionPanel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left, WrapContents = false };
        _chkTimestamp = new CheckBox { Text = "出力ファイルの日時を先頭ファイルに合わせる", AutoSize = true, Checked = true };
        _chkOpenFolder = new CheckBox { Text = "完了後に保存先フォルダを開く", AutoSize = true, Checked = true };
        optionPanel.Controls.AddRange([_chkTimestamp, _chkOpenFolder]);
        outputLayout.Controls.Add(optionPanel, 0, 3);
        outputLayout.SetColumnSpan(optionPanel, 3);
        root.Controls.Add(outputGroup, 0, 1);

        // ---- 進捗 ----
        var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
        _progress = new ProgressBar { Dock = DockStyle.Fill, Height = 22, Minimum = 0, Maximum = 1000 };
        _lblStatus = new Label { AutoSize = true, Text = "MTS ファイルを追加してください。", Margin = new Padding(0, 4, 0, 0) };
        progressLayout.Controls.Add(_progress, 0, 0);
        progressLayout.Controls.Add(_lblStatus, 0, 1);
        root.Controls.Add(progressLayout, 0, 2);

        // ---- 実行ボタン ----
        var actionPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        _btnStart = MakeButton("結合開始", async (_, _) => await StartMergeAsync());
        _btnStart.Font = new Font(Font, FontStyle.Bold);
        _btnStart.MinimumSize = new Size(130, 34);
        _btnCancel = MakeButton("キャンセル", (_, _) => _cts?.Cancel());
        _btnCancel.MinimumSize = new Size(110, 34);
        _btnCancel.Enabled = false;
        actionPanel.Controls.AddRange([_btnStart, _btnCancel]);
        root.Controls.Add(actionPanel, 0, 3);
        AcceptButton = null;

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        ResumeLayout(true);

        if (initialPaths.Length > 0) AddPaths(initialPaths);
        RefreshList();
    }

    // ================================================================
    // UI 部品
    // ================================================================

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 28), UseVisualStyleBackColor = true };
        b.Click += onClick;
        return b;
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };

    // ================================================================
    // ファイルの追加・削除・並べ替え
    // ================================================================

    private void AddFromDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "結合する MTS ファイルを選択",
            Filter = "MPEG-TS ファイル (*.mts;*.m2ts;*.ts;*.m2t)|*.mts;*.m2ts;*.ts;*.m2t|すべてのファイル (*.*)|*.*",
            Multiselect = true,
        };
        if (_files.Count > 0) dlg.InitialDirectory = _files[^1].Directory;
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = !IsBusy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (IsBusy) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var candidates = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                // フォルダ（SD カードの AVCHD フォルダなど）がドロップされたら中の対象ファイルを集める
                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(path, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                    }).Where(IsAllowedExtension));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            else
            {
                candidates.Add(path);
            }
        }

        // 一度に追加されたファイルは名前の自然順に並べる（ドロップ順は不定のため）
        candidates.Sort(NaturalStringComparer.Instance);

        int added = 0, duplicate = 0, unsupported = 0, failed = 0;
        foreach (string path in candidates)
        {
            if (!IsAllowedExtension(path)) { unsupported++; continue; }
            if (_files.Any(f => FileIdentity.IsSameFile(f.Path, path))) { duplicate++; continue; }
            try
            {
                _files.Add(InputFile.Load(path));
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failed++;
            }
        }

        RefreshList(selectRange: added > 0 ? (_files.Count - added, added) : null);

        var notes = new List<string>();
        if (duplicate > 0) notes.Add($"重複のためスキップ: {duplicate} 件");
        if (unsupported > 0) notes.Add($"対象外の拡張子のためスキップ: {unsupported} 件（対応: {string.Join(", ", AllowedExtensions)}）");
        if (failed > 0) notes.Add($"読み込めなかったファイル: {failed} 件");
        if (notes.Count > 0)
            MessageBox.Show(this, string.Join("\n", notes), "ファイルの追加", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool IsAllowedExtension(string path) =>
        AllowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private int[] SelectedIndices() => _list.SelectedIndices.Cast<int>().Order().ToArray();

    private void RemoveSelected()
    {
        var selected = SelectedIndices();
        if (selected.Length == 0) return;
        foreach (int i in selected.Reverse()) _files.RemoveAt(i);
        int next = Math.Min(selected[0], _files.Count - 1);
        RefreshList(selectRange: next >= 0 ? (next, 1) : null);
    }

    private void ClearAll()
    {
        if (_files.Count == 0) return;
        if (MessageBox.Show(this, "リストからすべてのファイルを外しますか？\n（元のファイルは削除されません）", "すべて削除",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _files.Clear();
        RefreshList();
    }

    private void MoveSelected(int delta)
    {
        var selected = new bool[_files.Count];
        foreach (int i in SelectedIndices()) selected[i] = true;

        if (delta < 0)
        {
            for (int i = 1; i < _files.Count; i++)
                if (selected[i] && !selected[i - 1]) Swap(i, i - 1);
        }
        else
        {
            for (int i = _files.Count - 2; i >= 0; i--)
                if (selected[i] && !selected[i + 1]) Swap(i, i + 1);
        }
        RefreshList(selection: selected);

        void Swap(int a, int b)
        {
            (_files[a], _files[b]) = (_files[b], _files[a]);
            (selected[a], selected[b]) = (selected[b], selected[a]);
        }
    }

    private void MoveSelectedToEdge(bool top)
    {
        var selectedSet = SelectedIndices().ToHashSet();
        if (selectedSet.Count == 0) return;
        var picked = _files.Where((_, i) => selectedSet.Contains(i)).ToList();
        var rest = _files.Where((_, i) => !selectedSet.Contains(i)).ToList();
        _files.Clear();
        _files.AddRange(top ? picked.Concat(rest) : rest.Concat(picked));
        RefreshList(selectRange: (top ? 0 : rest.Count, picked.Count));
    }

    private void SortFiles(bool byDate)
    {
        var selectedFiles = SelectedIndices().Select(i => _files[i]).ToHashSet();
        var sorted = byDate
            ? _files.OrderBy(f => f.LastWriteTime).ThenBy(f => f.Path, NaturalStringComparer.Instance).ToList()
            : _files.OrderBy(f => f.Path, NaturalStringComparer.Instance).ToList();
        _files.Clear();
        _files.AddRange(sorted);
        RefreshList(selection: _files.Select(selectedFiles.Contains).ToArray());
    }

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsBusy) return;
        switch (e.KeyCode)
        {
            case Keys.Delete:
                RemoveSelected();
                e.Handled = true;
                break;
            case Keys.Up when e.Control:
                MoveSelected(-1);
                e.Handled = true;
                break;
            case Keys.Down when e.Control:
                MoveSelected(+1);
                e.Handled = true;
                break;
            case Keys.A when e.Control:
                foreach (ListViewItem item in _list.Items) item.Selected = true;
                e.Handled = true;
                break;
        }
    }

    private void RefreshList(bool[]? selection = null, (int Start, int Count)? selectRange = null)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        for (int i = 0; i < _files.Count; i++)
        {
            var f = _files[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(f.FileName);
            item.SubItems.Add(DiskInfo.FormatBytes(f.Size));
            item.SubItems.Add(f.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
            item.SubItems.Add(InputFile.FormatName(f.Format));
            item.SubItems.Add(f.Directory);
            item.ToolTipText = $"{f.Path}\n{f.Size:N0} バイト";
            if (f.Format == TsFormat.Unknown) item.ForeColor = Color.Firebrick;

            if (selection is not null && i < selection.Length) item.Selected = selection[i];
            if (selectRange is { } r && i >= r.Start && i < r.Start + r.Count) item.Selected = true;
            _list.Items.Add(item);
        }
        _list.ShowItemToolTips = true;
        _list.EndUpdate();

        if (_list.SelectedIndices.Count > 0)
        {
            int first = _list.SelectedIndices[0];
            _list.EnsureVisible(first);
            _list.FocusedItem = _list.Items[first];
        }

        ApplyOutputDefaults();
        UpdateSummary();
        UpdateButtons();
    }

    // ================================================================
    // 出力先
    // ================================================================

    private void BrowseOutputDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "保存先フォルダを選択", UseDescriptionForTitle = true };
        if (Directory.Exists(_txtDir.Text)) dlg.InitialDirectory = _txtDir.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtDir.Text = dlg.SelectedPath;
            _dirEdited = true;
            // 既定のファイル名は保存先フォルダ内での重複を避けて採番し直す
            ApplyOutputDefaults();
        }
    }

    private void ResetOutputDefaults()
    {
        _dirEdited = false;
        _nameEdited = false;
        ApplyOutputDefaults();
    }

    /// <summary>ユーザーが手入力していない項目だけ既定値で更新する。</summary>
    private void ApplyOutputDefaults()
    {
        if (_files.Count == 0) return;
        var first = _files[0];
        _suppressEditTracking = true;
        try
        {
            if (!_dirEdited) _txtDir.Text = first.Directory;
            if (!_nameEdited) _txtName.Text = MakeDefaultName(first, _txtDir.Text);
        }
        finally
        {
            _suppressEditTracking = false;
        }
        UpdateSummary();
    }

    /// <summary>「先頭ファイル名-merge.拡張子」。既存ファイルや入力と衝突する場合は (2), (3)... を付ける。</summary>
    private string MakeDefaultName(InputFile first, string directory)
    {
        string stem = Path.GetFileNameWithoutExtension(first.FileName) + MergeSuffix;
        string ext = Path.GetExtension(first.FileName);
        for (int n = 1; ; n++)
        {
            string name = n == 1 ? stem + ext : $"{stem} ({n}){ext}";
            if (!IsValidDirectory(directory)) return name;
            string full = Path.Combine(directory, name);
            if (!File.Exists(full) && !_files.Any(f => FileIdentity.IsSameFile(f.Path, full))) return name;
        }
    }

    private static bool IsValidDirectory(string directory)
    {
        try { return Path.IsPathFullyQualified(directory) && Directory.Exists(directory); }
        catch (ArgumentException) { return false; }
    }

    /// <summary>入力されたファイル名を検証し、拡張子がなければ先頭ファイルの拡張子を補う。</summary>
    private string? ResolveOutputPath(out string? error)
    {
        error = null;
        string dir = _txtDir.Text.Trim();
        string name = _txtName.Text.Trim();

        if (dir.Length == 0) { error = "保存先フォルダを指定してください。"; return null; }
        if (name.Length == 0) { error = "ファイル名を指定してください。"; return null; }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "ファイル名に使用できない文字が含まれています（\\ / : * ? \" < > | など）。";
            return null;
        }
        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            error = "ファイル名の末尾にピリオドや空白は使用できません。";
            return null;
        }
        if (!Path.HasExtension(name) && _files.Count > 0) name += Path.GetExtension(_files[0].FileName);

        try
        {
            if (!Path.IsPathFullyQualified(dir)) { error = "保存先フォルダはフルパスで指定してください。"; return null; }
            return Path.GetFullPath(Path.Combine(dir, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "保存先のパスが不正です。";
            return null;
        }
    }

    // ================================================================
    // 合計サイズ・空き容量の表示
    // ================================================================

    private long TotalSize => _files.Sum(f => f.Size);

    private record SpaceCheck(long? Free, string? FileSystem, long? MaxFileSize, bool Enough, bool TooLargeForFs);

    private SpaceCheck CheckSpace(string directory, long required)
    {
        long? free = DiskInfo.GetFreeBytes(directory);
        string? fs = DiskInfo.GetFileSystemName(directory);
        long? max = DiskInfo.GetMaxFileSize(fs);
        bool enough = free is { } f && f >= required + SpaceMargin;
        bool tooLarge = max is { } m && required > m;
        return new SpaceCheck(free, fs, max, enough, tooLarge);
    }

    private void UpdateSummary(bool updateStatus = true)
    {
        long total = TotalSize;
        _lblTotal.Text = $"合計: {_files.Count} ファイル / {DiskInfo.FormatBytes(total)}（{total:N0} バイト）";

        string dir = _txtDir.Text.Trim();
        if (_files.Count == 0 || dir.Length == 0)
        {
            _lblFree.Text = "";
        }
        else if (!IsValidDirectory(dir))
        {
            _lblFree.Text = "保存先フォルダが見つかりません。";
            _lblFree.ForeColor = Color.Firebrick;
        }
        else
        {
            var space = CheckSpace(dir, total);
            string fsText = space.FileSystem is null ? "" : $" [{space.FileSystem}]";
            string freeText = space.Free is { } free ? DiskInfo.FormatBytes(free) : "取得できません";
            string text = $"保存先の空き容量: {freeText}{fsText}　/　必要な容量: {DiskInfo.FormatBytes(total)}";
            if (space.TooLargeForFs)
                text += $"　※ {space.FileSystem} は 4GB 以上のファイルを保存できません";
            else if (!space.Enough)
                text += "　※ 空き容量が不足しています";
            _lblFree.Text = text;
            _lblFree.ForeColor = space.Enough && !space.TooLargeForFs ? SystemColors.ControlText : Color.Firebrick;
        }

        if (updateStatus && !IsBusy)
        {
            _lblStatus.Text = _files.Count switch
            {
                0 => "MTS ファイルを追加してください。",
                1 => "結合するには 2 つ以上のファイルを追加してください。",
                _ => "並び順と出力先を確認して「結合開始」を押してください。",
            };
        }
        UpdateButtons();
    }

    private bool IsBusy => _cts is not null;

    private void UpdateButtons()
    {
        bool busy = IsBusy;
        int selected = _list.SelectedIndices.Count;
        bool hasFiles = _files.Count > 0;

        _btnAdd.Enabled = !busy;
        _btnRemove.Enabled = !busy && selected > 0;
        _btnClear.Enabled = !busy && hasFiles;
        _btnTop.Enabled = _btnUp.Enabled = !busy && selected > 0 && !SelectedAreAtEdge(top: true);
        _btnBottom.Enabled = _btnDown.Enabled = !busy && selected > 0 && !SelectedAreAtEdge(top: false);
        _btnSortName.Enabled = _btnSortDate.Enabled = !busy && _files.Count > 1;
        _txtDir.Enabled = _txtName.Enabled = _btnBrowse.Enabled = _btnResetName.Enabled = !busy;
        _chkTimestamp.Enabled = _chkOpenFolder.Enabled = !busy;
        _btnStart.Enabled = !busy && _files.Count >= 2;
        _btnCancel.Enabled = busy;
    }

    /// <summary>選択項目がすでに上端（または下端）に詰まっているか。</summary>
    private bool SelectedAreAtEdge(bool top)
    {
        var sel = SelectedIndices();
        int n = sel.Length;
        return top ? sel[^1] == n - 1 : sel[0] == _files.Count - n;
    }

    // ================================================================
    // 結合処理
    // ================================================================

    private async Task StartMergeAsync()
    {
        if (IsBusy) return;
        if (!ValidateBeforeMerge(out string output, out bool overwrite)) return;

        var inputs = _files.ToList();
        var first = inputs[0];
        long total = inputs.Sum(f => f.Size);

        _cts = new CancellationTokenSource();
        UpdateButtons();
        _list.AllowDrop = AllowDrop = false;

        var sw = Stopwatch.StartNew();
        // Progress<T> の通知は非同期に届くため、終了後に遅れて届いた通知で表示を上書きしない
        bool finished = false;
        var progress = new Progress<MergeProgress>(p => { if (!finished) ShowProgress(p, sw.Elapsed); });
        _progress.Value = 0;
        _lblStatus.Text = "結合中...";
        var previousState = NativeMethods.SetThreadExecutionState(
            NativeMethods.ExecutionState.Continuous | NativeMethods.ExecutionState.SystemRequired);

        try
        {
            await MergeEngine.MergeAsync(
                inputs, output, overwrite,
                _chkTimestamp.Checked ? first.CreationTime : null,
                _chkTimestamp.Checked ? first.LastWriteTime : null,
                progress, _cts.Token);
            finished = true;

            _progress.Value = _progress.Maximum;
            _lblStatus.Text = $"完了しました（{sw.Elapsed:hh\\:mm\\:ss}）: {output}";
            if (_chkOpenFolder.Checked)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{output}\"") { UseShellExecute = true });
            else
                MessageBox.Show(this, $"結合が完了しました。\n\n{output}", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // 続けて別の結合をするときに上書きしないよう、既定のファイル名を採番し直す
            ApplyOutputDefaults();
        }
        catch (OperationCanceledException)
        {
            finished = true;
            _progress.Value = 0;
            _lblStatus.Text = "キャンセルしました。出力途中のファイルは削除しました。";
        }
        catch (Exception ex)
        {
            finished = true;
            _progress.Value = 0;
            _lblStatus.Text = "エラーのため中止しました。出力途中のファイルは削除しました。";
            MessageBox.Show(this, $"結合に失敗しました。元のファイルは変更されていません。\n\n{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            NativeMethods.SetThreadExecutionState(previousState == 0 ? NativeMethods.ExecutionState.Continuous : previousState);
            _cts.Dispose();
            _cts = null;
            _list.AllowDrop = AllowDrop = true;
            UpdateSummary(updateStatus: false);
            if (_closeAfterCancel) Close();
        }
    }

    private void ShowProgress(MergeProgress p, TimeSpan elapsed)
    {
        if (p.BytesTotal <= 0) return;
        _progress.Value = (int)Math.Clamp(p.BytesDone * _progress.Maximum / p.BytesTotal, 0, _progress.Maximum);
        double percent = 100.0 * p.BytesDone / p.BytesTotal;
        double speed = elapsed.TotalSeconds > 0 ? p.BytesDone / elapsed.TotalSeconds : 0;
        string remaining = speed > 0
            ? TimeSpan.FromSeconds((p.BytesTotal - p.BytesDone) / speed).ToString(@"hh\:mm\:ss")
            : "--:--:--";
        _lblStatus.Text =
            $"結合中... {percent:0.0}%　({p.FileIndex + 1}/{p.FileCount} ファイル目)　" +
            $"{DiskInfo.FormatBytes(p.BytesDone)} / {DiskInfo.FormatBytes(p.BytesTotal)}　" +
            $"{DiskInfo.FormatBytes((long)speed)}/秒　残り {remaining}";
    }

    private bool ValidateBeforeMerge(out string output, out bool overwrite)
    {
        output = "";
        overwrite = false;

        if (_files.Count < 2)
            return Fail("結合するには 2 つ以上のファイルを追加してください。");

        // 追加後にファイルが消えたり変わったりしていないか確認
        var missing = _files.Where(f => !f.Refresh()).Select(f => f.Path).ToList();
        if (missing.Count > 0)
        {
            RefreshList();
            return Fail("次のファイルが見つかりません:\n" + string.Join("\n", missing));
        }
        RefreshList(selection: Enumerable.Range(0, _list.Items.Count).Select(i => _list.Items[i].Selected).ToArray());

        // パケット形式のチェック
        var formats = _files.Select(f => f.Format).Where(f => f != TsFormat.Unknown).Distinct().ToList();
        if (formats.Count > 1)
            return Fail("188 バイト形式 (TS) と 192 バイト形式 (M2TS/MTS) のファイルが混在しています。\n形式の異なるファイルは結合できません。");
        if (_files.Any(f => f.Format == TsFormat.Unknown))
        {
            if (MessageBox.Show(this,
                    "MPEG-TS 形式として認識できないファイルが含まれています（赤字の行）。\n" +
                    "結合結果が正しく再生できない可能性があります。続行しますか？",
                    "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;
        }

        // 出力先
        string? path = ResolveOutputPath(out string? error);
        if (path is null) return Fail(error!);
        string dir = Path.GetDirectoryName(path)!;

        if (!Directory.Exists(dir))
        {
            if (MessageBox.Show(this, $"保存先フォルダが存在しません。作成しますか？\n\n{dir}", "確認",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail($"保存先フォルダを作成できませんでした。\n\n{ex.Message}");
            }
        }

        // 元ファイルを絶対に上書きしない
        var same = _files.FirstOrDefault(f => FileIdentity.IsSameFile(f.Path, path));
        if (same is not null)
            return Fail($"出力ファイルが入力ファイルと同じです。別の名前を指定してください。\n\n{same.Path}");

        if (Directory.Exists(path))
            return Fail($"同じ名前のフォルダが存在します。別の名前を指定してください。\n\n{path}");

        if (File.Exists(path))
        {
            if (MessageBox.Show(this, $"出力ファイルは既に存在します。上書きしますか？\n\n{path}", "上書きの確認",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return false;
            overwrite = true;
        }

        // 容量チェック（不足時は実行しない）
        long total = TotalSize;
        var space = CheckSpace(dir, total);
        if (space.TooLargeForFs)
            return Fail($"保存先のファイルシステム ({space.FileSystem}) は 4GB 以上のファイルを保存できません。\n" +
                        $"結合後のサイズ: {DiskInfo.FormatBytes(total)}\n\nNTFS または exFAT のドライブを保存先に指定してください。");
        if (space.Free is null)
            return Fail("保存先の空き容量を取得できませんでした。別の保存先を指定してください。");
        if (!space.Enough)
            return Fail($"保存先の空き容量が不足しているため実行できません。\n\n" +
                        $"必要な容量: {DiskInfo.FormatBytes(total + SpaceMargin)}\n空き容量: {DiskInfo.FormatBytes(space.Free.Value)}");

        output = path;
        return true;

        bool Fail(string message)
        {
            MessageBox.Show(this, message, "実行できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (IsBusy)
        {
            e.Cancel = true;
            if (MessageBox.Show(this, "結合処理中です。中止して終了しますか？\n（出力途中のファイルは削除されます）", "確認",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                _closeAfterCancel = true;
                _cts?.Cancel();
            }
            return;
        }
        base.OnFormClosing(e);
    }
}
