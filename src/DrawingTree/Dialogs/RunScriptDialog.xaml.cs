/// <summary>
/// RunScriptDialog.xaml.cs
/// Lists script files in the application's scripts/ directory and allows running them.
/// </summary>

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Logging;

using Button           = System.Windows.Controls.Button;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DrawingTree.Dialogs;

public partial class RunScriptDialog : Window
{
    private static readonly string[] ScriptExtensions = { ".py", ".ps1", ".bat", ".cmd" };

    private string ScriptsDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");

    public RunScriptDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateScriptList();
    }

    /// <summary>
    /// Scans the scripts directory and builds a row (name + Run button) for each script.
    /// </summary>
    private void PopulateScriptList()
    {
        ScriptListPanel.Children.Clear();

        if (!Directory.Exists(ScriptsDir))
        {
            var msg = new TextBlock
            {
                Text = $"No scripts folder found at:\n{ScriptsDir}",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap
            };
            ScriptListPanel.Children.Add(msg);
            return;
        }

        var files = Directory.GetFiles(ScriptsDir)
            .Where(f => ScriptExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            var msg = new TextBlock
            {
                Text = "No script files found.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(8)
            };
            ScriptListPanel.Children.Add(msg);
            return;
        }

        foreach (var filePath in files)
        {
            var row = BuildScriptRow(filePath);
            ScriptListPanel.Children.Add(row);
        }
    }

    /// <summary>
    /// Creates a row Grid containing the script name and a Run button.
    /// </summary>
    private Grid BuildScriptRow(string filePath)
    {
        var row = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = Path.GetFileName(filePath),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);

        var runBtn = new Button
        {
            Content = "Run",
            Width = 52,
            Height = 24,
            FontSize = 11,
            Tag = filePath
        };
        runBtn.Click += RunButton_Click;
        Grid.SetColumn(runBtn, 1);

        row.Children.Add(label);
        row.Children.Add(runBtn);
        return row;
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string filePath) return;

        string scriptName = Path.GetFileName(filePath);
        var confirm = MessageBox.Show(
            $"Run script '{scriptName}'?",
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        ExecuteScript(filePath, scriptName);
    }

    /// <summary>
    /// Executes the script with the appropriate runner based on file extension.
    /// Shows a result dialog when the process completes.
    /// </summary>
    private void ExecuteScript(string filePath, string scriptName)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string fileName, arguments;

        switch (ext)
        {
            case ".py":
                fileName  = "python";
                arguments = $"\"{filePath}\"";
                break;
            case ".ps1":
                fileName  = "powershell";
                arguments = $"-ExecutionPolicy Bypass -File \"{filePath}\"";
                break;
            default:
                fileName  = filePath;
                arguments = string.Empty;
                break;
        }

        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = ScriptsDir
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start process.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            string output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            string summary = process.ExitCode == 0
                ? $"Script '{scriptName}' completed successfully."
                : $"Script '{scriptName}' exited with code {process.ExitCode}.";

            if (!string.IsNullOrWhiteSpace(output))
                summary += $"\n\n{output.Trim()}";

            var image = process.ExitCode == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning;
            MessageBox.Show(summary, "Script Result", MessageBoxButton.OK, image);
            Logger.Instance.Info($"RunScript: '{scriptName}' exit={process.ExitCode}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to run script:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"RunScript: failed to start '{scriptName}'", ex);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
