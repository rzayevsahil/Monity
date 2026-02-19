using System.Windows.Forms;

namespace Monity.Updater;

public class UpdaterForm : Form
{
    private readonly string _logPath;
    private readonly Label _label;
    private readonly ProgressBar _progressBar;
    private readonly TextBox _logBox;

    public UpdaterForm(string logPath)
    {
        _logPath = logPath;
        Text = "Monity güncelleniyor";
        Size = new Size(480, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        _label = new Label
        {
            Text = "Güncelleme uygulanıyor, lütfen bekleyin.",
            AutoSize = true,
            Location = new Point(16, 16),
            MaximumSize = new Size(440, 0)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(16, 48),
            Size = new Size(432, 24),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };

        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(16, 88),
            Size = new Size(432, 160),
            Font = new Font("Consolas", 9)
        };

        Controls.Add(_label);
        Controls.Add(_progressBar);
        Controls.Add(_logBox);
    }

    public void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }
        _logBox.AppendText(message + Environment.NewLine);
        _logBox.ScrollToCaret();
    }

    public void Finish(bool success)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Finish(success));
            return;
        }
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.MarqueeAnimationSpeed = 0;
        if (success)
        {
            Close();
        }
        else
        {
            Text = "Monity güncelleme - Hata";
            AppendLog("");
            AppendLog($"Sorun olursa log: {_logPath}");
        }
    }
}
