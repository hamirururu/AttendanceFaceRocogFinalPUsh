using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AttendanceFaceRocog
{
    public partial class EmployeeProfileControl : UserControl
    {
        private VideoCapture? _capture;
        private PictureBox? picCamera;
        private bool _isCameraRunning = false;
        private Mat? _lastFrame;
        private readonly FaceRecognitionService _faceService;
        private DataTable? _allEmployees;

        public EmployeeProfileControl()
        {
            InitializeComponent();
            _faceService = new FaceRecognitionService();
            SetupButtonEvents();
            SetupSearchEvent();

            PnlCameraEmpAdd.Visible = false;
            PanelDetails.Visible = false;  // Hide details panel initially
            
            // Load profiles when control is created
            this.Load += EmployeeProfileControl_Load;
        }

        private void EmployeeProfileControl_Load(object? sender, EventArgs e)
        {
            LoadAllProfiles();
        }

        private void SetupButtonEvents()
        {
            BtnStartScan.Click += BtnStartScan_Click;
            BtnAdd.Click += BtnAdd_Click;
            BtnCancel.Click += BtnCancel_Click;
            
            // If you have a close/back button for details panel
            if (this.Controls.Find("guna2GradientButton1", true).FirstOrDefault() is Guna.UI2.WinForms.Guna2GradientButton btnClose)
    {
        btnClose.Click += (s, e) => CloseDetailsPanel();
    }
        }

        private void SetupSearchEvent()
        {
            TxtSearchBar.TextChanged += TxtSearchBar_TextChanged;
        }

        #region Profile Display & Search

        /// <summary>
        /// Load all employee profiles into the panel
        /// </summary>
        private void LoadAllProfiles()
        {
            try
            {
                _allEmployees = DatabaseHelper.GetAllEmployeesWithFaces();
                DisplayProfiles(_allEmployees);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading profiles: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Search bar text changed event - filter profiles
        /// </summary>
        private void TxtSearchBar_TextChanged(object? sender, EventArgs e)
        {
            FilterProfiles(TxtSearchBar.Text);
        }

        /// <summary>
        /// Filter profiles based on search text
        /// </summary>
        private void FilterProfiles(string searchText)
        {
            if (_allEmployees == null) return;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                DisplayProfiles(_allEmployees);
                return;
            }

            // Filter by name or employee code
            var filteredRows = _allEmployees.AsEnumerable()
                .Where(row =>
                    row["FullName"].ToString()?.ToLower().Contains(searchText.ToLower()) == true ||
                    row["empCode"].ToString()?.ToLower().Contains(searchText.ToLower()) == true
                ).ToList();

            if (filteredRows.Any())
            {
                DataTable filteredTable = _allEmployees.Clone();
                filteredRows.ForEach(row => filteredTable.ImportRow(row));
                DisplayProfiles(filteredTable);
            }
            else
            {
                ShowNoResultsMessage();
            }
        }

        /// <summary>
        /// Display employee profiles in the panel
        /// </summary>
        private void DisplayProfiles(DataTable employees)
        {
            PanelProfileList.Controls.Clear();

            if (employees.Rows.Count == 0)
            {
                ShowNoProfilesMessage();
                return;
            }

            // Create a scrollable flow layout panel
            var flowPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(PanelProfileList.Width - 20, PanelProfileList.Height - 20),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.White
            };

            // Add profile cards
            foreach (DataRow row in employees.Rows)
            {
                var profileCard = CreateProfileCard(
                    Convert.ToInt32(row["empID"]),
                    row["empCode"].ToString() ?? "",
                    Convert.ToInt32(row["empID"]),
                    row["FullName"].ToString() ?? "",
                    row["imgPath"].ToString() ?? ""
                );
                flowPanel.Controls.Add(profileCard);
            }

            PanelProfileList.Controls.Add(flowPanel);
        }

        /// <summary>
        /// Create a profile card for an employee
        /// </summary>
        private Panel CreateProfileCard(int empId, string empCode, int faceId, string fullName, string imagePath)
        {
            var card = new Panel
            {
                Size = new Size(PanelProfileList.Width - 50, 100),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(5),
                Cursor = Cursors.Hand
            };

            // Add border effect
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(230, 230, 230), 2))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            // Profile picture
            var picProfile = new PictureBox
            {
                Size = new Size(70, 70),
                Location = new Point(15, 15),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Load image or default avatar
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    using (var img = Image.FromFile(imagePath))
                    {
                        picProfile.Image = new Bitmap(img);
                    }
                }
                catch
                {
                    picProfile.Image = CreateDefaultAvatar();
                }
            }
            else
            {
                picProfile.Image = CreateDefaultAvatar();
            }

            // Employee name
            var lblName = new Label
            {
                Text = fullName,
                Font = new Font("Poppins", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(100, 20),
                AutoSize = true
            };

            // Employee code
            var lblCode = new Label
            {
                Text = $"ID: {empCode}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Gray,
                Location = new Point(100, 45),
                AutoSize = true
            };

            // View button
            var btnView = new Guna.UI2.WinForms.Guna2Button
            {
                Text = "View Details",
                Size = new Size(120, 35),
                Location = new Point(card.Width - 140, 32),
                BorderRadius = 8,
                FillColor = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            btnView.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, imagePath);

            card.Controls.Add(picProfile);
            card.Controls.Add(lblName);
            card.Controls.Add(lblCode);
            card.Controls.Add(btnView);

            // Click anywhere on card to view details
            card.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, imagePath);

            return card;
        }

        /// <summary>
        /// Show employee details in the details panel
        /// </summary>
        private void ShowEmployeeDetails(int empId, string empCode, string fullName, string imagePath)
        {
            try
            {
                PanelDetails.Visible = true;
                PanelDetails.BringToFront();

                // Populate the controls
                LblFullName.Text = fullName;              // Display full name
                ShowTextEmp.Text = empCode;               // Display employee code (EMP-001)
                ShowTxtFaceId.Text = $"FACE-{empId:D3}"; // Display face ID (FACE-001)

                // Load profile picture into pnlScannerContainer
                if (pnlScannerContainer.Controls.Count == 0 || 
                    pnlScannerContainer.Controls[0] is not PictureBox)
                {
                    var picBox = new PictureBox
                    {
                        Dock = DockStyle.Fill,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.FromArgb(17, 24, 39)
                    };
                    pnlScannerContainer.Controls.Clear();
                    pnlScannerContainer.Controls.Add(picBox);
                }

                var profilePic = pnlScannerContainer.Controls[0] as PictureBox;
                if (profilePic != null)
                {
                    // Dispose old image
                    profilePic.Image?.Dispose();
                    
                    if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                    {
                        using (var img = Image.FromFile(imagePath))
                        {
                            profilePic.Image = new Bitmap(img);
                        }
                    }
                    else
                    {
                        profilePic.Image = CreateDefaultAvatar();
                    }
                }

                // Store employee data in button tags for Edit/Delete operations
                BtnEdit.Tag = new { empId, empCode, fullName, imagePath };
                BtnDelete.Tag = new { empId, empCode, fullName, imagePath };
                
                // Setup Edit and Delete button events (if not already done)
                SetupDetailsButtonEvents();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying employee details: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Create default avatar image
        /// </summary>
        private Bitmap CreateDefaultAvatar()
        {
            Bitmap bmp = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(200, 200, 200));
                g.DrawString("👤", new Font("Segoe UI", 40, FontStyle.Bold),
                    Brushes.Gray, new PointF(20, 20));
            }
            return bmp;
        }

        /// <summary>
        /// Show message when no profiles exist
        /// </summary>
        private void ShowNoProfilesMessage()
        {
            var lblNoData = new Label
            {
                Text = "📭 No employee profiles found.\n\nClick the + button to add new employees.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            PanelProfileList.Controls.Add(lblNoData);
        }

        /// <summary>
        /// Show message when search has no results
        /// </summary>
        private void ShowNoResultsMessage()
        {
            PanelProfileList.Controls.Clear();
            var lblNoResults = new Label
            {
                Text = "🔍 No profiles match your search.\n\nTry a different search term.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            PanelProfileList.Controls.Add(lblNoResults);
        }

        /// <summary>
        /// Refresh profile list (call after adding/editing/deleting)
        /// </summary>
        public void RefreshProfiles()
        {
            LoadAllProfiles();
            TxtSearchBar.Clear();
        }

        #endregion

        #region Details Panel - Edit & Delete

        /// <summary>
        /// Setup Edit and Delete button click events
        /// </summary>
        private void SetupDetailsButtonEvents()
        {
            // Remove existing handlers to avoid duplicates
            BtnEdit.Click -= BtnEdit_Click;
            BtnDelete.Click -= BtnDelete_Click;
            
            // Add handlers
            BtnEdit.Click += BtnEdit_Click;
            BtnDelete.Click += BtnDelete_Click;
        }

        /// <summary>
        /// Handle Edit button click
        /// </summary>
        private void BtnEdit_Click(object? sender, EventArgs e)
        {
            if (BtnEdit.Tag == null) return;

            try
            {
                dynamic empData = BtnEdit.Tag;
                
                // Show edit dialog
                using (var editForm = CreateEditDialog(empData.empId, empData.empCode, empData.fullName))
                {
                    if (editForm.ShowDialog() == DialogResult.OK)
                    {
                        // Refresh the profile list
                        RefreshProfiles();
                        
                        MessageBox.Show("Employee updated successfully!",
                            "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        
                        // Hide details panel
                        PanelDetails.Visible = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error editing employee: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Handle Delete button click
        /// </summary>
        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (BtnDelete.Tag == null) return;

            try
            {
                dynamic empData = BtnDelete.Tag;
                
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{empData.fullName}'?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    // Delete employee from database
                    DatabaseHelper.DeleteEmployee(empData.empId);
                    
                    MessageBox.Show("Employee deleted successfully!",
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    // Hide details panel
                    PanelDetails.Visible = false;
                    
                    // Refresh the profile list
                    RefreshProfiles();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting employee: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Create edit dialog for employee
        /// </summary>
        private Form CreateEditDialog(int empId, string empCode, string currentName)
        {
            var dialog = new Form
            {
                Text = "Edit Employee",
                Size = new Size(400, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            var lblTitle = new Label
            {
                Text = $"Edit Employee - {empCode}",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(20, 20),
                AutoSize = true
            };

            var lblName = new Label
            {
                Text = "Full Name:",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(20, 60),
                AutoSize = true
            };

            var txtName = new TextBox
            {
                Text = currentName,
                Location = new Point(20, 85),
                Size = new Size(340, 30),
                Font = new Font("Segoe UI", 11F)
            };

            var btnSave = new Button
            {
                Text = "Save",
                Location = new Point(20, 125),
                Size = new Size(160, 35),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(200, 125),
                Size = new Size(160, 35),
                BackColor = Color.FromArgb(229, 231, 235),
                ForeColor = Color.FromArgb(55, 65, 81),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            btnSave.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("Please enter a name.", "Required",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Update employee in database
                DatabaseHelper.UpdateEmployee(empId, txtName.Text);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };

            btnCancel.Click += (s, e) =>
            {
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            };

            dialog.Controls.AddRange(new Control[] { lblTitle, lblName, txtName, btnSave, btnCancel });
            dialog.AcceptButton = btnSave;
            dialog.CancelButton = btnCancel;

            return dialog;
        }

        #endregion

        #region Camera Operations

        private void InitializeCameraPanel()
        {
            PnlCamContainer.Controls.Clear();
            picCamera = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Black
            };
            PnlCamContainer.Controls.Add(picCamera);
        }

        private void StartCamera()
        {
            if (_isCameraRunning) return;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    _capture = new VideoCapture(i, VideoCapture.API.DShow);
                    if (_capture.IsOpened) break;
                    _capture?.Dispose();
                }
                catch { _capture?.Dispose(); }
            }

            if (_capture == null || !_capture.IsOpened)
            {
                MessageBox.Show("No camera could be opened.");
                return;
            }

            _capture.ImageGrabbed += ProcessFrame;
            _capture.Start();
            _isCameraRunning = true;
            BtnStartScan.Text = "Stop Camera";
        }

        private void StopCamera()
        {
            if (!_isCameraRunning) return;

            if (_capture != null)
            {
                _capture.ImageGrabbed -= ProcessFrame;
                _capture.Stop();
                _capture.Dispose();
                _capture = null;
            }

            _isCameraRunning = false;
            if (picCamera != null) picCamera.Image = null;
            BtnStartScan.Text = "Start Camera";
        }

        private void ProcessFrame(object? sender, EventArgs e)
        {
            if (picCamera == null || picCamera.IsDisposed || _capture == null) return;

            _lastFrame?.Dispose();
            _lastFrame = new Mat();

            try
            {
                _capture.Retrieve(_lastFrame);
                if (_lastFrame.IsEmpty) return;

                CvInvoke.Flip(_lastFrame, _lastFrame, FlipType.Horizontal);

                Bitmap bmp = _lastFrame.ToBitmap();

                if (picCamera.InvokeRequired)
                {
                    picCamera.Invoke(new Action(() =>
                    {
                        var old = picCamera.Image;
                        picCamera.Image = bmp;
                        old?.Dispose();
                    }));
                }
                else
                {
                    var old = picCamera.Image;
                    picCamera.Image = bmp;
                    old?.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessFrame error: {ex.Message}");
            }
        }

        #endregion

        #region Button Events

        private void BtnStartScan_Click(object? sender, EventArgs e)
        {
            if (!_isCameraRunning)
            {
                InitializeCameraPanel();
                StartCamera();
            }
            else
            {
                StopCamera();
            }
        }

        private async void BtnAdd_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtEnterName.Text))
            {
                MessageBox.Show("Please enter employee name.", "Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_lastFrame == null || _lastFrame.IsEmpty)
            {
                MessageBox.Show("Please capture a face image first.", "Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Disable buttons during processing
                BtnAdd.Enabled = false;
                BtnAdd.Text = "Checking...";
                
                // Small delay for UI update
                await Task.Delay(100);

                // Check if face already exists in the system
                var recognitionResult = _faceService.RecognizeFace(_lastFrame);
                
                if (recognitionResult.HasValue)
                {
                    var (existingEmpId, confidence, isStable) = recognitionResult.Value;
                    
                    // If confidence is high enough, face is already registered
                    if (confidence > 70) // Adjust threshold as needed (70-80 is good)
                    {
                        var existingEmployee = DatabaseHelper.GetEmployeeById(existingEmpId);
                        
                        if (existingEmployee.HasValue)
                        {
                            // Re-enable button
                            BtnAdd.Enabled = true;
                            BtnAdd.Text = "ADD";
                            
                            var result = MessageBox.Show(
                                $"⚠️ DUPLICATE FACE DETECTED!\n\n" +
                                $"This face is already registered to:\n" +
                                $"━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                $"👤 Name: {existingEmployee.Value.fullName}\n" +
                                $"🆔 Code: {existingEmployee.Value.empCode}\n" +
                                $"📊 Match Confidence: {confidence:F1}%\n" +
                                $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                                $"Adding the same person multiple times can cause\n" +
                                $"conflicts in the attendance system.\n\n" +
                                $"Do you still want to proceed?",
                                "⚠️ Duplicate Face Warning",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                        
                            if (result == DialogResult.No)
                            {
                                return; // Don't add duplicate
                            }
                        }
                    }
                }

                // Update button text
                BtnAdd.Text = "Saving...";
                await Task.Delay(100);

                // Proceed with adding the employee
                int empId = DatabaseHelper.AddEmployee(TxtEnterName.Text);
                
                string faceImagePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "FaceImages",
                    $"emp_{empId}_{DateTime.Now:yyyyMMddHHmmss}.jpg"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(faceImagePath)!);
                _lastFrame.ToBitmap().Save(faceImagePath);

                DatabaseHelper.AddFaceImage(empId, faceImagePath);

                // Retrain the model with the new face
                BtnAdd.Text = "Training...";
                await Task.Run(() => _faceService.TrainModel());

                MessageBox.Show(
                    $"✓ EMPLOYEE ADDED SUCCESSFULLY!\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"👤 Name: {TxtEnterName.Text}\n" +
                    $"🆔 Code: EMP-{empId:D3}\n" +
                    $"📸 Face image saved and trained\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                    $"The new employee can now use face recognition\n" +
                    $"for attendance tracking.",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                // Clear and refresh
                TxtEnterName.Clear();
                StopCamera();
                BtnAdd.Enabled = true;
                BtnAdd.Text = "ADD";
                PnlCameraEmpAdd.Visible = false;
                RefreshProfiles();
            }
            catch (Exception ex)
            {
                BtnAdd.Enabled = true;
                BtnAdd.Text = "ADD";
                MessageBox.Show($"❌ Error adding employee:\n\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            StopCamera();
            TxtEnterName.Clear();
            PnlCameraEmpAdd.Visible = false;
        }

        private void btnAddEmp_Click(object sender, EventArgs e)
        {
            PnlCameraEmpAdd.Visible = true;
            PnlCameraEmpAdd.BringToFront();
        }

        #endregion

        #region Designer Events

        private void PnlReadyAdd_Paint(object sender, PaintEventArgs e)
        {
        }

        private void PnlCameraEmpAdd_Paint(object sender, PaintEventArgs e)
        {
        }

        #endregion

        /// <summary>
        /// Close details panel and return to profile list
        /// </summary>
        private void CloseDetailsPanel()
        {
            PanelDetails.Visible = false;
            PanelProfileList.BringToFront();
        }
    }
}
