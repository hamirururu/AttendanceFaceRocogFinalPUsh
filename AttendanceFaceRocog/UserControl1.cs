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
        private CascadeClassifier? _faceDetector;
        private PictureBox? picCamera;
        private bool _isCameraRunning = false;

        public UserControl1()
        {
            InitializeComponent();
            CreateStatusLabel();
            SetupButtonEvents();
            this.Resize += UserControl1_Resize;
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
            btnTimeIn.Click += BtnTimeIn_Click;
            btnTimeOut.Click += BtnTimeOut_Click;
            btnStartBreak.Click += BtnStartBreak_Click;
            btnStopBreak.Click += BtnStopBreak_Click;
            btnStartScan.Click += BtnStartScan_Click;

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

            btnTimeIn.Size = new Size(buttonWidth, buttonHeight);
            btnTimeOut.Size = new Size(buttonWidth, buttonHeight);
            btnStartBreak.Size = new Size(buttonWidth, buttonHeight);
            btnStopBreak.Size = new Size(buttonWidth, buttonHeight);

            int row1Y = lblSelectAction.Bottom + 11;
            btnTimeIn.Left = buttonStartX;
            btnTimeIn.Top = row1Y;
            btnTimeOut.Left = btnTimeIn.Right + buttonSpacing;
            btnTimeOut.Top = row1Y;

            int row2Y = btnTimeIn.Bottom + 10;
            btnStartBreak.Left = buttonStartX;
            btnStartBreak.Top = row2Y;
            btnStopBreak.Left = btnStartBreak.Right + buttonSpacing;
            btnStopBreak.Top = row2Y;

            btnStartScan.Size = new Size(totalButtonWidth, buttonHeight);
            btnStartScan.Left = buttonStartX;
            btnStartScan.Top = btnStopBreak.Bottom + 10;
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

            try
            {
                string cascadePath = "haarcascade_frontalface_default.xml";

                if (!File.Exists(cascadePath))
                {
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
                        "Camera will work but face detection will be disabled.",
                        "Face Detection Disabled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _faceDetector = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading face detector: {ex.Message}");
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
                UpdateStatus("●  Camera Active (No Detection)", Color.FromArgb(59, 130, 246));
                return;
            }

            try
            {
                Mat grayFrame = new Mat();
                CvInvoke.CvtColor(frame, grayFrame, ColorConversion.Bgr2Gray);
                CvInvoke.EqualizeHist(grayFrame, grayFrame);

                Rectangle[] faces = _faceDetector.DetectMultiScale(
                    grayFrame,
                    scaleFactor: 1.1,
                    minNeighbors: 3,
                    minSize: new Size(30, 30)
                );

                if (faces.Length > 0)
                {
                    UpdateStatus($"●  {faces.Length} Face(s) Detected", Color.LimeGreen);
                }
                else
                {
                    UpdateStatus("●  No Face Detected", Color.FromArgb(249, 115, 22));
                }

                foreach (var face in faces)
                {
                    CvInvoke.Rectangle(frame, face, new MCvScalar(0, 255, 0), 3);

                    string label = "Face";
                    Point textOrg = new Point(face.X, face.Y - 10);
                    CvInvoke.PutText(frame, label, textOrg, FontFace.HersheySimplex, 0.8, new MCvScalar(0, 255, 0), 2);
                }

                grayFrame.Dispose();
            }
            catch (Exception ex)
            {
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

        #endregion

        #region Button Click Events

        private void BtnStartScan_Click(object? sender, EventArgs e)
        {
            if (!_isCameraRunning)
            {
                InitializeCameraPanel();
                StartCamera();
                btnStartScan.Text = "Stop Face Scan";
                btnStartScan.FillColor = Color.FromArgb(239, 68, 68);
            }
            else
            {
                StopCamera();
                btnStartScan.Text = "Start Face Scan";
                btnStartScan.FillColor = Color.FromArgb(99, 102, 241);
            }
        }

        private void ResetAllButtonStyles()
        {
            btnTimeIn.FillColor = Color.White;
            btnTimeIn.ForeColor = Color.FromArgb(55, 65, 81);
            btnTimeIn.BorderColor = Color.FromArgb(209, 213, 219);
            btnTimeIn.BorderThickness = 1;

            btnTimeOut.FillColor = Color.White;
            btnTimeOut.ForeColor = Color.FromArgb(55, 65, 81);
            btnTimeOut.BorderColor = Color.FromArgb(209, 213, 219);
            btnTimeOut.BorderThickness = 1;

            btnStartBreak.FillColor = Color.White;
            btnStartBreak.ForeColor = Color.FromArgb(55, 65, 81);
            btnStartBreak.BorderColor = Color.FromArgb(209, 213, 219);
            btnStartBreak.BorderThickness = 1;

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

        #endregion

        /// <summary>
        /// Clean up resources when the control is being disposed
        /// </summary>
        public void Cleanup()
        {
            StopCamera();
            _faceDetector?.Dispose();
        }
    }
}
