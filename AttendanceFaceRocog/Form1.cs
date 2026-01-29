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

        public Form1()
        {
            InitializeComponent();
            ApplyTheme();
            InitializeClock();
            InitializeScannerControl();

            this.Resize += Form1_Resize;
        }

        private void InitializeScannerControl()
        {
            // Create UserControl1 and add it to guna2Panel2
            scannerControl = new UserControl1();
            scannerControl.Dock = DockStyle.Fill;
            guna2Panel2.Controls.Add(scannerControl);
        }

        private void ApplyTheme()
        {
            this.BackColor = AppTheme.Light.Background;
            guna2Panel1.FillColor = AppTheme.Light.Card;
            guna2HtmlLabel1.ForeColor = AppTheme.Light.Foreground;
            guna2HtmlLabel1.Font = AppTheme.HeadingSmall;
            guna2HtmlLabel2.ForeColor = AppTheme.Light.Foreground;
            guna2Panel2.FillColor = AppTheme.Light.Card;
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
            // UserControl1 will handle its own layout since it's Dock.Fill
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            scannerControl?.Cleanup();
            clockTimer?.Dispose();
            base.OnFormClosing(e);
        }

        private void SetActiveButton(Guna.UI2.WinForms.Guna2Button activeBtn)
        {
            var buttons = new[] { btnScanner, btnRecentAct, btnProfiles };

            foreach (var btn in buttons)
            {
                btn.FillColor = Color.Transparent;
                btn.ForeColor = Color.Gray;
                btn.Font = new Font("Segoe UI", 10, FontStyle.Regular);

                if (btn == btnScanner)
                    btn.Image = Properties.Resources.cameraa_gray;
                else if (btn == btnRecentAct)
                    btn.Image = Properties.Resources.presony_gray;
                else
                    btn.Image = Properties.Resources.acti_gray;
            }

            activeBtn.FillColor = Color.FromArgb(108, 99, 255);
            activeBtn.ForeColor = Color.White;
            activeBtn.Font = new Font("Segoe UI", 10, FontStyle.Bold);

            if (activeBtn == btnScanner)
                activeBtn.Image = Properties.Resources.cameraa_white;
            else if (activeBtn == btnRecentAct)
                activeBtn.Image = Properties.Resources.persony_white;
            else
                activeBtn.Image = Properties.Resources.acti_white;
        }

        private void BtnScanner_Click(object? sender, EventArgs e)
        {
            SetActiveButton(btnScanner);
            guna2Panel2.Show();
            scannerControl?.Show();
        }

        private void BtnProfiles_Click(object? sender, EventArgs e)
        {
            SetActiveButton(btnProfiles);
            // Handle profiles view
        }

        public void guna2Panel1_Paint(object? sender, PaintEventArgs e)
        {
        }
    }
}