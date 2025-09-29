using System.Drawing;
using System.Windows.Forms;

namespace KeyLockr.Tray;

public sealed class ExternalKeyboardWarningForm : Form
{
    private readonly Timer _timer;
    private readonly Label _countdownLabel;
    private int _remainingSeconds;

    public ExternalKeyboardWarningForm(string warningMessage, int countdownSeconds = 10)
    {
        _remainingSeconds = Math.Max(3, countdownSeconds);

        Text = "确认锁定内置键盘";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(420, 200);

        var messageLabel = new Label
        {
            AutoSize = false,
            Text = warningMessage + "\n\n若继续锁定，建议确保外接键盘正常工作。",
            Dock = DockStyle.Top,
            Height = 100,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _countdownLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            Height = 60
        };

        var continueButton = new Button
        {
            Text = "继续锁定",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5)
        };

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5)
        };

        buttonsPanel.Controls.Add(continueButton);
        buttonsPanel.Controls.Add(cancelButton);

        Controls.Add(buttonsPanel);
        Controls.Add(_countdownLabel);
        Controls.Add(messageLabel);

        AcceptButton = cancelButton;
        CancelButton = cancelButton;

        _timer = new Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateCountdown();
        Shown += (_, _) => { UpdateCountdown(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Stop();
    }

    private void UpdateCountdown()
    {
        _countdownLabel.Text = $"将于 {_remainingSeconds} 秒后自动取消";
        if (_remainingSeconds <= 0)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        _remainingSeconds--;
    }
}
