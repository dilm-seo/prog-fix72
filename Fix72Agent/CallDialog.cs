using System.Drawing;

namespace Fix72Agent;

public class CallDialog : Form
{
    public CallDialog(string phone)
    {
        Text = "Appeler Etienne — Fix72";
        Size = new Size(440, 240);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Icon = IconFactory.CreateShieldIcon(Color.FromArgb(0, 102, 204));

        var lbl1 = new Label
        {
            Text = "Composez ce numéro depuis votre téléphone :",
            Font = new Font("Segoe UI", 11),
            Location = new Point(20, 20),
            AutoSize = true
        };
        Controls.Add(lbl1);

        var lblPhone = new Label
        {
            Text = phone,
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 102, 204),
            Location = new Point(20, 55),
            Size = new Size(400, 60),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(lblPhone);

        var btnCopy = new Button
        {
            Text = "📋  Copier le numéro",
            Location = new Point(60, 140),
            Size = new Size(150, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 240, 240),
            Font = new Font("Segoe UI", 10)
        };
        btnCopy.Click += (s, e) =>
        {
            Clipboard.SetText(phone);
            btnCopy.Text = "✓ Copié !";
        };
        Controls.Add(btnCopy);

        var btnClose = new Button
        {
            Text = "Fermer",
            Location = new Point(230, 140),
            Size = new Size(150, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 102, 204),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };
        btnClose.FlatAppearance.BorderSize = 0;
        Controls.Add(btnClose);
        AcceptButton = btnClose;
    }
}
