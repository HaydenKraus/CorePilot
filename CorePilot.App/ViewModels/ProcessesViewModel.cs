using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CorePilot.App.Localization;
using CorePilot.Core.Abstractions;
using CorePilot.Core.Models;
using ICollectionView = System.ComponentModel.ICollectionView;

namespace CorePilot.App.ViewModels;

/// <summary>Physical core grouping handed to the affinity editor.</summary>
internal sealed class CoreDefinition
{
    public CoreDefinition(
        int packageId,
        int physicalCoreId,
        int packageCount,
        bool hasPerformance,
        bool hasEfficiency,
        IReadOnlyList<LogicalProcessor> processors)
    {
        PackageId = packageId;
        PhysicalCoreId = physicalCoreId;
        PackageCount = packageCount;
        HasPerformance = hasPerformance;
        HasEfficiency = hasEfficiency;
        Processors = processors;
    }

    public int PackageId { get; }

    public int PhysicalCoreId { get; }

    public int PackageCount { get; }

    public bool HasPerformance { get; }

    public bool HasEfficiency { get; }

    public bool ShowEfficiency => HasPerformance && HasEfficiency;

    public IReadOnlyList<LogicalProcessor> Processors { get; }
}

/// <summary>Owns the live process list, filtering, refresh loop and status messages.</summary>
public sealed class ProcessesViewModel : ObservableObject, IDisposable
{
    private static LocalizationService Loc => LocalizationService.Instance;

    private readonly ObservableCollection<ProcessItemViewModel> _items = new();
    private readonly Dictionary<int, List<ProcessItemViewModel>> _childrenByPid = new();
    private readonly List<ProcessItemViewModel> _treeRoots = new();
    private int _treeOrdinal;
    private string _sortProperty = nameof(ProcessItemViewModel.Name);
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private readonly List<CoreDefinition> _coreTemplate = new();

    private CpuTopology? _topology;
    private IRuleService? _rulesService;
    private bool _started;
    private bool _refreshing;
    private bool _disposed;
    private bool _isActive = true;
    private bool _waitingForServices;
    private bool _isElevated;
    private string _searchText = string.Empty;
    private bool _onlyWithRules;
    private ProcessItemViewModel? _selectedProcess;
    private int _visibleCount;
    private DateTime? _lastUpdated;
    private string _statusText = string.Empty;
    private string? _statusKey = "status.ready";
    private bool _hasError;

    public ProcessesViewModel()
    {
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterProcess;

        if (_view is ListCollectionView listView)
        {
            listView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProcessItemViewModel.IsInAppGroup)));
            listView.SortDescriptions.Add(new SortDescription(nameof(ProcessItemViewModel.IsInAppGroup), ListSortDirection.Descending));
            listView.SortDescriptions.Add(new SortDescription(nameof(ProcessItemViewModel.TreeSortKey), ListSortDirection.Ascending));
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _timer.Tick += (_, _) =>
        {
            if (_isActive)
            {
                _ = RefreshAsync();
            }
        };

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        RunTaskCommand = new RelayCommand(RunTask);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ClearSelectionCommand = new RelayCommand(() => SelectedProcess = null);
        ToggleTreeCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProcessItemViewModel item)
            {
                ToggleExpansion(item);
            }
        });
    }

    public ICollectionView View => _view;

    public RelayCommand RefreshCommand { get; }

    public RelayCommand RunTaskCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand ToggleTreeCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            _view.Refresh();
            UpdateCounts();
        }
    }

    public bool OnlyWithRules
    {
        get => _onlyWithRules;
        set
        {
            if (!SetProperty(ref _onlyWithRules, value))
            {
                return;
            }

            _view.Refresh();
            UpdateCounts();
        }
    }

    public ProcessItemViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (!SetProperty(ref _selectedProcess, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelection));
            if (value is not null)
            {
                _ = value.EnsureCommandLineAsync();
            }
        }
    }

    public bool HasSelection => SelectedProcess is not null;

    public bool IsEmpty => VisibleCount == 0;

    public int VisibleCount
    {
        get => _visibleCount;
        private set
        {
            if (!SetProperty(ref _visibleCount, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsBusy => _refreshing;

    public bool IsElevated
    {
        get => _isElevated;
        private set => SetProperty(ref _isElevated, value);
    }

    public DateTime? LastUpdated
    {
        get => _lastUpdated;
        private set
        {
            if (!SetProperty(ref _lastUpdated, value))
            {
                return;
            }

            OnPropertyChanged(nameof(LastUpdatedText));
        }
    }

    public string StatusMessage => _statusKey is not null ? Loc[_statusKey] : _statusText;

    public bool HasError => _hasError;

    public string ProcessCountText => string.Format(Loc["status.processCount"], _items.Count);

    public string LastUpdatedText => _lastUpdated is { } updated
        ? string.Format(Loc["status.updated"], updated.ToString("HH:mm:ss"))
        : Loc["status.waiting"];

    internal IReadOnlyList<CoreDefinition> CoreTemplate => _coreTemplate;

    internal int LogicalProcessorCount => _topology?.Count ?? 0;

    internal ulong FullAffinityMask => _topology?.FullMask ?? 0;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _isActive = true;
        EnsureRulesSubscription();
        UpdateTimerInterval();
        Loc.PropertyChanged += OnLocalizationChanged;
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Pauses the periodic scan while the window is hidden or minimized; the
    /// watcher keeps enforcing rules in the background.
    /// </summary>
    public void SetActive(bool active)
    {
        if (_disposed || _isActive == active)
        {
            return;
        }

        _isActive = active;
        if (active)
        {
            _timer.Start();
            _ = RefreshAsync();
        }
        else
        {
            _timer.Stop();
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed || _refreshing)
        {
            return;
        }

        var service = AppServices.Processes;
        if (service is null)
        {
            _waitingForServices = true;
            SetStatusKey("status.waiting", false);
            return;
        }

        _refreshing = true;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            EnsureRulesSubscription();
            UpdateTimerInterval();
            EnsureTopology();

            var snapshots = await Task.Run(() => service.GetProcesses()).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            ApplySnapshots(snapshots);
            LastUpdated = DateTime.Now;

            if (IsElevated != service.IsElevated)
            {
                IsElevated = service.IsElevated;
            }

            if (_waitingForServices)
            {
                _waitingForServices = false;
                SetStatusKey("status.ready", false);
            }
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
        }
        finally
        {
            _refreshing = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public void ReportError(string? message) =>
        SetStatusText(string.IsNullOrWhiteSpace(message) ? Loc["common.error"] : message, true);

    public void ReportInfo(string message) => SetStatusText(message, false);

    internal void RemoveProcess(int pid)
    {
        var item = _items.FirstOrDefault(p => p.Pid == pid);
        if (item is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedProcess, item))
        {
            SelectedProcess = null;
        }

        _items.Remove(item);
        RebuildTree();
        ApplyCollapseState();
        _view.Refresh();
        UpdateCounts();
    }

    internal void RefreshRuleFlags()
    {
        foreach (var item in _items)
        {
            item.RefreshRuleState();
        }

        _view.Refresh();
        UpdateCounts();
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ProcessCountText));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(StatusMessage));
        ProcessItemViewModel.RefreshPriorityLabels();

        foreach (var item in _items)
        {
            item.RefreshLocalization();
        }

        _view.Refresh();
    }

    private void ApplySnapshots(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var existing = new Dictionary<int, ProcessItemViewModel>(_items.Count);
        foreach (var item in _items)
        {
            existing[item.Pid] = item;
        }

        var structureChanged = false;
        foreach (var snapshot in snapshots)
        {
            if (existing.TryGetValue(snapshot.Pid, out var item))
            {
                structureChanged |= item.Update(snapshot);
            }
            else
            {
                item = new ProcessItemViewModel(snapshot, this);
                existing[snapshot.Pid] = item;
                _items.Add(item);
                structureChanged = true;
            }
        }

        var live = new HashSet<int>(snapshots.Select(s => s.Pid));
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (live.Contains(_items[i].Pid))
            {
                continue;
            }

            if (ReferenceEquals(SelectedProcess, _items[i]))
            {
                SelectedProcess = null;
            }

            _items.RemoveAt(i);
            structureChanged = true;
        }

        // Rebuild the parent/child tree and apply the current collapse state.
        // Membership always invalidates the tree; parent links are refreshed too
        // because a new process can attach to an existing one without new PIDs.
        RebuildTree();
        ApplyCollapseState();

        // Re-grouping/re-sorting the whole collection is only required when its
        // membership or the group key changed; values alone update through bindings.
        if (structureChanged)
        {
            _view.Refresh();
            OnPropertyChanged(nameof(ProcessCountText));
        }

        UpdateCounts();
    }

    /// <summary>Rebuilds the process forest from parent PIDs and assigns tree metadata.</summary>
    private void RebuildTree()
    {
        var byPid = new Dictionary<int, ProcessItemViewModel>(_items.Count);
        foreach (var item in _items)
        {
            byPid[item.Pid] = item;
        }

        _treeRoots.Clear();
        _childrenByPid.Clear();

        foreach (var item in _items)
        {
            ProcessItemViewModel? parent = null;
            if (!item.IsApp
                && item.ParentPid > 0
                && item.ParentPid != item.Pid
                && byPid.TryGetValue(item.ParentPid, out var candidate)
                && !IsShellProcess(candidate)
                && !IsAncestor(item.Pid, candidate, byPid))
            {
                parent = candidate;
            }

            if (parent is null)
            {
                _treeRoots.Add(item);
                continue;
            }

            if (!_childrenByPid.TryGetValue(parent.Pid, out var children))
            {
                children = new List<ProcessItemViewModel>();
                _childrenByPid[parent.Pid] = children;
            }

            children.Add(item);
        }

        foreach (var children in _childrenByPid.Values)
        {
            children.Sort(CompareProcesses);
        }

        _treeRoots.Sort(CompareProcesses);

        _treeOrdinal = 0;
        foreach (var root in _treeRoots)
        {
            AssignTreeNode(root, 0, Array.Empty<bool>(), true, root);
        }
    }

    private int CompareProcesses(ProcessItemViewModel left, ProcessItemViewModel right)
    {
        var result = _sortProperty switch
        {
            nameof(ProcessItemViewModel.Pid) => left.Pid.CompareTo(right.Pid),
            nameof(ProcessItemViewModel.CpuPercent) => left.CpuPercent.CompareTo(right.CpuPercent),
            nameof(ProcessItemViewModel.WorkingSetBytes) => left.WorkingSetBytes.CompareTo(right.WorkingSetBytes),
            nameof(ProcessItemViewModel.Priority) => left.Priority.CompareTo(right.Priority),
            nameof(ProcessItemViewModel.AffinityCount) => left.AffinityCount.CompareTo(right.AffinityCount),
            nameof(ProcessItemViewModel.IsSuspended) => left.IsSuspended.CompareTo(right.IsSuspended),
            _ => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        };

        if (result == 0)
        {
            result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        return _sortDirection == ListSortDirection.Descending ? -result : result;
    }

    private static bool IsShellProcess(ProcessItemViewModel process) =>
        string.Equals(process.Name, "explorer", StringComparison.OrdinalIgnoreCase);

    private static bool IsAncestor(int pid, ProcessItemViewModel candidate, IReadOnlyDictionary<int, ProcessItemViewModel> byPid)
    {
        var current = candidate;
        for (var guard = 0; guard < 64; guard++)
        {
            if (current.Pid == pid)
            {
                return true;
            }

            if (current.ParentPid <= 0 || !byPid.TryGetValue(current.ParentPid, out var next))
            {
                return false;
            }

            current = next;
        }

        return true;
    }

    private void AssignTreeNode(
        ProcessItemViewModel item,
        int depth,
        bool[] lines,
        bool isLast,
        ProcessItemViewModel root)
    {
        item.Depth = depth;
        item.IsLastChild = isLast;
        if (!item.AncestorLines.SequenceEqual(lines))
        {
            item.AncestorLines = lines;
        }

        item.IsInAppGroup = root.IsApp;
        item.TreeSortKey = (_treeOrdinal++).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

        if (!_childrenByPid.TryGetValue(item.Pid, out var children) || children.Count == 0)
        {
            item.HasChildren = false;
            return;
        }

        item.HasChildren = true;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var childIsLast = index == children.Count - 1;
            var childLines = depth == 0 ? Array.Empty<bool>() : AppendLine(lines, !isLast);
            AssignTreeNode(child, depth + 1, childLines, childIsLast, root);
        }
    }

    private static bool[] AppendLine(bool[] lines, bool value)
    {
        var result = new bool[lines.Length + 1];
        Array.Copy(lines, result, lines.Length);
        result[^1] = value;
        return result;
    }

    /// <summary>Marks rows hidden behind a collapsed ancestor.</summary>
    private void ApplyCollapseState()
    {
        foreach (var root in _treeRoots)
        {
            ApplyCollapseState(root, false);
        }
    }

    private void ApplyCollapseState(ProcessItemViewModel item, bool hidden)
    {
        item.IsHiddenByCollapse = hidden;

        if (!_childrenByPid.TryGetValue(item.Pid, out var children))
        {
            return;
        }

        var childrenHidden = hidden || !item.IsExpanded;
        foreach (var child in children)
        {
            ApplyCollapseState(child, childrenHidden);
        }
    }

    internal void OnExpansionChanged(ProcessItemViewModel item)
    {
        ApplyCollapseState();

        // Refreshing the collection view synchronously from a ToggleButton binding
        // gets swallowed by WPF's re-entrancy guard, so defer it to the dispatcher.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _view.Refresh();
                UpdateCounts();
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Applies a column sort while keeping every parent above its children.</summary>
    public void SetSort(string? property, ListSortDirection direction)
    {
        if (string.IsNullOrWhiteSpace(property))
        {
            return;
        }

        _sortProperty = property;
        _sortDirection = direction;
        RebuildTree();
        ApplyCollapseState();
        _view.Refresh();
    }

    /// <summary>Flips the expansion state of a process branch (invoked by the row chevron).</summary>
    public void ToggleExpansion(ProcessItemViewModel item)
    {
        if (item.HasChildren)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    private bool FilterProcess(object candidate)
    {
        if (candidate is not ProcessItemViewModel item)
        {
            return false;
        }

        if (_onlyWithRules && !item.HasRule)
        {
            return false;
        }

        if (item.IsHiddenByCollapse && string.IsNullOrWhiteSpace(_searchText))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        var query = _searchText.Trim();
        return Contains(item.Name, query)
               || item.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
               || Contains(item.ExecutablePath, query)
               || Contains(item.CommandLine, query)
               || Contains(item.Owner, query);
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void UpdateCounts()
    {
        VisibleCount = _view.Cast<object>().Count();
        OnPropertyChanged(nameof(ProcessCountText));
    }

    private void SetStatusKey(string key, bool error)
    {
        _statusKey = key;
        _statusText = string.Empty;
        _hasError = error;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private void SetStatusText(string text, bool error)
    {
        _statusKey = null;
        _statusText = text;
        _hasError = error;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private void UpdateTimerInterval()
    {
        var settingsService = AppServices.Settings;
        var milliseconds = settingsService?.Settings?.RefreshIntervalMs ?? 1500;
        if (milliseconds < 500)
        {
            milliseconds = 500;
        }

        var interval = TimeSpan.FromMilliseconds(milliseconds);
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }
    }

    private void EnsureTopology()
    {
        try
        {
            var topologyService = AppServices.Topology;
            if (topologyService is null)
            {
                return;
            }

            var topology = topologyService.GetTopology();
            if (topology is null || topology.Count == 0 || ReferenceEquals(topology, _topology))
            {
                return;
            }

            _topology = topology;

            var hasPerformance = topology.LogicalProcessors.Any(p => p.IsPerformanceCore);
            var hasEfficiency = topology.LogicalProcessors.Any(p => !p.IsPerformanceCore);
            var packageCount = topology.LogicalProcessors.Select(p => p.PackageId).Distinct().Count();

            _coreTemplate.Clear();
            foreach (var group in topology.LogicalProcessors
                         .GroupBy(p => (p.PackageId, p.PhysicalCoreId))
                         .OrderBy(g => g.Key.PackageId)
                         .ThenBy(g => g.Key.PhysicalCoreId))
            {
                var processors = group.OrderBy(p => p.Index).ToArray();
                _coreTemplate.Add(new CoreDefinition(
                    group.Key.PackageId,
                    group.Key.PhysicalCoreId,
                    packageCount,
                    processors.Any(p => p.IsPerformanceCore),
                    processors.Any(p => !p.IsPerformanceCore),
                    processors));
            }

            OnPropertyChanged(nameof(FullAffinityMask));
            OnPropertyChanged(nameof(LogicalProcessorCount));

            foreach (var item in _items)
            {
                item.EnsureCoreGroups();
            }
        }
        catch
        {
            // Topology is a convenience; affinity editing degrades gracefully without it.
        }
    }

    private void EnsureRulesSubscription()
    {
        var rules = AppServices.Rules;
        if (ReferenceEquals(rules, _rulesService))
        {
            return;
        }

        if (_rulesService is not null)
        {
            _rulesService.Changed -= OnRulesChanged;
        }

        _rulesService = rules;
        if (_rulesService is not null)
        {
            _rulesService.Changed += OnRulesChanged;
        }
    }

    private void OnRulesChanged(object? sender, EventArgs e) => RefreshRuleFlags();

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "Item[]")
        {
            RefreshLocalization();
        }
    }

    private void RunTask()
    {
        var service = AppServices.Processes;
        if (service is null)
        {
            ReportError(Loc["error.noProcessService"]);
            return;
        }

        try
        {
            var dialog = new CorePilot.App.Controls.RunTaskDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var result = service.StartNew(dialog.ExecutablePath, dialog.Arguments, dialog.WorkingDirectory);
            if (result.Success)
            {
                ReportInfo(Loc["status.taskStarted"]);
                _ = RefreshAsync();
            }
            else
            {
                ReportError(result.Error);
            }
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        Loc.PropertyChanged -= OnLocalizationChanged;

        if (_rulesService is not null)
        {
            _rulesService.Changed -= OnRulesChanged;
            _rulesService = null;
        }
    }
}
