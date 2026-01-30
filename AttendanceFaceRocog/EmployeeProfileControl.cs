using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Drawing;
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

        public EmployeeProfileControl()
        {
            InitializeComponent();
            _faceService = new FaceRecognitionService();
            SetupButtonEvents();

            PnlCameraEmpAdd.Visible = false;
        }

        private void SetupButtonEvents()
        {
            BtnStartScan.Click += BtnStartScan_Click;
            BtnAdd.Click += BtnAdd_Click;
            BtnCancel.Click += BtnCancel_Click;
        }

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

                // Flip for mirror effect
                CvInvoke.Flip(_lastFrame, _lastFrame, FlipType.Horizontal);

                // Detect faces and draw rectangles
                var faces = _faceService.DetectFaces(_lastFrame);
                foreach (var face in faces)
                {
                    CvInvoke.Rectangle(_lastFrame, face, new MCvScalar(0, 255, 0), 3);
                }

                Bitmap bmp = _lastFrame.ToBitmap();

                if (picCamera.InvokeRequired)
                {
                    picCamera.Invoke(() =>
                    {
                        picCamera.Image?.Dispose();
                        picCamera.Image = bmp;
                    });
                }
                else
                {
                    picCamera.Image?.Dispose();
                    picCamera.Image = bmp;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessFrame error: {ex.Message}");
            }
        }

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

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            string fullName = TxtEnterName.Text.Trim();

            if (string.IsNullOrEmpty(fullName))
            {
                MessageBox.Show("Please enter employee name.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_isCameraRunning || _lastFrame == null || _lastFrame.IsEmpty)
            {
                MessageBox.Show("Please start the camera and position face in view.",
                    "Camera Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Add employee to database
                int empId = DatabaseHelper.AddEmployee(fullName);

                // Capture and save face
                string? facePath = _faceService.CaptureFace(_lastFrame, empId);

                if (facePath == null)
                {
                    MessageBox.Show("No face detected. Please position your face in the camera.",
                        "Face Not Detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Save face image path to database
                DatabaseHelper.AddFaceImage(empId, facePath);

                // Retrain the model
                _faceService.TrainModel();

                MessageBox.Show($"Employee '{fullName}' added successfully!",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Clear form
                TxtEnterName.Text = "";
                StopCamera();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding employee: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            TxtEnterName.Text = "";
            StopCamera();

            PnlCameraEmpAdd.Hide();

            PnlReadyAdd.Show();
            PanelDetails.Show();
        }

        public void Cleanup()
        {
            StopCamera();
            _lastFrame?.Dispose();
            _faceService?.Dispose();
        }

        private void PnlCameraEmpAdd_Paint(object sender, PaintEventArgs e)
        {

        }

        private void btnAddEmp_Click(object sender, EventArgs e)
        {
            PnlCameraEmpAdd.Show();

            PnlReadyAdd.Hide();
            PanelDetails.Hide();
        }

        private void PnlReadyAdd_Paint(object sender, PaintEventArgs e)
        {

        }

        private void PanelDetails_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
