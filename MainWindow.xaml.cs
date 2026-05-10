using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FluxBars
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture? capture;

        // EXACT Lua structure
        private readonly double[] bars;
        private readonly double[] smoothing;
        private readonly double[] targets;

        // Visual rectangles
        private readonly List<Rectangle> leftRects = new();
        private readonly List<Rectangle> rightRects = new();

        // Settings
        private const int NUM_BARS = 32;
        private const int HALF_BARS = NUM_BARS / 2;

        private const int BUFFER_SIZE = 256;

        // EXACT Lua values
        private const double GAIN = 10;
        private const double EXPONENT = 1.4;
        private const double BASS_BOOST = 1.5;

        private const double SMOOTH_UP = 10;
        private const double SMOOTH_DOWN = 5;

        private const double FFT_INTERVAL = 0.03;

        // Visual tuning
        private const double HEIGHT_MULTIPLIER = 10.5;

        // NEW:
        // Boost final FFT output slightly
        // so bars are visible again
        private const double OUTPUT_MULTIPLIER = 12.0;

        private const double BAR_GAP = 2;

        // Lua timing
        private double fftTimer = 0;
        private DateTime lastFrame = DateTime.Now;

        // Audio samples
        private float[] latestSamples = Array.Empty<float>();
        private readonly object sampleLock = new();

        public MainWindow()
        {
            InitializeComponent();

            bars = new double[NUM_BARS];
            smoothing = new double[NUM_BARS];
            targets = new double[NUM_BARS];

            CreateBars();
            StartAudio();

            DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            timer.Tick += Update;
            timer.Start();
        }

        // Create bars
        private void CreateBars()
        {
            for (int i = 0; i < HALF_BARS; i++)
            {
                Rectangle left = new()
                {
                    Fill = Brushes.White,
                    RadiusX = 2,
                    RadiusY = 2
                };

                Rectangle right = new()
                {
                    Fill = Brushes.White,
                    RadiusX = 2,
                    RadiusY = 2
                };

                leftRects.Add(left);
                rightRects.Add(right);

                BarCanvas.Children.Add(left);
                BarCanvas.Children.Add(right);
            }
        }

        // Capture desktop audio
        private void StartAudio()
        {
            capture = new WasapiLoopbackCapture();

            capture.DataAvailable += (s, e) =>
            {
                int sampleCount =
                    e.BytesRecorded / 4;

                if (sampleCount <= 0)
                    return;

                float[] samples =
                    new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = Math.Abs(
                        BitConverter.ToSingle(
                            e.Buffer,
                            i * 4
                        )
                    );
                }

                lock (sampleLock)
                {
                    latestSamples = samples;
                }
            };

            capture.StartRecording();
        }

        // EXACT Lua fakeFFT()
        // WITH soft compression
        private double[] FakeFFT(float[] samples)
        {
            double[] result =
                new double[NUM_BARS];

            int chunkSize =
                samples.Length / NUM_BARS;

            if (chunkSize <= 0)
                return result;

            for (int i = 0; i < NUM_BARS; i++)
            {
                double sum = 0;

                for (int j = 0; j < chunkSize; j++)
                {
                    int idx =
                        (i * chunkSize) + j;

                    if (idx >= samples.Length)
                        break;

                    sum += Math.Abs(samples[idx]);
                }

                // EXACT Lua logic
                double value =
                    (sum / chunkSize) * GAIN;

                // Bass boost
                if (i < NUM_BARS * 0.3)
                    value *= BASS_BOOST;

                // Exponent shaping
                value =
                    Math.Pow(value, EXPONENT);

                // Soft compression
                value =
                    value / (1 + value * 0.12);

                // IMPORTANT:
                // Re-amplify AFTER compression
                // so bars stay visible
                value *= OUTPUT_MULTIPLIER;

                result[i] = value;
            }

            return result;
        }

        private void Update(
            object? sender,
            EventArgs e
        )
        {
            DateTime now = DateTime.Now;

            double dt =
                (now - lastFrame).TotalSeconds;

            lastFrame = now;

            fftTimer += dt;

            // EXACT Lua timing
            if (fftTimer >= FFT_INTERVAL)
            {
                fftTimer -= FFT_INTERVAL;

                float[] samplesCopy;

                lock (sampleLock)
                {
                    if (latestSamples.Length == 0)
                        return;

                    samplesCopy =
                        (float[])latestSamples.Clone();
                }

                if (samplesCopy.Length >= BUFFER_SIZE)
                {
                    double[] rawBars =
                        FakeFFT(samplesCopy);

                    for (int i = 0; i < NUM_BARS; i++)
                    {
                        targets[i] =
                            rawBars[i];
                    }
                }
            }

            // EXACT Lua smoothing
            for (int i = 0; i < NUM_BARS; i++)
            {
                double target =
                    targets[i];

                double speed =
                    target > smoothing[i]
                    ? SMOOTH_UP
                    : SMOOTH_DOWN;

                double factor =
                    1 - Math.Exp(-speed * dt);

                smoothing[i] +=
                    (target - smoothing[i]) * factor;

                bars[i] =
                    smoothing[i];
            }

            DrawBars();
        }

        private void DrawBars()
        {
            double w = ActualWidth;
            double h = ActualHeight;

            double centerX = w / 2;

            // EXACT Lua width logic
            double barWidth =
                (w / 2) / HALF_BARS;

            for (int i = 0; i < HALF_BARS; i++)
            {
                // Opposite mirror mode
                int index =
                    HALF_BARS - 1 - i;

                double value =
                    bars[index];

                // EXACT Lua scaling
                double height =
                    value * HEIGHT_MULTIPLIER;

                // Scale to window size
                height *= h / 500.0;

                // Prevent REAL overflow only
                if (height > h)
                    height = h;

                // Lower bars slightly
                height -= 5;

                // Prevent negatives
                if (height < 0)
                    height = 0;

                double y =
                    h - height;

                // Lua positions
                double xR =
                    centerX +
                    (i * barWidth);

                double xL =
                    centerX -
                    ((i + 1) * barWidth);

                // Fill screen width
                double width =
                    Math.Max(
                        4,
                        barWidth - BAR_GAP
                    );

                Rectangle left =
                    leftRects[i];

                Rectangle right =
                    rightRects[i];

                left.Width = width;
                right.Width = width;

                left.Height = height;
                right.Height = height;

                Canvas.SetLeft(left, xL);
                Canvas.SetLeft(right, xR);

                Canvas.SetTop(left, y);
                Canvas.SetTop(right, y);
            }
        }
    }
}