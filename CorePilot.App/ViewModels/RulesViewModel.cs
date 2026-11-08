using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CorePilot.App.Localization;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;
using CpuPrio = CorePilot.Core.Models.CpuPriority;
using IoPrio = CorePilot.Core.Models.IoPriority;
using MemPrio = CorePilot.Core.Models.MemoryPriority;

namespace CorePilot.App.ViewModels;

/// <summary>One logical CPU toggle inside the affinity grid.</summary>
public sealed class CoreItemViewModel : PageViewModel
{
    private readonly Action _changed;
    private bool _isSelected;

    public CoreItemViewModel(int index, bool isPerformanceCore, bool isVirtual, Action changed)
    {
        Index = index;
        IsPerformanceCore = isPerformanceCore;
        IsVirtual = isVirtual;
        _changed = changed;
    }

    public int Index { get; }

    public bool IsPerformanceCore { get; }

    /// <summary>True for an SMT/hyper-threading sibling of a physical core.</summary>
    public bool IsVirtual { get; }

    public string Kind => LocalizationService.Instance[IsPerformanceCore ? "rules.core.p" : "rules.core.e"];

    public string Tooltip => string.Format(
        "{0} · {1}",
        string.Format(
            LocalizationService.Instance["rules.core.tooltip"],
            Index,
            LocalizationService.Instance[IsPerformanceCore ? "rules.core.performance" : "rules.core.efficiency"]),
        LocalizationService.Instance[IsVirtual ? "process.core.virtual" : "process.core.physical"]);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                _changed();
            }
        }
    }

    public void Refresh() => OnAllChanged();
}

/// <summary>A physical core grouping its logical processors.</summary>
public sealed class CpuCoreGroupViewModel : PageViewModel
{
    public CpuCoreGroupViewModel(int index, IReadOnlyList<CoreItemViewModel> cores)
    {
        Index = index;
        Cores = cores;
    }

    public int Index { get; }

    public IReadOnlyList<CoreItemViewModel> Cores { get; }

    public string Label => string.Format(LocalizationService.Instance["rules.core.physical"], Index + 1);

    public void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>Combo box entry for a nullable priority (null = leave unchanged).</summary>
public sealed class PriorityOption : PageViewModel
{
    private readonly string _key;

    public PriorityOption(object? value, string key)
    {
        Value = value;
        _key = key;
    }

    public object? Value { get; }

    public string Label => LocalizationService.Instance[_key];

    public void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>State of the rule editor panel (create/edit).</summary>
public sealed class RuleEditorViewModel : PageViewModel, IDisposable
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly List<CoreItemViewModel> _cores = new();
    private Guid _id = Guid.Empty;
    private string _displayName = string.Empty;
    private string _executablePath = string.Empty;
    private PriorityOption? _cpuPriority;
    private PriorityOption? _ioPriority;
    private PriorityOption? _memoryPriority;
    private bool _enabled = true;
    private bool _applyOnStart = true;
    private bool _keepEnforced = true;
    private bool _hasTopology;

    public RuleEditorViewModel(ICpuTopologyService? topology)
    {
        BuildGroups(topology);
        CpuPriorities = BuildOptions(
            new PriorityOption(null, "rules.prio.default"),
            new PriorityOption(CpuPrio.Idle, "rules.prio.idle"),
            new PriorityOption(CpuPrio.BelowNormal, "rules.prio.belowNormal"),
            new PriorityOption(CpuPrio.Normal, "rules.prio.normal"),
            new PriorityOption(CpuPrio.AboveNormal, "rules.prio.aboveNormal"),
            new PriorityOption(CpuPrio.High, "rules.prio.high"),
            new PriorityOption(CpuPrio.RealTime, "rules.prio.realTime"));

        IoPriorities = BuildOptions(
            new PriorityOption(null, "rules.prio.default"),
            new PriorityOption(IoPrio.VeryLow, "rules.prio.veryLow"),
            new PriorityOption(IoPrio.Low, "rules.prio.low"),
            new PriorityOption(IoPrio.Normal, "rules.prio.normal"),
            new PriorityOption(IoPrio.High, "rules.prio.high"));

        MemoryPriorities = BuildOptions(
            new PriorityOption(null, "rules.prio.default"),
            new PriorityOption(MemPrio.VeryLow, "rules.prio.veryLow"),
            new PriorityOption(MemPrio.Low, "rules.prio.low"),
            new PriorityOption(MemPrio.Medium, "rules.prio.medium"),
            new PriorityOption(MemPrio.High, "rules.prio.high"),
            new PriorityOption(MemPrio.VeryHigh, "rules.prio.veryHigh"));

        _cpuPriority = CpuPriorities[0];
        _ioPriority = IoPriorities[0];
        _memoryPriority = MemoryPriorities[0];

        SelectAllCoresCommand = new RelayCommand(SelectAllCores);
        SelectNoCoresCommand = new RelayCommand(SelectNoCores);

        Loc.PropertyChanged += OnLocalizationChanged;
    }

    public ObservableCollection<CpuCoreGroupViewModel> Groups { get; } = new();

    public ObservableCollection<PriorityOption> CpuPriorities { get; }

    public ObservableCollection<PriorityOption> IoPriorities { get; }

    public ObservableCollection<PriorityOption> MemoryPriorities { get; }

    public RelayCommand SelectAllCoresCommand { get; }

    public RelayCommand SelectNoCoresCommand { get; }

    public bool IsNew => _id == Guid.Empty;

    public Guid Id => _id;

    public string HeaderText => IsNew ? Loc["rules.editor.title.new"] : Loc["rules.editor.title.edit"];

    public bool HasTopology
    {
        get => _hasTopology;
        private set => Set(ref _hasTopology, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (Set(ref _executablePath, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(_executablePath);

    public PriorityOption? CpuPriority
    {
        get => _cpuPriority;
        set => Set(ref _cpuPriority, value);
    }

    public PriorityOption? IoPriority
    {
        get => _ioPriority;
        set => Set(ref _ioPriority, value);
    }

    public PriorityOption? MemoryPriority
    {
        get => _memoryPriority;
        set => Set(ref _memoryPriority, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public bool ApplyOnStart
    {
        get => _applyOnStart;
        set => Set(ref _applyOnStart, value);
    }

    public bool KeepEnforced
    {
        get => _keepEnforced;
        set => Set(ref _keepEnforced, value);
    }

    public ulong AffinityMask => CpuMaskFormatter.Mask(_cores);

    public string MaskText => AffinityMask == 0
        ? Loc["rules.mask.none"]
        : CpuMaskFormatter.Format(AffinityMask);

    public string SummaryText => string.Format(Loc["rules.editor.summary"], SelectedCoreCount, Math.Max(1, _cores.Count));

    public int SelectedCoreCount => _cores.Count(c => c.IsSelected);

    public void Load(AffinityRule rule)
    {
        _id = rule.Id;
        _displayName = rule.DisplayName;
        _executablePath = rule.ExecutablePath;
        _enabled = rule.Enabled;
        _applyOnStart = rule.ApplyOnStart;
        _keepEnforced = rule.KeepEnforced;
        _cpuPriority = Find(CpuPriorities, rule.Priority);
        _ioPriority = Find(IoPriorities, rule.IoPriority);
        _memoryPriority = Find(MemoryPriorities, rule.MemoryPriority);

        foreach (var core in _cores)
        {
            core.IsSelected = (rule.AffinityMask & (1UL << core.Index)) != 0;
        }

        OnAllChanged();
    }

    public void Reset()
    {
        _id = Guid.Empty;
        _displayName = string.Empty;
        _executablePath = string.Empty;
        _enabled = true;
        _applyOnStart = true;
        _keepEnforced = true;
        _cpuPriority = CpuPriorities[0];
        _ioPriority = IoPriorities[0];
        _memoryPriority = MemoryPriorities[0];

        foreach (var core in _cores)
        {
            core.IsSelected = false;
        }

        OnAllChanged();
    }

    public void SetPath(string path)
    {
        ExecutablePath = path;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }

    public AffinityRule BuildRule() => new()
    {
        Id = _id == Guid.Empty ? Guid.NewGuid() : _id,
        ExecutablePath = (_executablePath ?? string.Empty).Trim().Trim('"'),
        DisplayName = string.IsNullOrWhiteSpace(_displayName)
            ? System.IO.Path.GetFileNameWithoutExtension((_executablePath ?? string.Empty).Trim().Trim('"'))
            : _displayName.Trim(),
        AffinityMask = AffinityMask,
        Priority = _cpuPriority?.Value as CpuPrio?,
        IoPriority = _ioPriority?.Value as IoPrio?,
        MemoryPriority = _memoryPriority?.Value as MemPrio?,
        Enabled = _enabled,
        ApplyOnStart = _applyOnStart,
        KeepEnforced = _keepEnforced,
        UpdatedAt = DateTime.UtcNow
    };

    public void Dispose() => Loc.PropertyChanged -= OnLocalizationChanged;

    private void BuildGroups(ICpuTopologyService? topology)
    {
        Groups.Clear();
        _cores.Clear();

        CpuTopology? cpu = null;
        try
        {
            cpu = topology?.GetTopology();
        }
        catch
        {
            cpu = null;
        }

        HasTopology = cpu is not null && cpu.Count > 0;
        if (cpu is null)
        {
            return;
        }

        var index = 0;
        foreach (var group in cpu.LogicalProcessors
                     .GroupBy(p => p.PhysicalCoreId)
                     .OrderBy(g => g.Min(p => p.Index)))
        {
            var cores = group
                .OrderBy(p => p.Index)
                .Select((p, i) => new CoreItemViewModel(p.Index, p.IsPerformanceCore, i > 0, OnCoreSelectionChanged))
                .ToList();

            _cores.AddRange(cores);
            Groups.Add(new CpuCoreGroupViewModel(index, cores));
            index++;
        }
    }

    private void OnCoreSelectionChanged()
    {
        OnPropertyChanged(nameof(AffinityMask));
        OnPropertyChanged(nameof(MaskText));
        OnPropertyChanged(nameof(SelectedCoreCount));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void SelectAllCores()
    {
        foreach (var core in _cores)
        {
            core.IsSelected = true;
        }
    }

    private void SelectNoCores()
    {
        foreach (var core in _cores)
        {
            core.IsSelected = false;
        }
    }

    private static ObservableCollection<PriorityOption> BuildOptions(params PriorityOption[] options) =>
        new(options);

    private static PriorityOption Find(ObservableCollection<PriorityOption> options, object? value)
    {
        foreach (var option in options)
        {
            if (Equals(option.Value, value))
            {
                return option;
            }
        }

        return options[0];
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var option in CpuPriorities.Concat(IoPriorities).Concat(MemoryPriorities))
        {
            option.Refresh();
        }

        foreach (var group in Groups)
        {
            group.Refresh();
        }

        foreach (var core in _cores)
        {
            core.Refresh();
        }

        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(MaskText));
        OnPropertyChanged(nameof(SummaryText));
    }
}

/// <summary>Rules screen: list of rule cards plus a live editor panel.</summary>
public sealed class RulesViewModel : PageViewModel, IDisposable
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly IRuleService _rules;
    private readonly IProcessService _processes;
    private readonly Dispatcher _dispatcher;
    private readonly RuleEditorViewModel _editor;

    private bool _suppressReload;
    private RuleItemViewModel? _selected;
    private bool _hasRules;

    public RulesViewModel()
        : this(AppServices.Rules, AppServices.Processes, AppServices.Topology)
    {
    }

    public RulesViewModel(IRuleService rules, IProcessService processes, ICpuTopologyService? topology)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _editor = new RuleEditorViewModel(topology);

        NewRuleCommand = new RelayCommand(StartNewRule);
        SaveRuleCommand = new RelayCommand(SaveEditor);
        DeleteRuleCommand = new RelayCommand(parameter => Delete(parameter as RuleItemViewModel));
        ApplyRuleCommand = new RelayCommand(parameter => ApplyNow(parameter as RuleItemViewModel));
        BrowseCommand = new RelayCommand(Browse);

        _rules.Changed += OnRulesChanged;
        Reload();
    }

    public ObservableCollection<RuleItemViewModel> Rules { get; } = new();

    public RuleEditorViewModel Editor => _editor;

    public RelayCommand NewRuleCommand { get; }

    public RelayCommand SaveRuleCommand { get; }

    public RelayCommand DeleteRuleCommand { get; }

    public RelayCommand ApplyRuleCommand { get; }

    public RelayCommand BrowseCommand { get; }

    public bool HasRules
    {
        get => _hasRules;
        private set => Set(ref _hasRules, value);
    }

    public RuleItemViewModel? SelectedRule
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
            {
                return;
            }

            if (value is not null)
            {
                _editor.Load(value.Rule);
            }
        }
    }

    public void Dispose()
    {
        _rules.Changed -= OnRulesChanged;
        _editor.Dispose();
    }

    private void Reload()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(Reload));
            return;
        }

        var selectedId = _selected?.Id;

        Rules.Clear();
        foreach (var rule in _rules.Rules.OrderByDescending(r => r.UpdatedAt))
        {
            Rules.Add(new RuleItemViewModel(rule, SaveItem));
        }

        HasRules = Rules.Count > 0;

        if (selectedId.HasValue)
        {
            var match = Rules.FirstOrDefault(r => r.Id == selectedId.Value);
            if (match is not null)
            {
                SelectedRule = match;
            }
        }
    }

    private void SaveItem(RuleItemViewModel item)
    {
        _suppressReload = true;
        try
        {
            _rules.Upsert(item.Rule);
        }
        finally
        {
            _suppressReload = false;
        }
    }

    private void OnRulesChanged(object? sender, EventArgs e)
    {
        if (_suppressReload)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            Reload();
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(Reload));
        }
    }

    private void StartNewRule()
    {
        SelectedRule = null;
        _editor.Reset();
    }

    private void SaveEditor()
    {
        if (!_editor.CanSave)
        {
            return;
        }

        var rule = _editor.BuildRule();
        AffinityRule saved = rule;

        _suppressReload = true;
        try
        {
            saved = _rules.Upsert(rule);
        }
        finally
        {
            _suppressReload = false;
        }

        Reload();
        var match = Rules.FirstOrDefault(r => r.Id == saved.Id);
        if (match is not null)
        {
            SelectedRule = match;
            match.StatusText = Loc["rules.status.saved"];
        }
    }

    private void Delete(RuleItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            string.Format(Loc["rules.delete.question"], item.Name),
            Loc["rules.delete.title"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _suppressReload = true;
        try
        {
            _rules.Remove(item.Id);
        }
        finally
        {
            _suppressReload = false;
        }

        if (ReferenceEquals(_selected, item))
        {
            _selected = null;
            OnPropertyChanged(nameof(SelectedRule));
            _editor.Reset();
        }

        Reload();
    }

    private void ApplyNow(RuleItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var rule = item.Rule.Clone();
        var applying = Loc["rules.status.applying"];
        var appliedTemplate = Loc["rules.status.applied"];
        var failedTemplate = Loc["rules.status.failed"];
        item.StatusText = applying;

        Task.Run(() =>
        {
            var applied = 0;
            var failed = 0;
            string? error = null;
            IReadOnlyList<ProcessSnapshot> processes;

            try
            {
                processes = _processes.GetProcesses();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                processes = Array.Empty<ProcessSnapshot>();
            }

            foreach (var process in processes)
            {
                if (!rule.Matches(process.ExecutablePath, process.Name))
                {
                    continue;
                }

                var result = _rules.Apply(process, rule);
                if (result.Success)
                {
                    applied++;
                }
                else
                {
                    failed++;
                    error ??= result.Error;
                }
            }

            var message = failed > 0
                ? string.Format(failedTemplate, applied, failed, error ?? string.Empty)
                : string.Format(appliedTemplate, applied);

            _dispatcher.BeginInvoke(new Action(() => item.StatusText = message));
        });
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc["rules.editor.browse"],
            Filter = Loc["rules.editor.filter"],
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _editor.SetPath(dialog.FileName);
        }
    }
}
