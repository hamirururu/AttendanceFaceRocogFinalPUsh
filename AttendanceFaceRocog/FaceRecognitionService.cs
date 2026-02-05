using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Face;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;

namespace AttendanceFaceRocog
{
    public class FaceRecognitionService : IDisposable
    {
        private readonly CascadeClassifier _faceDetector;
        private LBPHFaceRecognizer? _recognizer;
        private readonly Dictionary<int, int> _labelToEmpId = new();
        private bool _isModelTrained = false;

        private readonly string _facesFolder;

        // Face processing constants
        private const int FACE_SIZE = 100;
        private const double UNKNOWN_THRESHOLD = 95;

        // Recognition stability tracking
        private const int RECOGNITION_HISTORY_SIZE = 5;
        private Queue<int> _recognitionHistory = new Queue<int>();

        // PROXIMITY DETECTION: Minimum face size to be considered "near" the camera
        // Face must be at least this many pixels wide/tall to be recognized
        private const int MIN_FACE_SIZE_FOR_RECOGNITION = 150;  // Adjust based on camera distance
        private const int MIN_FACE_SIZE_FOR_DETECTION = 80;     // Minimum to even detect
        private const int MAX_FACE_SIZE = 400;                   // Maximum face size

        // Percentage of frame the face should occupy to be considered "close enough"
        private const double MIN_FACE_AREA_RATIO = 0.03;  // Face must be at least 3% of frame area

        public FaceRecognitionService()
        {
            _facesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Faces");
            Directory.CreateDirectory(_facesFolder);

            string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");

            if (!File.Exists(cascadePath))
            {
                throw new FileNotFoundException(
                    $"Haar Cascade file not found at: {cascadePath}\n\n" +
                    "Please download it from:\n" +
                    "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml\n" +
                    "and place it in your application folder.");
            }

            try
            {
                _faceDetector = new CascadeClassifier(cascadePath);

                if (_faceDetector == null)
                {
                    throw new Exception("Failed to initialize face detector - CascadeClassifier is null");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load face detector: {ex.Message}", ex);
            }

            try
            {
                _recognizer = new LBPHFaceRecognizer(2, 8, 8, 8, 100);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to initialize LBPH recognizer: {ex.Message}\n\n" +
                    "This may indicate missing EmguCV runtime libraries.", ex);
            }
        }

        private Image<Gray, byte>? NormalizeFace(Image<Gray, byte>? face)
        {
            if (face == null) return null;

            var resized = face.Resize(FACE_SIZE, FACE_SIZE, Inter.Cubic);
            resized._EqualizeHist();
            CvInvoke.GaussianBlur(resized, resized, new Size(3, 3), 0);

            return resized;
        }

        /// <summary>
        /// Check if a face is close enough to the camera based on its size
        /// </summary>
        private bool IsFaceCloseEnough(Rectangle face, int frameWidth, int frameHeight)
        {
            // Check minimum face size
            if (face.Width < MIN_FACE_SIZE_FOR_RECOGNITION || face.Height < MIN_FACE_SIZE_FOR_RECOGNITION)
            {
                return false;
            }

            // Check face area ratio relative to frame
            double faceArea = face.Width * face.Height;
            double frameArea = frameWidth * frameHeight;
            double areaRatio = faceArea / frameArea;

            return areaRatio >= MIN_FACE_AREA_RATIO;
        }

        /// <summary>
        /// Get the largest (closest) face from detected faces
        /// </summary>
        private Rectangle? GetClosestFace(Rectangle[] faces, int frameWidth, int frameHeight)
        {
            if (faces.Length == 0) return null;

            // Sort by area (largest first = closest to camera)
            var sortedFaces = faces.OrderByDescending(f => f.Width * f.Height).ToArray();

            // Return the largest face that meets the proximity requirement
            foreach (var face in sortedFaces)
            {
                if (IsFaceCloseEnough(face, frameWidth, frameHeight))
                {
                    return face;
                }
            }

            return null;
        }

        /// <summary>
        /// Capture and save face for a new employee (only if close enough)
        /// </summary>
        public string? CaptureFace(Mat frame, int empId)
        {
            Rectangle[] faces = DetectFaces(frame);

            if (faces.Length == 0) return null;

            // Get the closest face that meets proximity requirements
            Rectangle? closestFace = GetClosestFace(faces, frame.Width, frame.Height);

            if (!closestFace.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("Face detected but not close enough to camera for capture.");
                return null;
            }

            Rectangle face = closestFace.Value;

            Mat faceRegion = new Mat(frame, face);
            Mat grayFace = new Mat();
            CvInvoke.CvtColor(faceRegion, grayFace, ColorConversion.Bgr2Gray);

            Image<Gray, byte> faceImage = grayFace.ToImage<Gray, byte>();
            Image<Gray, byte>? normalized = NormalizeFace(faceImage);

            if (normalized == null)
            {
                faceRegion.Dispose();
                grayFace.Dispose();
                faceImage.Dispose();
                return null;
            }

            string fileName = $"emp_{empId}_{DateTime.Now:yyyyMMddHHmmss}.jpg";
            string filePath = Path.Combine(_facesFolder, fileName);
            normalized.Save(filePath);

            faceRegion.Dispose();
            grayFace.Dispose();
            faceImage.Dispose();
            normalized.Dispose();

            return filePath;
        }

        /// <summary>
        /// Detect all faces in a frame
        /// </summary>
        public Rectangle[] DetectFaces(Mat frame)
        {
            Mat grayFrame = new Mat();
            CvInvoke.CvtColor(frame, grayFrame, ColorConversion.Bgr2Gray);
            CvInvoke.EqualizeHist(grayFrame, grayFrame);

            var faces = _faceDetector.DetectMultiScale(
                grayFrame,
                scaleFactor: 1.08,
                minNeighbors: 4,
                minSize: new Size(MIN_FACE_SIZE_FOR_DETECTION, MIN_FACE_SIZE_FOR_DETECTION),
                maxSize: new Size(MAX_FACE_SIZE, MAX_FACE_SIZE)
            );

            grayFrame.Dispose();
            return faces;
        }

        /// <summary>
        /// Detect only faces that are close to the camera
        /// </summary>
        public Rectangle[] DetectCloseFaces(Mat frame)
        {
            Rectangle[] allFaces = DetectFaces(frame);

            // Filter to only faces that are close enough
            var closeFaces = allFaces
                .Where(f => IsFaceCloseEnough(f, frame.Width, frame.Height))
                .OrderByDescending(f => f.Width * f.Height) // Largest (closest) first
                .ToArray();

            return closeFaces;
        }

        /// <summary>
        /// Train the face recognizer with all saved faces
        /// </summary>
        public void TrainModel()
        {
            var employeeFaces = DatabaseHelper.GetAllEmployeesWithFaces();

            if (employeeFaces.Rows.Count == 0)
            {
                _isModelTrained = false;
                return;
            }

            List<Mat> faceImages = new();
            List<int> labels = new();
            _labelToEmpId.Clear();

            var employeeGroups = employeeFaces.AsEnumerable()
                .GroupBy(row => (int)row["empID"]);

            int labelIndex = 0;

            foreach (var empGroup in employeeGroups)
            {
                int empId = empGroup.Key;

                foreach (DataRow row in empGroup)
                {
                    string imgPath = row["imgPath"].ToString()!;

                    if (!File.Exists(imgPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Image file not found: {imgPath}");
                        continue;
                    }

                    try
                    {
                        Image<Gray, byte> faceImg = new Image<Gray, byte>(imgPath);

                        if (faceImg.Width == 0 || faceImg.Height == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Invalid image dimensions: {imgPath}");
                            faceImg.Dispose();
                            continue;
                        }

                        Image<Gray, byte>? normalized = NormalizeFace(faceImg);

                        if (normalized != null && normalized.Width == FACE_SIZE && normalized.Height == FACE_SIZE)
                        {
                            if (!normalized.Mat.IsEmpty)
                            {
                                faceImages.Add(normalized.Mat.Clone());
                                labels.Add(labelIndex);

                                var flipped = normalized.Flip(FlipType.Horizontal);
                                if (!flipped.Mat.IsEmpty)
                                {
                                    faceImages.Add(flipped.Mat.Clone());
                                    labels.Add(labelIndex);
                                }
                                flipped.Dispose();
                            }
                            normalized.Dispose();
                        }

                        faceImg.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading face {imgPath}: {ex.Message}");
                    }
                }

                _labelToEmpId[labelIndex] = empId;
                labelIndex++;
            }

            if (faceImages.Count < 2)
            {
                System.Diagnostics.Debug.WriteLine("Not enough face images to train. Need at least 2.");
                _isModelTrained = false;

                foreach (var img in faceImages)
                    img.Dispose();

                return;
            }

            try
            {
                using var faceVector = new Emgu.CV.Util.VectorOfMat(faceImages.ToArray());
                using var labelVector = new Emgu.CV.Util.VectorOfInt(labels.ToArray());

                _recognizer?.Train(faceVector, labelVector);
                _isModelTrained = true;

                System.Diagnostics.Debug.WriteLine($"Model trained with {faceImages.Count} images for {_labelToEmpId.Count} employees");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Training failed: {ex.Message}");
                _isModelTrained = false;
            }
            finally
            {
                foreach (var img in faceImages)
                    img.Dispose();
            }
        }

        /// <summary>
        /// Recognize a face in a frame - only recognizes faces close to the camera
        /// Returns: (empId, confidence, isStable)
        /// </summary>
        public (int empId, double confidence, bool isStable)? RecognizeFace(Mat frame)
        {
            if (!_isModelTrained || _recognizer == null)
                return null;

            // Use DetectCloseFaces to only get faces near the camera
            Rectangle[] closeFaces = DetectCloseFaces(frame);

            if (closeFaces.Length == 0)
                return null;

            // Use the largest (closest) face
            Rectangle face = closeFaces[0];

            try
            {
                Mat faceRegion = new Mat(frame, face);
                Mat grayFace = new Mat();
                CvInvoke.CvtColor(faceRegion, grayFace, ColorConversion.Bgr2Gray);

                Image<Gray, byte> faceImage = grayFace.ToImage<Gray, byte>();
                Image<Gray, byte>? normalized = NormalizeFace(faceImage);

                if (normalized == null)
                {
                    faceRegion.Dispose();
                    grayFace.Dispose();
                    faceImage.Dispose();
                    return null;
                }

                var result = _recognizer.Predict(normalized.Mat);

                faceRegion.Dispose();
                grayFace.Dispose();
                faceImage.Dispose();
                normalized.Dispose();

                if (result.Label >= 0 && result.Distance < UNKNOWN_THRESHOLD)
                {
                    if (_labelToEmpId.TryGetValue(result.Label, out int empId))
                    {
                        // Update recognition history
                        _recognitionHistory.Enqueue(empId);
                        if (_recognitionHistory.Count > RECOGNITION_HISTORY_SIZE)
                            _recognitionHistory.Dequeue();

                        // Check if recognition is stable
                        bool isStable = _recognitionHistory.Count >= RECOGNITION_HISTORY_SIZE &&
                                       _recognitionHistory.All(id => id == empId);

                        double confidence = 100 - result.Distance;
                        return (empId, confidence, isStable);
                    }
                }

                // Face detected but not recognized - clear history
                _recognitionHistory.Clear();
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Recognition error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clear recognition history (call when switching users or resetting)
        /// </summary>
        public void ClearRecognitionHistory()
        {
            _recognitionHistory.Clear();
        }

        public void Dispose()
        {
            _faceDetector?.Dispose();
            _recognizer?.Dispose();
        }
    }
}