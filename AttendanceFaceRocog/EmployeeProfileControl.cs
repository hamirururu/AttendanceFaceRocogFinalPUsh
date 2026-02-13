using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        public EmployeeProfileControl()
        {
            InitializeComponent();

            // Use singleton instance - same instance as UserControl1
            _faceService = FaceRecognitionService.Instance;
            _faceService.TrainModel();

            SetupButtonEvents();
            SetupSearchEvent();

            PnlCameraEmpAdd.Visible = false;
            PanelDetails.Visible = false;

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

        private void LoadAllProfiles()
        {
            try
            {
                _allEmployees = DatabaseHelper.GetAllEmployeesForDisplay();
                DisplayProfiles(_allEmployees);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading profiles: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TxtSearchBar_TextChanged(object? sender, EventArgs e)
        {
            FilterProfiles(TxtSearchBar.Text);
        }

        private void FilterProfiles(string searchText)
        {
            if (_allEmployees == null) return;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                DisplayProfiles(_allEmployees);
                return;
            }

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
                    Font = new Font("Segoe UI", 11)
                };
                PanelProfileList.Controls.Add(lblNoProfiles);
                return;
            }

            var flowPanel = new FlowLayoutPanel
            {
                Location = new Point(8, 8),
                Size = new Size(PanelProfileList.Width - 16, PanelProfileList.Height - 16),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(2)
            };

            foreach (DataRow row in employees.Rows)
            {
                int empId = (int)row["empID"];
                string fullName = row["FullName"].ToString() ?? "";
                string empCode = row["empCode"]?.ToString() ?? $"EMP-{empId:D3}";

                // Get profile photo path (colored photo)
                string? profilePhotoPath = DatabaseHelper.GetProfilePhoto(empId);

                var empCard = CreateEmployeeCard(empId, fullName, empCode, profilePhotoPath);
                flowPanel.Controls.Add(empCard);
            }

            PanelProfileList.Controls.Add(flowPanel);
        }

        private Panel CreateEmployeeCard(int empId, string fullName, string empCode, string? photoPath)
        {
            var cardPanel = new Panel
            {
                Size = new Size(555, 100),
                BackColor = Color.White,
                Margin = new Padding(3),
                BorderStyle = BorderStyle.FixedSingle
            };

            cardPanel.ForeColor = Color.FromArgb(230, 230, 230);

            // Employee Photo (colored profile picture)
            var picFace = new PictureBox
            {
                Location = new Point(12, 12),
                Size = new Size(70, 70),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle
            };

            try
            {
                if (!string.IsNullOrEmpty(photoPath) && File.Exists(photoPath))
                {
                    using (var img = Image.FromFile(photoPath))
                    {
                        picFace.Image = new Bitmap(img);
                    }
                }
                else
                {
                    picFace.Image = CreateDefaultAvatarForProfile();
                }
            }
            catch
            {
                picFace.Image = CreateDefaultAvatarForProfile();
            }

            cardPanel.Controls.Add(picFace);

            var lblName = new Label
            {
                Text = fullName,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(95, 15),
                MaximumSize = new Size(350, 25),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPanel.Controls.Add(lblName);

            var lblCode = new Label
            {
                Text = empCode,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(107, 114, 128),
                Location = new Point(95, 42),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPanel.Controls.Add(lblCode);

            var lblInfo = new Label
            {
                Text = $"Added on {DateTime.Now:MMM dd, yyyy}",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(156, 163, 175),
                Location = new Point(95, 60),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPanel.Controls.Add(lblInfo);

            var btnViewDetails = new Button
            {
                Text = "View Details",
                Location = new Point(450, 32),
                Size = new Size(95, 35),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 197, 94),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };

            btnViewDetails.FlatAppearance.BorderColor = Color.FromArgb(34, 197, 94);
            btnViewDetails.FlatAppearance.BorderSize = 2;

            btnViewDetails.Click += (s, e) => ShowEmployeeDetails(empId, empCode, fullName, photoPath);
            cardPanel.Controls.Add(btnViewDetails);

            return cardPanel;
        }

        private Image CreateDefaultAvatarForProfile()
        {
            var bmp = new Bitmap(70, 70);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 200, 200)), 0, 0, 70, 70);
                g.DrawString("👤", new Font("Segoe UI", 30), Brushes.White, new PointF(10, 8));
            }
            return bmp;
        }

        private void ShowEmployeeDetails(int empId, string empCode, string fullName, string? imagePath)
        {
            try
            {
                PanelDetails.Visible = true;
                PanelDetails.BringToFront();

                LblFullName.Text = fullName;
                ShowTextEmp.Text = empCode;
                ShowTxtFaceId.Text = $"FACE-{empId:D3}";

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
                    profilePic.Image?.Dispose();

                    // Get colored profile photo
                    string? profilePhotoPath = DatabaseHelper.GetProfilePhoto(empId);

                    if (!string.IsNullOrEmpty(profilePhotoPath) && File.Exists(profilePhotoPath))
                    {
                        using (var img = Image.FromFile(profilePhotoPath))
                        {
                            profilePic.Image = new Bitmap(img);
                        }
                    }
                    else
                    {
                        profilePic.Image = CreateDefaultAvatar();
                    }
                }

                BtnEdit.Tag = new { empId, empCode, fullName, imagePath };
                BtnDelete.Tag = new { empId, empCode, fullName, imagePath };

                SetupDetailsButtonEvents();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying employee details: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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

        private void RefreshProfiles()
        {
            LoadAllProfiles();
        }

        #endregion

        #region Details Panel - Edit & Delete

        private void SetupDetailsButtonEvents()
        {
            BtnEdit.Click -= BtnEdit_Click;
            BtnDelete.Click -= BtnDelete_Click;

            BtnEdit.Click += BtnEdit_Click;
            BtnDelete.Click += BtnDelete_Click;
        }

        private void BtnEdit_Click(object? sender, EventArgs e)
        {
            if (BtnEdit.Tag == null) return;

            try
            {
                dynamic empData = BtnEdit.Tag;

                using (var editForm = CreateEditDialog(empData.empId, empData.empCode, empData.fullName))
                {
                    if (editForm.ShowDialog() == DialogResult.OK)
                    {
                        RefreshProfiles();
                        MessageBox.Show("Employee updated successfully!",
                            "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    DatabaseHelper.DeleteEmployee(empData.empId);
                    MessageBox.Show("Employee deleted successfully!",
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    PanelDetails.Visible = false;
                    RefreshProfiles();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting employee: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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

            _recognizedEmpId = null;
            _recognizedEmpName = null;
            _isRecognitionStable = false;

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

                        if (isStable)
                        {
                            SetAddButtonState(false, $"Already Registered");
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
                        SetAddButtonState(true, "ADD");
                    }
                    else
                    {
                        _recognizedEmpId = null;
                        _recognizedEmpName = null;
                        _isRecognitionStable = false;
                        UpdateRecognitionStatus("● No face detected", Color.Gray);
                        SetAddButtonState(false, "No Face");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Face recognition error: {ex.Message}");
            }
        }

        private void SetAddButtonState(bool enabled, string text)
        {
            if (BtnAdd.InvokeRequired)
            {
                BtnAdd.Invoke(new Action(() =>
                {
                    BtnAdd.Enabled = enabled;
                    BtnAdd.Text = text;
                    BtnAdd.FillColor = enabled ? Color.FromArgb(34, 197, 94) : Color.FromArgb(156, 163, 175);
                }));
            }
            else
            {
                BtnAdd.Enabled = enabled;
                BtnAdd.Text = text;
                BtnAdd.FillColor = enabled ? Color.FromArgb(34, 197, 94) : Color.FromArgb(156, 163, 175);
            }
        }

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

                // Check for duplicate face
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

                // Add employee to database
                int empId = DatabaseHelper.AddEmployee(TxtEnterName.Text);

                // ✅ STEP 1: Capture 4 GRAYSCALE face images for training
                BtnAdd.Text = "Capturing training images...";
                await Task.Delay(100);

                System.Collections.Generic.List<string> trainingImagePaths =
                    _faceService.CaptureMultipleFaces(_lastFrame, empId, 4);

                if (trainingImagePaths.Count == 0)
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

                // Save training images to database
                foreach (string path in trainingImagePaths)
                {
                    DatabaseHelper.AddFaceImage(empId, path);
                }

                // ✅ STEP 2: Capture 1 COLORED profile photo (face only)
                BtnAdd.Text = "Capturing profile photo...";
                await Task.Delay(100);

                string? profilePhotoPath = CaptureFaceOnlyColoredPhoto(_lastFrame, empId);

                if (!string.IsNullOrEmpty(profilePhotoPath))
                {
                    DatabaseHelper.AddProfilePhoto(empId, profilePhotoPath);
                }

                // ✅ STEP 3: Retrain the model
                BtnAdd.Text = "Training model...";
                await Task.Run(() => _faceService.TrainModel());

                MessageBox.Show(
                    $"✓ EMPLOYEE ADDED SUCCESSFULLY!\n\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"👤 Name: {TxtEnterName.Text}\n" +
                    $"🆔 Code: EMP-{empId:D3}\n" +
                    $"📸 {trainingImagePaths.Count} training images captured\n" +
                    $"🎨 1 colored profile photo saved\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                    $"The new employee can now use face recognition!",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

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

        /// <summary>
        /// Capture COLORED face-only photo for profile display
        /// </summary>
        private string? CaptureFaceOnlyColoredPhoto(Mat frame, int empId)
        {
            try
            {
                Rectangle[] faces = _faceService.DetectFaces(frame);
                if (faces.Length == 0) return null;

                // Get the largest face
                Rectangle face = faces.OrderByDescending(f => f.Width * f.Height).First();

                // Add padding around face (20%)
                int padding = (int)(face.Width * 0.2);
                int x = Math.Max(0, face.X - padding);
                int y = Math.Max(0, face.Y - padding);
                int width = Math.Min(frame.Width - x, face.Width + 2 * padding);
                int height = Math.Min(frame.Height - y, face.Height + 2 * padding);

                Rectangle paddedFace = new Rectangle(x, y, width, height);

                // Extract COLORED face region
                Mat coloredFaceRegion = new Mat(frame, paddedFace);

                // Save as colored JPEG
                string fileName = $"emp_{empId}_profile_color_{DateTime.Now:yyyyMMddHHmmss}.jpg";
                string filePath = Path.Combine(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Faces"),
                    fileName);

                Bitmap bmp = coloredFaceRegion.ToBitmap();
                bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
                bmp.Dispose();
                coloredFaceRegion.Dispose();

                System.Diagnostics.Debug.WriteLine($"✓ Colored profile photo saved: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing colored profile photo: {ex.Message}");
                return null;
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

        private void CloseDetailsPanel()
        {
            PanelDetails.Visible = false;
            PanelProfileList.BringToFront();
        }
    }
}