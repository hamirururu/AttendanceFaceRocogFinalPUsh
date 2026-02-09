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
        // SINGLETON PATTERN - Shared instance across all controls
        private static FaceRecognitionService? _instance;
        private static readonly object _lock = new object();
        
        public static FaceRecognitionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new FaceRecognitionService();
                    }
                }
                return _instance;
            }
        }

        // Event to notify when model is retrained
        public event EventHandler? ModelRetrained;

        private readonly CascadeClassifier _faceDetector;
        private LBPHFaceRecognizer? _recognizer;
        private readonly Dictionary<int, int> _labelToEmpId = new();
        private bool _isModelTrained = false;

        private readonly string _facesFolder;

        // Face processing constants - OPTIMIZED
        private const int FACE_SIZE = 100;
        private const double UNKNOWN_THRESHOLD = 80;  // Lowered from 95 for stricter matching

        // Recognition stability tracking - REDUCED for faster recognition
        private const int RECOGNITION_HISTORY_SIZE = 3;  // Reduced from 5
        private Queue<int> _recognitionHistory = new Queue<int>();

        // PROXIMITY DETECTION
        private const int MIN_FACE_SIZE_FOR_RECOGNITION = 120;  // Reduced from 150
        private const int MIN_FACE_SIZE_FOR_DETECTION = 60;     // Reduced from 80
        private const int MAX_FACE_SIZE = 500;                   // Increased from 400
        private const double MIN_FACE_AREA_RATIO = 0.02;         // Reduced from 0.03

        // Private constructor for singleton
        private FaceRecognitionService()
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
                // OPTIMIZED LBPH parameters for better recognition
                // radius=1, neighbors=8, gridX=8, gridY=8, threshold=100
                _recognizer = new LBPHFaceRecognizer(1, 8, 8, 8, 100);
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
            
            // Apply CLAHE for better contrast
            CvInvoke.GaussianBlur(resized, resized, new Size(3, 3), 0);

            return resized;
        }

        private bool IsFaceCloseEnough(Rectangle face, int frameWidth, int frameHeight)
        {
            if (face.Width < MIN_FACE_SIZE_FOR_RECOGNITION || face.Height < MIN_FACE_SIZE_FOR_RECOGNITION)
            {
                return false;
            }

            double faceArea = face.Width * face.Height;
            double frameArea = frameWidth * frameHeight;
            double areaRatio = faceArea / frameArea;

            return areaRatio >= MIN_FACE_AREA_RATIO;
        }

        private Rectangle? GetClosestFace(Rectangle[] faces, int frameWidth, int frameHeight)
        {
            if (faces.Length == 0) return null;

            var sortedFaces = faces.OrderByDescending(f => f.Width * f.Height).ToArray();

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
        /// Capture multiple face images for better training (captures 5 variations)
        /// </summary>
        public List<string> CaptureMultipleFaces(Mat frame, int empId, int count = 5)
        {
            List<string> savedPaths = new List<string>();
            Rectangle[] faces = DetectFaces(frame);

            if (faces.Length == 0) return savedPaths;

            Rectangle? closestFace = GetClosestFace(faces, frame.Width, frame.Height);
            if (!closestFace.HasValue) return savedPaths;

            Rectangle face = closestFace.Value;

            try
            {
                Mat faceRegion = new Mat(frame, face);
                Mat grayFace = new Mat();
                CvInvoke.CvtColor(faceRegion, grayFace, ColorConversion.Bgr2Gray);

                Image<Gray, byte> faceImage = grayFace.ToImage<Gray, byte>();

                // Save original normalized face
                Image<Gray, byte>? normalized = NormalizeFace(faceImage);
                if (normalized != null)
                {
                    string fileName = $"emp_{empId}_{DateTime.Now:yyyyMMddHHmmss}_0.jpg";
                    string filePath = Path.Combine(_facesFolder, fileName);
                    normalized.Save(filePath);
                    savedPaths.Add(filePath);

                    // Save horizontal flip
                    var flipped = normalized.Flip(FlipType.Horizontal);
                    string flipPath = Path.Combine(_facesFolder, $"emp_{empId}_{DateTime.Now:yyyyMMddHHmmss}_flip.jpg");
                    flipped.Save(flipPath);
                    savedPaths.Add(flipPath);
                    flipped.Dispose();

                    // Save slightly brightened version
                    var brightened = normalized.Clone();
                    brightened._GammaCorrect(1.2);
                    string brightPath = Path.Combine(_facesFolder, $"emp_{empId}_{DateTime.Now:yyyyMMddHHmmss}_bright.jpg");
                    brightened.Save(brightPath);
                    savedPaths.Add(brightPath);
                    brightened.Dispose();

                    // Save slightly darkened version
                    var darkened = normalized.Clone();
                    darkened._GammaCorrect(0.8);
                    string darkPath = Path.Combine(_facesFolder, $"emp_{empId}_{DateTime.Now:yyyyMMddHHmmss}_dark.jpg");
                    darkened.Save(darkPath);
                    savedPaths.Add(darkPath);
                    darkened.Dispose();

                    normalized.Dispose();
                }

                faceRegion.Dispose();
                grayFace.Dispose();
                faceImage.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing multiple faces: {ex.Message}");
            }

            return savedPaths;
        }

        /// <summary>
        /// Original single capture (backwards compatibility)
        /// </summary>
        public string? CaptureFace(Mat frame, int empId)
        {
            var paths = CaptureMultipleFaces(frame, empId, 1);
            return paths.FirstOrDefault();
        }

        public Rectangle[] DetectFaces(Mat frame)
        {
            Mat grayFrame = new Mat();
            CvInvoke.CvtColor(frame, grayFrame, ColorConversion.Bgr2Gray);
            CvInvoke.EqualizeHist(grayFrame, grayFrame);

            var faces = _faceDetector.DetectMultiScale(
                grayFrame,
                scaleFactor: 1.05,      // More sensitive (was 1.08)
                minNeighbors: 3,         // Reduced from 4
                minSize: new Size(MIN_FACE_SIZE_FOR_DETECTION, MIN_FACE_SIZE_FOR_DETECTION),
                maxSize: new Size(MAX_FACE_SIZE, MAX_FACE_SIZE)
            );

            grayFrame.Dispose();
            return faces;
        }

        public Rectangle[] DetectCloseFaces(Mat frame)
        {
            Rectangle[] allFaces = DetectFaces(frame);

            var closeFaces = allFaces
                .Where(f => IsFaceCloseEnough(f, frame.Width, frame.Height))
                .OrderByDescending(f => f.Width * f.Height)
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
                System.Diagnostics.Debug.WriteLine("No employee faces found in database.");
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
                int imagesForEmployee = 0;

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
                                imagesForEmployee++;

                                // Add flipped version for augmentation
                                var flipped = normalized.Flip(FlipType.Horizontal);
                                if (!flipped.Mat.IsEmpty)
                                {
                                    faceImages.Add(flipped.Mat.Clone());
                                    labels.Add(labelIndex);
                                    imagesForEmployee++;
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

                if (imagesForEmployee > 0)
                {
                    _labelToEmpId[labelIndex] = empId;
                    labelIndex++;
                    System.Diagnostics.Debug.WriteLine($"Loaded {imagesForEmployee} images for empId {empId}");
                }
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

                System.Diagnostics.Debug.WriteLine($"Model trained successfully with {faceImages.Count} images for {_labelToEmpId.Count} employees");
                
                // Notify listeners that model was retrained
                ModelRetrained?.Invoke(this, EventArgs.Empty);
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
        /// Recognize a face in a frame
        /// </summary>
        public (int empId, double confidence, bool isStable)? RecognizeFace(Mat frame)
        {
            if (!_isModelTrained || _recognizer == null)
            {
                System.Diagnostics.Debug.WriteLine("Model not trained, cannot recognize.");
                return null;
            }

            Rectangle[] closeFaces = DetectCloseFaces(frame);

            if (closeFaces.Length == 0)
                return null;

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

                System.Diagnostics.Debug.WriteLine($"Recognition result - Label: {result.Label}, Distance: {result.Distance}");

                if (result.Label >= 0 && result.Distance < UNKNOWN_THRESHOLD)
                {
                    if (_labelToEmpId.TryGetValue(result.Label, out int empId))
                    {
                        _recognitionHistory.Enqueue(empId);
                        if (_recognitionHistory.Count > RECOGNITION_HISTORY_SIZE)
                            _recognitionHistory.Dequeue();

                        // Faster stability check - only need 3 consecutive matches now
                        bool isStable = _recognitionHistory.Count >= RECOGNITION_HISTORY_SIZE &&
                                       _recognitionHistory.All(id => id == empId);

                        double confidence = Math.Max(0, 100 - result.Distance);
                        return (empId, confidence, isStable);
                    }
                }

                _recognitionHistory.Clear();
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Recognition error: {ex.Message}");
                return null;
            }
        }

        public void ClearRecognitionHistory()
        {
            _recognitionHistory.Clear();
        }

        public bool IsModelTrained => _isModelTrained;
        public int TrainedEmployeeCount => _labelToEmpId.Count;

        public void Dispose()
        {
            _faceDetector?.Dispose();
            _recognizer?.Dispose();
            _instance = null;
        }
    }
}