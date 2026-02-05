using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AttendanceFaceRocog
{
    public partial class UserControl1 : UserControl
    {
        #region Fields

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
        private bool _hasLoggedAttendance = false;

        // Timer to auto-clear profile status
        private System.Windows.Forms.Timer? _autoClearTimer;
        private const int AUTO_CLEAR_DELAY_MS = 5000;

        // Auto-scanning flag
        private bool _isAutoScanEnabled = true;
        private bool _isProcessingAttendance = false;

        // Flag to prevent camera start after cleanup
        private bool _isCleanedUp = false;

        // Time period definitions
        private static readonly TimeSpan EARLY_LOGIN_END = new TimeSpan(9, 0, 0);      // 9:00 AM
        private static readonly TimeSpan MORNING_WORK_END = new TimeSpan(12, 0, 0);    // 12:00 PM
        private static readonly TimeSpan LUNCH_END = new TimeSpan(13, 0, 0);           // 1:00 PM
        private static readonly TimeSpan AFTERNOON_WORK_END = new TimeSpan(18, 0, 0);  // 6:00 PM

        #endregion

        #region Constructor & Initialization

        public UserControl1()
        {
            InitializeComponent();
            CreateStatusLabel();
            InitializeProfilePanel();
            InitializeAutoClearTimer();
            
            // Initialize face service in constructor to avoid null reference
            InitializeFaceService();
            
            this.Resize += UserControl1_Resize;

            // Auto-start camera when control loads
            this.Load += UserControl1_Load;
        }

        /// <summary>
        /// Initialize face recognition service
        /// </summary>
        private void InitializeFaceService()
        {
            try
            {
                _faceService = new FaceRecognitionService();
                _faceService.TrainModel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing face service: {ex.Message}");
                MessageBox.Show("Error initializing face recognition service. Face detection may not work properly.",
                    "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Automatically start camera and face scanning when control loads
        /// </summary>
        private async void UserControl1_Load(object? sender, EventArgs e)
        {
            // Small delay to ensure UI is fully loaded
            await Task.Delay(500);

            // Check if cleanup was called during the delay
            if (_isCleanedUp || this.IsDisposed || !this.Visible)
                return;

            // Auto-start the camera
            AutoStartCamera();
        }

        private void InitializeAutoClearTimer()
        {
            _autoClearTimer = new System.Windows.Forms.Timer();
            _autoClearTimer.Interval = AUTO_CLEAR_DELAY_MS;
            _autoClearTimer.Tick += AutoClearTimer_Tick;
        }

        private void AutoClearTimer_Tick(object? sender, EventArgs e)
        {
            _autoClearTimer?.Stop();
            ClearProfileStatus();

            // Reset for next scan
            _hasLoggedAttendance = false;
            _isProcessingAttendance = false;

            // Restart camera for next employee
            if (_isAutoScanEnabled && !_isCameraRunning)
            {
                AutoStartCamera();
            }
        }

        private void ClearProfileStatus()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(ClearProfileStatus));
                return;
            }

            PanelProfileStatus.Visible = false;
            LblFullName.Text = "";
            LblEmployeeID.Text = "";
            ProfilePic.Image?.Dispose();
            ProfilePic.Image = null;
            TxtTime1.Text = "";
            TxtDate.Text = "";

            PanelActionStatus.FillColor = Color.White;
            foreach (Control ctrl in PanelActionStatus.Controls)
            {
                if (ctrl is Label lbl && lbl.Name == "LblActionStatus")
                {
                    lbl.Text = "";
                    break;
                }
            }

            PanelActivity.Controls.Clear();
            lblStatus.Text = "●  Ready to Scan";
            lblStatus.ForeColor = Color.FromArgb(156, 163, 175);
        }

        private void InitializeProfilePanel()
        {
            PanelProfileStatus.Visible = false;
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

        #endregion

        #region Time-Based Attendance Logic

        /// <summary>
        /// Determines the current time period and returns appropriate action
        /// </summary>
        private AttendancePeriod GetCurrentTimePeriod()
        {
            TimeSpan currentTime = DateTime.Now.TimeOfDay;

            if (currentTime < EARLY_LOGIN_END)
                return AttendancePeriod.EarlyLogin;
            else if (currentTime < MORNING_WORK_END)
                return AttendancePeriod.MorningWork;
            else if (currentTime < LUNCH_END)
                return AttendancePeriod.LunchBreak;
            else if (currentTime < AFTERNOON_WORK_END)
                return AttendancePeriod.AfternoonWork;
            else
                return AttendancePeriod.AfterWork;
        }

        /// <summary>
        /// Main method to handle attendance based on current time
        /// Returns the action to perform, or null if user cancelled
        /// </summary>
        private string? HandleAttendanceByTime()
        {
            AttendancePeriod period = GetCurrentTimePeriod();
            
            // Get current status if employee is recognized
            var status = (hasTimeIn: false, hasTimeOut: false, hasStartBreak: false, hasStopBreak: false);
            if (_recognizedEmpId.HasValue)
            {
                status = DatabaseHelper.GetTodayAttendanceStatus(_recognizedEmpId.Value);
            }

            switch (period)
            {
                case AttendancePeriod.EarlyLogin:
                    // Before 9:00 AM - Ask user what action to perform
                    return ShowEarlyLoginDialog();

                case AttendancePeriod.MorningWork:
                    // 9:00 AM - 11:59 AM - Auto Time In (if not already done)
                    if (status.hasTimeIn)
                    {
                        MessageBox.Show("Time In has already been recorded for today.\n\nPlease select another action manually if needed.",
                            "Already Recorded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return ShowAfternoonDialog(); // Show all options
                    }
                    return "TimeIn";

                case AttendancePeriod.LunchBreak:
                    // 12:00 PM - 1:00 PM - Show break options
                    return ShowLunchBreakDialog();

                case AttendancePeriod.AfternoonWork:
                    // 1:01 PM - 5:59 PM - Manual selection
                    return ShowAfternoonDialog();

                case AttendancePeriod.AfterWork:
                    // After 6:00 PM - Auto Time Out (if not already done)
                    if (status.hasTimeOut)
                    {
                        MessageBox.Show("Time Out has already been recorded for today.",
                            "Already Recorded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return null;
                    }
                    return "TimeOut";

                default:
                    return null;
            }
        }

        /// <summary>
        /// Shows dialog for early login (before 9:00 AM)
        /// </summary>
        private string? ShowEarlyLoginDialog()
        {
            using (var dialog = CreateActionDialog(
                "Early Login Detected",
                "It's before 9:00 AM. What action do you want to perform?",
                new[] { "Time In", "Time Out", "Start Break", "Stop Break", "Cancel" }))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.Tag?.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Shows dialog for lunch break (12:00 PM - 1:00 PM)
        /// </summary>
        private string? ShowLunchBreakDialog()
        {
            using (var dialog = CreateActionDialog(
                "Lunch Break Time",
                "It's lunch time (12:00 PM - 1:00 PM). Select an action:",
                new[] { "Start Break", "Stop Break", "Time In", "Cancel" }))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.Tag?.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Shows dialog for afternoon work period
        /// </summary>
        private string? ShowAfternoonDialog()
        {
            using (var dialog = CreateActionDialog(
                "Afternoon Work Period",
                "Select the action you want to perform:",
                new[] { "Time In", "Time Out", "Start Break", "Stop Break", "Cancel" }))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.Tag?.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a styled action selection dialog with disabled buttons for completed actions
        /// </summary>
        private Form CreateActionDialog(string title, string message, string[] actions)
        {
            var dialog = new Form
            {
                Text = title,
                Size = new Size(400, 320),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            var lblMessage = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(20, 20),
                Size = new Size(360, 50),
                TextAlign = ContentAlignment.MiddleCenter
            };
            dialog.Controls.Add(lblMessage);

            // Get attendance status for the recognized employee
            var status = (hasTimeIn: false, hasTimeOut: false, hasStartBreak: false, hasStopBreak: false);
            if (_recognizedEmpId.HasValue)
            {
                status = DatabaseHelper.GetTodayAttendanceStatus(_recognizedEmpId.Value);
            }

            int yPos = 80;
            foreach (string action in actions)
            {
                var btn = new Button
                {
                    Text = action,
                    Size = new Size(340, 40),
                    Location = new Point(20, yPos),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;

                // Check if action is already completed
                bool isDisabled = action switch
                {
                    "Time In" => status.hasTimeIn,
                    "Time Out" => status.hasTimeOut,
                    "Start Break" => status.hasStartBreak,
                    "Stop Break" => status.hasStopBreak,
                    _ => false
                };

                // Style based on action type
                switch (action)
                {
                    case "Time In":
                        btn.BackColor = isDisabled ? Color.FromArgb(180, 220, 200) : Color.FromArgb(16, 185, 129);
                        btn.ForeColor = Color.White;
                        break;
                    case "Time Out":
                        btn.BackColor = isDisabled ? Color.FromArgb(240, 180, 180) : Color.FromArgb(239, 68, 68);
                        btn.ForeColor = Color.White;
                        break;
                    case "Start Break":
                        btn.BackColor = isDisabled ? Color.FromArgb(250, 200, 170) : Color.FromArgb(249, 115, 22);
                        btn.ForeColor = Color.White;
                        break;
                    case "Stop Break":
                        btn.BackColor = isDisabled ? Color.FromArgb(200, 180, 230) : Color.FromArgb(139, 92, 246);
                        btn.ForeColor = Color.White;
                        break;
                    case "Cancel":
                        btn.BackColor = Color.FromArgb(229, 231, 235);
                        btn.ForeColor = Color.FromArgb(55, 65, 81);
                        break;
                }

                if (isDisabled)
                {
                    btn.Enabled = false;
                    btn.Text = $"{action} ✓ (Done)";
                    btn.Cursor = Cursors.Default;
                }

                btn.Click += (s, e) =>
                {
                    if (action == "Cancel")
                    {
                        dialog.DialogResult = DialogResult.Cancel;
                    }
                    else
                    {
                        dialog.Tag = action.Replace(" ", "");
                        dialog.DialogResult = DialogResult.OK;
                    }
                    dialog.Close();
                };

                dialog.Controls.Add(btn);
                yPos += 45;
            }

            return dialog;
        }

        /// <summary>
        /// Updates button states based on current time period
        /// </summary>

        private void HighlightButton(Guna.UI2.WinForms.Guna2Button btn, Color color)
        {
            btn.FillColor = color;
            btn.ForeColor = Color.White;
            btn.BorderThickness = 0;
        }

        #endregion

        #region Camera & Face Detection

        /// <summary>
        /// Automatically starts the camera for face scanning
        /// </summary>
        private void AutoStartCamera()
        {
            // Don't start if already cleaned up or disposed
            if (_isCleanedUp || this.IsDisposed || !this.Visible)
                return;

            if (_isCameraRunning) return;

            // Ensure face service is initialized
            if (_faceService == null)
            {
                InitializeFaceService();
            }

            InitializeCameraPanel();
            StartCamera();

            UpdateStatus("●  Auto-scanning...", Color.LimeGreen);
        }

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

            // Don't create new face service here - it's already initialized in constructor
            // Just ensure it's trained
            if (_faceService != null)
            {
                try
                {
                    _faceService.TrainModel();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error training model: {ex.Message}");
                }
            }
        }

        private void StartCamera()
        {
            if (_isCameraRunning) return;

            _hasLoggedAttendance = false;
            _isProcessingAttendance = false;
            _autoClearTimer?.Stop();
            PanelProfileStatus.Visible = false;

            bool cameraOpened = false;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    _capture = new VideoCapture(i, VideoCapture.API.DShow);
                    if (_capture.IsOpened)
                    {
                        cameraOpened = true;
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
                MessageBox.Show("No camera could be opened. Please check if your camera is connected.",
                    "Camera Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _capture.ImageGrabbed += ProcessFrame;
            _capture.Start();
            _isCameraRunning = true;
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

            UpdateStatus("●  Ready to Scan", Color.FromArgb(156, 163, 175));
        }

        /// <summary>
        /// Processes each frame from the camera
        /// </summary>
        private void ProcessFrame(object? sender, EventArgs e)
        {
            if (picCamera == null || picCamera.IsDisposed || _capture == null) return;

            Mat frame = new Mat();
            try
            {
                _capture.Retrieve(frame);

                if (frame == null || frame.IsEmpty) return;

                CvInvoke.Flip(frame, frame, FlipType.Horizontal);

                // Process face detection
                ProcessFaceDetection(frame);

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

        /// <summary>
        /// Processes face detection and triggers attendance logging
        /// </summary>
        private void ProcessFaceDetection(Mat frame)
        {
            if (_faceService == null || _isProcessingAttendance)
            {
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

                            // Auto-log attendance when recognition is stable
                            if (!_hasLoggedAttendance && !_isProcessingAttendance)
                            {
                                _isProcessingAttendance = true;
                                AutoLogAttendance();
                            }
                        }
                        else
                        {
                            UpdateStatus("●  Verifying...", Color.FromArgb(249, 115, 22));
                        }

                        // Draw face rectangles
                        var faces = _faceService.DetectFaces(frame);
                        foreach (var face in faces)
                        {
                            var color = isStable ? new MCvScalar(0, 255, 0) : new MCvScalar(0, 165, 255);
                            CvInvoke.Rectangle(frame, face, color, 3);

                            string displayText = isStable ? emp.Value.fullName : "Verifying...";
                            CvInvoke.PutText(frame, displayText,
                                new Point(face.X, face.Y - 10),
                                FontFace.HersheySimplex, 0.7, color, 2);
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
                        UpdateStatus("●  Face Not Recognized", Color.FromArgb(239, 68, 68));

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
                        UpdateStatus("●  Scanning...", Color.FromArgb(156, 163, 175));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Face detection error: {ex.Message}");
            }
        }

        #endregion

        #region Attendance Logging Methods

        /// <summary>
        /// Automatically logs attendance based on time-based rules
        /// </summary>
        private void AutoLogAttendance()
        {
            if (_recognizedEmpId == null || !_isRecognitionStable || _hasLoggedAttendance)
            {
                _isProcessingAttendance = false;
                return;
            }

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(AutoLogAttendance));
                return;
            }

            try
            {
                // Get the action based on time rules
                string? action = HandleAttendanceByTime();

                if (string.IsNullOrEmpty(action))
                {
                    // User cancelled or no action determined
                    _isProcessingAttendance = false;
                    _hasLoggedAttendance = false;
                    return;
                }

                selectedAction = action;
                string currentTime = DateTime.Now.ToString("hh:mm:ss tt");

                // Execute the appropriate logging method
                bool success = false;
                string message = "";

                switch (action)
                {
                    case "TimeIn":
                        (success, message) = LogTimeIn(_recognizedEmpId.Value, currentTime);
                        break;
                    case "TimeOut":
                        (success, message) = LogTimeOut(_recognizedEmpId.Value, currentTime);
                        break;
                    case "StartBreak":
                        (success, message) = LogStartBreak(_recognizedEmpId.Value, currentTime);
                        break;
                    case "StopBreak":
                        (success, message) = LogStopBreak(_recognizedEmpId.Value, currentTime);
                        break;
                }

                _hasLoggedAttendance = true;

                // Stop camera after logging
                StopCamera();

                if (success)
                {
                    var empDetails = DatabaseHelper.GetEmployeeById(_recognizedEmpId.Value);
                    if (empDetails.HasValue)
                    {
                        DisplayAttendanceConfirmation(empDetails.Value.empId, empDetails.Value.fullName, currentTime);
                    }

                    MessageBox.Show($"{FormatActionName(action)} recorded successfully for {_recognizedEmpName}!",
                        "Attendance Recorded", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    StartAutoClearTimer();
                }
                else
                {
                    MessageBox.Show($"{message}\n\nEmployee: {_recognizedEmpName}",
                        "Already Recorded", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    _isProcessingAttendance = false;

                    // Restart camera for next attempt
                    if (_isAutoScanEnabled)
                    {
                        Task.Delay(2000).ContinueWith(_ =>
                        {
                            if (!_isCameraRunning)
                            {
                                this.Invoke(new Action(AutoStartCamera));
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error logging attendance: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _isProcessingAttendance = false;
            }
        }

        /// <summary>
        /// Logs Time In for an employee
        /// </summary>
        private (bool success, string message) LogTimeIn(int empId, string time)
        {
            return DatabaseHelper.LogAttendance(empId, time, "", "", "");
        }

        /// <summary>
        /// Logs Time Out for an employee
        /// </summary>
        private (bool success, string message) LogTimeOut(int empId, string time)
        {
            return DatabaseHelper.LogAttendance(empId, "", time, "", "");
        }

        /// <summary>
        /// Logs Start Break for an employee
        /// </summary>
        private (bool success, string message) LogStartBreak(int empId, string time)
        {
            return DatabaseHelper.LogAttendance(empId, "", "", time, "");
        }

        /// <summary>
        /// Logs Stop Break for an employee
        /// </summary>
        private (bool success, string message) LogStopBreak(int empId, string time)
        {
            return DatabaseHelper.LogAttendance(empId, "", "", "", time);
        }

        private string FormatActionName(string action)
        {
            return action switch
            {
                "TimeIn" => "Time In",
                "TimeOut" => "Time Out",
                "StartBreak" => "Start Break",
                "StopBreak" => "Stop Break",
                _ => action
            };
        }

        private void StartAutoClearTimer()
        {
            if (_autoClearTimer != null)
            {
                _autoClearTimer.Stop();
                _autoClearTimer.Start();
            }
        }

        #endregion

        #region UI Display Methods

        private void DisplayAttendanceConfirmation(int empId, string fullName, string actionTime)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => DisplayAttendanceConfirmation(empId, fullName, actionTime)));
                return;
            }

            try
            {
                string? imagePath = DatabaseHelper.GetEmployeeFaceImage(empId);

                LblFullName.Text = fullName;
                LblEmployeeID.Text = $"EMP-{empId:D3}";

                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    using (var img = Image.FromFile(imagePath))
                    {
                        ProfilePic.Image = new Bitmap(img);
                    }
                }
                else
                {
                    ProfilePic.Image = CreateDefaultAvatar();
                }

                TxtTime1.Text = actionTime;
                TxtDate.Text = DateTime.Now.ToString("MMM dd, yyyy").ToUpper();

                SetActionStatusPanel(selectedAction);
                DisplayEmployeeActivity(empId);

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
            PanelActionStatus.FillColor = Color.White;

            Label? actionLabel = null;

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

        private void DisplayEmployeeActivity(int empId)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => DisplayEmployeeActivity(empId)));
                return;
            }

            try
            {
                PanelActivity.Controls.Clear();

                var headerLabel = new Label
                {
                    Text = "📋 Recent Activity",
                    Font = new Font("Poppins", 12F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(55, 65, 81),
                    Location = new Point(12, 10),
                    AutoSize = true
                };
                PanelActivity.Controls.Add(headerLabel);

                var history = DatabaseHelper.GetEmployeeAttendanceHistory(empId, 7);

                if (history.Rows.Count == 0)
                {
                    var noDataLabel = new Label
                    {
                        Text = "No recent activity found.",
                        Font = new Font("Segoe UI", 10F),
                        ForeColor = Color.Gray,
                        Location = new Point(12, 45),
                        AutoSize = true
                    };
                    PanelActivity.Controls.Add(noDataLabel);
                    return;
                }

                var flowPanel = new FlowLayoutPanel
                {
                    Location = new Point(5, 40),
                    Size = new Size(PanelActivity.Width - 10, PanelActivity.Height - 50),
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = Color.Transparent
                };

                foreach (DataRow row in history.Rows)
                {
                    var activityPanel = CreateActivityItem(
                        row["Date"]?.ToString() ?? "",
                        row["Time In"]?.ToString() ?? "-",
                        row["Time Out"]?.ToString() ?? "-",
                        row["Start Break"]?.ToString() ?? "-",
                        row["Stop Break"]?.ToString() ?? "-"
                    );
                    flowPanel.Controls.Add(activityPanel);
                }

                PanelActivity.Controls.Add(flowPanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying activity: {ex.Message}");
            }
        }

        private Panel CreateActivityItem(string date, string timeIn, string timeOut, string startBreak, string stopBreak)
        {
            var panel = new Panel
            {
                Size = new Size(PanelActivity.Width - 30, 70),
                BackColor = Color.FromArgb(249, 250, 251),
                Margin = new Padding(3),
                Padding = new Padding(8)
            };

            var dateLabel = new Label
            {
                Text = $"📅 {date}",
                Font = new Font("Poppins", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(8, 5),
                AutoSize = true
            };

            var detailsLabel = new Label
            {
                Text = $"🟢 In: {(string.IsNullOrEmpty(timeIn) ? "-" : timeIn)}   " +
                       $"🔴 Out: {(string.IsNullOrEmpty(timeOut) ? "-" : timeOut)}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(107, 114, 128),
                Location = new Point(8, 28),
                AutoSize = true
            };

            var breakLabel = new Label
            {
                Text = $"☕ Break: {(string.IsNullOrEmpty(startBreak) ? "-" : startBreak)} → {(string.IsNullOrEmpty(stopBreak) ? "-" : stopBreak)}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(107, 114, 128),
                Location = new Point(8, 48),
                AutoSize = true
            };

            panel.Controls.Add(dateLabel);
            panel.Controls.Add(detailsLabel);
            panel.Controls.Add(breakLabel);

            return panel;
        }

        #endregion

        #region Layout

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
            int buttonSpacing = 20;
            int totalButtonWidth = (buttonWidth * 2) + buttonSpacing;
            int buttonStartX = (panelWidth - totalButtonWidth) / 2;

            pnlStatusBadge.Size = new Size(180, 32);
            pnlStatusBadge.Left = (panelWidth - 180) / 2;
            pnlStatusBadge.Top = pnlScannerContainer.Bottom + 6;
        }

        #endregion

        #region Button Click Events

        private void BtnStartScan_Click(object? sender, EventArgs e)
        {
            _autoClearTimer?.Stop();

            if (!_isCameraRunning)
            {
                _isAutoScanEnabled = true;
                AutoStartCamera();
            }
            else
            {
                _isAutoScanEnabled = false;
                StopCamera();
            }
        }

        private void BtnTimeIn_Click(object? sender, EventArgs e)
        {
            selectedAction = "TimeIn";
            _hasLoggedAttendance = false;
            _isProcessingAttendance = false;
        }

        private void BtnTimeOut_Click(object? sender, EventArgs e)
        {
            selectedAction = "TimeOut";
            _hasLoggedAttendance = false;
            _isProcessingAttendance = false;
        }

        private void BtnStartBreak_Click(object? sender, EventArgs e)
        {
            selectedAction = "StartBreak";
            _hasLoggedAttendance = false;
            _isProcessingAttendance = false;
        }

        private void BtnStopBreak_Click(object? sender, EventArgs e)
        {
            selectedAction = "StopBreak";
            _hasLoggedAttendance = false;
            _isProcessingAttendance = false;
        }

        #endregion

        #region Cleanup

        public void Cleanup()
        {
            // Set flag first to prevent any new camera starts
            _isCleanedUp = true;
            _isAutoScanEnabled = false;
            
            _autoClearTimer?.Stop();
            _autoClearTimer?.Dispose();
            _autoClearTimer = null;
            
            StopCamera();
            
            if (_faceService != null)
            {
                try
                {
                    _faceService.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing face service: {ex.Message}");
                }
                _faceService = null;
            }
        }

        #endregion

        #region Visibility Handling

        /// <summary>
        /// Stops camera when control becomes invisible (navigating to another control)
        /// </summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // Don't do anything if already cleaned up
            if (_isCleanedUp) return;

            if (!this.Visible)
            {
                // Stop camera when control is hidden
                _isAutoScanEnabled = false;
                _autoClearTimer?.Stop();
                StopCamera();
            }
            else if (this.Visible && !_isCameraRunning)
            {
                // Restart camera when control becomes visible again
                _isAutoScanEnabled = true;
                AutoStartCamera();
            }
        }

        #endregion
    }

    #region Enums

    /// <summary>
    /// Defines the attendance time periods
    /// </summary>
    public enum AttendancePeriod
    {
        EarlyLogin,      // Before 9:00 AM
        MorningWork,     // 9:00 AM - 11:59 AM
        LunchBreak,      // 12:00 PM - 1:00 PM
        AfternoonWork,   // 1:01 PM - 5:59 PM
        AfterWork        // 6:00 PM onwards
    }
}

    #endregion