using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AttendanceFaceRocog
{
    public partial class UserControl1 : UserControl
    {
        private string selectedAction = "TimeIn";
        public string SelectedAction => selectedAction;
        private Label lblStatus = null!;

        // Camera & Face Detection
        private VideoCapture? _capture;
        private PictureBox? picCamera;
        private bool _isCameraRunning = false;
        private FaceRecognitionService? _faceService;
        private int? _recognizedEmpId;
        private string? _recognizedEmpName;

        // Recognition state tracking
        private bool _isRecognitionStable = false;
        private bool _hasLoggedAttendance = false; // Prevent duplicate logs

        public UserControl1()
        {
            InitializeComponent();
            CreateStatusLabel();
            SetupButtonEvents();
            InitializeProfilePanel();
            this.Resize += UserControl1_Resize;
        }

        private void InitializeProfilePanel()
        {
            // Hide profile panel initially
            PanelProfileStatus.Visible = false;

            // Initialize labels if they don't exist in designer
            if (LblFullName == null || LblEmployeeID == null || ProfilePic == null ||
                TxtTime == null || TxtDate == null || PanelActionStatus == null)
            {
                // These should be created in the designer
                // This is just a safety check
            }
        }

        private void CreateStatusLabel()
        {
            pnlStatusBadge.Controls.Clear();
            lblStatus = new Label
            {
                Text = "●  Ready to Scan",
                ForeColor = Color.FromArgb(156, 163, 175),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlStatusBadge.Controls.Add(lblStatus);
        }

        private void SetupButtonEvents()
        {
            BtnTimeIn.Click += BtnTimeIn_Click;
            BtnTimeOut.Click += BtnTimeOut_Click;
            BtnStartBreak.Click += BtnStartBreak_Click;
            BtnStopBreak.Click += BtnStopBreak_Click;
            BtnStartScan.Click += BtnStartScan_Click;

            // Set initial state
            BtnTimeIn_Click(null, EventArgs.Empty);
        }

        private void UserControl1_Resize(object? sender, EventArgs e)
        {
            CenterControls();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CenterControls();
        }

        private void CenterControls()
        {
            int panelWidth = guna2Panel2.ClientSize.Width;
            int panelHeight = guna2Panel2.ClientSize.Height;

            // Scanner dimensions
            int scannerWidth = 415;
            int scannerHeight = 293;

            if (panelWidth > 700)
            {
                scannerWidth = Math.Min(550, (int)(panelWidth * 0.68));
                scannerHeight = (int)(scannerWidth * 0.707);
            }

            int maxScannerHeight = panelHeight - 280;
            if (scannerHeight > maxScannerHeight && maxScannerHeight > 200)
            {
                scannerHeight = maxScannerHeight;
                scannerWidth = (int)(scannerHeight / 0.707);
            }

            pnlScannerContainer.Size = new Size(scannerWidth, scannerHeight);
            pnlScannerContainer.Left = (panelWidth - scannerWidth) / 2;
            pnlScannerContainer.Top = 20;

            int buttonWidth = 150;
            int buttonHeight = 40;
            int buttonSpacing = 20;
            int totalButtonWidth = (buttonWidth * 2) + buttonSpacing;
            int buttonStartX = (panelWidth - totalButtonWidth) / 2;

            pnlStatusBadge.Size = new Size(140, 32);
            pnlStatusBadge.Left = (panelWidth - 140) / 2;
            pnlStatusBadge.Top = pnlScannerContainer.Bottom + 6;

            lblSelectAction.Left = (panelWidth - lblSelectAction.Width) / 2;
            lblSelectAction.Top = pnlStatusBadge.Bottom + 11;

            BtnTimeIn.Size = new Size(buttonWidth, buttonHeight);
            BtnTimeOut.Size = new Size(buttonWidth, buttonHeight);
            BtnStartBreak.Size = new Size(buttonWidth, buttonHeight);
            BtnStopBreak.Size = new Size(buttonWidth, buttonHeight);

            int row1Y = lblSelectAction.Bottom + 11;
            BtnTimeIn.Left = buttonStartX;
            BtnTimeIn.Top = row1Y;
            BtnTimeOut.Left = BtnTimeIn.Right + buttonSpacing;
            BtnTimeOut.Top = row1Y;

            int row2Y = BtnTimeIn.Bottom + 10;
            BtnStartBreak.Left = buttonStartX;
            BtnStartBreak.Top = row2Y;
            BtnStopBreak.Left = BtnStartBreak.Right + buttonSpacing;
            BtnStopBreak.Top = row2Y;

            BtnStartScan.Size = new Size(totalButtonWidth, buttonHeight);
            BtnStartScan.Left = buttonStartX;
            BtnStartScan.Top = BtnStopBreak.Bottom + 10;
        }

        #region Camera & Face Detection

        private void InitializeCameraPanel()
        {
            pnlScannerContainer.Controls.Clear();
            picCamera = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Black
            };
            pnlScannerContainer.Controls.Add(picCamera);

            // Initialize face recognition service
            _faceService = new FaceRecognitionService();
            _faceService.TrainModel();
        }

        private void StartCamera()
        {
            if (_isCameraRunning) return;

            // Reset attendance logging flag when starting camera
            _hasLoggedAttendance = false;
            PanelProfileStatus.Visible = false;

            bool cameraOpened = false;
            int successfulCameraIndex = -1;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    _capture = new VideoCapture(i, VideoCapture.API.DShow);
                    if (_capture.IsOpened)
                    {
                        cameraOpened = true;
                        successfulCameraIndex = i;
                        break;
                    }
                    else
                    {
                        _capture?.Dispose();
                    }
                }
                catch
                {
                    _capture?.Dispose();
                }
            }

            if (!cameraOpened || _capture == null)
            {
                MessageBox.Show("No camera could be opened. Please check if your camera is connected.");
                return;
            }

            _capture.ImageGrabbed += ProcessFrame;
            _capture.Start();
            _isCameraRunning = true;
            lblStatus.Text = "●  Scanning...";
            lblStatus.ForeColor = Color.LimeGreen;
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

            if (picCamera != null)
            {
                picCamera.Image = null;
            }

            lblStatus.Text = "●  Ready to Scan";
            lblStatus.ForeColor = Color.FromArgb(156, 163, 175);
        }

        private void ProcessFrame(object? sender, EventArgs e)
        {
            if (picCamera == null || picCamera.IsDisposed || _capture == null) return;

            Mat frame = new Mat();
            try
            {
                _capture.Retrieve(frame);

                if (frame == null || frame.IsEmpty) return;

                // Flip for un-mirrored view
                CvInvoke.Flip(frame, frame, FlipType.Horizontal);

                DetectAndDrawFaces(frame);

                Bitmap bmp = frame.ToBitmap();

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
            finally
            {
                frame?.Dispose();
            }
        }

        private void DetectAndDrawFaces(Mat frame)
        {
            if (_faceService == null)
            {
                UpdateStatus("●  Face Service Not Ready", Color.FromArgb(249, 115, 22));
                return;
            }

            try
            {
                var result = _faceService.RecognizeFace(frame);

                if (result.HasValue)
                {
                    var (empId, confidence, isStable) = result.Value;

                    var emp = DatabaseHelper.GetEmployeeById(empId);

                    if (emp.HasValue)
                    {
                        _recognizedEmpId = emp.Value.empId;
                        _recognizedEmpName = emp.Value.fullName;
                        _isRecognitionStable = isStable;

                        if (isStable)
                        {
                            UpdateStatus($"●  {emp.Value.fullName}", Color.LimeGreen);

                            // Automatically log attendance when recognition is stable
                            if (!_hasLoggedAttendance)
                            {
                                AutoLogAttendance();
                            }
                        }
                        else
                        {
                            UpdateStatus($"●  Verifying...", Color.FromArgb(249, 115, 22));
                        }

                        var faces = _faceService.DetectFaces(frame);
                        foreach (var face in faces)
                        {
                            var color = isStable ? new MCvScalar(0, 255, 0) : new MCvScalar(0, 165, 255);

                            CvInvoke.Rectangle(frame, face, color, 3);

                            if (isStable)
                            {
                                string displayText = $"{emp.Value.fullName}";
                                CvInvoke.PutText(frame, displayText,
                                    new Point(face.X, face.Y - 10),
                                    FontFace.HersheySimplex, 0.7, color, 2);
                            }
                            else
                            {
                                CvInvoke.PutText(frame, "Verifying...",
                                    new Point(face.X, face.Y - 10),
                                    FontFace.HersheySimplex, 0.7, color, 2);
                            }
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
                        UpdateStatus("●  Unknown Face", Color.FromArgb(249, 115, 22));

                        foreach (var face in faces)
                        {
                            CvInvoke.Rectangle(frame, face, new MCvScalar(0, 0, 255), 3);
                            CvInvoke.PutText(frame, "Unknown",
                                new Point(face.X, face.Y - 10),
                                FontFace.HersheySimplex, 0.7, new MCvScalar(0, 0, 255), 2);
                        }
                    }
                    else
                    {
                        _recognizedEmpId = null;
                        _recognizedEmpName = null;
                        _isRecognitionStable = false;
                        UpdateStatus("●  No Face Detected", Color.FromArgb(156, 163, 175));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Face detection error: {ex.Message}");
            }
        }

        private void AutoLogAttendance()
        {
            if (_recognizedEmpId == null || !_isRecognitionStable || _hasLoggedAttendance)
                return;

            // Marshal to UI thread for all operations
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(AutoLogAttendance));
                return;
            }

            try
            {
                // Get current time
                string currentTime = DateTime.Now.ToString("hh:mm:ss tt");

                // Set time values based on selected action
                string timeIn = "";
                string timeOut = "";
                string startBreak = "";
                string stopBreak = "";

                switch (selectedAction)
                {
                    case "TimeIn":
                        timeIn = currentTime;
                        break;
                    case "TimeOut":
                        timeOut = currentTime;
                        break;
                    case "StartBreak":
                        startBreak = currentTime;
                        break;
                    case "StopBreak":
                        stopBreak = currentTime;
                        break;
                }

                // Log attendance to database with all parameters
                DatabaseHelper.LogAttendance(_recognizedEmpId.Value, timeIn, timeOut, startBreak, stopBreak);

                _hasLoggedAttendance = true;

                // Get employee details with image
                var empDetails = DatabaseHelper.GetEmployeeById(_recognizedEmpId.Value);
                if (empDetails.HasValue)
                {
                    // Display profile status panel
                    DisplayAttendanceConfirmation(empDetails.Value.empId, empDetails.Value.fullName);
                }

                // Stop camera after successful log
                StopCamera();
                BtnStartScan.Text = "Start Face Scan";
                BtnStartScan.FillColor = Color.FromArgb(99, 102, 241);

                // Show success message
                MessageBox.Show($"{selectedAction} recorded successfully for {_recognizedEmpName}!",
                    "Attendance Recorded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error logging attendance: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisplayAttendanceConfirmation(int empId, string fullName)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => DisplayAttendanceConfirmation(empId, fullName)));
                return;
            }

            try
            {
                // Get employee face image from database
                string? imagePath = DatabaseHelper.GetEmployeeFaceImage(empId);

                // Set employee name
                LblFullName.Text = fullName;

                // Set employee ID
                LblEmployeeID.Text = $"EMP-{empId:D3}";

                // Set profile picture
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    using (var img = Image.FromFile(imagePath))
                    {
                        ProfilePic.Image = new Bitmap(img);
                    }
                }
                else
                {
                    // Set default avatar if no image
                    ProfilePic.Image = CreateDefaultAvatar();
                }

                // Set time
                TxtTime.Text = DateTime.Now.ToString("hh:mm:ss tt");

                // Set date
                TxtDate.Text = DateTime.Now.ToString("MMM dd, yyyy").ToUpper();

                // Set action status panel with gradient colors
                SetActionStatusPanel(selectedAction);

                // Show the profile panel
                PanelProfileStatus.Visible = true;
                PanelProfileStatus.BringToFront();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying confirmation: {ex.Message}");
            }
        }

        private void SetActionStatusPanel(string action)
        {
            // Clear existing gradient
            PanelActionStatus.FillColor = Color.White;

            Label? actionLabel = null;

            // Find or create action label inside PanelActionStatus
            foreach (Control ctrl in PanelActionStatus.Controls)
            {
                if (ctrl is Label lbl && lbl.Name == "LblActionStatus")
                {
                    actionLabel = lbl;
                    break;
                }
            }

            if (actionLabel == null)
            {
                actionLabel = new Label
                {
                    Name = "LblActionStatus",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Poppins", 14F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.Transparent
                };
                PanelActionStatus.Controls.Add(actionLabel);
            }

            switch (action)
            {
                case "TimeIn":
                    PanelActionStatus.FillColor = Color.FromArgb(16, 185, 129);
                    actionLabel.Text = "✓ TIME IN\nAttendance Recorded";
                    break;

                case "TimeOut":
                    PanelActionStatus.FillColor = Color.FromArgb(239, 68, 68);
                    actionLabel.Text = "→ TIME OUT\nAttendance Recorded";
                    break;

                case "StartBreak":
                    PanelActionStatus.FillColor = Color.FromArgb(249, 115, 22);
                    actionLabel.Text = "☕ START BREAK\nAttendance Recorded";
                    break;

                case "StopBreak":
                    PanelActionStatus.FillColor = Color.FromArgb(139, 92, 246);
                    actionLabel.Text = "↺ STOP BREAK\nAttendance Recorded";
                    break;
            }
        }

        private Bitmap CreateDefaultAvatar()
        {
            Bitmap bmp = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(200, 200, 200));
                g.DrawString("?", new Font("Segoe UI", 40, FontStyle.Bold),
                    Brushes.Gray, new PointF(30, 20));
            }
            return bmp;
        }

        private void UpdateStatus(string text, Color color)
        {
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action(() =>
                {
                    lblStatus.Text = text;
                    lblStatus.ForeColor = color;
                }));
            }
            else
            {
                lblStatus.Text = text;
                lblStatus.ForeColor = color;
            }
        }

        #endregion

        #region Button Click Events

        private void BtnStartScan_Click(object? sender, EventArgs e)
        {
            if (!_isCameraRunning)
            {
                InitializeCameraPanel();
                StartCamera();
                BtnStartScan.Text = "Stop Face Scan";
                BtnStartScan.FillColor = Color.FromArgb(239, 68, 68);
            }
            else
            {
                StopCamera();
                BtnStartScan.Text = "Start Face Scan";
                BtnStartScan.FillColor = Color.FromArgb(99, 102, 241);
            }
        }

        private void ResetAllButtonStyles()
        {
            BtnTimeIn.FillColor = Color.White;
            BtnTimeIn.ForeColor = Color.FromArgb(55, 65, 81);
            BtnTimeIn.BorderColor = Color.FromArgb(209, 213, 219);
            BtnTimeIn.BorderThickness = 1;

            BtnTimeOut.FillColor = Color.White;
            BtnTimeOut.ForeColor = Color.FromArgb(55, 65, 81);
            BtnTimeOut.BorderColor = Color.FromArgb(209, 213, 219);
            BtnTimeOut.BorderThickness = 1;

            BtnStartBreak.FillColor = Color.White;
            BtnStartBreak.ForeColor = Color.FromArgb(55, 65, 81);
            BtnStartBreak.BorderColor = Color.FromArgb(209, 213, 219);
            BtnStartBreak.BorderThickness = 1;

            BtnStopBreak.FillColor = Color.White;
            BtnStopBreak.ForeColor = Color.FromArgb(55, 65, 81);
            BtnStopBreak.BorderColor = Color.FromArgb(209, 213, 219);
            BtnStopBreak.BorderThickness = 1;
        }

        private void BtnTimeIn_Click(object? sender, EventArgs e)
        {
            selectedAction = "TimeIn";
            ResetAllButtonStyles();
            BtnTimeIn.FillColor = Color.FromArgb(16, 185, 129);
            BtnTimeIn.ForeColor = Color.White;
            BtnTimeIn.BorderThickness = 0;

            // Reset attendance flag for new action
            _hasLoggedAttendance = false;
        }

        private void BtnTimeOut_Click(object? sender, EventArgs e)
        {
            selectedAction = "TimeOut";
            ResetAllButtonStyles();
            BtnTimeOut.FillColor = Color.FromArgb(239, 68, 68);
            BtnTimeOut.ForeColor = Color.White;
            BtnTimeOut.BorderThickness = 0;

            _hasLoggedAttendance = false;
        }

        private void BtnStartBreak_Click(object? sender, EventArgs e)
        {
            selectedAction = "StartBreak";
            ResetAllButtonStyles();
            BtnStartBreak.FillColor = Color.FromArgb(249, 115, 22);
            BtnStartBreak.ForeColor = Color.White;
            BtnStartBreak.BorderThickness = 0;

            _hasLoggedAttendance = false;
        }

        private void BtnStopBreak_Click(object? sender, EventArgs e)
        {
            selectedAction = "StopBreak";
            ResetAllButtonStyles();
            BtnStopBreak.FillColor = Color.FromArgb(139, 92, 246);
            BtnStopBreak.ForeColor = Color.White;
            BtnStopBreak.BorderThickness = 0;

            _hasLoggedAttendance = false;
        }

        #endregion

        public void Cleanup()
        {
            StopCamera();
            _faceService?.Dispose();
        }

        private void BtnTimeIn_Click_1(object sender, EventArgs e)
        {

        }
    }
}