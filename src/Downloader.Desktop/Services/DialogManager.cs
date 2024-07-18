﻿using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;

namespace Downloader.Desktop.Services;

public class DialogManager
{
    private static readonly Dictionary<object, Visual> RegistrationMapper =
        new Dictionary<object, Visual>();

    static DialogManager()
    {
        RegisterProperty.Changed.AddClassHandler<Visual>(RegisterChanged);
    }

    private static void RegisterChanged(Visual sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is null)
        {
            throw new InvalidOperationException("The DialogManager can only be registered on a Visual");
        }

        // Unregister any old registered context
        if (e.OldValue != null)
        {
            RegistrationMapper.Remove(e.OldValue);
        }

        // Register any new context
        if (e.NewValue != null)
        {
            RegistrationMapper.Add(e.NewValue, sender);
        }
    }

    /// <summary>
    /// This property handles the registration of Views and ViewModel
    /// </summary>
    public static readonly AttachedProperty<object?> RegisterProperty = AvaloniaProperty.RegisterAttached<DialogManager, Visual, object?>(
        "Register");

    /// <summary>
    /// Accessor for Attached property <see cref="RegisterProperty"/>.
    /// </summary>
    public static void SetRegister(AvaloniaObject element, object value)
    {
        element.SetValue(RegisterProperty, value);
    }

    /// <summary>
    /// Accessor for Attached property <see cref="RegisterProperty"/>.
    /// </summary>
    public static object? GetRegister(AvaloniaObject element)
    {
        return element.GetValue(RegisterProperty);
    }

    /// <summary>
    /// Gets the associated <see cref="Visual"/> for a given context. Returns null, if none was registered
    /// </summary>
    /// <param name="context">The context to lookup</param>
    /// <returns>The registered Visual for the context or null if none was found</returns>
    public static Visual? GetVisualForContext(object context)
    {
        return RegistrationMapper.TryGetValue(context, out var result) ? result : null;
    }

    /// <summary>
    /// Gets the parent <see cref="TopLevel"/> for the given context. Returns null, if no TopLevel was found
    /// </summary>
    /// <param name="context">The context to lookup</param>
    /// <returns>The registered TopLevel for the context or null if none was found</returns>
    public static TopLevel? GetTopLevelForContext(object context)
    {
        return TopLevel.GetTopLevel(GetVisualForContext(context));
    }
}
