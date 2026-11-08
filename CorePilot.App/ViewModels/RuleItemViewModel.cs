using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CorePilot.App.Localization;
using CorePilot.Core.Models;

namespace CorePilot.App.ViewModels;

/// <summary>Self-contained INotifyPropertyChanged base for the rules and settings screens.</summary>
public abstract class PageViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void OnAllChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>Formats an affinity mask as "0-3, 6".</summary>
public static class CpuMaskFormatter
{
    public static string Format(ulong mask)
    {
        var indexes = CpuTopology.MaskToIndexes(mask).ToList();
        if (indexes.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var start = indexes[0];
        var previous = indexes[0];

        for (var i = 1; i <= indexes.Count; i++)
        {
            var atEnd = i == indexes.Count;
            if (!atEnd && indexes[i] == previous + 1)
            {
                previous = indexes[i];
                continue;
            }

            if (previous - start >= 2)
            {
                parts.Add($"{start}-{previous}");
            }
            else if (previous == start)
            {
                parts.Add(start.ToString());
            }
            else
            {
                parts.Add(start.ToString());
                parts.Add(previous.ToString());
            }

            if (!atEnd)
            {
                start = indexes[i];
                previous = indexes[i];
            }
        }

        return string.Join(", ", parts);
    }

    public static ulong Mask(IEnumerable<CoreItemViewModel> cores)
    {
        ulong mask = 0;
        foreach (var core in cores)
        {
            if (core.IsSelected && core.Index is >= 0 and < 64)
            {
                mask |= 1UL << core.Index;
            }
        }

        return mask;
    }
}

/// <summary>Localized priority names.</summary>
public static class PriorityNames
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public static string Format(CpuPriority value)
    {
        var key = value switch
        {
            CpuPriority.Idle => "idle",
            CpuPriority.BelowNormal => "belowNormal",
            CpuPriority.AboveNormal => "aboveNormal",
            CpuPriority.High => "high",
            CpuPriority.RealTime => "realTime",
            _ => "normal"
        };

        return Loc["rules.prio." + key];
    }

    public static string Format(IoPriority value)
    {
        var key = value switch
        {
            IoPriority.VeryLow => "veryLow",
            IoPriority.Low => "low",
            IoPriority.High => "high",
            _ => "normal"
        };

        return Loc["rules.prio." + key];
    }

    public static string Format(MemoryPriority value)
    {
        var key = value switch
        {
            MemoryPriority.VeryLow => "veryLow",
            MemoryPriority.Low => "low",
            MemoryPriority.Medium => "medium",
            MemoryPriority.High => "high",
            _ => "veryHigh"
        };

        return Loc["rules.prio." + key];
    }
}

/// <summary>One rule card in the rules list.</summary>
public sealed class RuleItemViewModel : PageViewModel
{
    private readonly Action<RuleItemViewModel>? _save;
    private AffinityRule _rule;
    private bool _suppress;
    private string _statusText = string.Empty;

    internal RuleItemViewModel(AffinityRule rule, Action<RuleItemViewModel>? save)
    {
        _rule = rule;
        _save = save;
    }

    public AffinityRule Rule => _rule;

    public Guid Id => _rule.Id;

    public string Name => string.IsNullOrWhiteSpace(_rule.DisplayName)
        ? System.IO.Path.GetFileNameWithoutExtension(_rule.ExecutablePath)
        : _rule.DisplayName;

    public string Path => _rule.ExecutablePath;

    public string MaskText => _rule.AffinityMask == 0
        ? LocalizationService.Instance["rules.mask.none"]
        : CpuMaskFormatter.Format(_rule.AffinityMask);

    public string PriorityText
    {
        get
        {
            var parts = new List<string>();
            if (_rule.Priority.HasValue)
            {
                parts.Add($"{LocalizationService.Instance["rules.card.cpu"]}: {PriorityNames.Format(_rule.Priority.Value)}");
            }

            if (_rule.IoPriority.HasValue)
            {
                parts.Add($"{LocalizationService.Instance["rules.card.io"]}: {PriorityNames.Format(_rule.IoPriority.Value)}");
            }

            if (_rule.MemoryPriority.HasValue)
            {
                parts.Add($"{LocalizationService.Instance["rules.card.memory"]}: {PriorityNames.Format(_rule.MemoryPriority.Value)}");
            }

            return parts.Count == 0
                ? LocalizationService.Instance["rules.prio.default"]
                : string.Join(" · ", parts);
        }
    }

    public bool Enabled
    {
        get => _rule.Enabled;
        set => Update(rule => rule.Enabled = value, _rule.Enabled, value);
    }

    public bool ApplyOnStart
    {
        get => _rule.ApplyOnStart;
        set => Update(rule => rule.ApplyOnStart = value, _rule.ApplyOnStart, value);
    }

    public bool KeepEnforced
    {
        get => _rule.KeepEnforced;
        set => Update(rule => rule.KeepEnforced = value, _rule.KeepEnforced, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>Refreshes the card from a stored rule without triggering a save.</summary>
    public void Refresh(AffinityRule rule)
    {
        _suppress = true;
        _rule = rule;
        OnAllChanged();
        _suppress = false;
    }

    private void Update(Action<AffinityRule> mutate, bool current, bool value)
    {
        if (current == value)
        {
            return;
        }

        mutate(_rule);
        _rule.UpdatedAt = DateTime.UtcNow;
        OnAllChanged();

        if (_suppress)
        {
            return;
        }

        _save?.Invoke(this);
    }
}
