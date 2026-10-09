using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Discovery Documents run wiring (08-UI-SPEC S1): Generate Documents / Cancel Generation, progress and outcome lines.
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
