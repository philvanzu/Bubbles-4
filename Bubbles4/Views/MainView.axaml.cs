using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bubbles4.Controls;
using Bubbles4.Models;
using Bubbles4.Services;
using Bubbles4.ViewModels;
using SDL2;

namespace Bubbles4.Views;

public partial class MainView : UserControl
{
    private Panel? _fullscreenOverlay;
    private ContentControl? _imgViewerContainer;
    private Panel? _originalParent;
    private int _originalIndex;
    private IInputElement? _previousFocusedElement;
    private readonly SdlInputService _sdlInput=new();
    private readonly CancellationTokenSource _cts = new();
    private FastImageViewer? _fastImageViewer;
    
    public MainView()
    {
        InitializeComponent();
        
        // Find your controls by name, assuming you have x:Name on them
        _fullscreenOverlay = this.FindControl<Panel>("FullscreenOverlay");
        _imgViewerContainer = this.FindControl<ContentControl>("ImageViewerContainer");
        if(_imgViewerContainer!= null) _imgViewerContainer.Focusable = true;
        _fastImageViewer = this.FindControl<FastImageViewer>("ImageViewer");

        // Remember original parent and index to restore later
        _originalParent = (Panel)_imgViewerContainer?.Parent!;
        _originalIndex = _originalParent.Children.IndexOf(_imgViewerContainer!);

        // Subscribe to DataContext changes to watch IsFullscreen property
        this.DataContextChanged += MainView_DataContextChanged;
        
        _sdlInput.Initialize();

        _sdlInput.StickUpdated +=  ControllerStickUpdated;
        _sdlInput.ButtonChanged += ControllerButtonChanged;


        Task.Run(async() =>
        {
            try
            {
                await _sdlInput.StartPollingAsync(_cts.Token);
                return ;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Controller polling task cancelled");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

            }

        });


        _imgViewerContainer!.DoubleTapped += ImgViewerDoubleTapped;
        _imgViewerContainer.PointerWheelChanged += ImgViewerMouseWheelChanged;
        _imgViewerContainer.PointerPressed += ImgViewerPointerPressed;
        _imgViewerContainer.PointerReleased += ImgViewerPointerReleased;
        _imgViewerContainer.PointerMoved += ImgViewerPointerMoved;
        _imgViewerContainer.KeyUp += ImgViewerKeyUp;
    }

    


    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _cts.Cancel();
        _sdlInput.Shutdown();
        _cts.Dispose();
        base.OnUnloaded(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var window = this.GetVisualRoot() as Window;
        if (window != null)
        {
            window.AddHandler(InputElement.KeyUpEvent, OnGlobalKeyUp,  RoutingStrategies.Bubble, true);
        }
    }


    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is MainViewModel vm) vm.OnClose();
    }

    private void MainView_DataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFullscreen))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var vm = DataContext as MainViewModel;
                if (vm != null)
                    ToggleFullscreen(vm.IsFullscreen);
            });
        }
    }

    private void ToggleFullscreen(bool fullscreen)
    {
        if(_fullscreenOverlay==null || _imgViewerContainer == null || _originalParent==null) return;
        if (fullscreen)
        {
            _previousFocusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            // Move ImageViewer to fullscreen overlay
            _originalParent.Children.Remove(_imgViewerContainer);
            _fullscreenOverlay.Children.Add(_imgViewerContainer);
            _fullscreenOverlay.IsVisible = true;
            //save reference to the control who currently has focus
            _imgViewerContainer.Focus();
        }
        else
        {
            // Move ImageViewer back to original parent at original position
            _fullscreenOverlay.Children.Remove(_imgViewerContainer);
            _originalParent.Children.Insert(_originalIndex, _imgViewerContainer);
            _fullscreenOverlay.IsVisible = false;
            //restore focus to saved control
            _previousFocusedElement?.Focus();
        }
    }
    private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        if (SearchBox.IsFocused || DataContext is not MainViewModel vm )
            return;
        
        switch (e.Key)
        {
            case Key.Space:
            case Key.Right:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                    vm.PreviousCommand.Execute(null);
                else
                    vm.NextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left:
            case Key.Back:
                vm.PreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (vm.IsFullscreen)
                    vm.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Home:
                vm.FirstPageCommand.Execute(null);
                break;
            case Key.End:
                vm.LastPageCommand.Execute(null);
                break;
        }
    }
    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SearchButton.Command?.Execute(SearchButton.CommandParameter);
        }
    }
    private void ImgViewerKeyUp(object? _, KeyEventArgs e)
    {
        if (_fastImageViewer != null)
        {
            switch (e.Key)
            {
                case Key.H:
                    e.Handled = true;
                    _fastImageViewer.FitHeight();
                    break;
                case  Key.W:
                    e.Handled = true;
                    _fastImageViewer.FitWidth();
                    break;
                case Key.B:
                    e.Handled = true;
                    _fastImageViewer.Fit();
                    break;
                case Key.F:
                    e.Handled = true;
                    _fastImageViewer.FitStock();
                    break;
                case Key.Down :
                    e.Handled = true;
                    _fastImageViewer.OnDownArrowPressed();
                    break;
                case Key.Up :
                    e.Handled = true;
                    _fastImageViewer.OnUpArrowPressed();
                    break;
                case Key.Add:
                    e.Handled = true;
                    _fastImageViewer.Zoom(1);
                    break;
                case Key.Subtract:
                    e.Handled = true;
                    _fastImageViewer.Zoom(-1);
                    break;
            }
        }
    }

    private void ImgViewerPointerMoved(object? s, PointerEventArgs e)
    {
        if (_fastImageViewer != null)
        {
            _fastImageViewer.OnPointerMoved(s, e);
        }
    }

    private void ImgViewerPointerReleased(object? s, PointerReleasedEventArgs e)
    {
        if (_fastImageViewer != null)
            _fastImageViewer.OnPointerReleased(s, e);
    }

    private void ImgViewerPointerPressed(object? s, PointerPressedEventArgs e)
    {
        if (_fastImageViewer != null)
            _fastImageViewer.OnPointerPressed(s, e);
    }

    private void ImgViewerMouseWheelChanged(object? s, PointerWheelEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            vm.Config != null &&
            ( vm.Config.ScrollAction == LibraryConfig.ScrollActions.TurnPage ||
              vm.IsFullscreen == false) )
        {
            if (Math.Abs(e.Delta.Y - (-1.0)) < 0.01f) _ = vm.Next();
                
            else if (Math.Abs(e.Delta.Y - 1.0) < 0.01f) _ = vm.Previous(); 
        }
        else _fastImageViewer!.OnMouseWheel(s, e);
    }

    private void ImgViewerDoubleTapped(object? _, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        { 
            vm.ToggleFullscreenCommand.Execute(null);
        }
    }

    private void ControllerStickUpdated(object? __, StickEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_fastImageViewer != null)
            {
                if (e.Stick == StickName.LStick)
                {
                    _fastImageViewer.OnLeftStickUpdate(e);
                }
                else if (e.Stick == StickName.RStick)
                {
                    _fastImageViewer.OnRightStickUpdate(e);
                }

            }
        });
    }
    private void ControllerButtonChanged(object? __, ButtonEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (DataContext is not MainViewModel vm)
                return;
            if (e.Pressed == false)
            {
                switch (e.Button)
                {
                    case ButtonName.LB:
                        vm.PreviousCommand.Execute(null);
                        break;
                    case ButtonName.RB:
                        vm.NextCommand.Execute(null);
                        break;
                }
            }
        });
    }
    



}