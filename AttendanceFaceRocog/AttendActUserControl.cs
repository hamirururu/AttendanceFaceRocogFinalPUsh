using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AttendanceFaceRocog
{
    public partial class AttendActUserControl : UserControl
    {
        public AttendActUserControl()
        {
            InitializeComponent();
            
            // Load data when control is created
            this.Load += AttendActUserControl_Load;
        }

        private void AttendActUserControl_Load(object? sender, EventArgs e)
        {
            LoadAttendanceStatistics();
            LoadActivityTimeline();
        }

        /// <summary>
        /// Load attendance statistics (counts) into the top panels
        /// </summary>
        private void LoadAttendanceStatistics()
        {
            try
            {
                var stats = DatabaseHelper.GetTodayAttendanceStats();

                // Update the count labels
                TxtTimeIn1.Text = stats.timeInCount.ToString();
                TxtTimeOut1.Text = stats.timeOutCount.ToString();
                TxtBreakIn1.Text = stats.startBreakCount.ToString();
                TxtOutBreak1.Text = stats.stopBreakCount.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading statistics: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Load activity timeline with all today's attendance records
        /// </summary>
        private void LoadActivityTimeline()
        {
            try
            {
                PanelActTimeLine.Controls.Clear();

                var attendanceData = DatabaseHelper.GetTodayAllAttendance();

                if (attendanceData.Rows.Count == 0)
                {
                    ShowNoActivityMessage();
                    return;
                }

                // Create a scrollable flow layout panel
                var flowPanel = new FlowLayoutPanel
                {
                    Location = new Point(10, 10),
                    Size = new Size(PanelActTimeLine.Width - 20, PanelActTimeLine.Height - 20),
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = Color.White
                };

                // Add activity items
                foreach (DataRow row in attendanceData.Rows)
                {
                    var activityItem = CreateActivityTimelineItem(
                        row["FullName"].ToString() ?? "",
                        row["empCode"].ToString() ?? "",
                        row["TimeIn"].ToString() ?? "",
                        row["TimeOut"].ToString() ?? "",
                        row["StartBreak"].ToString() ?? "",
                        row["StopBreak"].ToString() ?? "",
                        Convert.ToDateTime(row["LogTime"])
                    );
                    flowPanel.Controls.Add(activityItem);
                }

                PanelActTimeLine.Controls.Add(flowPanel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading activity timeline: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Creates a single activity timeline item
        /// </summary>
        private Panel CreateActivityTimelineItem(string fullName, string empCode, 
            string timeIn, string timeOut, string startBreak, string stopBreak, DateTime logTime)
        {
            var panel = new Panel
            {
                Size = new Size(PanelActTimeLine.Width - 50, 140),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(5)
            };

            // Add border effect
            panel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
                }
            };

            // Employee info header
            var lblEmployee = new Label
            {
                Text = $"👤 {fullName}",
                Font = new Font("Poppins", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(15, 10),
                AutoSize = true
            };

            var lblEmpCode = new Label
            {
                Text = empCode,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Gray,
                Location = new Point(15, 35),
                AutoSize = true
            };

            var lblDate = new Label
            {
                Text = $"📅 {logTime:MMM dd, yyyy hh:mm tt}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(panel.Width - 220, 12),
                AutoSize = true
            };

            // Activity details section
            int yPos = 60;

            // Time In
            if (!string.IsNullOrEmpty(timeIn))
            {
                var lblTimeIn = CreateActivityLabel("🟢 Time In:", timeIn, new Point(20, yPos));
                panel.Controls.Add(lblTimeIn);
                yPos += 22;
            }

            // Time Out
            if (!string.IsNullOrEmpty(timeOut))
            {
                var lblTimeOut = CreateActivityLabel("🔴 Time Out:", timeOut, new Point(20, yPos));
                panel.Controls.Add(lblTimeOut);
                yPos += 22;
            }

            // Start Break
            if (!string.IsNullOrEmpty(startBreak))
            {
                var lblStartBreak = CreateActivityLabel("☕ Start Break:", startBreak, new Point(350, 60));
                panel.Controls.Add(lblStartBreak);
            }

            // Stop Break
            if (!string.IsNullOrEmpty(stopBreak))
            {
                var lblStopBreak = CreateActivityLabel("↺ Stop Break:", stopBreak, new Point(350, 82));
                panel.Controls.Add(lblStopBreak);
            }

            panel.Controls.Add(lblEmployee);
            panel.Controls.Add(lblEmpCode);
            panel.Controls.Add(lblDate);

            return panel;
        }

        /// <summary>
        /// Helper method to create activity labels
        /// </summary>
        private Label CreateActivityLabel(string labelText, string value, Point location)
        {
            var label = new Label
            {
                Text = $"{labelText} {value}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(75, 85, 99),
                Location = location,
                AutoSize = true
            };
            return label;
        }

        /// <summary>
        /// Show message when there's no activity
        /// </summary>
        private void ShowNoActivityMessage()
        {
            var lblNoData = new Label
            {
                Text = "📭 No attendance records for today.\n\nRecords will appear here as employees check in.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            PanelActTimeLine.Controls.Add(lblNoData);
        }

        /// <summary>
        /// Refresh the data (can be called externally)
        /// </summary>
        public void RefreshData()
        {
            LoadAttendanceStatistics();
            LoadActivityTimeline();
        }

        private void guna2PictureBox2_Click(object sender, EventArgs e)
        {
            // Optional: Add refresh functionality
            RefreshData();
        }

        private void PanelActTimeLine_Paint(object sender, PaintEventArgs e)
        {
            // Keep existing paint event
        }
    }
}
