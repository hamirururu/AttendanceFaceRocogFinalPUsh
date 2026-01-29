using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace AttendanceFaceRocog
{
    public partial class Form1 : Form
    {
        private System.Windows.Forms.Timer clockTimer = null!;  // Initialized in InitializeClock()
        private string selectedAction = "TimeIn";
        public string SelectedAction => selectedAction;
        private Label lblStatus = null!;  // Initialized in CreateStatusLabel()

        // Camera & Face Detection
        private VideoCapture? _capture;           // Can be null when camera not running
        private CascadeClassifier? _faceDetector; // Can be null if detection disabled
        private PictureBox? picCamera;            // Can be null before camera init
        private bool _isCameraRunning = false;

        public Form1()
        {
            InitializeComponent();
            ApplyTheme();
            InitializeClock();
            SetupButtonEvents();
            CreateStatusLabel();

            this.Resize += Form1_Resize;
            guna2Panel2.Resize += GunA2Panel2_Resize;
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

            // Load Haar Cascade (put xml in your project folder)
            try
            {
                string cascadePath = "haarcascade_frontalface_default.xml";

                // Check if file exists
                if (!File.Exists(cascadePath))
                {
                    // Try alternate locations
                    string[] possiblePaths = {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml"),
                        Path.Combine(Environment.CurrentDirectory, "haarcascade_frontalface_default.xml"),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty, "haarcascade_frontalface_default.xml")
                    };

                    foreach (string path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            cascadePath = path;
                            break;
                        }
                    }
                }

                if (File.Exists(cascadePath))
                {
                    _faceDetector = new CascadeClassifier(cascadePath);
                    System.Diagnostics.Debug.WriteLine("Face detector loaded successfully");
                }
                else
                {
                    MessageBox.Show(
                        "Face detection file 'haarcascade_frontalface_default.xml' not found.\n\n" +
                        "Camera will work but face detection will be disabled.\n\n" +
                        "Download from: https://github.com/opencv/opencv/blob/master/data/haarcascades/haarcascade_frontalface_default.xml\n" +
                        "And place it in your application folder.",
                        "Face Detection Disabled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _faceDetector = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading face detector: {ex.Message}\n\nCamera will work but face detection will be disabled.");
                _faceDetector = null;
            }
        }

        private void StartCamera()
        {
            if (_isCameraRunning) return;

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
                        System.Diagnostics.Debug.WriteLine($"Camera opened successfully at index {i}");
                        break;
                    }
                    else
                    {
                        _capture?.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open camera at index {i}: {ex.Message}");
                    _capture?.Dispose();
                }
            }

            if (!cameraOpened || _capture == null)  // Added null check
            {
                MessageBox.Show("No camera could be opened. Please check if your camera is connected and not being used by another application.");
                return;
            }

            _capture.ImageGrabbed += ProcessFrame;
            _capture.Start();

            _isCameraRunning = true;
            lblStatus.Text = "●  Scanning...";
            lblStatus.ForeColor = Color.LimeGreen;

            System.Diagnostics.Debug.WriteLine($"Camera started successfully on index {successfulCameraIndex}");
        }

        private void StopCamera()
        {
            if (!_isCameraRunning) return;

            if (_capture != null)  // Added null check
            {
                _capture.ImageGrabbed -= ProcessFrame;
                _capture.Stop();
                _capture.Dispose();
                _capture = null;
            }

            _isCameraRunning = false;
            
            if (picCamera != null)  // Added null check
            {
                picCamera.Image = null;
            }

            lblStatus.Text = "●  Ready to Scan";
            lblStatus.ForeColor = Color.FromArgb(156, 163, 175);
        }

        private void ProcessFrame(object? sender, EventArgs e)
        {
            if (picCamera == null || picCamera.IsDisposed)
            {
                System.Diagnostics.Debug.WriteLine("PictureBox is null or disposed");
                return;
            }

            if (_capture == null) return;  // Added null check

            Mat frame = new Mat();
            try
            {
                _capture.Retrieve(frame);

                if (frame == null || frame.IsEmpty)
                {
                    System.Diagnostics.Debug.WriteLine("Frame is null or empty");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Frame captured: {frame.Width}x{frame.Height}");

                // Detect faces in the frame
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
            if (_faceDetector == null)
            {
                // Face detection disabled, just update status
                UpdateStatus("●  Camera Active (Face Detection Disabled)", Color.FromArgb(59, 130, 246));
                return;
            }

            try
            {
                // Convert to grayscale for better detection
                Mat grayFrame = new Mat();
                CvInvoke.CvtColor(frame, grayFrame, ColorConversion.Bgr2Gray);
                CvInvoke.EqualizeHist(grayFrame, grayFrame);

                // Detect faces
                Rectangle[] faces = _faceDetector.DetectMultiScale(
                    grayFrame,
                    scaleFactor: 1.1,
                    minNeighbors: 3,
                    minSize: new Size(30, 30)
                );

                // Update status based on face detection
                if (faces.Length > 0)
                {
                    UpdateStatus($"●  {faces.Length} Face(s) Detected", Color.LimeGreen);
                }
                else
                {
                    UpdateStatus("●  No Face Detected", Color.FromArgb(249, 115, 22));
                }

                // Draw rectangles around detected faces
                foreach (var face in faces)
                {
                    // Draw main rectangle (green)
                    CvInvoke.Rectangle(
                        frame,
                        face,
                        new MCvScalar(0, 255, 0), // Green color (BGR format)
                        3 // Thickness
                    );

                    // Optional: Draw a label above the face
                    string label = "Face";
                    FontFace fontFace = FontFace.HersheySimplex;
                    double fontScale = 0.8;
                    int thickness = 2;
                    int baseline = 0;

                    Size textSize = CvInvoke.GetTextSize(label, fontFace, fontScale, thickness, ref baseline);
                    Point textOrg = new Point(face.X, face.Y - 10);

                    // Draw text background
                    CvInvoke.Rectangle(
                        frame,
                        new Rectangle(textOrg.X, textOrg.Y - textSize.Height - 5, textSize.Width, textSize.Height + 5),
                        new MCvScalar(0, 255, 0),
                        -1 // Fill
                    );

                    // Draw text
                    CvInvoke.PutText(
                        frame,
                        label,
                        textOrg,
                        FontFace.HersheySimplex,
                        fontScale,
                        new MCvScalar(0, 0, 0), // Black text
                        thickness
                    );
                }

                grayFrame.Dispose();
            }
            catch (Exception ex)
            {
                // Handle any detection errors silently to avoid interrupting the video stream
                System.Diagnostics.Debug.WriteLine($"Face detection error: {ex.Message}");
            }
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

        private void btnStartScan_Click(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"btnStartScan_Click called. Camera running: {_isCameraRunning}");

            if (!_isCameraRunning)
            {
                InitializeCameraPanel();
                StartCamera();
                btnStartScan.Text = "Stop Face Scan";
                btnStartScan.FillColor = Color.FromArgb(239, 68, 68); // Red when active
            }
            else
            {
                StopCamera();
                btnStartScan.Text = "Start Face Scan";
                btnStartScan.FillColor = Color.FromArgb(59, 130, 246); // Blue when inactive
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCamera();
            _faceDetector?.Dispose();
            clockTimer?.Dispose();
            base.OnFormClosing(e);
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

        private void ApplyTheme()
        {
            this.BackColor = AppTheme.Light.Background;
            guna2Panel1.FillColor = AppTheme.Light.Card;
            guna2HtmlLabel1.ForeColor = AppTheme.Light.Foreground;
            guna2HtmlLabel1.Font = AppTheme.HeadingSmall;
            guna2HtmlLabel2.ForeColor = AppTheme.Light.Foreground;
            guna2Panel2.FillColor = AppTheme.Light.Card;
            guna2Panel3.FillColor = AppTheme.Light.Card;
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

        private void SetupButtonEvents()
        {
            btnTimeIn.Click += BtnTimeIn_Click;
            btnTimeOut.Click += BtnTimeOut_Click;
            btnStartBreak.Click += BtnStartBreak_Click;
            btnStopBreak.Click += BtnStopBreak_Click;

            // Remove any existing handlers first to avoid duplicates
            btnStartScan.Click -= btnStartScan_Click;
            btnStartScan.Click += btnStartScan_Click;
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            CenterControls();
        }

        private void GunA2Panel2_Resize(object? sender, EventArgs e)
        {
            CenterControls();
        }

        private void CenterControls()
        {
            int panelWidth = guna2Panel2.ClientSize.Width;
            int panelHeight = guna2Panel2.ClientSize.Height;

            // Scanner dimensions - match reference proportions
            int scannerWidth = 415;
            int scannerHeight = 293;
            
            // Scale scanner if panel is large enough
            if (panelWidth > 700)
            {
                scannerWidth = Math.Min(550, (int)(panelWidth * 0.68));
                scannerHeight = (int)(scannerWidth * 0.707);
            }

            // Ensure scanner fits in available space (leave ~250px for controls below)
            int maxScannerHeight = panelHeight - 280;
            if (scannerHeight > maxScannerHeight && maxScannerHeight > 200)
            {
                scannerHeight = maxScannerHeight;
                scannerWidth = (int)(scannerHeight / 0.707);
            }

            // Position scanner at top, centered
            pnlScannerContainer.Size = new Size(scannerWidth, scannerHeight);
            pnlScannerContainer.Left = (panelWidth - scannerWidth) / 2;
            pnlScannerContainer.Top = 20;

            // Button dimensions - fixed size like reference
            int buttonWidth = 150;
            int buttonHeight = 40;
            int buttonSpacing = 20;
            int totalButtonWidth = (buttonWidth * 2) + buttonSpacing;
            int buttonStartX = (panelWidth - totalButtonWidth) / 2;

            // Position status badge - centered below scanner
            pnlStatusBadge.Size = new Size(140, 32);
            pnlStatusBadge.Left = (panelWidth - 140) / 2;
            pnlStatusBadge.Top = pnlScannerContainer.Bottom + 6;

            // Position "Select Action:" label
            lblSelectAction.Left = (panelWidth - lblSelectAction.Width) / 2;
            lblSelectAction.Top = pnlStatusBadge.Bottom + 11;

            // Resize buttons to match reference
            btnTimeIn.Size = new Size(buttonWidth, buttonHeight);
            btnTimeOut.Size = new Size(buttonWidth, buttonHeight);
            btnStartBreak.Size = new Size(buttonWidth, buttonHeight);
            btnStopBreak.Size = new Size(buttonWidth, buttonHeight);

            // Position TIME IN and TIME OUT buttons (Row 1)
            int row1Y = lblSelectAction.Bottom + 11;
            btnTimeIn.Left = buttonStartX;
            btnTimeIn.Top = row1Y;
            btnTimeOut.Left = btnTimeIn.Right + buttonSpacing;
            btnTimeOut.Top = row1Y;

            // Position START BREAK and STOP BREAK buttons (Row 2)
            int row2Y = btnTimeIn.Bottom + 10;
            btnStartBreak.Left = buttonStartX;
            btnStartBreak.Top = row2Y;
            btnStopBreak.Left = btnStartBreak.Right + buttonSpacing;
            btnStopBreak.Top = row2Y;

            // Position Start Face Scan button (Row 3)
            btnStartScan.Size = new Size(totalButtonWidth, buttonHeight);
            btnStartScan.Left = buttonStartX;
            btnStartScan.Top = btnStopBreak.Bottom + 10;
        }

        private void ResetAllButtonStyles()
        {
            // Reset TIME IN
            btnTimeIn.FillColor = Color.White;
            btnTimeIn.ForeColor = Color.FromArgb(55, 65, 81);
            btnTimeIn.BorderColor = Color.FromArgb(209, 213, 219);
            btnTimeIn.BorderThickness = 1;

            // Reset TIME OUT
            btnTimeOut.FillColor = Color.White;
            btnTimeOut.ForeColor = Color.FromArgb(55, 65, 81);
            btnTimeOut.BorderColor = Color.FromArgb(209, 213, 219);
            btnTimeOut.BorderThickness = 1;

            // Reset START BREAK
            btnStartBreak.FillColor = Color.White;
            btnStartBreak.ForeColor = Color.FromArgb(55, 65, 81);
            btnStartBreak.BorderColor = Color.FromArgb(209, 213, 219);
            btnStartBreak.BorderThickness = 1;

            // Reset STOP BREAK
            btnStopBreak.FillColor = Color.White;
            btnStopBreak.ForeColor = Color.FromArgb(55, 65, 81);
            btnStopBreak.BorderColor = Color.FromArgb(209, 213, 219);
            btnStopBreak.BorderThickness = 1;
        }

        private void BtnTimeIn_Click(object? sender, EventArgs e)
        {
            selectedAction = "TimeIn";
            ResetAllButtonStyles();
            btnTimeIn.FillColor = Color.FromArgb(16, 185, 129);
            btnTimeIn.ForeColor = Color.White;
            btnTimeIn.BorderThickness = 0;
        }

        private void BtnTimeOut_Click(object? sender, EventArgs e)
        {
            selectedAction = "TimeOut";
            ResetAllButtonStyles();
            btnTimeOut.FillColor = Color.FromArgb(239, 68, 68);
            btnTimeOut.ForeColor = Color.White;
            btnTimeOut.BorderThickness = 0;
        }

        private void BtnStartBreak_Click(object? sender, EventArgs e)
        {
            selectedAction = "StartBreak";
            ResetAllButtonStyles();
            btnStartBreak.FillColor = Color.FromArgb(249, 115, 22);
            btnStartBreak.ForeColor = Color.White;
            btnStartBreak.BorderThickness = 0;
        }

        private void BtnStopBreak_Click(object? sender, EventArgs e)
        {
            selectedAction = "StopBreak";
            ResetAllButtonStyles();
            btnStopBreak.FillColor = Color.FromArgb(139, 92, 246);
            btnStopBreak.ForeColor = Color.White;
            btnStopBreak.BorderThickness = 0;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterControls();
            BtnTimeIn_Click(null, EventArgs.Empty);
        }

        private void SetActiveButton(Guna.UI2.WinForms.Guna2Button activeBtn)
        {
            var buttons = new[]
            {
                btnScanner,
                btnRecentAct,
                btnProfiles
            };

            foreach (var btn in buttons)
            {
                btn.FillColor = Color.Transparent;
                btn.ForeColor = Color.Gray;
                btn.Font = new Font("Segoe UI", 10, FontStyle.Regular);

                // INACTIVE ICONS (GRAY)
                if (btn == btnScanner)
                    btn.Image = Properties.Resources.cameraa_gray;
                else if (btn == btnRecentAct)
                    btn.Image = Properties.Resources.presony_gray;
                else
                    btn.Image = Properties.Resources.acti_gray;
            }

            // ACTIVE STYLE
            activeBtn.FillColor = Color.FromArgb(108, 99, 255);
            activeBtn.ForeColor = Color.White;
            activeBtn.Font = new Font("Segoe UI", 10, FontStyle.Bold);

            // ACTIVE ICONS (WHITE)
            if (activeBtn == btnScanner)
                activeBtn.Image = Properties.Resources.cameraa_white;
            else if (activeBtn == btnRecentAct)
                activeBtn.Image = Properties.Resources.persony_white;
            else
                activeBtn.Image = Properties.Resources.acti_white;
        }

        private void btnScanner_Click(object? sender, EventArgs e)
        {
            SetActiveButton(btnScanner);
        }

        private void btnRecentAct_Click(object? sender, EventArgs e)
        {
            SetActiveButton(btnRecentAct);
        }

        private void btnProfiles_Click(object? sender, EventArgs e)
        {
            SetActiveButton(btnProfiles);
        }

        private void pnlScannerContainer_Paint(object? sender, PaintEventArgs e)
        {

        }

        private void pnlStatusBadge_Paint(object? sender, PaintEventArgs e)
        {

        }
    }
}