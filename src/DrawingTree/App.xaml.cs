using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using DrawingTree.Logging;

namespace DrawingTree;

/// &lt;summary&gt;
/// Interaction logic for App.xaml
/// &lt;/summary&gt;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Instance.Error($"Unhandled UI exception: {e.Exception}");
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Logger.Instance.Error($"Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }
}
