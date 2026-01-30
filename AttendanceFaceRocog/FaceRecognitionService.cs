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
        private LBPHFaceRecognizer? _recognizer; // CHANGE 1: Use LBPH instead of EigenFace - more robust
        private readonly Dictionary<int, int> _labelToEmpId = new();
        private bool _isModelTrained = false;

        private readonly string _facesFolder;

        // CHANGE 2: Add constants for better recognition
        private const int FACE_SIZE = 100;
        private const double UNKNOWN_THRESHOLD = 95; // LBPH threshold (lower = stricter)

        // CHANGE 3: Add recognition stability tracking
        private const int RECOGNITION_HISTORY_SIZE = 5;
        private Queue<int> _recognitionHistory = new Queue<int>();

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

                // Validate cascade loaded successfully
                if (_faceDetector == null)
                {
                    throw new Exception("Failed to initialize face detector - CascadeClassifier is null");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load face detector: {ex.Message}", ex);
            }

            // CHANGE 4: Initialize LBPH with optimized parameters
            // Parameters: radius=2, neighbors=8, grid_x=8, grid_y=8, threshold=100
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

        // CHANGE 5: Add face normalization method for consistent preprocessing
        private Image<Gray, byte>? NormalizeFace(Image<Gray, byte>? face)
        {
            if (face == null) return null;

            var resized = face.Resize(FACE_SIZE, FACE_SIZE, Inter.Cubic);

            // Improve contrast for better LBPH recognition
            resized._EqualizeHist();

            // Small blur reduces noise and improves stability
            CvInvoke.GaussianBlur(resized, resized, new Size(3, 3), 0);

            return resized;
        }

        /// <summary>
        /// Capture and save face for a new employee
        /// </summary>
        public string? CaptureFace(Mat frame, int empId)
        {
            Rectangle[] faces = DetectFaces(frame);

            if (faces.Length == 0) return null;

            // Get the largest face
            Rectangle face = faces[0];
            foreach (var f in faces)
            {
                if (f.Width * f.Height > face.Width * face.Height)
                    face = f;
            }

            // Extract and resize face region
            Mat faceRegion = new Mat(frame, face);
            Mat grayFace = new Mat();
            CvInvoke.CvtColor(faceRegion, grayFace, ColorConversion.Bgr2Gray);

            // CHANGE 6: Use normalization instead of simple resize
            Image<Gray, byte> faceImage = grayFace.ToImage<Gray, byte>();
            Image<Gray, byte>? normalized = NormalizeFace(faceImage);

            if (normalized == null)
            {
                faceRegion.Dispose();
                grayFace.Dispose();
                faceImage.Dispose();
                return null;
            }

            // Save face image
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
        /// Detect faces in a frame
        /// </summary>
        public Rectangle[] DetectFaces(Mat frame)
        {
            Mat grayFrame = new Mat();
            CvInvoke.CvtColor(frame, grayFrame, ColorConversion.Bgr2Gray);

            // CHANGE 7: Apply histogram equalization for better detection in varying lighting
            CvInvoke.EqualizeHist(grayFrame, grayFrame);

            // CHANGE 8: More lenient detection parameters
            var faces = _faceDetector.DetectMultiScale(
                grayFrame,
                scaleFactor: 1.08,  // Changed from 1.1 to 1.08 for better detection
                minNeighbors: 4,     // Changed from 5 to 4 for more sensitivity
                minSize: new Size(60, 60),  // Changed from 50 to 60
                maxSize: new Size(250, 250) // Add max size to filter out false positives
            );

            grayFrame.Dispose();
            return faces;
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

            // CHANGE 9: Group by employee to ensure proper labeling
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
                        // CHANGE 10: Load and normalize each face image
                        Image<Gray, byte> faceImg = new Image<Gray, byte>(imgPath);

                        // Validate loaded image
                        if (faceImg.Width == 0 || faceImg.Height == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Invalid image dimensions: {imgPath}");
                            faceImg.Dispose();
                            continue;
                        }

                        Image<Gray, byte>? normalized = NormalizeFace(faceImg);

                        if (normalized != null && normalized.Width == FACE_SIZE && normalized.Height == FACE_SIZE)
                        {
                            // Validate the Mat is not empty
                            if (!normalized.Mat.IsEmpty)
                            {
                                // Add original image
                                faceImages.Add(normalized.Mat.Clone()); // Clone to ensure independence
                                labels.Add(labelIndex);

                                // CHANGE 11: Add flipped version for better angle tolerance
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

                // Map label to empID
                _labelToEmpId[labelIndex] = empId;
                labelIndex++;
            }

            // CHANGE 12: Need at least 2 images to train
            if (faceImages.Count < 2)
            {
                System.Diagnostics.Debug.WriteLine($"Not enough training data: {faceImages.Count} images. Need at least 2.");
                _isModelTrained = false;

                // Cleanup
                foreach (var img in faceImages)
                    img.Dispose();

                return;
            }

            try
            {
                // Validate all images are valid and same size
                bool allValid = true;
                for (int i = 0; i < faceImages.Count; i++)
                {
                    if (faceImages[i] == null || faceImages[i].IsEmpty ||
                        faceImages[i].Width != FACE_SIZE || faceImages[i].Height != FACE_SIZE)
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid image at index {i}");
                        allValid = false;
                        break;
                    }
                }

                if (!allValid)
                {
                    System.Diagnostics.Debug.WriteLine("Training aborted: Invalid images detected");
                    _isModelTrained = false;

                    // Cleanup
                    foreach (var img in faceImages)
                        img.Dispose();

                    return;
                }

                // Validate labels match images count
                if (labels.Count != faceImages.Count)
                {
                    System.Diagnostics.Debug.WriteLine($"Label count mismatch: {labels.Count} labels vs {faceImages.Count} images");
                    _isModelTrained = false;

                    // Cleanup
                    foreach (var img in faceImages)
                        img.Dispose();

                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Training with {faceImages.Count} images and {_labelToEmpId.Count} unique employees");

                _recognizer?.Dispose();
                _recognizer = new LBPHFaceRecognizer(2, 8, 8, 8, 100);

                _recognizer.Train(faceImages.ToArray(), labels.ToArray());

                _isModelTrained = true;

                // Optionally save the model
                try
                {
                    string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "face_recognizer.xml");
                    _recognizer.Write(modelPath);
                    System.Diagnostics.Debug.WriteLine($"Model saved to: {modelPath}");
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not save model: {saveEx.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Training failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _isModelTrained = false;
            }

            // Cleanup
            foreach (var img in faceImages)
                img.Dispose();
        }

        /// <summary>
        /// Recognize a face and return employee ID with stability check
        /// </summary>
        public (int empId, double confidence, bool isStable)? RecognizeFace(Mat frame)
        {
            if (!_isModelTrained || _recognizer == null) return null;

            Rectangle[] faces = DetectFaces(frame);
            if (faces.Length == 0)
            {
                // CHANGE 13: Clear history when no face detected
                _recognitionHistory.Clear();
                return null;
            }

            // Get largest face
            Rectangle face = faces[0];
            foreach (var f in faces)
            {
                if (f.Width * f.Height > face.Width * face.Height)
                    face = f;
            }

            Mat faceRegion = new Mat(frame, face);
            Mat grayFace = new Mat();
            CvInvoke.CvtColor(faceRegion, grayFace, ColorConversion.Bgr2Gray);

            // CHANGE 14: Normalize face before prediction
            Image<Gray, byte> faceImage = grayFace.ToImage<Gray, byte>();
            Image<Gray, byte>? normalized = NormalizeFace(faceImage);

            if (normalized == null)
            {
                faceRegion.Dispose();
                grayFace.Dispose();
                faceImage.Dispose();
                _recognitionHistory.Clear();
                return null;
            }

            var result = _recognizer.Predict(normalized);

            faceRegion.Dispose();
            grayFace.Dispose();
            faceImage.Dispose();
            normalized.Dispose();

            // CHANGE 15: Check confidence threshold and validate label
            if (result.Label >= 0 &&
                result.Distance <= UNKNOWN_THRESHOLD &&
                _labelToEmpId.ContainsKey(result.Label))
            {
                int recognizedEmpId = _labelToEmpId[result.Label];

                // CHANGE 16: Add to recognition history
                _recognitionHistory.Enqueue(recognizedEmpId);
                if (_recognitionHistory.Count > RECOGNITION_HISTORY_SIZE)
                    _recognitionHistory.Dequeue();

                // CHANGE 17: Check for stable recognition (majority vote)
                bool isStable = false;
                if (_recognitionHistory.Count >= 3)
                {
                    int mostCommon = _recognitionHistory
                        .GroupBy(x => x)
                        .OrderByDescending(g => g.Count())
                        .First().Key;

                    int matchCount = _recognitionHistory.Count(x => x == mostCommon);

                    // Need at least 3 consistent recognitions
                    isStable = matchCount >= 3 && mostCommon == recognizedEmpId;
                }

                return (recognizedEmpId, result.Distance, isStable);
            }

            // CHANGE 18: Unknown face - clear history
            _recognitionHistory.Clear();
            return null;
        }

        public void Dispose()
        {
            _faceDetector?.Dispose();
            _recognizer?.Dispose();
        }
    }
}