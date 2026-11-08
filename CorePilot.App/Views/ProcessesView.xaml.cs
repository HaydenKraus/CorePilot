using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CorePilot.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace CorePilot.App.Views;

public partial class ProcessesView : UserControl
{
    private const double FixedColumnsWidth = 460;

    private INotifyPropertyChanged? _viewModel;

    public ProcessesView()
    {
        InitializeComponent();
        ProcessGrid.SizeChanged += (_, _) => ResizeNameColumn();
        PreviewKeyDown += OnPreviewKeyDown;
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as INotifyPropertyChanged;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        if (DataContext is ProcessesViewModel { HasSelection: true })
        {
            AnimateDrawer();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessesViewModel.HasSelection) &&
            DataContext is ProcessesViewModel { HasSelection: true })
        {
            AnimateDrawer();
        }
    }

    /// <summary>The details pane floats in from the right whenever a process is picked.</summary>
    private void AnimateDrawer()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        DetailsPanel.BeginAnimation(OpacityProperty, fade);

        if (DetailsPanel.RenderTransform is TranslateTransform slide)
        {
            var move = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            slide.BeginAnimation(TranslateTransform.XProperty, move);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ProcessesViewModel viewModel && viewModel.HasSelection)
        {
            viewModel.SelectedProcess = null;
            e.Handled = true;
        }
    }

    /// <summary>Sorts siblings inside the process tree instead of flattening the hierarchy.</summary>
    private void ProcessGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ProcessesViewModel viewModel)
        {
            return;
        }

        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var column in ProcessGrid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = direction;
        viewModel.SetSort(e.Column.SortMemberPath, direction);
    }

    /// <summary>
    /// DataGrid star columns do not work inside grouping (they measure with infinite width),
    /// so the name column is sized manually to the remaining viewport space.
    /// </summary>
    private void ResizeNameColumn()
    {
        if (ProcessGrid.Columns.Count == 0)
        {
            return;
        }

        var available = ProcessGrid.ActualWidth - FixedColumnsWidth - 14;
        ProcessGrid.Columns[0].Width = new DataGridLength(Math.Max(140, available));
    }

    /// <summary>Right-clicking a row should act on that row, as in Task Manager.</summary>
    private void ProcessGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (source is null)
        {
            return;
        }

        var row = ItemsControl.ContainerFromElement(ProcessGrid, source) as DataGridRow;
        if (row is not null)
        {
            row.IsSelected = true;
            ProcessGrid.SelectedItem = row.Item;
            row.Focus();
        }
    }
}
