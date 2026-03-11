using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace AttendanceFaceRocog
{
    public sealed class LivenessDetectionService
    {
        private readonly Queue<FrameMetrics> _history = new();
        private readonly Queue<Rectangle> _faceHistory = new();
        private readonly Queue<int> _eyeOpenHistory = new();
        private readonly CascadeClassifier? _eyeDetector;

        private BlinkStage _blinkStage = BlinkStage.WaitingOpen;
        private int _blinkStageFrames = 0;
        private bool _blinkDetected = false;
        private int _blinkHoldFrames = 0;

        private const int MAX_SAMPLES = 6;
        private const int MIN_REQUIRED_SAMPLES = 4;
        private const int EYE_HISTORY_SIZE = 3;
        private const int BLINK_STAGE_TIMEOUT = 12;
        private const int BLINK_HOLD_DURATION = 10;

        public LivenessDetectionService()
        {
            try
            {
                string eyeCascadePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "haarcascade_eye_tree_eyeglasses.xml");

                if (File.Exists(eyeCascadePath))
                {
                    _eyeDetector = new CascadeClassifier(eyeCascadePath);
                    System.Diagnostics.Debug.WriteLine("✓ Eye cascade loaded successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠ Eye cascade not found. Blink detection disabled.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠ Failed to load eye cascade: {ex.Message}");
            }
        }

        public void Reset()
        {
            _history.Clear();
            _faceHistory.Clear();
            ResetBlinkTracking();
        }

        public LivenessCheckResult Evaluate(Mat frame, Rectangle face)
        {
            if (frame == null || frame.IsEmpty || face.Width <= 0 || face.Height <= 0)
            {
                Reset();
                return new LivenessCheckResult(false, "Invalid face sample.");
            }

            Rectangle bounds = new Rectangle(Point.Empty, frame.Size);
            Rectangle safeFace = Rectangle.Intersect(bounds, face);

            if (safeFace.Width < 60 || safeFace.Height < 60)
            {
                Reset();
                return new LivenessCheckResult(false, "Face too small.");
            }

            using Mat faceRegion = new Mat(frame, safeFace);
            using Mat rawGray = new Mat();
            using Mat equalizedGray = new Mat();

            CvInvoke.CvtColor(faceRegion, rawGray, ColorConversion.Bgr2Gray);
            rawGray.CopyTo(equalizedGray);
            CvInvoke.EqualizeHist(equalizedGray, equalizedGray);

            double meanIntensity = CalculateMeanIntensity(rawGray);
            double intensityStdDev = CalculateIntensityStdDev(rawGray);
            double darkRatio = CalculateDarkRatio(rawGray);
            double hotspotRatio = CalculateHotspotRatio(rawGray);
            double reflectionRatio = CalculateReflectionRatio(rawGray);
            double reflectionClusterCount = CalculateReflectionClusterCount(rawGray);

            double laplacianVariance = CalculateLaplacianVariance(equalizedGray);
            double edgeDensity = CalculateEdgeDensity(equalizedGray);
            double glareRatio = CalculateGlareRatio(equalizedGray);

            _history.Enqueue(new FrameMetrics(
                laplacianVariance,
                edgeDensity,
                glareRatio,
                meanIntensity,
                intensityStdDev,
                darkRatio,
                hotspotRatio,
                reflectionRatio,
                reflectionClusterCount));

            _faceHistory.Enqueue(safeFace);

            while (_history.Count > MAX_SAMPLES)
            {
                _history.Dequeue();
            }

            while (_faceHistory.Count > MAX_SAMPLES)
            {
                _faceHistory.Dequeue();
            }

            if (_eyeDetector == null)
            {
                return new LivenessCheckResult(false, "Eye detector unavailable. Blink verification required.");
            }

            bool blinkDetected = DetectBlink(equalizedGray);

            if (_history.Count < MIN_REQUIRED_SAMPLES || _faceHistory.Count < MIN_REQUIRED_SAMPLES)
            {
                return new LivenessCheckResult(false, "Checking face authenticity...");
            }

            double avgLaplacian = _history.Average(x => x.LaplacianVariance);
            double avgEdgeDensity = _history.Average(x => x.EdgeDensity);
            double avgGlareRatio = _history.Average(x => x.GlareRatio);
            double avgMeanIntensity = _history.Average(x => x.MeanIntensity);
            double avgIntensityStdDev = _history.Average(x => x.IntensityStdDev);
            double avgDarkRatio = _history.Average(x => x.DarkRatio);
            double avgHotspotRatio = _history.Average(x => x.HotspotRatio);
            double avgReflectionRatio = _history.Average(x => x.ReflectionRatio);
            double avgReflectionClusterCount = _history.Average(x => x.ReflectionClusterCount);

            double centerXRange =
                _faceHistory.Max(r => r.X + (r.Width / 2.0)) -
                _faceHistory.Min(r => r.X + (r.Width / 2.0));

            double centerYRange =
                _faceHistory.Max(r => r.Y + (r.Height / 2.0)) -
                _faceHistory.Min(r => r.Y + (r.Height / 2.0));

            double avgFaceWidth = _faceHistory.Average(r => r.Width);
            double avgFaceHeight = _faceHistory.Average(r => r.Height);

            bool hasNaturalMotion =
                centerXRange >= avgFaceWidth * 0.04 ||
                centerYRange >= avgFaceHeight * 0.04 ||
                (blinkDetected &&
                 (centerXRange >= avgFaceWidth * 0.02 ||
                  centerYRange >= avgFaceHeight * 0.02));

            bool motionTooLarge =
                centerXRange > avgFaceWidth * 0.60 ||
                centerYRange > avgFaceHeight * 0.60;

            bool looksLikePhoneReflection =
                avgReflectionRatio > 0.012 ||
                avgReflectionClusterCount >= 1.0 ||
                (avgReflectionRatio > 0.007 && avgGlareRatio > 0.040) ||
                (avgReflectionClusterCount >= 1.0 && avgHotspotRatio > 0.020);

            bool looksLikePhoneScreen =
                (avgMeanIntensity < 58 && avgDarkRatio > 0.34) ||
                (avgHotspotRatio > 0.050 && avgGlareRatio > 0.070) ||
                (avgGlareRatio > 0.075 && avgReflectionRatio > 0.008) ||
                (avgLaplacian < 52 && avgHotspotRatio > 0.030);

            bool looksLikePrintedPhoto =
                avgIntensityStdDev < 26 &&
                avgEdgeDensity < 0.060 &&
                avgLaplacian < 45 &&
                avgHotspotRatio < 0.03;

            bool strongIrLiveFace =
                avgMeanIntensity >= 72 &&
                avgMeanIntensity <= 210 &&
                avgIntensityStdDev >= 32 &&
                avgDarkRatio < 0.30 &&
                avgHotspotRatio < 0.03 &&
                avgGlareRatio < 0.065 &&
                avgEdgeDensity > 0.060 &&
                avgLaplacian > 55 &&
                avgReflectionRatio < 0.007 &&
                avgReflectionClusterCount < 1.0;

            System.Diagnostics.Debug.WriteLine(
                $"Anti-spoof: mean={avgMeanIntensity:F2}, std={avgIntensityStdDev:F2}, dark={avgDarkRatio:F4}, hotspot={avgHotspotRatio:F4}, glare={avgGlareRatio:F4}, lap={avgLaplacian:F2}, edge={avgEdgeDensity:F4}, reflect={avgReflectionRatio:F4}, reflectClusters={avgReflectionClusterCount:F2}, blink={blinkDetected}, moveX={centerXRange:F2}, moveY={centerYRange:F2}");

            if (looksLikePhoneReflection)
            {
                return new LivenessCheckResult(false, "Phone reflection detected. Attendance blocked.");
            }

            if (looksLikePhoneScreen)
            {
                return new LivenessCheckResult(false, "Phone screen detected. Attendance blocked.");
            }

            if (looksLikePrintedPhoto)
            {
                return new LivenessCheckResult(false, "Printed photo detected. Attendance blocked.");
            }

            if (motionTooLarge)
            {
                return new LivenessCheckResult(false, "Spoof-like motion detected. Attendance blocked.");
            }

            if (!blinkDetected)
            {
                return new LivenessCheckResult(false, "Please blink naturally to verify liveness.");
            }

            if (!hasNaturalMotion)
            {
                return new LivenessCheckResult(false, "Move your face slightly and blink.");
            }

            if (strongIrLiveFace)
            {
                return new LivenessCheckResult(true, "Live face verified.");
            }

            return new LivenessCheckResult(false, "Live face not verified.");
        }

        private bool DetectBlink(Mat grayFace)
        {
            if (_blinkDetected)
            {
                _blinkHoldFrames++;
                if (_blinkHoldFrames <= BLINK_HOLD_DURATION)
                {
                    return true;
                }

                ResetBlinkTracking();
            }

            if (_eyeDetector == null)
            {
                return false;
            }

            try
            {
                Rectangle upperFace = new Rectangle(
                    grayFace.Width / 10,
                    grayFace.Height / 10,
                    Math.Max(1, grayFace.Width - (grayFace.Width / 5)),
                    Math.Max(1, (int)(grayFace.Height * 0.38)));

                using Mat eyeRegion = new Mat(grayFace, upperFace);

                int halfWidth = Math.Max(1, eyeRegion.Width / 2);
                Rectangle leftEyeArea = new Rectangle(0, 0, halfWidth, eyeRegion.Height);
                Rectangle rightEyeArea = new Rectangle(halfWidth, 0, eyeRegion.Width - halfWidth, eyeRegion.Height);

                using Mat leftEyeRegion = new Mat(eyeRegion, leftEyeArea);
                using Mat rightEyeRegion = new Mat(eyeRegion, rightEyeArea);

                int openScore = 0;
                if (HasEyeCandidate(leftEyeRegion))
                {
                    openScore++;
                }

                if (HasEyeCandidate(rightEyeRegion))
                {
                    openScore++;
                }

                _eyeOpenHistory.Enqueue(openScore);
                while (_eyeOpenHistory.Count > EYE_HISTORY_SIZE)
                {
                    _eyeOpenHistory.Dequeue();
                }

                double smoothedOpenScore = _eyeOpenHistory.Average();
                EyeState eyeState = GetEyeState(openScore, smoothedOpenScore);

                _blinkStageFrames++;

                switch (_blinkStage)
                {
                    case BlinkStage.WaitingOpen:
                        if (eyeState == EyeState.Open)
                        {
                            _blinkStage = BlinkStage.WaitingClosed;
                            _blinkStageFrames = 0;
                        }
                        else if (_blinkStageFrames > BLINK_STAGE_TIMEOUT)
                        {
                            ResetBlinkTracking();
                        }
                        break;

                    case BlinkStage.WaitingClosed:
                        if (eyeState == EyeState.Closed)
                        {
                            _blinkStage = BlinkStage.WaitingReopen;
                            _blinkStageFrames = 0;
                        }
                        else if (_blinkStageFrames > BLINK_STAGE_TIMEOUT)
                        {
                            _blinkStage = BlinkStage.WaitingOpen;
                            _blinkStageFrames = 0;
                        }
                        break;

                    case BlinkStage.WaitingReopen:
                        if (eyeState == EyeState.Open)
                        {
                            _blinkDetected = true;
                            _blinkHoldFrames = 0;
                        }
                        else if (_blinkStageFrames > BLINK_STAGE_TIMEOUT)
                        {
                            _blinkStage = BlinkStage.WaitingOpen;
                            _blinkStageFrames = 0;
                        }
                        break;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"Blink: stage={_blinkStage}, rawOpenScore={openScore}, smooth={smoothedOpenScore:F2}, eyeState={eyeState}, detected={_blinkDetected}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Blink detection error: {ex.Message}");
            }

            return _blinkDetected;
        }

        private bool HasEyeCandidate(Mat region)
        {
            Rectangle[] eyes = _eyeDetector!.DetectMultiScale(
                region,
                scaleFactor: 1.05,
                minNeighbors: 3,
                minSize: new Size(
                    Math.Max(10, region.Width / 6),
                    Math.Max(10, region.Height / 5)),
                maxSize: new Size(
                    Math.Max(20, region.Width - 4),
                    Math.Max(20, region.Height - 4)));

            return eyes.Length > 0;
        }

        private EyeState GetEyeState(int openScore, double smoothedOpenScore)
        {
            if (openScore >= 2 || smoothedOpenScore >= 0.65)
            {
                return EyeState.Open;
            }

            if (openScore == 0 && smoothedOpenScore <= 0.50)
            {
                return EyeState.Closed;
            }

            return EyeState.Transitional;
        }

        private void ResetBlinkTracking()
        {
            _blinkStage = BlinkStage.WaitingOpen;
            _blinkStageFrames = 0;
            _blinkDetected = false;
            _blinkHoldFrames = 0;
            _eyeOpenHistory.Clear();
        }

        private static double CalculateMeanIntensity(Mat gray)
        {
            MCvScalar mean = CvInvoke.Mean(gray);
            return mean.V0;
        }

        private static double CalculateIntensityStdDev(Mat gray)
        {
            MCvScalar mean = default;
            MCvScalar stdDev = default;
            CvInvoke.MeanStdDev(gray, ref mean, ref stdDev);
            return stdDev.V0;
        }

        private static double CalculateDarkRatio(Mat gray)
        {
            using Mat dark = new Mat();
            CvInvoke.Threshold(gray, dark, 35, 255, ThresholdType.BinaryInv);

            int darkPixels = CvInvoke.CountNonZero(dark);
            return (double)darkPixels / (gray.Rows * gray.Cols);
        }

        private static double CalculateHotspotRatio(Mat gray)
        {
            using Mat bright = new Mat();
            CvInvoke.Threshold(gray, bright, 245, 255, ThresholdType.Binary);

            int brightPixels = CvInvoke.CountNonZero(bright);
            return (double)brightPixels / (gray.Rows * gray.Cols);
        }

        private static double CalculateLaplacianVariance(Mat gray)
        {
            using Mat lap = new Mat();

            CvInvoke.Laplacian(gray, lap, DepthType.Cv64F);

            MCvScalar mean = default;
            MCvScalar stdDev = default;
            CvInvoke.MeanStdDev(lap, ref mean, ref stdDev);

            double std = stdDev.V0;
            return std * std;
        }

        private static double CalculateEdgeDensity(Mat gray)
        {
            using Mat edges = new Mat();
            CvInvoke.Canny(gray, edges, 80, 160);

            int edgePixels = CvInvoke.CountNonZero(edges);
            return (double)edgePixels / (gray.Rows * gray.Cols);
        }

        private static double CalculateGlareRatio(Mat gray)
        {
            using Mat bright = new Mat();
            CvInvoke.Threshold(gray, bright, 240, 255, ThresholdType.Binary);

            int brightPixels = CvInvoke.CountNonZero(bright);
            return (double)brightPixels / (gray.Rows * gray.Cols);
        }

        private static double CalculateReflectionRatio(Mat gray)
        {
            using Mat bright = new Mat();
            using Mat cleaned = new Mat();
            using Mat kernel = CreateOnesKernel(3, 3);

            CvInvoke.Threshold(gray, bright, 238, 255, ThresholdType.Binary);
            CvInvoke.MorphologyEx(
                bright,
                cleaned,
                MorphOp.Open,
                kernel,
                new Point(-1, -1),
                1,
                BorderType.Reflect,
                default);

            int reflectionPixels = CvInvoke.CountNonZero(cleaned);
            return (double)reflectionPixels / (gray.Rows * gray.Cols);
        }

        private static double CalculateReflectionClusterCount(Mat gray)
        {
            using Mat bright = new Mat();
            using Mat cleaned = new Mat();
            using Mat hierarchy = new Mat();
            using Mat kernel = CreateOnesKernel(5, 5);
            using VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();

            CvInvoke.Threshold(gray, bright, 236, 255, ThresholdType.Binary);
            CvInvoke.MorphologyEx(
                bright,
                cleaned,
                MorphOp.Close,
                kernel,
                new Point(-1, -1),
                1,
                BorderType.Reflect,
                default);

            CvInvoke.FindContours(
                cleaned,
                contours,
                hierarchy,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);

            double faceArea = gray.Rows * gray.Cols;
            int suspiciousClusters = 0;

            for (int i = 0; i < contours.Size; i++)
            {
                using VectorOfPoint contour = contours[i];

                double contourArea = CvInvoke.ContourArea(contour);
                if (contourArea <= 0)
                {
                    continue;
                }

                double areaRatio = contourArea / faceArea;
                if (areaRatio < 0.0012 || areaRatio > 0.18)
                {
                    continue;
                }

                Rectangle rect = CvInvoke.BoundingRectangle(contour);
                double aspectRatio = rect.Width / (double)Math.Max(1, rect.Height);

                bool elongated = aspectRatio >= 2.4 || aspectRatio <= 0.42;
                bool largePatch = rect.Width >= gray.Width * 0.18 || rect.Height >= gray.Height * 0.18;

                if (elongated || largePatch)
                {
                    suspiciousClusters++;
                }
            }

            return suspiciousClusters;
        }

        private static Mat CreateOnesKernel(int width, int height)
        {
            Mat kernel = new Mat(height, width, DepthType.Cv8U, 1);
            kernel.SetTo(new MCvScalar(1));
            return kernel;
        }

        private enum BlinkStage
        {
            WaitingOpen,
            WaitingClosed,
            WaitingReopen
        }

        private enum EyeState
        {
            Open,
            Closed,
            Transitional
        }

        private readonly record struct FrameMetrics(
            double LaplacianVariance,
            double EdgeDensity,
            double GlareRatio,
            double MeanIntensity,
            double IntensityStdDev,
            double DarkRatio,
            double HotspotRatio,
            double ReflectionRatio,
            double ReflectionClusterCount);
    }

    public readonly record struct LivenessCheckResult(bool IsLive, string Message);
}
