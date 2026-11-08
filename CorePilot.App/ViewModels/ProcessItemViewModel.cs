using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using CorePilot.App.Localization;
using CorePilot.App.Services;
using CorePilot.Core.Models;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace CorePilot.App.ViewModels;

/// <summary>Shared, language-aware priority choices for the detail panel.</summary>
internal static class PriorityCatalog
{
    private static readonly LocalizedOption<CpuPriority>[] CpuOptionsArray =
    {
        new(CpuPriority.Idle, "priority.cpu.idle"),
        new(CpuPriority.BelowNormal, "priority.cpu.belowNormal"),
        new(CpuPriority.Normal, "priority.cpu.normal"),
        new(CpuPriority.AboveNormal, "priority.cpu.aboveNormal"),
        new(CpuPriority.High, "priority.cpu.high"),
        new(CpuPriority.RealTime, "priority.cpu.realtime")
    };

    private static readonly LocalizedOption<IoPriority>[] IoOptionsArray =
    {
        new(IoPriority.VeryLow, "priority.io.veryLow"),
        new(IoPriority.Low, "priority.io.low"),
        new(IoPriority.Normal, "priority.io.normal"),
        new(IoPriority.High, "priority.io.high")
    };

    private static readonly LocalizedOption<MemoryPriority>[] MemoryOptionsArray =
    {
        new(MemoryPriority.VeryLow, "priority.memory.veryLow"),
        new(MemoryPriority.Low, "priority.memory.low"),
        new(MemoryPriority.Medium, "priority.memory.medium"),
        new(MemoryPriority.High, "priority.memory.high"),
        new(MemoryPriority.VeryHigh, "priority.memory.veryHigh")
    };

    public static IReadOnlyList<LocalizedOption<CpuPriority>> CpuOptions => CpuOptionsArray;

    public static IReadOnlyList<LocalizedOption<IoPriority>> IoOptions => IoOptionsArray;

    public static IReadOnlyList<LocalizedOption<MemoryPriority>> MemoryOptions => MemoryOptionsArray;

    public static LocalizedOption<CpuPriority> CpuOption(CpuPriority value) =>
        CpuOptionsArray.FirstOrDefault(o => o.Value == value) ?? CpuOptionsArray[0];

    public static LocalizedOption<IoPriority> IoOption(IoPriority value) =>
        IoOptionsArray.FirstOrDefault(o => o.Value == value) ?? IoOptionsArray[0];

    public static LocalizedOption<MemoryPriority> MemoryOption(MemoryPriority value) =>
        MemoryOptionsArray.FirstOrDefault(o => o.Value == value) ?? MemoryOptionsArray[0];

    public static string CpuKey(CpuPriority value) => value switch
    {
        CpuPriority.Idle => "priority.cpu.idle",
        CpuPriority.BelowNormal => "priority.cpu.belowNormal",
        CpuPriority.AboveNormal => "priority.cpu.aboveNormal",
        CpuPriority.High => "priority.cpu.high",
        CpuPriority.RealTime => "priority.cpu.realtime",
        _ => "priority.cpu.normal"
    };

    public static void RefreshLabels()
    {
        foreach (var option in CpuOptionsArray)
        {
            option.RefreshLabel();
        }

        foreach (var option in IoOptionsArray)
        {
            option.RefreshLabel();
        }

        foreach (var option in MemoryOptionsArray)
        {
            option.RefreshLabel();
        }
    }
}

/// <summary>One logical CPU toggle inside the affinity grid.</summary>
public sealed class CoreChipViewModel : ObservableObject
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly ProcessItemViewModel _owner;
    private bool _isSelected;

    internal CoreChipViewModel(LogicalProcessor processor, ProcessItemViewModel owner, bool isVirtual)
    {
        Index = processor.Index;
        IsPerformanceCore = processor.IsPerformanceCore;
        IsVirtual = isVirtual;
        _owner = owner;
    }

    public int Index { get; }

    public bool IsPerformanceCore { get; }

    /// <summary>True for an SMT/hyper-threading sibling of a physical core.</summary>
    public bool IsVirtual { get; }

    public string Label => Index.ToString(CultureInfo.InvariantCulture);

    public string Tooltip => string.Format(
        "{0} · {1}",
        string.Format(Loc["process.logicalCpu"], Index),
        Loc[IsVirtual ? "process.core.virtual" : "process.core.physical"]);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _owner.OnCoreChipToggled(this);
            }
        }
    }

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value)
        {
            return;
        }

        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }

    internal void RefreshLocalization() => OnPropertyChanged(nameof(Tooltip));
}

/// <summary>Logical CPUs belonging to one physical core.</summary>
public sealed class CoreGroupViewModel : ObservableObject
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly CoreDefinition _definition;

    internal CoreGroupViewModel(CoreDefinition definition, ProcessItemViewModel owner)
    {
        _definition = definition;
        Cores = definition.Processors.Select((p, i) => new CoreChipViewModel(p, owner, i > 0)).ToArray();
    }

    public IReadOnlyList<CoreChipViewModel> Cores { get; }

    public string Title => _definition.PackageCount > 1
        ? string.Format(Loc["process.coreNamePackage"], _definition.PackageId, _definition.PhysicalCoreId)
        : string.Format(Loc["process.coreName"], _definition.PhysicalCoreId);

    public bool ShowEfficiency => _definition.ShowEfficiency;

    public string EfficiencyLabel => _definition.HasPerformance && _definition.HasEfficiency
        ? "P/E"
        : _definition.HasPerformance ? "P" : "E";

    internal void SetMask(ulong mask)
    {
        foreach (var chip in Cores)
        {
            var enabled = chip.Index is >= 0 and < 64 && (mask & (1UL << chip.Index)) != 0;
            chip.SetSelectedSilently(enabled);
        }
    }

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var chip in Cores)
        {
            chip.RefreshLocalization();
        }
    }
}

/// <summary>One live process plus its detail-panel state and actions.</summary>
public sealed class ProcessItemViewModel : ObservableObject
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly ProcessesViewModel _owner;
    private readonly List<CoreGroupViewModel> _coreGroups = new();

    private ProcessSnapshot _snapshot;
    private int _pid;
    private string _name = string.Empty;
    private string? _executablePath;
    private string? _commandLine;
    private long _workingSetBytes;
    private double _cpuPercent;
    private int _threadCount;
    private string? _processOwner;
    private CpuPriority _priority;
    private IoPriority _ioPriority = IoPriority.Normal;
    private MemoryPriority _memoryPriority = MemoryPriority.Medium;
    private ulong _affinityMask;
    private bool _isSuspended;
    private bool _isElevated;
    private DateTime? _startTime;
    private bool _hasRule;
    private Guid? _ruleId;
    private bool _suppressPriority;
    private bool _updatingCores;
    private bool _cpuTouched;
    private bool _ioTouched;
    private bool _memoryTouched;
    private bool _commandLineLoaded;
    private ImageSource? _icon;
    private string? _iconPath;
    private int _depth;
    private bool _hasChildren;
    private bool _isLastChild;
    private bool _isExpanded = true;
    private bool _isHiddenByCollapse;
    private bool _isInAppGroup;
    private string _treeSortKey = string.Empty;
    private IReadOnlyList<bool> _ancestorLines = Array.Empty<bool>();
    private LocalizedOption<CpuPriority>? _selectedCpuPriority;
    private LocalizedOption<IoPriority>? _selectedIoPriority;
    private LocalizedOption<MemoryPriority>? _selectedMemoryPriority;

    internal ProcessItemViewModel(ProcessSnapshot snapshot, ProcessesViewModel owner)
    {
        _snapshot = snapshot;
        _owner = owner;

        ToggleSuspendCommand = new RelayCommand(ToggleSuspend);
        TerminateCommand = new RelayCommand(Terminate);
        RestartCommand = new RelayCommand(() => _ = RestartAsync());
        TrimCommand = new RelayCommand(Trim);
        CopyCommandLineCommand = new RelayCommand(CopyCommandLine);
        CopyPathCommand = new RelayCommand(CopyPath);
        OpenFileCommand = new RelayCommand(OpenFile);
        OpenFileLocationCommand = new RelayCommand(OpenFileLocation);
        SetPriorityCommand = new RelayCommand(SetPriorityFromMenu);
        SaveRuleCommand = new RelayCommand(SaveRule);
        UpdateRuleCommand = new RelayCommand(ApplyExistingRule);
        SelectAllCoresCommand = new RelayCommand(SelectAllCores);
        SelectNoCoresCommand = new RelayCommand(SelectNoCores);

        Update(snapshot);
        RequestIcon();
    }

    public ObservableCollection<CoreGroupViewModel> CoreGroups { get; } = new();

    public RelayCommand ToggleSuspendCommand { get; }

    public RelayCommand TerminateCommand { get; }

    public RelayCommand RestartCommand { get; }

    public RelayCommand TrimCommand { get; }

    public RelayCommand CopyCommandLineCommand { get; }

    public RelayCommand CopyPathCommand { get; }

    public RelayCommand OpenFileCommand { get; }

    public RelayCommand OpenFileLocationCommand { get; }

    public RelayCommand SetPriorityCommand { get; }

    public RelayCommand SaveRuleCommand { get; }

    public RelayCommand UpdateRuleCommand { get; }

    public RelayCommand SelectAllCoresCommand { get; }

    public RelayCommand SelectNoCoresCommand { get; }

    public int Pid
    {
        get => _pid;
        private set => SetProperty(ref _pid, value);
    }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>True when the process owns a visible top-level window; used for Task Manager style grouping.</summary>
    public bool IsApp => _snapshot.HasMainWindow;

    public int ParentPid => _snapshot.ParentPid;

    /// <summary>Nesting level inside the process tree (0 for tree roots).</summary>
    public int Depth
    {
        get => _depth;
        internal set
        {
            if (SetProperty(ref _depth, value))
            {
                OnPropertyChanged(nameof(HasParent));
            }
        }
    }

    public bool HasParent => Depth > 0;

    public bool HasChildren
    {
        get => _hasChildren;
        internal set => SetProperty(ref _hasChildren, value);
    }

    public bool IsLastChild
    {
        get => _isLastChild;
        internal set => SetProperty(ref _isLastChild, value);
    }

    /// <summary>Per-level flags for painting the vertical tree branches to the left of the row.</summary>
    public IReadOnlyList<bool> AncestorLines
    {
        get => _ancestorLines;
        internal set => SetProperty(ref _ancestorLines, value);
    }

    /// <summary>Expansion state of the child rows; bound to the row chevron.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
            {
                return;
            }

            _owner.OnExpansionChanged(this);
        }
    }

    /// <summary>True when one of the ancestors is collapsed, so the row is filtered out.</summary>
    public bool IsHiddenByCollapse
    {
        get => _isHiddenByCollapse;
        internal set => SetProperty(ref _isHiddenByCollapse, value);
    }

    /// <summary>Group of the tree root: true for the "Apps" section, false for background processes.</summary>
    public bool IsInAppGroup
    {
        get => _isInAppGroup;
        internal set => SetProperty(ref _isInAppGroup, value);
    }

    /// <summary>Lexicographic key that keeps a parent directly above its descendants.</summary>
    public string TreeSortKey
    {
        get => _treeSortKey;
        internal set => SetProperty(ref _treeSortKey, value);
    }

    public string? ExecutablePath
    {
        get => _executablePath;
        private set
        {
            if (SetProperty(ref _executablePath, value))
            {
                OnPropertyChanged(nameof(PathText));
                OnPropertyChanged(nameof(HasExecutablePath));
                RequestIcon();
            }
        }
    }

    public string? CommandLine
    {
        get => _commandLine;
        private set
        {
            if (SetProperty(ref _commandLine, value))
            {
                OnPropertyChanged(nameof(CommandLineDisplay));
            }
        }
    }

    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        private set
        {
            if (SetProperty(ref _workingSetBytes, value))
            {
                OnPropertyChanged(nameof(MemoryText));
            }
        }
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        private set => SetProperty(ref _cpuPercent, value);
    }

    public int ThreadCount
    {
        get => _threadCount;
        private set
        {
            if (SetProperty(ref _threadCount, value))
            {
                OnPropertyChanged(nameof(ThreadsText));
            }
        }
    }

    public string? Owner
    {
        get => _processOwner;
        private set
        {
            if (SetProperty(ref _processOwner, value))
            {
                OnPropertyChanged(nameof(OwnerText));
            }
        }
    }

    public CpuPriority Priority
    {
        get => _priority;
        private set
        {
            if (SetProperty(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityText));
            }
        }
    }

    public ulong AffinityMask
    {
        get => _affinityMask;
        private set
        {
            if (!SetProperty(ref _affinityMask, value))
            {
                return;
            }

            OnPropertyChanged(nameof(AffinityText));
            OnPropertyChanged(nameof(CoresText));
            OnPropertyChanged(nameof(AffinityCount));
        }
    }

    public bool IsSuspended
    {
        get => _isSuspended;
        private set
        {
            if (!SetProperty(ref _isSuspended, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(SuspendResumeText));
        }
    }

    public bool IsElevated
    {
        get => _isElevated;
        private set => SetProperty(ref _isElevated, value);
    }

    public DateTime? StartTime
    {
        get => _startTime;
        private set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(StartTimeText));
            }
        }
    }

    public bool HasRule => _hasRule;

    public Guid? RuleId => _ruleId;

    public bool HasCoreGroups => CoreGroups.Count > 0;

    public IReadOnlyList<LocalizedOption<CpuPriority>> CpuPriorityOptions => PriorityCatalog.CpuOptions;

    public IReadOnlyList<LocalizedOption<IoPriority>> IoPriorityOptions => PriorityCatalog.IoOptions;

    public IReadOnlyList<LocalizedOption<MemoryPriority>> MemoryPriorityOptions => PriorityCatalog.MemoryOptions;

    public LocalizedOption<CpuPriority>? SelectedCpuPriority
    {
        get => _selectedCpuPriority;
        set
        {
            if (!SetProperty(ref _selectedCpuPriority, value) || value is null || _suppressPriority)
            {
                return;
            }

            if (value.Value == _priority)
            {
                return;
            }

            ApplyCpuPriority(value.Value);
        }
    }

    public LocalizedOption<IoPriority>? SelectedIoPriority
    {
        get => _selectedIoPriority;
        set
        {
            if (!SetProperty(ref _selectedIoPriority, value) || value is null || _suppressPriority)
            {
                return;
            }

            if (value.Value == _ioPriority)
            {
                return;
            }

            ApplyIoPriority(value.Value);
        }
    }

    public LocalizedOption<MemoryPriority>? SelectedMemoryPriority
    {
        get => _selectedMemoryPriority;
        set
        {
            if (!SetProperty(ref _selectedMemoryPriority, value) || value is null || _suppressPriority)
            {
                return;
            }

            if (value.Value == _memoryPriority)
            {
                return;
            }

            ApplyMemoryPriority(value.Value);
        }
    }

    public string PidText => $"PID {Pid}";

    public string PathText => string.IsNullOrWhiteSpace(ExecutablePath) ? Name : ExecutablePath!;

    public bool HasExecutablePath => !string.IsNullOrWhiteSpace(ExecutablePath);

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public string OwnerText => string.IsNullOrWhiteSpace(Owner) ? Loc["common.unknown"] : Owner!;

    public string ThreadsText => ThreadCount.ToString(CultureInfo.CurrentCulture);

    public string StartTimeText => StartTime is { } started
        ? started.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
        : Loc["common.unknown"];

    public string MemoryText => FormatBytes(WorkingSetBytes);

    public string CoresText
    {
        get
        {
            if (AffinityMask == 0)
            {
                return "—";
            }

            var total = _owner.LogicalProcessorCount;
            return total > 0 ? $"{AffinityCount}/{total}" : $"0x{AffinityMask:X}";
        }
    }

    public int AffinityCount => CpuTopology.MaskCount(AffinityMask);

    public string AffinityText
    {
        get
        {
            if (AffinityMask == 0)
            {
                return "—";
            }

            var total = _owner.LogicalProcessorCount;
            return total > 0
                ? $"0x{AffinityMask:X}  ·  {AffinityCount}/{total}"
                : $"0x{AffinityMask:X}";
        }
    }

    public string PriorityText => Loc[PriorityCatalog.CpuKey(Priority)];

    public string StatusText => Loc[IsSuspended ? "status.suspended" : "status.running"];

    public string SuspendResumeText => Loc[IsSuspended ? "action.resume" : "action.suspend"];

    public string RuleStatusText => Loc[HasRule ? "process.ruleExists" : "process.noRule"];

    public string CommandLineDisplay =>
        string.IsNullOrWhiteSpace(CommandLine) ? Loc["process.commandLineEmpty"] : CommandLine!;

    public bool Update(ProcessSnapshot snapshot)
    {
        var wasApp = _snapshot.HasMainWindow;
        var previousParent = _snapshot.ParentPid;
        _snapshot = snapshot;

        Pid = snapshot.Pid;
        Name = snapshot.Name;
        ExecutablePath = snapshot.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(snapshot.CommandLine))
        {
            CommandLine = snapshot.CommandLine;
        }

        WorkingSetBytes = snapshot.WorkingSetBytes;
        CpuPercent = snapshot.CpuPercent;
        ThreadCount = snapshot.ThreadCount;
        Owner = snapshot.Owner;
        Priority = snapshot.Priority;
        IsSuspended = snapshot.IsSuspended;
        IsElevated = snapshot.IsElevated;
        StartTime = snapshot.StartTime;
        AffinityMask = snapshot.AffinityMask;

        EnsureCoreGroups();
        if (!_updatingCores)
        {
            ApplyMaskToChips(AffinityMask);
        }

        RefreshRuleState();

        _suppressPriority = true;
        SelectedCpuPriority = PriorityCatalog.CpuOption(Priority);
        SelectedIoPriority = PriorityCatalog.IoOption(_ioPriority);
        SelectedMemoryPriority = PriorityCatalog.MemoryOption(_memoryPriority);
        _suppressPriority = false;

        // The setters above already raise change notifications only for properties
        // that actually changed; raising them again here caused thousands of
        // redundant UI updates per refresh tick.
        if (wasApp != snapshot.HasMainWindow || previousParent != snapshot.ParentPid)
        {
            OnPropertyChanged(nameof(IsApp));
            return true;
        }

        return false;
    }

    internal void EnsureCoreGroups()
    {
        if (_coreGroups.Count == 0)
        {
            foreach (var definition in _owner.CoreTemplate)
            {
                var group = new CoreGroupViewModel(definition, this);
                _coreGroups.Add(group);
                CoreGroups.Add(group);
            }

            if (_coreGroups.Count > 0)
            {
                OnPropertyChanged(nameof(HasCoreGroups));
                ApplyMaskToChips(AffinityMask);
            }
        }
    }

    internal void RefreshRuleState()
    {
        var hasRule = false;
        Guid? ruleId = null;

        try
        {
            var rules = AppServices.Rules;
            var rule = rules?.FindFor(ExecutablePath, Name);
            hasRule = rule is not null;
            ruleId = rule?.Id;

            if (!_ioTouched && rule?.IoPriority is { } ioPriority)
            {
                _ioPriority = ioPriority;
            }

            if (!_memoryTouched && rule?.MemoryPriority is { } memoryPriority)
            {
                _memoryPriority = memoryPriority;
            }
        }
        catch
        {
            // Rules are optional while the application is starting.
        }

        _ruleId = ruleId;
        if (_hasRule == hasRule)
        {
            return;
        }

        _hasRule = hasRule;
        OnPropertyChanged(nameof(HasRule));
        OnPropertyChanged(nameof(RuleStatusText));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(OwnerText));
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SuspendResumeText));
        OnPropertyChanged(nameof(RuleStatusText));
        OnPropertyChanged(nameof(CommandLineDisplay));

        foreach (var group in _coreGroups)
        {
            group.RefreshLocalization();
        }
    }

    public static void RefreshPriorityLabels() => PriorityCatalog.RefreshLabels();

    public async Task EnsureCommandLineAsync()
    {
        if (_commandLineLoaded)
        {
            return;
        }

        _commandLineLoaded = true;
        if (!string.IsNullOrWhiteSpace(CommandLine))
        {
            return;
        }

        var pid = Pid;
        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                return;
            }

            var commandLine = await Task.Run(() => service.GetCommandLine(pid)).ConfigureAwait(true);
            if (Pid == pid && !string.IsNullOrWhiteSpace(commandLine))
            {
                CommandLine = commandLine;
                OnPropertyChanged(nameof(CommandLineDisplay));
            }
        }
        catch
        {
            // Access can be denied for protected processes.
        }
    }

    internal void OnCoreChipToggled(CoreChipViewModel chip)
    {
        if (_updatingCores)
        {
            return;
        }

        var mask = BuildMaskFromChips();
        if (mask == 0)
        {
            _owner.ReportError(Loc["error.affinityEmpty"]);
            ApplyMaskToChips(AffinityMask);
            return;
        }

        ApplyAffinity(mask);
    }

    private void ApplyMaskToChips(ulong mask)
    {
        _updatingCores = true;
        try
        {
            foreach (var group in _coreGroups)
            {
                group.SetMask(mask);
            }
        }
        finally
        {
            _updatingCores = false;
        }
    }

    private ulong BuildMaskFromChips()
    {
        ulong mask = 0;
        foreach (var group in _coreGroups)
        {
            foreach (var chip in group.Cores)
            {
                if (chip.IsSelected && chip.Index is >= 0 and < 64)
                {
                    mask |= 1UL << chip.Index;
                }
            }
        }

        return mask;
    }

    private void ApplyAffinity(ulong mask)
    {
        if (mask == 0)
        {
            _owner.ReportError(Loc["error.affinityEmpty"]);
            ApplyMaskToChips(AffinityMask);
            return;
        }

        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                ApplyMaskToChips(AffinityMask);
                return;
            }

            var result = service.SetAffinity(Pid, mask);
            if (result.Success)
            {
                AffinityMask = mask;
                _owner.ReportInfo(Loc["status.affinityApplied"]);
            }
            else
            {
                _owner.ReportError(result.Error);
                ApplyMaskToChips(AffinityMask);
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
            ApplyMaskToChips(AffinityMask);
        }
    }

    private void SelectAllCores()
    {
        if (_coreGroups.Count == 0)
        {
            _owner.ReportError(Loc["error.topologyUnavailable"]);
            return;
        }

        _updatingCores = true;
        try
        {
            foreach (var group in _coreGroups)
            {
                foreach (var chip in group.Cores)
                {
                    chip.SetSelectedSilently(true);
                }
            }
        }
        finally
        {
            _updatingCores = false;
        }

        ApplyAffinity(BuildMaskFromChips());
    }

    private void SelectNoCores()
    {
        _updatingCores = true;
        try
        {
            foreach (var group in _coreGroups)
            {
                foreach (var chip in group.Cores)
                {
                    chip.SetSelectedSilently(false);
                }
            }
        }
        finally
        {
            _updatingCores = false;
        }

        // Windows rejects an empty affinity mask; wait for the user's next pick.
        _owner.ReportError(Loc["error.affinityEmpty"]);
    }

    private void ApplyCpuPriority(CpuPriority value)
    {
        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                SyncPrioritySelection();
                return;
            }

            var result = service.SetPriority(Pid, value);
            if (result.Success)
            {
                Priority = value;
                _cpuTouched = true;
                _owner.ReportInfo(Loc["status.priorityApplied"]);
            }
            else
            {
                _owner.ReportError(result.Error);
                SyncPrioritySelection();
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
            SyncPrioritySelection();
        }
    }

    private void ApplyIoPriority(IoPriority value)
    {
        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                SyncPrioritySelection();
                return;
            }

            var result = service.SetIoPriority(Pid, value);
            if (result.Success)
            {
                _ioPriority = value;
                _ioTouched = true;
                _owner.ReportInfo(Loc["status.priorityApplied"]);
            }
            else
            {
                _owner.ReportError(result.Error);
                SyncPrioritySelection();
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
            SyncPrioritySelection();
        }
    }

    private void ApplyMemoryPriority(MemoryPriority value)
    {
        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                SyncPrioritySelection();
                return;
            }

            var result = service.SetMemoryPriority(Pid, value);
            if (result.Success)
            {
                _memoryPriority = value;
                _memoryTouched = true;
                _owner.ReportInfo(Loc["status.priorityApplied"]);
            }
            else
            {
                _owner.ReportError(result.Error);
                SyncPrioritySelection();
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
            SyncPrioritySelection();
        }
    }

    private void SyncPrioritySelection()
    {
        _suppressPriority = true;
        try
        {
            SelectedCpuPriority = PriorityCatalog.CpuOption(Priority);
            SelectedIoPriority = PriorityCatalog.IoOption(_ioPriority);
            SelectedMemoryPriority = PriorityCatalog.MemoryOption(_memoryPriority);
        }
        finally
        {
            _suppressPriority = false;
        }
    }

    private void ToggleSuspend()
    {
        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                return;
            }

            var result = IsSuspended ? service.Resume(Pid) : service.Suspend(Pid);
            if (result.Success)
            {
                IsSuspended = !IsSuspended;
                _owner.ReportInfo(Loc[IsSuspended ? "status.suspendedDone" : "status.resumedDone"]);
            }
            else
            {
                _owner.ReportError(result.Error);
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private void Terminate()
    {
        var confirmation = string.Format(Loc["confirm.terminate"], Name, Pid);
        if (MessageBox.Show(
                confirmation,
                Loc["confirm.terminateTitle"],
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                return;
            }

            var result = service.Terminate(Pid);
            if (result.Success)
            {
                _owner.ReportInfo(Loc["status.terminated"]);
                _owner.RemoveProcess(Pid);
            }
            else
            {
                _owner.ReportError(result.Error);
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private async Task RestartAsync()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath))
        {
            _owner.ReportError(Loc["error.pathUnavailable"]);
            return;
        }

        var confirmation = string.Format(Loc["confirm.restart"], Name, Pid);
        if (MessageBox.Show(
                confirmation,
                Loc["confirm.restartTitle"],
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                return;
            }

            var terminated = service.Terminate(Pid);
            if (!terminated.Success)
            {
                _owner.ReportError(terminated.Error);
                return;
            }

            await Task.Delay(700).ConfigureAwait(true);

            var arguments = ExtractArguments();
            var workingDirectory = Path.GetDirectoryName(ExecutablePath);
            var started = service.StartNew(ExecutablePath, arguments, workingDirectory);
            if (started.Success)
            {
                _owner.ReportInfo(Loc["status.restarted"]);
                _ = _owner.RefreshAsync();
            }
            else
            {
                _owner.ReportError(started.Error);
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private string? ExtractArguments()
    {
        var commandLine = CommandLine?.Trim();
        var executable = ExecutablePath;
        if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        if (commandLine.StartsWith('"'))
        {
            var closing = commandLine.IndexOf('"', 1);
            if (closing > 0)
            {
                var quotedPath = commandLine[1..closing];
                if (string.Equals(quotedPath, executable, StringComparison.OrdinalIgnoreCase))
                {
                    return TrimOrNull(commandLine[(closing + 1)..]);
                }
            }

            return null;
        }

        if (commandLine.StartsWith(executable, StringComparison.OrdinalIgnoreCase))
        {
            return TrimOrNull(commandLine[executable.Length..]);
        }

        var space = commandLine.IndexOf(' ');
        return space > 0 ? TrimOrNull(commandLine[(space + 1)..]) : null;
    }

    private static string? TrimOrNull(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void Trim()
    {
        try
        {
            var service = AppServices.Processes;
            if (service is null)
            {
                _owner.ReportError(Loc["error.noProcessService"]);
                return;
            }

            var result = service.TrimWorkingSet(Pid);
            if (result.Success)
            {
                _owner.ReportInfo(Loc["status.trimmed"]);
            }
            else
            {
                _owner.ReportError(result.Error);
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private void RequestIcon()
    {
        var path = ExecutablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            // No executable path (protected process): keep the neutral glyph.
            return;
        }

        if (string.Equals(path, _iconPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _iconPath = path;
        _ = LoadIconAsync(path);
    }

    private async Task LoadIconAsync(string path)
    {
        var icon = await Task.Run(() => ProcessIconCache.Get(path)).ConfigureAwait(true);
        if (string.Equals(ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
        {
            Icon = icon;
        }
    }

    private void SetPriorityFromMenu(object? parameter)
    {
        if (parameter is CpuPriority priority)
        {
            ApplyCpuPriority(priority);
        }
    }

    private void OpenFile()
    {
        if (!HasExecutablePath || !File.Exists(ExecutablePath))
        {
            _owner.ReportError(Loc["error.pathUnavailable"]);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ExecutablePath!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private void OpenFileLocation()
    {
        if (!HasExecutablePath)
        {
            _owner.ReportError(Loc["error.pathUnavailable"]);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{ExecutablePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private void CopyPath()
    {
        if (!HasExecutablePath)
        {
            _owner.ReportError(Loc["error.nothingToCopy"]);
            return;
        }

        try
        {
            Clipboard.SetText(ExecutablePath!);
            _owner.ReportInfo(Loc["status.copied"]);
        }
        catch (Exception ex)
        {
            _owner.ReportError(string.IsNullOrWhiteSpace(ex.Message) ? Loc["error.clipboard"] : ex.Message);
        }
    }

    private void CopyCommandLine()
    {
        if (string.IsNullOrWhiteSpace(CommandLine))
        {
            _owner.ReportError(Loc["error.nothingToCopy"]);
            return;
        }

        try
        {
            Clipboard.SetText(CommandLine);
            _owner.ReportInfo(Loc["status.copied"]);
        }
        catch (Exception ex)
        {
            _owner.ReportError(string.IsNullOrWhiteSpace(ex.Message) ? Loc["error.clipboard"] : ex.Message);
        }
    }

    private void SaveRule() => SaveRuleCore(false);

    private void ApplyExistingRule() => SaveRuleCore(true);

    private void SaveRuleCore(bool updateOnly)
    {
        try
        {
            var rules = AppServices.Rules;
            if (rules is null)
            {
                _owner.ReportError(Loc["error.noRulesService"]);
                return;
            }

            var existing = rules.FindFor(ExecutablePath, Name);
            if (updateOnly && existing is null)
            {
                _owner.ReportError(Loc["process.noRule"]);
                return;
            }

            var rule = existing?.Clone() ?? new AffinityRule();
            rule.ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? Name : ExecutablePath!;
            rule.DisplayName = Name;
            rule.AffinityMask = AffinityMask;

            if (_cpuTouched)
            {
                rule.Priority = _priority;
            }

            if (_ioTouched)
            {
                rule.IoPriority = _ioPriority;
            }

            if (_memoryTouched)
            {
                rule.MemoryPriority = _memoryPriority;
            }

            rule.ApplyOnStart = true;
            rule.KeepEnforced = true;
            rule.Enabled = true;
            rule.UpdatedAt = DateTime.UtcNow;

            var saved = rules.Upsert(rule);
            _ruleId = saved.Id;
            _hasRule = true;
            OnPropertyChanged(nameof(HasRule));
            OnPropertyChanged(nameof(RuleStatusText));

            var applied = rules.Apply(_snapshot, saved);
            try
            {
                rules.Save();
            }
            catch
            {
                // The in-memory rule is already applied; persistence is best effort.
            }

            if (applied.Success)
            {
                _owner.ReportInfo(Loc[updateOnly ? "status.ruleUpdated" : "status.ruleSaved"]);
            }
            else
            {
                _owner.ReportError(applied.Error);
            }
        }
        catch (Exception ex)
        {
            _owner.ReportError(ex.Message);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0";
        }

        double value = bytes;
        if (value < 1024)
        {
            return $"{value:0} B";
        }

        if (value < 1024 * 1024)
        {
            return $"{value / 1024:0.#} KB";
        }

        if (value < 1024L * 1024 * 1024)
        {
            return $"{value / (1024 * 1024):0.#} MB";
        }

        return $"{value / (1024d * 1024 * 1024):0.##} GB";
    }
}
