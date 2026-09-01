using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ToDoTree.App.Behaviors;

/// <summary>
/// 表示された瞬間にフォーカスを取る添付プロパティ。
/// カードの上に現れる編集欄が、そのまま打ち始められるようにするために使う。
/// </summary>
public static class FocusBehavior
{
    public static readonly DependencyProperty FocusWhenVisibleProperty =
        DependencyProperty.RegisterAttached(
            "FocusWhenVisible",
            typeof(bool),
            typeof(FocusBehavior),
            new PropertyMetadata(false, OnFocusWhenVisibleChanged));

    public static void SetFocusWhenVisible(DependencyObject element, bool value) =>
        element.SetValue(FocusWhenVisibleProperty, value);

    public static bool GetFocusWhenVisible(DependencyObject element) =>
        element.GetValue(FocusWhenVisibleProperty) is true;

    private static void OnFocusWhenVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnIsVisibleChanged;

        if (e.NewValue is not true)
        {
            return;
        }

        element.IsVisibleChanged += OnIsVisibleChanged;

        // すでに見えている状態で付けられたときのために、その場でも一度試す。
        if (element.IsVisible)
        {
            Focus(element);
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not FrameworkElement element)
        {
            return;
        }

        Focus(element);
    }

    private static void Focus(FrameworkElement element) =>
        // レイアウトが落ち着いてからでないとフォーカスを取れない。
        element.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!element.IsVisible)
                {
                    return;
                }

                element.Focus();
                if (element is TextBox box)
                {
                    box.SelectAll();
                }
            }),
            DispatcherPriority.Input);
}
