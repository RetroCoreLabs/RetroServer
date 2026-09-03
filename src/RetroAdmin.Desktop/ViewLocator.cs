//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen 
// HackerCorp Labs — https://github.com/HackerCorpLabs
// RetroFS -- Accessing yesterday's data with today's standards"
//

using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RetroAdmin.Desktop.ViewModels;

namespace RetroAdmin.Desktop;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        string name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? type = typeof(ViewLocator).Assembly.GetType(name);

        if (type != null)
        {
            Control control = (Control)Activator.CreateInstance(type)!;
            control.DataContext = param;
            return control;
        }

        return new TextBlock
        {
            Text = "Not Found: " + name,
            Foreground = Avalonia.Media.Brushes.White
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
