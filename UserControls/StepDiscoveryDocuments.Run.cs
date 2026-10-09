using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Discovery Documents run wiring (08-UI-SPEC S1): Generate Documents / Cancel Generation, progress and outcome lines,
/// Regenerate..., Save Again, Open in Word and Open Discovery Folder.
/// </summary>
public partial class StepDiscoveryDocuments
{
    /// <summary>
    /// Builds the run's gateway; null uses WizardState.CreateGateway (tracked, so the cost ticker follows the run). Test seam.
    /// </summary>
    internal Func<ILlmGateway>? GatewayFactory;

    /// <summary>
    /// Builds the generator over the run's gateway. Test seam (tests use MaxParallel = 1 for a deterministic call order).
    /// </summary>
    internal Func<ILlmGateway, DiscoveryGenerator> GeneratorFactory = llm => new DiscoveryGenerator(llm);

    /// <summary>
    /// Shows the regenerate dialog; null uses ShowDialog(FindForm()). Test seam.
    /// </summary>
    internal Func<RegenerateDiscoveryDialog, DialogResult>? ShowDialogOverride;

    /// <summary>
    /// Opens a validated .docx through the shell. Test seam.
    /// </summary>
    internal Action<string> OpenFile = path =>
    {
        // Only DiscoveryStore.DocxPath results reach this: a validated name inside Discovery\ ending in .docx (T-08-50)
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    };

    /// <summary>
    /// Opens a folder in Explorer. Test seam.
    /// </summary>
    internal Action<string> OpenFolder = path =>
    {
        // Never shell-execute a path read from a file: explorer.exe with the quoted folder path (ASVS V12, T-08-51)
        using var process = Process.Start("explorer.exe", "\"" + path + "\"");
    };

    // Documents being written in the current run: key -> (part, part count); painted over the model's rows
    private readonly Dictionary<string, (int Part, int PartCount)> _writing = new(StringComparer.Ordinal);

    // Incremented per run: progress posted by an earlier run is dropped
    private int _runVersion;

    partial void WireRunHandlers()
    {
        _btnRun.Click += async (s, e) => await RunClickAsync();
        _btnRegenerate.Click += async (s, e) => await RegenerateSelectedAsync();
        _btnSaveAgain.Click += async (s, e) => await SaveAgainSelectedAsync();
        _btnOpenWord.Click += (s, e) => OpenSelectedInWord();
        _btnOpenFolder.Click += (s, e) => OpenDiscoveryFolder();
        // Double-click or Enter on a row = Open in Word when the file exists (UI-SPEC S1)
        _gridDocuments.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0)
                OpenSelectedInWord();
        };
        _gridDocuments.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            // Enter would otherwise move the selection down a row
            e.Handled = true;
            e.SuppressKeyPress = true;
            OpenSelectedInWord();
        };
    }

    // ----- Row actions -----

    // The selected row's saved record; null when nothing is selected or the row has no saved version
    private DiscoveryDocument? SelectedDocument()
    {
        var row = SelectedRow();
        return row == null ? null : _set.Find(row.Key);
    }

    /// <summary>
    /// Regenerate...: only through RegenerateDiscoveryDialog (the confirmation names the file it replaces and the estimate);
    /// OK runs this one document with the guidance and replaces only it (D-11, D-27).
    /// </summary>
    internal async Task RegenerateSelectedAsync()
    {
        if (_state == null || _isGenerating || !KeyReady())
            return;

        var row = SelectedRow();
        var document = SelectedDocument();
        if (row == null || document == null || row.State is DiscoveryRowState.NotGenerated or DiscoveryRowState.Writing)
            return;

        var item = DiscoveryWorklist.ForDocument(Draft, document, Settings, _corpus, _set);
        if (item == null)
        {
            SetStatus(DiscoveryUiText.RegenerateUnplannable(row.ShortTitle), Theme.Danger);
            return;
        }

        var isDeposition = item.Kind == DiscoveryKind.Deposition;
        var settingsLine = isDeposition
            ? DiscoveryUiText.SettingsLineDeposition(item.TestimonyPages,
                item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : DiscoveryUiText.SettingsLineWritten(item.RequestCount, _model.PartyChoice.Propounding?.Name ?? string.Empty,
                _model.PartyChoice.Responding?.Name ?? string.Empty, _model.IsMemo);
        var estimate = isDeposition
            ? GenerationCostEstimator.PriceDiscoveryDeposition(item.ChunkCount, item.TestimonyPages, _state.StoryModelConfig)
            : GenerationCostEstimator.PriceDiscoveryWrittenSet(item.CallCount, item.RequestCount, _state.StoryModelConfig);
        var path = DiscoveryStore.DocxPath(Draft.FolderPath, document.FileName);
        var fileOpen = path != null && FileLockDetector.IsOpenElsewhere(path);

        string guidance;
        using (var dialog = new RegenerateDiscoveryDialog(row.ShortTitle, document.FileName, isDeposition, settingsLine, fileOpen,
                   estimate))
        {
            var answer = ShowDialogOverride != null ? ShowDialogOverride(dialog) : dialog.ShowDialog(FindForm());
            if (answer != DialogResult.OK || IsDisposed)
                return;
            guidance = dialog.Guidance;
        }

        await RunDocumentsAsync(new[] { item }, new Dictionary<string, string>(StringComparer.Ordinal) { [item.Key] = guidance });
    }

    /// <summary>
    /// Save Again: rewrites a Not saved / File missing .docx from the stored content, with no model call and no cost (U47).
    /// </summary>
    internal async Task SaveAgainSelectedAsync()
    {
        if (_state == null || _isGenerating)
            return;

        var row = SelectedRow();
        if (row == null || _set.Find(row.Key) == null)
            return;

        var result = await DiscoveryGenerator.SaveAgainAsync(Draft, row.Key);
        if (IsDisposed)
            return;

        _set = LoadSet();
        RefreshModel();
        var fileName = result.FileName.Length > 0 ? result.FileName : row.FileName;
        if (result.Saved)
            SetStatus(DiscoveryUiText.Saved(fileName), Theme.Success);
        else if (result.StillOpen)
            SetStatus(DiscoveryUiText.StillOpen(fileName), Theme.Warning);
        else
            SetStatus(result.Error, Theme.Danger);
    }

    /// <summary>
    /// Open in Word: only a validated path inside Discovery\ that exists is opened (T-08-50).
    /// </summary>
    internal void OpenSelectedInWord()
    {
        if (_state == null || _isGenerating)
            return;

        var row = SelectedRow();
        if (row == null || string.IsNullOrWhiteSpace(Draft.FolderPath))
            return;

        var path = DiscoveryStore.DocxPath(Draft.FolderPath, row.FileName);
        if (path == null || !FileExists(path))
            return;

        try
        {
            OpenFile(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus(DiscoveryUiText.OpenFailed(row.FileName, ex.Message), Theme.Danger);
        }
    }

    /// <summary>
    /// Open Discovery Folder: the case folder plus the constant folder name, never a path read from a file (T-08-51).
    /// </summary>
    internal void OpenDiscoveryFolder()
    {
        if (_state == null || _isGenerating || string.IsNullOrWhiteSpace(Draft.FolderPath))
            return;
        if (!DiscoveryStore.HasDocuments(Draft.FolderPath))
        {
            PaintHeaderButton();
            return;
        }

        try
        {
            OpenFolder(Path.GetFullPath(DiscoveryStore.FolderPath(Draft.FolderPath)));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus(DiscoveryUiText.OpenFolderFailed(ex.Message), Theme.Danger);
        }
    }

    /// <summary>
    /// Run button: Generate Documents when idle, Cancel Generation (after C-D1) while running.
    /// </summary>
    internal async Task RunClickAsync()
    {
        if (_state == null)
            return;

        if (_isGenerating)
        {
            var (title, text) = DiscoveryUiText.CD1CancelGeneration();
            if (Confirm(title, text, MessageBoxIcon.Question) && _isGenerating)
                StopRun();
            return;
        }

        // A field typed within the debounce window counts; the model then lists what to write
        CommitPendingInput();
        if (!_model.HasGuide || !_model.CanGenerate(KeyReady(), FieldsValid()))
            return;

        // Writes only Not generated and Failed rows; nothing regenerates silently (D-11, D-27). No confirmation: the cost
        // is on screen (UI-SPEC S1 Run button)
        await RunDocumentsAsync(_model.ToWrite.ToList(), null);
    }

    /// <summary>
    /// Writes <paramref name="items"/> through DiscoveryGenerator on the pool; each finished document is saved before its
    /// row changes. Never throws: the outcome goes to the status line.
    /// </summary>
    private async Task RunDocumentsAsync(IReadOnlyList<DiscoveryWorkItem> items, IReadOnlyDictionary<string, string>? guidance)
    {
        if (_isGenerating || items.Count == 0)
            return;

        ILlmGateway gateway;
        try
        {
            gateway = GatewayFactory?.Invoke() ?? _state.CreateGateway();
        }
        catch (InvalidOperationException ex)
        {
            // No model or key for the provider: nothing was started or billed
            SetStatus(DiscoveryUiText.StatusFatal(ex.Message), Theme.Danger);
            return;
        }

        // Progress is marshalled with BeginInvoke, which needs a window handle on this (UI) thread
        _ = Handle;

        var version = ++_runVersion;
        // The grid's short titles ("Deposition of {name}"); the work item's title is the document caption
        var rowTitles = _model.Rows.GroupBy(r => r.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ShortTitle, StringComparer.Ordinal);
        var titles = items.GroupBy(i => i.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => rowTitles.TryGetValue(g.Key, out var t) ? t : g.First().ShortTitle,
                StringComparer.Ordinal);
        _writing.Clear();
        _progress.Maximum = Math.Max(1, items.Count);
        _progress.Value = 0;

        var cts = new CancellationTokenSource();
        EnterRunState(cts);
        SetStatus(string.Empty, Theme.TextMuted);

        var progress = new RunProgress(p => PostProgress(version, titles, p));
        var draft = Draft;
        var corpus = _corpus;
        var token = cts.Token;
        DiscoveryRunResult? result = null;
        string? error = null;
        var cancelled = false;
        try
        {
            var generator = GeneratorFactory(gateway);
            result = await Task.Run(() => generator.RunAsync(draft, corpus, items, guidance, progress, token));
        }
        catch (OperationCanceledException)
        {
            // The generator reports cancels in its result; a cancel before it started leaves only finished documents
            cancelled = true;
        }
        catch (Exception ex)
        {
            // UI boundary: the generator never throws by contract, so this is a defect; finished documents are saved
            error = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
        }
        finally
        {
            cts.Dispose();
            _writing.Clear();
            ExitRunState();
        }

        if (IsDisposed)
            return;

        _set = LoadSet();
        RefreshModel();

        if (error != null)
            SetStatus(DiscoveryUiText.StatusFatal(error), Theme.Danger);
        else if (result == null)
            SetStatus(DiscoveryUiText.StatusCancelled(0, items.Count), Theme.Warning);
        else
            ShowOutcome(result, cancelled);
    }

    // Outcome precedence: fatal > cancelled > problems > clean (UI-SPEC S1 status line)
    private void ShowOutcome(DiscoveryRunResult result, bool cancelled)
    {
        if (result.FatalMessage.Length > 0)
            SetStatus(DiscoveryUiText.StatusFatal(result.FatalMessage), Theme.Danger);
        else if (result.Cancelled || cancelled)
            SetStatus(DiscoveryUiText.StatusCancelled(result.Saved, result.Total), Theme.Warning);
        else if (result.Warnings > 0 || result.Failed > 0 || result.NotSaved > 0)
            SetStatus(DiscoveryUiText.StatusDoneWithProblems(result.Warnings, result.Failed, result.NotSaved), Theme.Warning);
        else
            SetStatus(DiscoveryUiText.StatusDone(result.Saved), Theme.Success);
    }

    // Called on the pool: hands the report to the UI thread without blocking the writer
    private void PostProgress(int version, IReadOnlyDictionary<string, string> titles, DiscoveryProgress p)
    {
        if (IsDisposed)
            return;

        try
        {
            if (InvokeRequired)
                BeginInvoke(() => ApplyProgress(version, titles, p));
            else
                ApplyProgress(version, titles, p);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The step closed while the run unwinds; the documents are saved by the generator either way
        }
    }

    private void ApplyProgress(int version, IReadOnlyDictionary<string, string> titles, DiscoveryProgress p)
    {
        // A report queued by an earlier run, or after this run ended, never repaints the rows
        if (IsDisposed || !_isGenerating || version != _runVersion)
            return;

        _progress.Value = Math.Min(_progress.Maximum, Math.Max(0, p.Finished));
        if (p.Done)
        {
            // The record is saved: the row takes its outcome from the sidecar
            _writing.Remove(p.Key);
            _set = LoadSet();
            _model = BuildModel();
            PaintRunRows();
            return;
        }

        _writing[p.Key] = (p.Part, p.PartCount);
        PaintRunRows();
        // With parallel documents the line names the most recently reported one (UI-SPEC S1)
        var title = titles.TryGetValue(p.Key, out var t) ? t : p.Key;
        if (!(_cts?.IsCancellationRequested ?? false))
            SetStatus(DiscoveryUiText.StatusRunning(p.Index, p.Total, title, p.Part, p.PartCount), Theme.Accent);
    }

    // The model's rows with the documents being written shown as Writing...
    private void PaintRunRows()
    {
        foreach (var row in _model.Rows)
        {
            if (!_writing.TryGetValue(row.Key, out var part))
                continue;
            row.State = DiscoveryRowState.Writing;
            row.Part = part.Part;
            row.PartCount = part.PartCount;
        }
        PaintDocuments();
    }

    /// <summary>
    /// Synchronous IProgress: the report is posted to the UI thread by the callback itself (Progress&lt;T&gt; would capture
    /// the pool context inside Task.Run).
    /// </summary>
    private sealed class RunProgress : IProgress<DiscoveryProgress>
    {
        private readonly Action<DiscoveryProgress> _report;

        public RunProgress(Action<DiscoveryProgress> report)
        {
            _report = report;
        }

        public void Report(DiscoveryProgress value) => _report(value);
    }
}
