using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Il2CppDumper;

internal sealed class MainForm : Form
{
    private readonly TextBox txtIl2Cpp = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtMetadata = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtOutput = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtLog = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly Button btnBrowseIl2Cpp = new() { Text = "..." };
    private readonly Button btnBrowseMetadata = new() { Text = "..." };
    private readonly Button btnBrowseOutput = new() { Text = "..." };
    private readonly Button btnDump = new() { Text = "Dump", Dock = DockStyle.Fill, Height = 36 };

    public MainForm()
    {
        Text = "Il2CppDumper GUI";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 520);
        Width = 900;
        Height = 600;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(10),
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        table.Controls.Add(new Label { Text = "Executable / binary", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        table.Controls.Add(txtIl2Cpp, 1, 0);
        table.Controls.Add(btnBrowseIl2Cpp, 2, 0);

        table.Controls.Add(new Label { Text = "global-metadata.dat", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        table.Controls.Add(txtMetadata, 1, 1);
        table.Controls.Add(btnBrowseMetadata, 2, 1);

        table.Controls.Add(new Label { Text = "Output folder", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        table.Controls.Add(txtOutput, 1, 2);
        table.Controls.Add(btnBrowseOutput, 2, 2);

        table.SetColumnSpan(btnDump, 3);
        table.Controls.Add(btnDump, 0, 3);

        table.SetColumnSpan(txtLog, 3);
        table.Controls.Add(txtLog, 0, 4);

        Controls.Add(table);

        btnBrowseIl2Cpp.Click += (_, _) => BrowseFile(
            txtIl2Cpp,
            "Select il2cpp executable/binary",
            "All files (*.*)|*.*");

        btnBrowseMetadata.Click += (_, _) => BrowseFile(
            txtMetadata,
            "Select global-metadata.dat",
            "Metadata file (global-metadata.dat)|global-metadata.dat|All files (*.*)|*.*");

        btnBrowseOutput.Click += (_, _) => BrowseFolder(txtOutput);
        btnDump.Click += async (_, _) => await RunDumpAsync();
    }

    private void BrowseFile(TextBox target, string title, string filter)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
        }
    }

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select output folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private async Task RunDumpAsync()
    {
        var il2cppPath = txtIl2Cpp.Text.Trim();
        var metadataPath = txtMetadata.Text.Trim();
        var outputPath = txtOutput.Text.Trim();

        if (string.IsNullOrWhiteSpace(il2cppPath) || !File.Exists(il2cppPath))
        {
            MessageBox.Show(this, "Укажи корректный путь к executable/binary.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
        {
            MessageBox.Show(this, "Укажи корректный путь к global-metadata.dat.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            MessageBox.Show(this, "Укажи output folder.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Directory.CreateDirectory(outputPath);

        SetUiEnabled(false);
        txtLog.Clear();
        AppendLog("Starting...");

        try
        {
            var exitCode = await Task.Run(() =>
                Program.RunDump(il2cppPath, metadataPath, outputPath, AppendLog));

            AppendLog(exitCode == 0 ? "Finished." : $"Failed with code {exitCode}.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetUiEnabled(true);
        }
    }

    private void SetUiEnabled(bool enabled)
    {
        btnBrowseIl2Cpp.Enabled = enabled;
        btnBrowseMetadata.Enabled = enabled;
        btnBrowseOutput.Enabled = enabled;
        btnDump.Enabled = enabled;
        txtIl2Cpp.Enabled = enabled;
        txtMetadata.Enabled = enabled;
        txtOutput.Enabled = enabled;
    }

    private void AppendLog(string message)
    {
        if (txtLog.InvokeRequired)
        {
            txtLog.BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        txtLog.AppendText(message + Environment.NewLine);
    }
}
