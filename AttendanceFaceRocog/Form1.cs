using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AttendanceFaceRocog
{
    public partial class Form1 : Form
    {
        private System.Windows.Forms.Timer clockTimer = null!;
        private UserControl1? scannerControl;
        private bool _isAdminMode = false;

        // Store original button position
        private Point _originalScannerButtonLocation;

        // Admin credentials (in production, store securely or use database)
        private const string ADMIN_PASSWORD = "admin123";

        public Form1()
        {
            InitializeComponent();
            ApplyTheme();
            InitializeClock();
            InitializeScannerControl();
            SetupAdminShortcut();

            // Save original button location
            _originalScannerButtonLocation = btnScanner.Location;

            this.Resize += Form1_Resize;

            // Hide admin buttons and center scanner button initially
            HideAdminButtons();
        }

        private void SetupAdminShortcut()
        {
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            // Check for Ctrl + Shift + ` (backtick/grave accent - OemTilde)
            if (e.Control && e.Shift && e.KeyCode == Keys.Oemtilde)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowAdminLogin();
            }

            // Escape to exit admin mode
            if (e.KeyCode == Keys.Escape && _isAdminMode)
            {
                ExitAdminMode();
            }
        }

        private void ShowAdminLogin()
        {
            using (var loginForm = new Form())
            {
                loginForm.Text = "Admin Login";
                loginForm.Size = new Size(350, 200);
                loginForm.StartPosition = FormStartPosition.CenterParent;
                loginForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                loginForm.MaximizeBox = false;
                loginForm.MinimizeBox = false;
                loginForm.BackColor = Color.White;

                var lblTitle = new Label
                {
                    Text = "🔐 Admin Access",
                    Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(55, 65, 81),
                    Location = new Point(20, 15),
                    AutoSize = true
                };

                var lblPassword = new Label
                {
                    Text = "Password:",
                    Font = new Font("Segoe UI", 10F),
                    Location = new Point(20, 55),
                    AutoSize = true
                };

                var txtPassword = new TextBox
                {
                    Location = new Point(20, 80),
                    Size = new Size(290, 30),
                    Font = new Font("Segoe UI", 11F),
                    PasswordChar = '●',
                    UseSystemPasswordChar = true
                };

                var btnLogin = new Button
                {
                    Text = "Login",
                    Location = new Point(20, 120),
                    Size = new Size(140, 35),
                    BackColor = Color.FromArgb(99, 102, 241),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btnLogin.FlatAppearance.BorderSize = 0;

                var btnCancel = new Button
                {
                    Text = "Cancel",
                    Location = new Point(170, 120),
                    Size = new Size(140, 35),
                    BackColor = Color.FromArgb(229, 231, 235),
                    ForeColor = Color.FromArgb(55, 65, 81),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 10F),
                    Cursor = Cursors.Hand   
                };
                btnCancel.FlatAppearance.BorderSize = 0;

                btnLogin.Click += (s, e) =>
                {
                    if (txtPassword.Text == ADMIN_PASSWORD)
                    {
                        _isAdminMode = true;
                        loginForm.DialogResult = DialogResult.OK;
                        loginForm.Close();
                    }
                    else
                    {
                        MessageBox.Show("Invalid password!", "Access Denied",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        txtPassword.Clear();
                        txtPassword.Focus();
                    }
                };

                btnCancel.Click += (s, e) =>
                {
                    loginForm.DialogResult = DialogResult.Cancel;
                    loginForm.Close();
                };

                // Allow Enter key to submit
                txtPassword.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        btnLogin.PerformClick();
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                };

                loginForm.Controls.AddRange(new Control[] { lblTitle, lblPassword, txtPassword, btnLogin, btnCancel });
                loginForm.AcceptButton = btnLogin;
                loginForm.CancelButton = btnCancel;

                txtPassword.Focus();

                if (loginForm.ShowDialog(this) == DialogResult.OK)
                {
                    EnterAdminMode();
                }
            }
        }

        private void EnterAdminMode()
        {
            _isAdminMode = true;

            // Show admin buttons
            BtnRecentAct.Visible = true;
            BtnProfiles.Visible = true;

            // Restore scanner button to original position
            btnScanner.Location = _originalScannerButtonLocation;

            // Show notification
            MessageBox.Show("Admin mode activated!\n\nPress ESC to exit admin mode.",
                "Admin Access Granted", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExitAdminMode()
        {
            _isAdminMode = false;
            HideAdminButtons();

            // Return to scanner view
            BtnScanner_Click(null, EventArgs.Empty);

            MessageBox.Show("Admin mode deactivated.",
                "Logged Out", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void HideAdminButtons()
        {
            BtnRecentAct.Visible = false;
            BtnProfiles.Visible = false;

            // Center the scanner button in the panel
            CenterScannerButton();
        }

        private void CenterScannerButton()
        {
            // Center btnScanner horizontally in guna2Panel4
            int panelWidth = guna2Panel4.ClientSize.Width;
            int buttonWidth = btnScanner.Width;
            int centeredX = (panelWidth - buttonWidth) / 2;

            btnScanner.Location = new Point(centeredX, btnScanner.Location.Y);
        }

        private void InitializeScannerControl()
        {
            // Create UserControl1 and add it to guna2Panel2
            scannerControl = new UserControl1();
            scannerControl.Dock = DockStyle.Fill;
            PanelControler.Controls.Add(scannerControl);
        }

        private void ApplyTheme()
        {
            this.BackColor = AppTheme.Light.Background;
            guna2Panel1.FillColor = AppTheme.Light.Card;
            guna2HtmlLabel1.ForeColor = AppTheme.Light.Foreground;
            guna2HtmlLabel1.Font = AppTheme.HeadingSmall;
            guna2HtmlLabel2.ForeColor = AppTheme.Light.Foreground;
            PanelControler.FillColor = AppTheme.Light.Card;
        }

        private void InitializeClock()
        {
            clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            clockTimer.Tick += (s, e) =>
            {
                guna2HtmlLabel2.Text = DateTime.Now.ToString("hh:mm:ss tt");
                guna2HtmlLabel3.Text = DateTime.Now.ToString("dddd, MMMM dd, yyyy");
            };
            clockTimer.Start();

            guna2HtmlLabel2.Text = DateTime.Now.ToString("hh:mm:ss tt");
            guna2HtmlLabel3.Text = DateTime.Now.ToString("dddd, MMMM dd, yyyy");
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            // Re-center scanner button when form resizes (only in user mode)
            if (!_isAdminMode)
            {
                CenterScannerButton();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            scannerControl?.Cleanup();
            clockTimer?.Dispose();
            base.OnFormClosing(e);
        }

        private void SetActiveButton(Guna.UI2.WinForms.Guna2Button activeBtn)
        {
            var buttons = new[] { btnScanner, BtnRecentAct, BtnProfiles };

            foreach (var btn in buttons)
            {
                btn.FillColor = Color.Transparent;
                btn.ForeColor = Color.Gray;
                btn.Font = new Font("Segoe UI", 10, FontStyle.Regular);

                if (btn == btnScanner)
                    btn.Image = Properties.Resources.cameraa_gray;
                else if (btn == BtnRecentAct)
                    btn.Image = Properties.Resources.presony_gray;
                else
                    btn.Image = Properties.Resources.acti_gray;
            }

            activeBtn.FillColor = Color.FromArgb(108, 99, 255);
            activeBtn.ForeColor = Color.White;
            activeBtn.Font = new Font("Segoe UI", 10, FontStyle.Bold);

            if (activeBtn == btnScanner)
                activeBtn.Image = Properties.Resources.cameraa_white;
            else if (activeBtn == BtnRecentAct)
                activeBtn.Image = Properties.Resources.persony_white;
            else
                activeBtn.Image = Properties.Resources.acti_white;
        }

        private void BtnScanner_Click(object? sender, EventArgs e)
        {
            SetActiveButton(btnScanner);
            CleanupCurrentControl();
            PanelControler.Controls.Clear();

            UserControl1 uc = new UserControl1();
            uc.Dock = DockStyle.Fill;
            PanelControler.Controls.Add(uc);
        }

        private void BtnProfiles_Click(object? sender, EventArgs e)
        {
            SetActiveButton(BtnProfiles);
            CleanupCurrentControl();
            PanelControler.Controls.Clear();

            EmployeeProfileControl uc = new EmployeeProfileControl();
            uc.Dock = DockStyle.Fill;
            PanelControler.Controls.Add(uc);
        }

        private void BtnRecentAct_Click(object sender, EventArgs e)
        {
            SetActiveButton(BtnRecentAct);
            CleanupCurrentControl();
            PanelControler.Controls.Clear();

            AttendActUserControl uc = new AttendActUserControl();
            uc.Dock = DockStyle.Fill;
            PanelControler.Controls.Add(uc);
        }

        /// <summary>
        /// Cleans up the current control before switching to another.
        /// Stops camera and releases resources from UserControl1.
        /// </summary>
        private void CleanupCurrentControl()
        {
            foreach (Control ctrl in PanelControler.Controls)
            {
                if (ctrl is UserControl1 scannerCtrl)
                {
                    scannerCtrl.Cleanup();
                }
            }
        }
    }
}