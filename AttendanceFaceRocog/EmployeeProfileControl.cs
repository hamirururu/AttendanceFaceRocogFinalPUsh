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
        
        // Add these fields for face recognition
        private int? _recognizedEmpId = null;
        private string? _recognizedEmpName = null;
        private bool _isRecognitionStable = false;
        private Label? lblRecognitionStatus;

        private string? _capturedFullPhotoPath = null;  // Store full photo path temporarily

        public EmployeeProfileControl()
        {
            InitializeComponent();
            
            // Use singleton instance - same instance as UserControl1
            _faceService = FaceRecognitionService.Instance;
            _faceService.TrainModel();
            
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
        /// Load all employee profiles into the panel (ONE per employee)
        /// </summary>
        private void LoadAllProfiles()
        {
            try
            {
                // Use the display method that returns ONE row per employee
                _allEmployees = DatabaseHelper.GetAllEmployeesForDisplay();
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

            if (employees == null || employees.Rows.Count == 0)
            {
                Label lblNoProfiles = new Label
                {
                    Text = "No employees added yet",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Gray,
                    Font = new Font("Arial", 11)
                };
                PanelProfileList.Controls.Add(lblNoProfiles);
                return;
            }

            foreach (DataRow row in employees.Rows)
            {
                int empId = (int)row["empID"];
                string fullName = row["FullName"].ToString() ?? "";
                string empCode = row["empCode"]?.ToString() ?? $"EMP-{empId:D3}";
                string imgPath = row["imgPath"]?.ToString() ?? "";

                // Get profile photo path - prioritize full photo over face image
                string? profilePhotoPath = DatabaseHelper.GetProfilePhoto(empId);
                string photoToDisplay = (!string.IsNullOrEmpty(profilePhotoPath) && File.Exists(profilePhotoPath))
                    ? profilePhotoPath
                    : imgPath;

                // Create a panel for each employee card
                Panel empPanel = new Panel
                {
                    Width = 250,
                    Height = 300,
                    Margin = new Padding(10),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White
                };

                // Display profile photo if available, otherwise face image
                PictureBox picProfile = new PictureBox
                {
                    Width = 250,
                    Height = 200,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.LightGray
                };

                try
                {
                    if (!string.IsNullOrEmpty(photoToDisplay) && File.Exists(photoToDisplay))
                    {
                        var img = new Bitmap(photoToDisplay);
                        picProfile.Image = img;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading profile photo: {ex.Message}");
                }

                picProfile.Dock = DockStyle.Top;
                empPanel.Controls.Add(picProfile);

                // Name label
                Label lblName = new Label
                {
                    Text = fullName,
                    Dock = DockStyle.Top,
                    Height = 35,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Arial", 11, FontStyle.Bold),
                    ForeColor = Color.Black
                };
                empPanel.Controls.Add(lblName);

                // Employee code label
                Label lblCode = new Label
                {
                    Text = $"Code: {empCode}",
                    Dock = DockStyle.Top,
                    Height = 25,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Arial", 9),
                    ForeColor = Color.DarkGray
                };
                empPanel.Controls.Add(lblCode);

                // View button
                Button btnView = new Button
                {
                    Text = "View Details",
                    Dock = DockStyle.Bottom,
                    Height = 35,
                    BackColor = Color.FromArgb(34, 197, 94),
                    ForeColor = Color.White,
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat
                };

                btnView.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, photoToDisplay);
                empPanel.Controls.Add(btnView);

                PanelProfileList.Controls.Add(empPanel);
            }
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
                Cursor = Cursors.Hand,
                Tag = empId
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
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(240, 240, 240)
            };

            // Load image or default avatar
            try
            {
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    picProfile.Image = Image.FromFile(imagePath);
                }
                else
                {
                    picProfile.Image = CreateDefaultAvatar(fullName);
                }
            }
            catch
            {
                picProfile.Image = CreateDefaultAvatar(fullName);
            }

            card.Controls.Add(picProfile);

            // Employee name
            var lblName = new Label
            {
                Text = fullName,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(100, 20),
                AutoSize = true
            };
            card.Controls.Add(lblName);

            // Employee code
            var lblCode = new Label
            {
                Text = empCode,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(107, 114, 128),
                Location = new Point(100, 50),
                AutoSize = true
            };
            card.Controls.Add(lblCode);

            // Click event for the card - pass all required parameters
            card.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, imagePath);
            picProfile.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, imagePath);
            lblName.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, imagePath);
            lblCode.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, imagePath);

            return card;
        }

        /// <summary>
        /// Create a default avatar with initials
        /// </summary>
        private Image CreateDefaultAvatar(string name)
        {
            var bmp = new Bitmap(70, 70);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillRectangle(new SolidBrush(Color.FromArgb(99, 102, 241)), 0, 0, 70, 70);
                
                // Get initials
                string initials = "";
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    initials = $"{parts[0][0]}{parts[^1][0]}".ToUpper();
                else if (parts.Length == 1 && parts[0].Length > 0)
                    initials = parts[0][0].ToString().ToUpper();
                else
                    initials = "?";

                using var font = new Font("Segoe UI", 20F, FontStyle.Bold);
                var size = g.MeasureString(initials, font);
                g.DrawString(initials, font, Brushes.White, 
                    (70 - size.Width) / 2, 
                    (70 - size.Height) / 2);
            }
            return bmp;
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
                    
                    // Try to get full profile photo first
                    string? profilePhotoPath = DatabaseHelper.GetProfilePhoto(empId);
                    string photoToDisplay = profilePhotoPath ?? imagePath ?? "";

                    // If no full profile photo, use face image
                    if (string.IsNullOrEmpty(photoToDisplay) || !File.Exists(photoToDisplay))
                    {
                        photoToDisplay = imagePath ?? "";
                    }

                    if (!string.IsNullOrEmpty(photoToDisplay) && File.Exists(photoToDisplay))
                    {
                        using (var img = Image.FromFile(photoToDisplay))
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
        /// Refresh profiles list
        /// </summary>
        private void RefreshProfiles()
        {
            LoadAllProfiles();
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
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            PnlCamContainer.Controls.Add(picCamera);

            // Add recognition status label
            lblRecognitionStatus = new Label
            {
                Text = "● Scanning...",
                ForeColor = Color.Gray,
                BackColor = Color.FromArgb(40, 40, 40),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleCenter
            };
            PnlCamContainer.Controls.Add(lblRecognitionStatus);
            lblRecognitionStatus.BringToFront();

            // Initialize Add button as disabled until new face is detected
            SetAddButtonState(false, "Waiting...");
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

            // Reset recognition state
            _recognizedEmpId = null;
            _recognizedEmpName = null;
            _isRecognitionStable = false;

            // Reset Add button to default state
            BtnAdd.Enabled = true;
            BtnAdd.Text = "ADD";
            BtnAdd.FillColor = Color.FromArgb(34, 197, 94);
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

                // Perform face recognition (same logic as UserControl1)
                ProcessFaceRecognition(_lastFrame);

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

/// <summary>
/// Process face recognition on the current frame
/// </summary>
private void ProcessFaceRecognition(Mat frame)
{
    try
    {
        var result = _faceService.RecognizeFace(frame);

        if (result.HasValue)
        {
            var (empId, confidence, isStable, distanceInches) = result.Value;
            var emp = DatabaseHelper.GetEmployeeById(empId);

            if (emp.HasValue)
            {
                _recognizedEmpId = emp.Value.empId;
                _recognizedEmpName = emp.Value.fullName;
                _isRecognitionStable = isStable;

                // Draw face rectangles with recognition info
                var faces = _faceService.DetectFaces(frame);
                foreach (var face in faces)
                {
                    var color = isStable ? new MCvScalar(0, 255, 0) : new MCvScalar(0, 165, 255);
                    CvInvoke.Rectangle(frame, face, color, 3);

                    string displayText = isStable
                        ? $"{emp.Value.fullName} ({confidence:F0}%)"
                        : "Verifying...";

                    CvInvoke.PutText(frame, displayText,
                        new Point(face.X, face.Y - 10),
                        FontFace.HersheySimplex, 0.6, color, 2);

                    if (isStable)
                    {
                        CvInvoke.PutText(frame, "REGISTERED",
                            new Point(face.X, face.Y + face.Height + 20),
                            FontFace.HersheySimplex, 0.5, new MCvScalar(0, 255, 0), 2);
                    }
                }

                UpdateRecognitionStatus(isStable
                    ? $"✓ Already registered: {emp.Value.fullName}"
                    : "● Verifying...",
                    isStable ? Color.LimeGreen : Color.Orange);

                // DISABLE Add button when face is already registered
                if (isStable)
                {
                    SetAddButtonState(false, $"Already Registered: {emp.Value.fullName}");
                }
            }
        }
        else
        {
            var faces = _faceService.DetectFaces(frame);

            if (faces.Length > 0)
            {
                _recognizedEmpId = null;
                _recognizedEmpName = null;
                _isRecognitionStable = false;

                foreach (var face in faces)
                {
                    CvInvoke.Rectangle(frame, face, new MCvScalar(255, 165, 0), 3);
                    CvInvoke.PutText(frame, "NEW FACE",
                        new Point(face.X, face.Y - 10),
                        FontFace.HersheySimplex, 0.6, new MCvScalar(255, 165, 0), 2);
                }

                UpdateRecognitionStatus("● New face - Ready to register", Color.DodgerBlue);

                // ENABLE Add button for new faces
                SetAddButtonState(true, "ADD");
            }
            else
            {
                _recognizedEmpId = null;
                _recognizedEmpName = null;
                _isRecognitionStable = false;
                UpdateRecognitionStatus("● No face detected", Color.Gray);

                // DISABLE Add button when no face detected
                SetAddButtonState(false, "No Face");
            }
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Face recognition error: {ex.Message}");
    }
}

/// <summary>
/// Enable or disable the Add button based on face detection status (thread-safe)
/// </summary>
private void SetAddButtonState(bool enabled, string text)
{
    if (BtnAdd.InvokeRequired)
    {
        BtnAdd.Invoke(new Action(() =>
        {
            BtnAdd.Enabled = enabled;
            BtnAdd.Text = text;
            
            // Visual feedback - change button appearance based on state
            if (enabled)
            {
                BtnAdd.FillColor = Color.FromArgb(34, 197, 94); // Green for enabled
            }
            else
            {
                BtnAdd.FillColor = Color.FromArgb(156, 163, 175); // Gray for disabled
            }
        }));
    }
    else
    {
        BtnAdd.Enabled = enabled;
        BtnAdd.Text = text;
        
        if (enabled)
        {
            BtnAdd.FillColor = Color.FromArgb(34, 197, 94); // Green
        }
        else
        {
            BtnAdd.FillColor = Color.FromArgb(156, 163, 175); // Gray
        }
    }
}

/// <summary>
/// Update recognition status label (thread-safe)
/// </summary>
private void UpdateRecognitionStatus(string text, Color color)
{
    if (lblRecognitionStatus == null) return;

    if (lblRecognitionStatus.InvokeRequired)
    {
        lblRecognitionStatus.Invoke(new Action(() =>
        {
            lblRecognitionStatus.Text = text;
            lblRecognitionStatus.ForeColor = color;
        }));
    }
    else
    {
        lblRecognitionStatus.Text = text;
        lblRecognitionStatus.ForeColor = color;
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
                BtnAdd.Enabled = false;
                BtnAdd.Text = "Checking...";
                
                await Task.Delay(100);

                // Check if face already exists
                var recognitionResult = _faceService.RecognizeFace(_lastFrame);
                
                if (recognitionResult.HasValue)
                {
                    var (existingEmpId, confidence, isStable, distanceInches) = recognitionResult.Value;
                    
                    if (confidence > 70)
                    {
                        var existingEmployee = DatabaseHelper.GetEmployeeById(existingEmpId);
                        
                        if (existingEmployee.HasValue)
                        {
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
                                $"Do you still want to proceed?",
                                "⚠️ Duplicate Face Warning",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                        
                            if (result == DialogResult.No)
                            {
                                return;
                            }
                        }
                    }
                }

                BtnAdd.Text = "Capturing...";
                await Task.Delay(100);

                // Add employee to database
                int empId = DatabaseHelper.AddEmployee(TxtEnterName.Text);
                
                // Capture MULTIPLE face variations for better training
                List<string> faceImagePaths = _faceService.CaptureMultipleFaces(_lastFrame, empId, 5);
                
                if (faceImagePaths.Count == 0)
                {
                    DatabaseHelper.DeleteEmployee(empId);
                    
                    BtnAdd.Enabled = true;
                    BtnAdd.Text = "ADD";
                    
                    MessageBox.Show(
                        "❌ Could not capture face!\n\n" +
                        "Please ensure:\n" +
                        "• Your face is clearly visible\n" +
                        "• You are close enough to the camera\n" +
                        "• There is good lighting",
                        "Face Capture Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                // Save all face image paths to database
                foreach (string path in faceImagePaths)
                {
                    DatabaseHelper.AddFaceImage(empId, path);
                }

                // Capture and save full profile photo
                BtnAdd.Text = "Saving Photo...";
                _capturedFullPhotoPath = _faceService.CaptureFullPhoto(_lastFrame, empId);
                
                if (!string.IsNullOrEmpty(_capturedFullPhotoPath))
                {
                    DatabaseHelper.AddProfilePhoto(empId, _capturedFullPhotoPath);
                }

                // Retrain the model with the new faces
                BtnAdd.Text = "Training...";
                await Task.Run(() => _faceService.TrainModel());

                MessageBox.Show(
                    $"✓ EMPLOYEE ADDED SUCCESSFULLY!\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"👤 Name: {TxtEnterName.Text}\n" +
                    $"🆔 Code: EMP-{empId:D3}\n" +
                    $"📸 {faceImagePaths.Count} face images captured & trained\n" +
                    $"📷 Profile photo saved\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                    $"The new employee can now use face recognition\n" +
                    $"for attendance tracking.",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                TxtEnterName.Clear();
                _capturedFullPhotoPath = null;
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
