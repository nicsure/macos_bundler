using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MacosBundler;

public sealed class MainForm : Form
{
    private readonly TextBox _zipPathTextBox = new();
    private readonly TextBox _outputFolderTextBox = new();
    private readonly TextBox _appNameTextBox = new();
    private readonly TextBox _appVersionTextBox = new();
    private readonly TextBox _logTextBox = new();

    public MainForm()
    {
        Text = "macOS Bundler";
        MinimumSize = new System.Drawing.Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(12),
            AutoSize = true
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var zipLabel = new Label { Text = "x64 ZIP file", AutoSize = true, Anchor = AnchorStyles.Left };
        var outputLabel = new Label { Text = "Output folder", AutoSize = true, Anchor = AnchorStyles.Left };
        var nameLabel = new Label { Text = "App name", AutoSize = true, Anchor = AnchorStyles.Left };
        var versionLabel = new Label { Text = "App version", AutoSize = true, Anchor = AnchorStyles.Left };

        _zipPathTextBox.Dock = DockStyle.Fill;
        _outputFolderTextBox.Dock = DockStyle.Fill;
        _appNameTextBox.Dock = DockStyle.Fill;
        _appVersionTextBox.Dock = DockStyle.Fill;
        _appVersionTextBox.Text = "1.0.0";

        var browseZipButton = new Button { Text = "Browse...", AutoSize = true };
        var browseOutputButton = new Button { Text = "Browse...", AutoSize = true };
        var createBundleButton = new Button { Text = "Create Bundle", AutoSize = true, Anchor = AnchorStyles.Right };

        browseZipButton.Click += (_, _) => BrowseZip();
        browseOutputButton.Click += (_, _) => BrowseOutputFolder();
        createBundleButton.Click += (_, _) => CreateBundle();

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.ReadOnly = true;

        layout.Controls.Add(zipLabel, 0, 0);
        layout.Controls.Add(_zipPathTextBox, 1, 0);
        layout.Controls.Add(browseZipButton, 2, 0);

        layout.Controls.Add(outputLabel, 0, 1);
        layout.Controls.Add(_outputFolderTextBox, 1, 1);
        layout.Controls.Add(browseOutputButton, 2, 1);

        layout.Controls.Add(nameLabel, 0, 2);
        layout.Controls.Add(_appNameTextBox, 1, 2);
        layout.SetColumnSpan(_appNameTextBox, 2);

        layout.Controls.Add(versionLabel, 0, 3);
        layout.Controls.Add(_appVersionTextBox, 1, 3);
        layout.SetColumnSpan(_appVersionTextBox, 2);

        layout.Controls.Add(createBundleButton, 2, 4);
        layout.SetColumnSpan(createBundleButton, 3);

        layout.Controls.Add(_logTextBox, 0, 5);
        layout.SetColumnSpan(_logTextBox, 3);

        Controls.Add(layout);
    }

    private void BrowseZip()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "ZIP files (*.zip)|*.zip|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _zipPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void CreateBundle()
    {
        _logTextBox.Clear();

        var zipPath = _zipPathTextBox.Text.Trim();
        var outputFolder = _outputFolderTextBox.Text.Trim();
        var appName = _appNameTextBox.Text.Trim();
        var appVersion = _appVersionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            AppendLog("Select a valid x64 ZIP file.");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            AppendLog("Select an output folder.");
            return;
        }

        if (string.IsNullOrWhiteSpace(appName))
        {
            AppendLog("Enter an application name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(appVersion))
        {
            AppendLog("Enter an application version.");
            return;
        }

        try
        {
            var workingRoot = CreateWorkingFolder();
            AppendLog($"Extracting ZIP to {workingRoot}");
            ZipFile.ExtractToDirectory(zipPath, workingRoot);

            var extractedRoot = ResolvePublishedRoot(workingRoot);
            AppendLog($"Using publish root: {extractedRoot}");

            var executableName = ResolveExecutableName(extractedRoot);
            AppendLog($"Detected executable: {executableName}");

            var bundlePath = Path.Combine(outputFolder, $"{appName}.app");
            if (Directory.Exists(bundlePath))
            {
                AppendLog("Removing existing bundle folder.");
                Directory.Delete(bundlePath, recursive: true);
            }

            var contentsPath = Path.Combine(bundlePath, "Contents");
            var macosPath = Path.Combine(contentsPath, "MacOS");
            var resourcesPath = Path.Combine(contentsPath, "Resources");

            Directory.CreateDirectory(macosPath);
            Directory.CreateDirectory(resourcesPath);

            AppendLog("Copying published binaries into bundle.");
            CopyDirectory(extractedRoot, macosPath);

            var infoPlistPath = Path.Combine(contentsPath, "Info.plist");
            File.WriteAllText(infoPlistPath, BuildInfoPlist(appName, appVersion, executableName));

            var pkgInfoPath = Path.Combine(contentsPath, "PkgInfo");
            File.WriteAllText(pkgInfoPath, "APPL????");

            AppendLog($"Bundle created at {bundlePath}");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }
    }

    private static string CreateWorkingFolder()
    {
        var workingRoot = Path.Combine(Path.GetTempPath(), "MacosBundler", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingRoot);
        return workingRoot;
    }

    private static string ResolvePublishedRoot(string workingRoot)
    {
        var directories = Directory.GetDirectories(workingRoot);
        if (directories.Length == 1)
        {
            return directories[0];
        }

        return workingRoot;
    }

    private static string ResolveExecutableName(string publishedRoot)
    {
        var ignoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dylib",
            ".pdb",
            ".json",
            ".deps",
            ".runtimeconfig"
        };

        var candidate = Directory.GetFiles(publishedRoot)
            .Select(path => new FileInfo(path))
            .Where(file => !ignoredExtensions.Contains(file.Extension))
            .OrderByDescending(file => file.Length)
            .FirstOrDefault();

        return candidate?.Name ?? throw new InvalidOperationException("Unable to determine executable name.");
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var targetDirectory = directory.Replace(sourcePath, destinationPath);
            Directory.CreateDirectory(targetDirectory);
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var targetFile = file.Replace(sourcePath, destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static string BuildInfoPlist(string appName, string appVersion, string executableName)
    {
        var bundleIdentifier = BuildBundleIdentifier(appName);

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        builder.AppendLine("<plist version=\"1.0\">");
        builder.AppendLine("<dict>");
        builder.AppendLine("  <key>CFBundleName</key>");
        builder.AppendLine($"  <string>{EscapePlistValue(appName)}</string>");
        builder.AppendLine("  <key>CFBundleDisplayName</key>");
        builder.AppendLine($"  <string>{EscapePlistValue(appName)}</string>");
        builder.AppendLine("  <key>CFBundleIdentifier</key>");
        builder.AppendLine($"  <string>{bundleIdentifier}</string>");
        builder.AppendLine("  <key>CFBundleExecutable</key>");
        builder.AppendLine($"  <string>{EscapePlistValue(executableName)}</string>");
        builder.AppendLine("  <key>CFBundlePackageType</key>");
        builder.AppendLine("  <string>APPL</string>");
        builder.AppendLine("  <key>CFBundleShortVersionString</key>");
        builder.AppendLine($"  <string>{EscapePlistValue(appVersion)}</string>");
        builder.AppendLine("  <key>CFBundleVersion</key>");
        builder.AppendLine($"  <string>{EscapePlistValue(appVersion)}</string>");
        builder.AppendLine("  <key>LSMinimumSystemVersion</key>");
        builder.AppendLine("  <string>10.13</string>");
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }

    private static string BuildBundleIdentifier(string appName)
    {
        var cleaned = Regex.Replace(appName.ToLowerInvariant(), "[^a-z0-9]+", ".").Trim('.');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "app";
        }

        return $"com.example.{cleaned}";
    }

    private static string EscapePlistValue(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private void AppendLog(string message)
    {
        _logTextBox.AppendText($"{message}{Environment.NewLine}");
    }
}
