using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AttendanceFaceRocog
{
    public partial class AttendActUserControl : UserControl
    {
        private DataTable? _allAttendanceData;  // Cache all attendance data for filtering

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
            SetupSearchBar();
        }

        /// <summary>
        /// Setup search bar event handler
        /// </summary>
        private void SetupSearchBar()
        {
            // Check if TxtSearchBar exists, if not you can add it programmatically or via designer
            var searchBar = this.Controls.Find("TxtSearchBar", true).FirstOrDefault() as TextBox;
            if (searchBar != null)
            {
                searchBar.TextChanged += TxtSearchBar_TextChanged;
            }
        }

        /// <summary>
        /// Smart search event - triggered on text change
        /// </summary>
        private void TxtSearchBar_TextChanged(object? sender, EventArgs e)
        {
            if (sender is not TextBox searchBox) return;

            string searchText = searchBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(searchText))
            {
                // If search is empty, show all records
                if (_allAttendanceData != null)
                {
                    DisplayAttendanceRecords(_allAttendanceData);
                }
            }
            else
            {
                // Perform smart search
                PerformSmartSearch(searchText);
            }
        }

        /// <summary>
        /// Smart search - searches by employee name and employee ID
        /// </summary>
        private void PerformSmartSearch(string searchText)
        {
            if (_allAttendanceData == null || _allAttendanceData.Rows.Count == 0)
            {
                ShowNoSearchResultsMessage();
                return;
            }

            // Filter data by name or employee code
            var filteredRows = _allAttendanceData.AsEnumerable()
                .Where(row =>
                {
                    string fullName = row["FullName"].ToString()?.ToLower() ?? "";
                    string empCode = row["empCode"].ToString()?.ToLower() ?? "";

                    // Search in name or employee code
                    return fullName.Contains(searchText) || empCode.Contains(searchText);
                })
                .ToList();

            if (filteredRows.Count > 0)
            {
                DataTable filteredTable = _allAttendanceData.Clone();
                foreach (var row in filteredRows)
                {
                    filteredTable.ImportRow(row);
                }
                DisplayAttendanceRecords(filteredTable);
            }
            else
            {
                ShowNoSearchResultsMessage();
            }
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
                // Cache all attendance data for search functionality
                _allAttendanceData = DatabaseHelper.GetTodayAllAttendance();
                
                DisplayAttendanceRecords(_allAttendanceData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading activity timeline: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Display attendance records in the timeline panel
        /// </summary>
        private void DisplayAttendanceRecords(DataTable attendanceData)
        {
            PanelActTimeLine.Controls.Clear();

            if (attendanceData == null || attendanceData.Rows.Count == 0)
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
                BackColor = Color.Transparent,
                Padding = new Padding(5)
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

        /// <summary>
        /// Create a single activity timeline item
        /// </summary>
        private Panel CreateActivityTimelineItem(string fullName, string empCode, string timeIn, 
            string timeOut, string startBreak, string stopBreak, DateTime logTime)
        {
            var itemPanel = new Panel
            {
                Size = new Size(900, 100),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                Margin = new Padding(5)
            };

            // Employee info
            var lblEmployee = new Label
            {
                Text = $"👤 {fullName} ({empCode})",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(10, 5),
                AutoSize = true
            };
            itemPanel.Controls.Add(lblEmployee);

            // Date/Time
            var lblDateTime = new Label
            {
                Text = $"📅 {logTime:yyyy-MM-dd}",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray,
                Location = new Point(10, 25),
                AutoSize = true
            };
            itemPanel.Controls.Add(lblDateTime);

            // Attendance details
            int yPos = 45;
            if (!string.IsNullOrEmpty(timeIn))
            {
                var lblTimeIn = CreateActivityLabel("⏱️ Time In:", timeIn, new Point(10, yPos));
                itemPanel.Controls.Add(lblTimeIn);
                yPos += 20;
            }

            if (!string.IsNullOrEmpty(timeOut))
            {
                var lblTimeOut = CreateActivityLabel("⏱️ Time Out:", timeOut, new Point(10, yPos));
                itemPanel.Controls.Add(lblTimeOut);
                yPos += 20;
            }

            if (!string.IsNullOrEmpty(startBreak))
            {
                var lblStartBreak = CreateActivityLabel("☕ Break Start:", startBreak, new Point(450, 45));
                itemPanel.Controls.Add(lblStartBreak);
            }

            if (!string.IsNullOrEmpty(stopBreak))
            {
                var lblStopBreak = CreateActivityLabel("☕ Break End:", stopBreak, new Point(450, 65));
                itemPanel.Controls.Add(lblStopBreak);
            }

            return itemPanel;
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
        /// Show message when search has no results
        /// </summary>
        private void ShowNoSearchResultsMessage()
        {
            PanelActTimeLine.Controls.Clear();
            var lblNoResults = new Label
            {
                Text = "🔍 No employees found matching your search.\n\nTry searching by name or employee ID (e.g., EMP-001)",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            PanelActTimeLine.Controls.Add(lblNoResults);
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
