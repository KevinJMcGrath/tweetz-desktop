﻿using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using tweetz5.Model;
using tweetz5.Utilities;

namespace tweetz5.Controls
{
    public sealed partial class Timeline : INotifyPropertyChanged
    {
        // ReSharper disable once MemberCanBePrivate.Global
        public static readonly RoutedCommand SelectItemCommand = new RoutedUICommand();

        private Thickness _tweetMargin;

        public Timeline()
        {
            InitializeComponent();
            Controller = new TimelineController((Timelines)DataContext);
            TimelineItems.PreviewMouseWheel += TimelineItemsOnPreviewMouseWheel;
            TimelineItems.Loaded += TimelineItemsOnLoaded;
            TimelineItems.SizeChanged += TimelineItemsOnSizeChanged;
            Unloaded += (sender, args) => Controller.Dispose();
        }

        public TimelineController Controller { get; }

        public Tweet GetSelectedTweet => TimelineItems.SelectedItem as Tweet;

        public Thickness TweetMargin
        {
            get => _tweetMargin;
            set
            {
                if (_tweetMargin == value) return;
                _tweetMargin = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void TimelineItemsOnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            var dpd = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListBox));
            dpd?.AddValueChanged(TimelineItems, OnItemsSourceChanged);
        }

        private void DragMoveWindow(object sender, MouseButtonEventArgs e)
        {
            Application.Current.MainWindow?.DragMove();
            e.Handled = false; // listbox item needs this to select item
        }

        private void OnItemsSourceChanged(object sender, EventArgs eventArgs)
        {
            if (TimelineItems.Items.Count > 0)
            {
                Run.Later(() =>
                {
                    if (TimelineItems.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item)
                    {
                        Keyboard.Focus(item);
                        TimelineItems.SelectedItem = null;
                    }
                });
            }
        }

        private void MoreOnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var frameworkElement = (FrameworkElement)sender;
            if (frameworkElement.ContextMenu != null)
            {
                frameworkElement.ContextMenu.PlacementTarget = this;
                frameworkElement.ContextMenu.DataContext = frameworkElement.DataContext;
                frameworkElement.ContextMenu.IsOpen = true;
            }
        }

        public void ScrollToTop()
        {
            if (TimelineItems.Items.Count > 0) TimelineItems.ScrollIntoView(TimelineItems.Items[0]);
        }

        private static void TimelineItemsOnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Modify listbox to scroll one line at a time.
            var scrollHost = (DependencyObject)sender;
            var scrollViewer = (ScrollViewer)GetScrollViewer(scrollHost);
            var offset = scrollViewer.VerticalOffset - (e.Delta > 0 ? 1 : -1);
            if (offset < 0)
            {
                scrollViewer.ScrollToVerticalOffset(0);
            }
            else if (offset > scrollViewer.ExtentHeight)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
            }
            else
            {
                scrollViewer.ScrollToVerticalOffset(offset);
            }
            e.Handled = true;
        }

        private static DependencyObject GetScrollViewer(DependencyObject o)
        {
            if (o is ScrollViewer)
            {
                return o;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);
                var result = GetScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void SelectItemCommandHandler(object sender, ExecutedRoutedEventArgs ea)
        {
            var updated = false;
            var down = Convert.ToBoolean(ea.Parameter);
            if (down)
            {
                if (TimelineItems.SelectedIndex < TimelineItems.Items.Count - 1)
                {
                    TimelineItems.SelectedIndex += 1;
                    updated = true;
                }
            }
            else
            {
                if (TimelineItems.SelectedIndex > 0)
                {
                    TimelineItems.SelectedIndex -= 1;
                    updated = true;
                }
            }
            if (updated)
            {
                var listboxItem = TimelineItems.ItemContainerGenerator.ContainerFromIndex(TimelineItems.SelectedIndex) as ListBoxItem;
                Keyboard.Focus(listboxItem);
                TimelineItems.ScrollIntoView(TimelineItems.SelectedItem);
            }
        }

        private void TimelineItemsOnSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
        {
            TweetMargin = sizeChangedEventArgs.NewSize.Width > 260 ? new Thickness(40, 0, 0, 0) : new Thickness();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}