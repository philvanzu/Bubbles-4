using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Bubbles4.ViewModels;

namespace Bubbles4.Behaviors;

public class AutoScrollToSelectedBehavior : Behavior<ItemsRepeater>
{
    public static bool SuppressNextAutoScroll;

    private ViewModelBase? _viewModel;
    private int _lastScrolledIndex = -1;
    private bool _isScrollPending;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            // Observe DataContext changes explicitly
            AssociatedObject.GetObservable(Control.DataContextProperty)
                .Subscribe(dc => TryHookIntoViewModel(dc));

            // Call once immediately in case DataContext is already set
            TryHookIntoViewModel(AssociatedObject.DataContext);
        }
    }

    private void TryHookIntoViewModel(object? dc)
    {
        if (_viewModel is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = dc as ViewModelBase;

        if (_viewModel is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        int index = -1;

        if (e.PropertyName == nameof(LibraryViewModel.SelectedItem))
        {
            if (_viewModel is LibraryViewModel libvm)
            {
                index = libvm.GetBookIndex(libvm.SelectedItem);
            }
        }
        else if (e.PropertyName == nameof(BookViewModel.SelectedPage))
        {
            if (_viewModel is BookViewModel bookvm)
            {
                index = bookvm.GetPageIndex(bookvm.SelectedPage);
            }
        }

        if (index != -1)
            ScheduleScrollIntoView(index);
    }

    private void ScheduleScrollIntoView(int index)
    {
        if (SuppressNextAutoScroll)
        {
            SuppressNextAutoScroll = false;
            return;
        }

        // Skip if scrolling to same index again
        if (_lastScrolledIndex == index)
            return;

        // Optionally, skip autoscroll for item zero if it's problematic
        // if (index == 0) return;

        if (_isScrollPending)
            return;

        _isScrollPending = true;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ScrollIntoView(index);
            _lastScrolledIndex = index;
            _isScrollPending = false;
        }, DispatcherPriority.Background);
    }

    private void ScrollIntoView(int index)
    {
        var repeater = AssociatedObject;
        if (repeater == null)
            return;

        var container = repeater.TryGetElement(index);
        if (container != null)
        {
            container.BringIntoView();
        }
    }
}

public class DummyBehavior : Behavior<ItemsRepeater>
{
    protected override void OnAttached()
    {
        Console.WriteLine("DummyBehavior attached");
    }
}
/*
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using Bubbles4.ViewModels;

namespace Bubbles4.Behaviors;

public class AutoScrollToSelectedBehavior : Behavior<ItemsRepeater>
{
    public static bool SuppressNextAutoScroll;
    private ViewModelBase? _viewModel;
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            // Observe DataContext changes explicitly
            AssociatedObject.GetObservable(Control.DataContextProperty)
                .Subscribe(dc => TryHookIntoViewModel(dc));

            // Call once immediately in case DataContext is already set
            TryHookIntoViewModel(AssociatedObject.DataContext);
        }
    }
    private void TryHookIntoViewModel(object? dc)
    {
        if (_viewModel is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            
        _viewModel = dc as ViewModelBase;

        if (_viewModel is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedItem))
        {
            if (_viewModel is LibraryViewModel libvm)
            {
                int idx = libvm.GetBookIndex(libvm.SelectedItem) ;
                if(idx != -1)
                    ScrollIntoView(idx);
            }
            
        }
        else if (e.PropertyName == nameof(BookViewModel.SelectedPage))
        {
            if (_viewModel is BookViewModel bookvm)
            {
                int idx = bookvm.GetPageIndex(bookvm.SelectedPage) ;
                if(idx != -1)
                    ScrollIntoView(idx);
            }
            
        }
    }


    private void ScrollIntoView(int index)
    {
        if (SuppressNextAutoScroll)
        {
            SuppressNextAutoScroll = false;
            return;
        }
        const double itemWidth = 132;
        const double itemHeight = 152;

        var repeater = AssociatedObject;
        var scrollViewer = FindParentScrollViewer(repeater);
        if (repeater == null || scrollViewer == null)
            return;

        var viewport = scrollViewer.Viewport;
        int columns = Math.Max(1, (int)(viewport.Width / itemWidth));
        int rows = Math.Max(1, (int)(viewport.Height / itemHeight));
        int row = index / columns;

        double itemTop = row * itemHeight;
        double itemBottom = itemTop + itemHeight;

        double offsetY = scrollViewer.Offset.Y;
        double viewportHeight = viewport.Height;

        if (itemBottom > offsetY + viewportHeight)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, itemBottom - viewportHeight);
        }
        else if (itemTop < offsetY)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, itemTop);
        }
    }

    private ScrollViewer? FindParentScrollViewer(Control? element)
    {
        while (element != null && element is not ScrollViewer)
        {
            element = element.Parent as Control;
        }
        return element as ScrollViewer;
    }
}
*/