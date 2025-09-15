/*
 * Copyright (c) 2025, Justin Linwood Ross
 * VSA Polygraph System v15.1 (Cognitive Core Interface) 
*/
using NAudio.Dsp;
using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FuturisticLieDetector
{
    public partial class Form1 : Form
    {
        #region Fields & Properties
        //  UI & Styling 
        private readonly Color _backColor = Color.FromArgb(5, 10, 20);
        private readonly Color _foreColor = Color.FromArgb(0, 255, 255);
        private readonly Color _accentColor = Color.FromArgb(70, 80, 110);
        private readonly Color _glowColor = Color.FromArgb(0, 192, 192);
        private readonly Color _stressColor = Color.FromArgb(255, 50, 50);
        private readonly Color _truthColor = Color.FromArgb(0, 255, 150);
        private readonly Color _reviewColor = Color.FromArgb(255, 0, 255);
        private readonly Font _mainFont = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
        private readonly Font _titleFont = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point, 0);
        private readonly Font _labelFont = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point, 0);
        private readonly Font _smallLabelFont = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

        //  Form Dragging 
        private Point _lastPoint;

        //  UI Controls 
        private MenuStrip menuStrip;
        private Label lblTitle, lblClose, lblMinimize, lblQuestion, lblStatus, lblVocalStress, lblSpeakerId;
        private FuturisticButton btnStartSession, btnNextQuestion, btnEndSession;
        private GlassPanel pnlPolygraph, pnlStressMeter, pnlQuestionBorder, pnlResults, pnlConfidenceMatrix;
        private Label lblResultOutcome, lblResultSummary;
        private FuturisticButton btnNewSession;
        private ListView lvSessionLog;
        private AnimatedGradientPanel pnlBackground;


        // Advanced UI Controls
        private CognitiveCorePanel pnlCognitiveCore;
        private LiveWaveformPanel pnlLiveWaveform;
        private SpectrogramPanel pnlSpectrogram;
        private VoiceprintPanel pnlQuestionerVoiceprint, pnlSubjectVoiceprint;
        private EmotionalStatePanel pnlEmotionalState;

        // Timers & Logic 
        private readonly Timer _uiUpdateTimer;
        private readonly Timer _pulseTimer;
        private readonly Timer _exitTimer;
        private readonly VocalStressTest _vocalStressTest;

        //  Animations & State 
        private float _pulseAlpha = 0f;
        private bool _isPulsingUp = true;
        private bool _isReviewMode = false;

        // Speech & Audio 
        private SpeechRecognitionEngine _speechEngine;
        private WaveInEvent _waveIn;
        private BufferedWaveProvider _bufferedWaveProvider;
        private readonly SoundPlayer _soundPlayer;
        private readonly AppSettings _settings;
        #endregion

        #region Constructor & Form Setup

        public Form1()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            SetupForm();
            InitializeCustomControls();
            SetupSpeechRecognition();
            SetupAudioCapture();


            // Initialize Timers 
            _uiUpdateTimer = new Timer
            {
                Interval = 30
            };
            _uiUpdateTimer.Tick += UIUpdateTimer_Tick;


            _pulseTimer = new Timer
            { 
                Interval = 40 
            };
            _pulseTimer.Tick += (s, e) =>
            {
                if (_isPulsingUp) _pulseAlpha += 0.08f; 
                else _pulseAlpha -= 0.08f;
                if (_pulseAlpha >= 1f) 
                {
                    _pulseAlpha = 1f;
                    _isPulsingUp = false; 
                }
                if (_pulseAlpha <= 0f) 
                {
                    _pulseAlpha = 0f; 
                    _isPulsingUp = true;
                }
                pnlQuestionBorder.Invalidate();
            };

            _exitTimer = new Timer
            {
                Interval = 4000
            };
            _exitTimer.Tick += (s, e) => 
            { 
                Close();
            };


            // Initialize Core Logic 
            _vocalStressTest = new VocalStressTest(_settings);
            _vocalStressTest.OnDataUpdate += VocalTest_OnDataUpdate;
            _vocalStressTest.OnStateChange += VocalTest_OnStateChange;
            _vocalStressTest.OnCalibrationComplete += VocalTest_OnCalibrationComplete;
            _vocalStressTest.OnMicroExpressionDetected += (description) =>
            {
                LogEvent(new EventLogItem("Micro-expression", description, Color.Magenta));
            };

            _soundPlayer = new SoundPlayer();

            // Final Setup 
            FormClosing += (s, e) => 
            {
                _exitTimer?.Stop();
                _waveIn?.Dispose(); 
                _speechEngine?.Dispose();
                _soundPlayer?.Dispose(); 
                _settings.Save(); 
            };

        }

        private void SetupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = _backColor;
            ForeColor = _foreColor;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1400, 800); 
            Font = _mainFont;
            DoubleBuffered = true;
        }

        private void InitializeCustomControls()
        {
            pnlBackground = new AnimatedGradientPanel
            {
                Dock = DockStyle.Fill,
                Color1 = Color.FromArgb(5, 10, 20),
                Color2 = Color.FromArgb(20, 5, 15)
            };
            //  Layout Definitions 
            const int margin = 20;
            const int padding = 10;
            int formWidth = Width;
            int formHeight = Height;

            //  Menu Strip 
            menuStrip = new MenuStrip
            {
                BackColor = _backColor, 
                ForeColor = _foreColor, 
                Font = new Font("Segoe UI", 10F),
                Renderer = new ToolStripProfessionalRenderer(new CustomColorTable()) 
            };
            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add("New Session", null, (s, e) => 
            { 
                _isReviewMode = false; 
                _vocalStressTest.StartSession(); 
                ResetUI(); 
            });
            fileMenu.DropDownItems.Add("Save Session Archive", null, Menu_SaveSession_Click);
            fileMenu.DropDownItems.Add("Load Session Archive", null, Menu_LoadSession_Click);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Exit", null, (s, e) => Close());
            menuStrip.Items.Add(fileMenu);
            MainMenuStrip = menuStrip;

            //  Custom Title Bar 
            lblTitle = new Label
            { 
                Text = "VSA Polygraph System v15.1 (Cognitive Core)", 
                Font = _titleFont, 
                Location = new Point(margin, menuStrip.Height + 5), 
                AutoSize = true, 
                BackColor = Color.Transparent
            };
            lblClose = new Label
            {
                Text = "X",
                Font = _mainFont,
                ForeColor = _accentColor,
                AutoSize = true, 
                Cursor = Cursors.Hand, 
                BackColor = Color.Transparent, 
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            lblMinimize = new Label
            { 
                Text = "-", Font = new Font("Segoe UI", 14F, FontStyle.Bold), 
                ForeColor = _accentColor, 
                AutoSize = true, 
                Cursor = Cursors.Hand, 
                BackColor = Color.Transparent, 
                Anchor = AnchorStyles.Top | AnchorStyles.Right 
            };
            lblClose.Location = new Point(Width - lblClose.Width - padding, padding / 2);
            lblMinimize.Location = new Point(lblClose.Left - lblMinimize.Width - (padding / 2), padding / 2 - 2);
            lblClose.Click += (s, e) => Close(); 
            lblMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            lblClose.MouseEnter += (s, e) => lblClose.ForeColor = _foreColor;
            lblClose.MouseLeave += (s, e) => lblClose.ForeColor = _accentColor;
            lblMinimize.MouseEnter += (s, e) => lblMinimize.ForeColor = _foreColor;
            lblMinimize.MouseLeave += (s, e) => lblMinimize.ForeColor = _accentColor;
            MouseDown += TitleBar_MouseDown;
            MouseMove += TitleBar_MouseMove;
            lblTitle.MouseDown += TitleBar_MouseDown; 
            lblTitle.MouseMove += TitleBar_MouseMove;

            int currentY = lblTitle.Bottom + padding;
            int rightColumnX = formWidth - 300 - margin;

            // CENTER COLUMN (THE MAIN ANALYSIS)
            int centerColumnWidth = 720;
            int centerColumnX = (formWidth - centerColumnWidth) / 2;

            pnlCognitiveCore = new CognitiveCorePanel
            { 
                Location = new Point(centerColumnX, currentY),
                Size = new Size(centerColumnWidth, 160)
            };
            currentY += pnlCognitiveCore.Height + padding;

            pnlPolygraph = new GlassPanel 
            {
                Location = new Point(centerColumnX, currentY), 
                Size = new Size(centerColumnWidth, 230)
            };
            pnlPolygraph.Paint += PolygraphPanel_Paint;
            currentY += pnlPolygraph.Height + padding;

            pnlLiveWaveform = new LiveWaveformPanel
            {
                Location = new Point(centerColumnX, currentY), 
                Size = new Size(centerColumnWidth, 70) 
            };
            currentY += pnlLiveWaveform.Height + padding;

            pnlSpectrogram = new SpectrogramPanel 
            {
                Location = new Point(centerColumnX, currentY),
                Size = new Size(centerColumnWidth, 70) 
            };
            currentY += pnlSpectrogram.Height + padding;

            pnlQuestionBorder = new GlassPanel
            {
                Location = new Point(centerColumnX, currentY), 
                Size = new Size(centerColumnWidth, 50)
            };
            lblQuestion = new Label
            {
                Text = "Press 'Start Session' to begin calibration.",
                Font = _mainFont, 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleCenter, 
                ForeColor = _foreColor, 
                BackColor = Color.Transparent 
            };
            pnlQuestionBorder.Controls.Add(lblQuestion);
            currentY += pnlQuestionBorder.Height + padding;

            btnStartSession = new FuturisticButton 
            {
                Text = "Start Session", 
                Location = new Point(centerColumnX, currentY), 
                Size = new Size(180, 50)
            };
            btnNextQuestion = new FuturisticButton 
            { 
                Text = "Next Question",
                Location = new Point(btnStartSession.Right + padding, currentY),
                Size = new Size(180, 50)
            };
            btnEndSession = new FuturisticButton
            { 
                Text = "End Session", 
                Location = new Point(btnNextQuestion.Right + padding, currentY), 
                Size = new Size(180, 50)
            };
            btnStartSession.Click += BtnStartSession_Click; 
            btnNextQuestion.Click += BtnNextQuestion_Click; 
            btnEndSession.Click += BtnEndSession_Click;

            //  LEFT COLUMN 
            int leftColumnWidth = 300;
            int leftColumnX = margin;
            currentY = lblTitle.Bottom + padding;

            var lblVoiceprintTitle = CreateGraphLabel("VOICEPRINT SIGNATURES", new Point(leftColumnX, currentY));
            currentY += lblVoiceprintTitle.Height + 5;
            pnlQuestionerVoiceprint = new VoiceprintPanel 
            {
                Location = new Point(leftColumnX, currentY),
                Size = new Size(145, 140), Title = "Questioner"
            };
            pnlSubjectVoiceprint = new VoiceprintPanel 
            {
                Location = new Point(pnlQuestionerVoiceprint.Right + padding, currentY),
                Size = new Size(145, 140), Title = "Subject" 
            };
            currentY += pnlQuestionerVoiceprint.Height + padding;

            var lblConfidenceTitle = CreateGraphLabel("SPEAKER CONFIDENCE MATRIX", 
                new Point(leftColumnX, currentY));
            currentY += lblConfidenceTitle.Height + 5;
            pnlConfidenceMatrix = new GlassPanel
            {
                Location = new Point(leftColumnX, currentY), 
                Size = new Size(leftColumnWidth, 90) 
            };
            pnlConfidenceMatrix.Paint += ConfidenceMatrix_Paint;
            currentY += pnlConfidenceMatrix.Height + padding;

            var lblEmotionalStateTitle = CreateGraphLabel("EMOTIONAL STATE ANALYSIS", new Point(leftColumnX, currentY));
            currentY += lblEmotionalStateTitle.Height + 5;
            pnlEmotionalState = new EmotionalStatePanel
            {
                Location = new Point(leftColumnX, currentY), 
                Size = new Size(leftColumnWidth, leftColumnWidth + 20) 
            };


            // RIGHT COLUMN 
            currentY = lblTitle.Bottom + padding;
            int rightColumnWidth = formWidth - pnlCognitiveCore.Right - margin - margin;

            lvSessionLog = new ListView 
            { 
                Location = new Point(pnlCognitiveCore.Right + margin, currentY), 
                Size = new Size(rightColumnWidth, formHeight - currentY - 140), 
                View = View.Details, BackColor = Color.FromArgb(20, 25, 45), 
                ForeColor = _foreColor, Font = new Font("Consolas", 9.5f), 
                BorderStyle = BorderStyle.None, 
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                FullRowSelect = true 
            };
            lvSessionLog.Columns.Add("Time", 80);
            lvSessionLog.Columns.Add("Event", 100);
            lvSessionLog.Columns.Add("Details", 180);
            lvSessionLog.MouseDoubleClick += LvSessionLog_MouseDoubleClick;

            lblStatus = CreateGraphLabel("STATUS: IDLE", 
                new Point(lvSessionLog.Left, lvSessionLog.Bottom + padding));
            lblVocalStress = CreateGraphLabel("Subject Vocal Stress (µt): 0.00", 
                new Point(lvSessionLog.Left, lblStatus.Bottom + 5));
            lblSpeakerId = CreateGraphLabel("SPEAKER ID: ---", 
                new Point(lvSessionLog.Left, lblVocalStress.Bottom + 5));

            pnlStressMeter = new GlassPanel
            { 
                Location = new Point(lvSessionLog.Left,
                lblSpeakerId.Bottom + padding),
                Size = new Size(rightColumnWidth, 40) 
            };
            pnlStressMeter.Paint += StressMeter_Paint;


            // Results Panel (Overlay)
            pnlResults = new GlassPanel 
            { 
                Location = new Point(0, 0), 
                Size = Size, Visible = false, 
                Anchor = AnchorStyles.Top | 
                AnchorStyles.Bottom | 
                AnchorStyles.Left | 
                AnchorStyles.Right 
            };
            lblResultOutcome = new Label 
            {
                Text = "DECEPTIVE",
                Font = new Font("Segoe UI", 48F, FontStyle.Bold), 
                ForeColor = _stressColor, 
                AutoSize = true, 
                BackColor = Color.Transparent };
            lblResultSummary = new Label
            { 
                Text = "Summary of findings...", 
                Font = _mainFont, 
                ForeColor = Color.White, TextAlign = ContentAlignment.TopCenter, 
                Size = new Size(500, 100),
                BackColor = Color.Transparent 
            };
            btnNewSession = new FuturisticButton 
            { 
                Text = "New Session",
                Size = new Size(220, 60)
            };
            btnNewSession.Click += BtnNewSession_Click;
            lblResultOutcome.Location = new Point((pnlResults.Width - lblResultOutcome.Width) / 2,
                (pnlResults.Height / 2) - 150);
            lblResultSummary.Location = new Point((pnlResults.Width - lblResultSummary.Width) / 2,
                lblResultOutcome.Bottom + padding);
            btnNewSession.Location = new Point((pnlResults.Width - btnNewSession.Width) / 2, 
                lblResultSummary.Bottom + margin);
            pnlResults.Controls.AddRange(new Control[] 
            { 
                lblResultOutcome, 
                lblResultSummary, 
                btnNewSession 
            });


            // Add all controls to form
            Controls.Add(pnlBackground);
            pnlBackground.Controls.AddRange(new Control[] 
            {
                menuStrip,
                lblTitle, 
                lblClose,
                lblMinimize,
                pnlQuestionBorder,
                lblStatus,
                lblSpeakerId,
                pnlPolygraph,
                btnStartSession, 
                btnNextQuestion, 
                btnEndSession, 
                lvSessionLog,
                pnlResults,
                pnlConfidenceMatrix, 
                lblConfidenceTitle, 
                pnlSpectrogram,
                pnlQuestionerVoiceprint,
                pnlSubjectVoiceprint, 
                lblVoiceprintTitle, 
                pnlEmotionalState, 
                lblEmotionalStateTitle,
                pnlCognitiveCore,
                pnlLiveWaveform, 
                lblVocalStress,
                pnlStressMeter 
            });
            ResetUI();
        }


        private void SetupSpeechRecognition()
        {
            try
            { 
                _speechEngine = new SpeechRecognitionEngine();
                _speechEngine.SetInputToDefaultAudioDevice(); 
                var responses = new Choices("yes", "no"); 
                var gb = new GrammarBuilder(responses); 
                var g = new Grammar(gb);
                _speechEngine.LoadGrammar(g);
                _speechEngine.SpeechRecognized += SpeechEngine_SpeechRecognized; 
            } 
            catch (Exception ex) 
            {
                ShowError($"Failed to initialize speech recognition: {ex.Message}\nPlease ensure a microphone is connected."); 
            } 
        }
        private void SetupAudioCapture() 
        { 
            try 
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(_settings.SampleRate, 1), 
                    DeviceNumber = _settings.AudioDeviceIndex 
                }; 
                _waveIn.DataAvailable += WaveIn_DataAvailable; 
                _bufferedWaveProvider = new BufferedWaveProvider(_waveIn.WaveFormat)
                {
                    DiscardOnBufferOverflow = true 
                };
            } 
            catch (Exception ex)
            {
                ShowError($"Failed to initialize audio capture: " +
                    $"{ex.Message}\nNo microphone detected or configured device is unavailable."); 
            }
        }

        #endregion

        #region Session Management
        private void Menu_SaveSession_Click(object sender, EventArgs e)
        {
            if (_vocalStressTest.CurrentSession == null || _isReviewMode)
            {
                MessageBox.Show("A live session must be in progress or completed to save.", "Save Error", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "VSA Session Archive (*.vsa)|*.vsa";
                sfd.Title = "Save Session Archive";
                sfd.FileName = $"Session_{DateTime.Now:yyyyMMdd_HHmm}.vsa";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = JsonConvert.SerializeObject(_vocalStressTest.CurrentSession, Formatting.Indented);
                        File.WriteAllText(sfd.FileName, json);
                        LogEvent(new EventLogItem("System", "Session archive saved.", Color.LimeGreen));
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to save session: {ex.Message}");
                    }
                }
            }
        }

        private void Menu_LoadSession_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "VSA Session Archive (*.vsa)|*.vsa";
                ofd.Title = "Load Session Archive";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(ofd.FileName);
                        var session = JsonConvert.DeserializeObject<SessionRecord>(json);
                        LoadSessionIntoUI(session);
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to load session: {ex.Message}");
                    }
                }
            }
        }

        private void LoadSessionIntoUI(SessionRecord session)
        {
            _isReviewMode = true;
            ResetUI();
            _vocalStressTest.LoadSession(session);

            lblStatus.Text = "STATUS: REVIEW MODE";
            lblStatus.ForeColor = _reviewColor;
            lblQuestion.Text = $"Reviewing Session from {session.SessionDate:g}";
            btnStartSession.Enabled = false;
            btnNextQuestion.Enabled = false;
            btnEndSession.Enabled = false;

            pnlQuestionerVoiceprint.UpdateSignature(session.QuestionerSignature);
            pnlSubjectVoiceprint.UpdateSignature(session.SubjectSignature);
            pnlEmotionalState.Clear();
            pnlSpectrogram.Clear();
            pnlLiveWaveform.Clear();

            lvSessionLog.Items.Clear();
            foreach (var log in session.EventLog)
            {
                LogEvent(log, log.Tag as QuestionLog);
            }
            MessageBox.Show("Session loaded in Review Mode.\nDouble-click a question in the Session Log to play back the audio.", 
                "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LvSessionLog_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (!_isReviewMode || lvSessionLog.SelectedItems.Count == 0) 
                return;

            var selectedItem = lvSessionLog.SelectedItems[0];
            if (selectedItem.Tag is QuestionLog questionLog && questionLog.AnswerAudio != null)
            {
                _soundPlayer.PlayAudio(questionLog.AnswerAudio, _waveIn.WaveFormat);
                lblQuestion.Text = $"PLAYING AUDIO FOR: {questionLog.QuestionText}";

                pnlEmotionalState.UpdateState(questionLog.AnalysisResult.EmotionalState);
                _vocalStressTest.LoadQuestionDataForReview(questionLog);
                pnlPolygraph.Invalidate();
            }
        }
        #endregion

        #region Custom Control Creation
        private Label CreateGraphLabel(string text, Point location) 
        { 
            return new Label
            { 
                Text = text, Font = _labelFont,
                Location = location, 
                AutoSize = true, 
                BackColor = Color.Transparent 
            }; 
        }
        #endregion

        #region Event Handlers & UI Updates
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (_bufferedWaveProvider == null || _isReviewMode) return;
            _bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

            byte[] bufferCopy = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, bufferCopy, e.BytesRecorded);

            pnlLiveWaveform.AddSample(bufferCopy);
            Task.Run(() =>
            {
                if (_vocalStressTest.IsCalibrating) _vocalStressTest.Calibrate(bufferCopy);
                else _vocalStressTest.ProcessLiveAudio(bufferCopy);
            });
        }

        private void BtnStartSession_Click(object sender, EventArgs e)
        {
            _isReviewMode = false;
            try
            {
                ResetUI();
                _vocalStressTest.StartSession();
                btnStartSession.Enabled = false; 
                btnEndSession.Enabled = true;
                _waveIn?.StartRecording();
                _uiUpdateTimer.Start(); 
            }
            catch (Exception ex)
            { 
                ShowError("Failed to start session: " + ex.Message);
            }
        }
        private void BtnNextQuestion_Click(object sender, EventArgs e) 
        {
            try
            {
                var question = _vocalStressTest.AskNextQuestion();
                if (question != null) { lblQuestion.Text = question;
                    LogEvent(new EventLogItem("Question", question, Color.Gray));
                    lblStatus.Text = "STATUS: LISTENING FOR SUBJECT..."; 
                    lblStatus.ForeColor = Color.Aqua;
                    btnNextQuestion.Enabled = false;
                    SetListeningIndicator(true); 
                    _bufferedWaveProvider.ClearBuffer();
                    _waveIn?.StartRecording();
                    _speechEngine?.RecognizeAsync(RecognizeMode.Single);
                } 
                else 
                { 
                    lblQuestion.Text = "All questions have been asked. Press 'End Session' to see the results."; 
                    btnNextQuestion.Enabled = false; 
                    SetListeningIndicator(false);
                } 
            } 
            catch (Exception ex) 
            { 
                ShowError("Error fetching next question: " + ex.Message);
            } 
        }
        private async void BtnEndSession_Click(object sender, EventArgs e)
        {
            try
            {
                SetListeningIndicator(false);
                _uiUpdateTimer.Stop();
                _waveIn?.StopRecording();
                _speechEngine?.RecognizeAsyncCancel();
                var resultData = await _vocalStressTest.EndSessionAsync();

                lblResultOutcome.Text = resultData.Result.ToString();
                lblResultSummary.Text = resultData.Summary;
                Color resultColor = Color.Yellow;
                switch (resultData.Result)
                {
                    case TestResult.TRUTHFUL: resultColor = _truthColor; 
                        break;
                    case TestResult.DECEPTIVE: resultColor = _stressColor;
                        break;
                }
                lblResultOutcome.ForeColor = resultColor;
                lblResultOutcome.Location = new Point((pnlResults.Width - lblResultOutcome.Width) / 2, 100);
                LogEvent(new EventLogItem("Session End", $"Final Analysis: {resultData.Result}", resultColor));


                pnlResults.Visible = true;
                pnlResults.BringToFront();
                _exitTimer.Start();
            }
            catch (Exception ex)
            {
                ShowError("Error ending session: " + ex.Message);
            }
        }

        private void BtnNewSession_Click(object sender, EventArgs e)
        {
            _exitTimer.Stop();
            _isReviewMode = false;
            pnlResults.Visible = false;
            _vocalStressTest.StartSession();
            ResetUI();
        }

        private async void SpeechEngine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            _waveIn?.StopRecording();
            if (e.Result == null || string.IsNullOrEmpty(e.Result.Text) || _bufferedWaveProvider.BufferedBytes == 0) 
                return;
            string response = e.Result.Text.ToLower();
            byte[] recordedAudio = new byte[_bufferedWaveProvider.BufferedBytes];
            _bufferedWaveProvider.Read(recordedAudio, 0, recordedAudio.Length);
            var speaker = await _vocalStressTest.IdentifySpeakerAsync(recordedAudio);

            Invoke(new Action(() =>
            {
                lblSpeakerId.Text = $"SPEAKER ID: {speaker.Type} (Confidence: {speaker.Confidence:P1})";
                LogEvent(new EventLogItem("Voice ID", $"Speaker identified as {speaker.Type}"));
                if (speaker.Type == SpeakerType.Subject)
                {
                    SetListeningIndicator(false);
                    lblStatus.Text = $"SUBJECT RESPONSE: '{response.ToUpper()}' - ANALYZING...";
                    _vocalStressTest.ProcessAnswerAsync(response, recordedAudio).ContinueWith(task =>
                    {
                        var analysisResult = task.Result;
                        Color stressColor = analysisResult.StressLevel > _vocalStressTest.GetStressThreshold() * 1.5 ? _stressColor : _foreColor;
                        LogEvent(new EventLogItem("Subject Answer", $"'{response.ToUpper()}' | Peak Stress: " +
                            $"{analysisResult.StressLevel:F2}", stressColor), 
                            analysisResult.ParentQuestion);

                        Invoke(new Action(() =>
                        {
                            btnNextQuestion.Enabled = !_vocalStressTest.IsTestFinished();
                            if (_vocalStressTest.IsTestFinished()) 
                            {
                                lblQuestion.Text = "All questions asked. Press 'End Session'."; }
                        }));
                    });

                }
                else
                {
                    lblStatus.Text = "Questioner detected. Awaiting subject response...";
                    _bufferedWaveProvider.ClearBuffer();
                    _waveIn?.StartRecording();
                    _speechEngine?.RecognizeAsync(RecognizeMode.Single);
                }
            }));
        }

        private void VocalTest_OnDataUpdate(VocalAnalysisData data) 
        { 
            if (InvokeRequired) 
            { Invoke(new Action(() => UpdateUIData(data)));
                return; 
            } UpdateUIData(data); 
        }
        private void UpdateUIData(VocalAnalysisData data) 
        { 
            if (_isReviewMode) 
                return;
            lblVocalStress.Text = $"Subject Vocal Stress (µt): {data.VocalStressLevel:F2}"; 
            pnlSpectrogram.UpdateData(data.LatestFft); 
            pnlEmotionalState.UpdateState(data.EmotionalState);
            pnlCognitiveCore.StressLevel = data.VocalStressLevel; 
        }
        private void VocalTest_OnStateChange(string newStateMessage, Color color) 
        { 
            if (InvokeRequired)
            { 
                Invoke(new Action(() => UpdateStateUI(newStateMessage, color)));
                return;
            }
            UpdateStateUI(newStateMessage, color);
        }
        private void UpdateStateUI(string message, Color color)
        {
            lblQuestion.Text = message; lblStatus.ForeColor = color;
            if (message.ToLower().Contains("calibrating")) 
                LogEvent(new EventLogItem("Calibration", message.Split(',')[0], color));
            if (_vocalStressTest.CurrentState == SessionState.InProgress && !btnNextQuestion.Enabled) 
            {
                _waveIn?.StopRecording();
                lblStatus.Text = "STATUS: IN PROGRESS"; 
                lblStatus.ForeColor = _foreColor;
                lblQuestion.Text = "Calibration complete. Press 'Next Question' to proceed.";
                LogEvent(new EventLogItem("Calibration", "All calibrations complete.", _truthColor));
                btnNextQuestion.Enabled = true;
            }
        }

        private void VocalTest_OnCalibrationComplete(SpeakerType speaker, VoiceSignature signature)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnCalibrationCompleteUI(speaker, signature)));
                return;
            }
            OnCalibrationCompleteUI(speaker, signature);
        }

        private void OnCalibrationCompleteUI(SpeakerType speaker, VoiceSignature signature)
        { 
            _bufferedWaveProvider.ClearBuffer();
            if (speaker == SpeakerType.Questioner) pnlQuestionerVoiceprint.UpdateSignature(signature);
            else if (speaker == SpeakerType.Subject) pnlSubjectVoiceprint.UpdateSignature(signature); 
        }
        private void UIUpdateTimer_Tick(object sender, EventArgs e) 
        { if (_isReviewMode)
                return;
            _vocalStressTest.Update();
            pnlPolygraph.Invalidate(); 
            pnlStressMeter.Invalidate(); 
            pnlConfidenceMatrix.Invalidate();
            pnlLiveWaveform.Invalidate(); 
            pnlCognitiveCore.Invalidate();
        }
        #endregion

        #region Drawing Methods
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Background is handled by the AnimatedGradientPanel
            base.OnPaintBackground(e);
        }

        private void PolygraphPanel_Paint(object sender, PaintEventArgs e) 
        {
            if (_vocalStressTest == null) 
                return;
            var g = e.Graphics; 
            g.Clear(Color.Transparent); 
            DrawSingleGraph(g, new Rectangle(0, 0, pnlPolygraph.Width, 75), 
                _vocalStressTest.StressHistory.ToList(), _stressColor, _accentColor, "STRESS");
            DrawSingleGraph(g, new Rectangle(0, 80, pnlPolygraph.Width, 75), 
                _vocalStressTest.PitchHistory.ToList(), _truthColor, _accentColor, "PITCH (Hz)"); 
            DrawSingleGraph(g, new Rectangle(0, 160, pnlPolygraph.Width, 70),
                _vocalStressTest.TimbreHistory.ToList(), Color.Cyan, _accentColor, "TIMBRE (Centroid Hz)");
        }
        private void ConfidenceMatrix_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; 
            g.Clear(Color.Transparent); 
            var speakerConf = _vocalStressTest.LastIdentification; 
            string[] labels = 
                {
                "RMS",
                "PITCH", 
                "TIMBRE" 
            }; 
            double[] qConf = 
                {
                speakerConf.RmsConfidence[0], 
                speakerConf.PitchConfidence[0], 
                speakerConf.TimbreConfidence[0] 
            }; 
            double[] sConf =
                { 
                speakerConf.RmsConfidence[1],
                speakerConf.PitchConfidence[1], 
                speakerConf.TimbreConfidence[1] 
            };
            var rect = pnlConfidenceMatrix.ClientRectangle; rect.Inflate(-10, -10);
            int itemHeight = rect.Height / 3;
            using (var qBrush = new SolidBrush(Color.FromArgb(150, Color.LightBlue)))
            using (var sBrush = new SolidBrush(Color.FromArgb(150, Color.Orange)))
            using (var labelBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
            using (var font = new Font("Consolas", 10f))
            {
                for (int i = 0;
                    i < 3; i++)
                {
                    int y = rect.Top + i * itemHeight;
                    g.DrawString(labels[i], font, labelBrush, rect.Left, y);
                    Rectangle barRect = new Rectangle(rect.Left + 50, y, rect.Width - 55, font.Height);
                    int splitPoint = (int)(barRect.Width * qConf[i]);
                    g.FillRectangle(qBrush, barRect.Left, y, splitPoint, barRect.Height);
                    g.FillRectangle(sBrush, barRect.Left + splitPoint, y, barRect.Width - splitPoint, barRect.Height);
                }
            }
        }
        private void DrawSingleGraph(Graphics g, Rectangle bounds, List<DataPoint> data, Color lineColor, Color gradientColor, string label = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var gridPen = new Pen(Color.FromArgb(30, _foreColor)))
            { 
                for (int i = 1;
                    i < 4; i++) g.DrawLine(gridPen,
                    bounds.Left, 
                    bounds.Top + i * bounds.Height / 4, 
                    bounds.Right, 
                    bounds.Top + i * bounds.Height / 4); 
                for (int i = 1;
                    i < 10;
                    i++) g.DrawLine(gridPen, 
                    bounds.Left + i * bounds.Width / 10,
                    bounds.Top, 
                    bounds.Left + i * bounds.Width / 10, 
                    bounds.Bottom); 
            }
            if (data == null || data.Count < 2) 
            {
                if (label != null) g.DrawString(label, _labelFont, new SolidBrush(gradientColor), 5, bounds.Top + 5);
                return;
            }
            float min = data.Min(d => d.Value);
            float max = data.Max(d => d.Value);
            if (label != null && label == "STRESS") 
            { 
                min = 0; 
                max = Math.Max(10f, max);
            } 
            else 
            if (max - min < 50) 
            { 
                float mid = (max + min) / 2; 
                min = mid - 25; 
                max = mid + 25;
            }
            if (min == max) max += 1;
            float range = (max - min);
            var path = new GraphicsPath();
            var points = new List<PointF>();
            for (int i = 0; i < data.Count; i++)
            {
                float x = (float)i / (data.Count - 1) * bounds.Width;
                float y = bounds.Bottom - ((data[i].Value - min) / range * (bounds.Height - 10) + 5);
                points.Add(new PointF(x, y));
            }
            if (points.Count > 1) path.AddCurve(points.ToArray());

            using (var linePen = new Pen(lineColor, 2f))
                g.DrawPath(linePen, path);
            points.Add(new PointF(bounds.Right, bounds.Bottom));
            points.Add(new PointF(bounds.Left, bounds.Bottom));
            using (var gradientBrush = new LinearGradientBrush(bounds, 
                Color.FromArgb(120, gradientColor),
                Color.FromArgb(0, gradientColor), 90f)) 
                g.FillPolygon(gradientBrush, points.ToArray());

            if (label != null) g.DrawString(label, _labelFont, new SolidBrush(Color.White), 5, bounds.Top + 5);
        }

        private void StressMeter_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; 
            float peakStress = _vocalStressTest.PeakStressLevel; 
            float maxStress = 20.0f; 
            float stressPercent = Math.Min(peakStress / maxStress, 1.0f);
            var rect = pnlStressMeter.ClientRectangle;
            rect.Inflate(-1, -1);
            using (var backBrush = new SolidBrush(Color.FromArgb(30, 30, 50)))
            {
                g.FillRectangle(backBrush, rect);
            }
            int barWidth = (int)(rect.Width * stressPercent);
            if (barWidth > 0)
            {
                Rectangle barRect = new Rectangle(rect.X, rect.Y, barWidth, rect.Height);
                Color barColor = stressPercent > 0.7f ? 
                    _stressColor : stressPercent > 0.4f ? Color.Orange : 
                    _truthColor;
                using (var barBrush = new LinearGradientBrush(barRect, Color.FromArgb(150, barColor), 
                    barColor, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(barBrush, barRect);
                }
            }
            using (var borderPen = new Pen(_accentColor, 1)) 
            {
                g.DrawRectangle(borderPen, rect); 
            }
            TextRenderer.DrawText(g, $"PEAK STRESS: {stressPercent:P0}", 
                _smallLabelFont, rect, 
                _foreColor, 
                TextFormatFlags.HorizontalCenter | 
                TextFormatFlags.VerticalCenter);
        }
        #endregion

        #region Helpers
        private void ResetUI() 
        {
            lvSessionLog.Items.Clear();
            lblStatus.Text = "STATUS: IDLE";
            lblStatus.ForeColor = Color.Yellow; 
            lblQuestion.Text = "Select 'File > New Session' to begin."; 
            btnStartSession.Enabled = true; 
            btnNextQuestion.Enabled = false;
            btnEndSession.Enabled = false; 
            pnlSpectrogram.Clear(); 
            pnlEmotionalState.Clear();
            pnlQuestionerVoiceprint.Clear();
            pnlSubjectVoiceprint.Clear();
            pnlLiveWaveform.Clear();
            pnlCognitiveCore.StressLevel = 0; 
        }
        private void LogEvent(EventLogItem logItem, QuestionLog tag = null) 
        { 
            if (lvSessionLog.InvokeRequired) 
            { 
                lvSessionLog.Invoke(new Action(() => LogEvent(logItem, tag))); 
                return; 
            } 
            var item = new ListViewItem(new[] 
            { 
                logItem.Timestamp.ToString("HH:mm:ss"), 
                logItem.EventType, 
                logItem.Details })
            { 
                ForeColor = ColorTranslator.FromHtml(logItem.ColorHtml) 
            }; 
            if (tag != null)
            { 
                item.Tag = tag;
                logItem.Tag = tag;
            } 
            lvSessionLog.Items.Add(item); 
            lvSessionLog.Items[lvSessionLog.Items.Count - 1].EnsureVisible(); 
        }
        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            _lastPoint = new Point(e.X, e.Y);
        }
        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) 
            {
                Left += e.X - 
                    _lastPoint.X; 
                Top += e.Y - 
                    _lastPoint.Y; 
            } 
        }
        private void ShowError(string message)
        { 
            MessageBox.Show(message, "Application Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void SetListeningIndicator(bool active) 
        { 
            if (active) _pulseTimer.Start();
            else 
            {
                _pulseTimer.Stop();
                _pulseAlpha = 0f;
                pnlQuestionBorder.Invalidate();
            }
        }
        #endregion

    } // End of Form1 class

    #region AppSettings
    public class AppSettings
    {
        public int AudioDeviceIndex { get; set; } = 0;
        public int SampleRate { get; set; } = 16000;
        public double StressThreshold { get; set; } = 1.5;

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");
        }
        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(GetConfigPath(), json);
            }
            catch
            { /* Failed to save settings, ignore */ }
        }
        public static AppSettings Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<AppSettings>(json);
                }
            }
            catch 
            { /* Failed to load, return default */ }
            return new AppSettings();
        }
    }
    #endregion
    #region Custom Controls, Animation & Helper Classes
    public class AnimatedGradientPanel : Panel
    {
        public Color Color1 { get; set; }
        public Color Color2 { get; set; }
        private float angle = 0;
        private readonly Timer timer;

        public AnimatedGradientPanel()
        {
            DoubleBuffered = true;
            timer = new Timer
            {
                Interval = 40
            };
            timer.Tick += (s, e) =>
            {
                angle = (angle + 0.5f) % 360;
                Invalidate();
            };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var brush = new LinearGradientBrush(ClientRectangle, Color1, Color2, angle))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }
    public class GlassPanel : Panel
    {
        public GlassPanel()
        {
            BackColor = Color.Transparent;
            DoubleBuffered = true; 
            Margin = new Padding(0);
        }
        protected override void OnPaint(PaintEventArgs e) 
        { 
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; 
            using (var brush = new SolidBrush(Color.FromArgb(50, 80, 90, 120))) 
            using (var path = GetRoundRectangle(ClientRectangle, 10)) 
            {
                e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(90, 0, 255, 255), 1)) e.Graphics.DrawPath(pen, path);
            }
        }
        protected GraphicsPath GetRoundRectangle(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath(); int diameter = radius * 2;
            if (diameter <= 0) 
            { path.AddRectangle(rectangle); 
                return path;
            }
            Rectangle arcRectTopLeft = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);
            Rectangle arcRectTopRight = new Rectangle(rectangle.Right - diameter, rectangle.Y, diameter, diameter); 
            Rectangle arcRectBottomRight = new Rectangle(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter);
            Rectangle arcRectBottomLeft = new Rectangle(rectangle.X, rectangle.Bottom - diameter, diameter, diameter);
            path.AddArc(arcRectTopLeft, 180, 90); 
            path.AddArc(arcRectTopRight, 270, 90);
            path.AddArc(arcRectBottomRight, 0, 90); 
            path.AddArc(arcRectBottomLeft, 90, 90); 
            path.CloseFigure(); return path;
        }
    }
    public class FuturisticButton : Button
    {
        private bool _isHovering = false;
        public FuturisticButton() 
        { 
            Size = new Size(200, 60); 
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0; 
            BackColor = Color.Transparent;
            ForeColor = Color.FromArgb(0, 255, 255); 
            Font = new Font("Segoe UI", 12F); 
            DoubleBuffered = true; 
        }
        protected override void OnMouseEnter(EventArgs e) 
        {
            base.OnMouseEnter(e);
            _isHovering = true;
            Invalidate(); 
        }
        protected override void OnMouseLeave(EventArgs e)
        { 
            base.OnMouseLeave(e); 
            _isHovering = false;
            Invalidate(); 
        }
        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            rect.Inflate(-1, -1);
            using (var path = GetRoundRectangle(rect, 8))
            {
                Color start = Color.FromArgb(70, 80, 110), end = Color.FromArgb(40, 50, 80);
                if (_isHovering) 
                { 
                    start = Color.FromArgb(90, 100, 130); 
                }
                using (var brush = new LinearGradientBrush(rect, start, end, 90f)) pevent.Graphics.FillPath(brush, path);
                using (var pen = new Pen(_isHovering ? 
                    Color.FromArgb(0, 255, 255) : 
                    Color.FromArgb(150, 0, 192, 192), 1.5f)) pevent.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(pevent.Graphics, Text, Font, rect, ForeColor, 
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter);
        }
        private GraphicsPath GetRoundRectangle(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath(); 
            int diameter = radius * 2;
            Rectangle arcRectTopLeft = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter); 
            Rectangle arcRectTopRight = new Rectangle(rectangle.Right - diameter, rectangle.Y, diameter, diameter);
            Rectangle arcRectBottomRight = new Rectangle(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter); 
            Rectangle arcRectBottomLeft = new Rectangle(rectangle.X, rectangle.Bottom - diameter, diameter, diameter);
            path.AddArc(arcRectTopLeft, 180, 90);
            path.AddArc(arcRectTopRight, 270, 90); 
            path.AddArc(arcRectBottomRight, 0, 90);
            path.AddArc(arcRectBottomLeft, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
    public class CustomColorTable : ProfessionalColorTable
    { 
        public override Color MenuItemBorder => Color.FromArgb(0, 255, 255);
        public override Color MenuItemSelected => Color.FromArgb(70, 80, 110);
        public override Color MenuBorder => Color.Black;
        public override Color ToolStripDropDownBackground => Color.FromArgb(20, 25, 40); 
        public override Color ImageMarginGradientBegin => Color.FromArgb(20, 25, 40);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(20, 25, 40);
        public override Color ImageMarginGradientEnd => Color.FromArgb(20, 25, 40); 
    }


    public class CognitiveCorePanel : GlassPanel
    {
        private float _animationPhase = 0;
        private readonly Timer _animationTimer;
        public float StressLevel { get; set; }

        public CognitiveCorePanel()
        {
            _animationTimer = new Timer
            {
                Interval = 20
            };
            _animationTimer.Tick += (s, e) => 
            {
                _animationPhase += 0.02f;
                Invalidate();
            };
            _animationTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var center = new PointF(Width / 2f, Height / 2f);
            float stressFactor = 1f + Math.Min(StressLevel / 10f, 1f);
            float baseRadius = Math.Min(Width, Height) / 4f;

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(center.X - baseRadius * 3, center.Y - baseRadius * 3, baseRadius * 6, baseRadius * 6);
                using (var pgb = new PathGradientBrush(path))
                {
                    // Clamp the color values to the valid 0-255 range.
                    int redValue = (int)(StressLevel * 10);
                    int greenValue = 255 - redValue;

                    redValue = Math.Max(0, Math.Min(255, redValue));
                    greenValue = Math.Max(0, Math.Min(255, greenValue));

                    pgb.CenterColor = Color.FromArgb(100, redValue, greenValue, 255);
                    pgb.SurroundColors = new[] 
                    {
                        Color.Transparent
                    };
                    g.FillEllipse(pgb, center.X - baseRadius * 3, center.Y - baseRadius * 3, baseRadius * 6, baseRadius * 6);
                }
            }

            for (int i = 5; i > 0; i--)
            {
                float phase = _animationPhase * i * 0.5f;
                float radius = baseRadius * (1f + 0.15f * i) + (float)Math.Sin(phase) * 5f * stressFactor;
                int alpha = (int)(150 - i * 25 - (StressLevel * 5));
                if (alpha < 10) alpha = 10;
                Color color = i == 1 ? 
                    Color.FromArgb(200, 255, 50, 50) : 
                    Color.FromArgb(alpha, 0, 255, 255);
                using (var pen = new Pen(color, 1.5f + (i == 1 ? stressFactor : 0)))
                {
                    g.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                }
            }
            TextRenderer.DrawText(g, "COGNITIVE CORE", new Font("Consolas", 12f), new Point(10, 10), Color.White);
        }
    }
    public class LiveWaveformPanel : GlassPanel
    {
        private readonly Queue<short> _samples = new Queue<short>();
        private readonly object _lockObject = new object();
        private const int MaxSamples = 512;

        public void AddSample(byte[] buffer)
        {
            lock (_lockObject)
            {
                for (int i = 0; i < buffer.Length - 1; i += 2)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    _samples.Enqueue(sample);
                }
                while (_samples.Count > MaxSamples)
                {
                    _samples.Dequeue();
                }
            }
        }

        public void Clear()
        {
            lock (_lockObject)
            {
                _samples.Clear();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            short[] sampleArray;
            lock (_lockObject)
            {
                if (_samples.Count == 0) 
                    return;
                sampleArray = _samples.ToArray();
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var points = new PointF[sampleArray.Length];
            float midY = ClientRectangle.Height / 2f;

            for (int i = 0; i < sampleArray.Length; i++)
            {
                float x = (float)i / (sampleArray.Length - 1) *
                    ClientRectangle.Width;
                float y = midY - (sampleArray[i] / (float)short.MaxValue * midY);
                points[i] = new PointF(x, y);
            }

            if (points.Length > 1)
            {
                using (var pen = new Pen(Color.FromArgb(0, 255, 255), 1.5f))
                {
                    g.DrawLines(pen, points);
                }
            }
        }
    }
    public class SpectrogramPanel : GlassPanel 
    {
        private readonly List<float[]> _fftData = new List<float[]>(); 
        private const int FftHistoryCount = 128; 
        public void UpdateData(Complex[] fftResult) 
        {
            if (fftResult == null) return; 
            var magnitudes = new float[fftResult.Length / 2]; 
            for (int i = 0;
                i < magnitudes.Length; i++)
            {
                magnitudes[i] = (float)Math.Log10(Math.Sqrt(fftResult[i].X * fftResult[i].X + fftResult[i].Y * fftResult[i].Y) + 1);
            }
            _fftData.Add(magnitudes); 
            while
                (_fftData.Count > FftHistoryCount) _fftData.RemoveAt(0); 
            Invalidate(); 
        } 
        public void Clear() 
        {
            _fftData.Clear();
            Invalidate(); 
        }
        protected override void OnPaint(PaintEventArgs e) 
        { 
            base.OnPaint(e); 
            if (_fftData.Count == 0) 
                return; 
            var g = e.Graphics; 
            int columnWidth = Math.Max(1, ClientRectangle.Width / FftHistoryCount); 
            for (int x = 0; 
                x < _fftData.Count; x++)
            {
                var magnitudes = _fftData[x]; 
                if (magnitudes == null) 
                    continue; 
                int rectHeight = ClientRectangle.Height / magnitudes.Length; 
                for (int y = 0; y < magnitudes.Length; y++)
                { 
                    float magnitude = Math.Min(1.0f, magnitudes[y] / 2.0f); 
                    if (magnitude < 0.1) 
                        continue;
                    Color color = GetColorForMagnitude(magnitude); 
                    using (var brush = new SolidBrush(color)) 
                    { 
                        g.FillRectangle(brush, ClientRectangle.Width - ((x + 1) * columnWidth),
                            ClientRectangle.Bottom - (int)(((float)y / magnitudes.Length) * 
                            ClientRectangle.Height) - rectHeight, columnWidth, rectHeight); 
                    } 
                } 
            } 
        } 
        private Color GetColorForMagnitude(float mag) 
        { 
            int alpha = (int)(255 * Math.Max(0, (mag - 0.1f) * 1.5f)); 
            if (mag > 0.8f) 
                return 
                    Color.FromArgb(alpha, 255, 100, 100); 
            if (mag > 0.5f) 
                return 
                    Color.FromArgb(alpha, 255, 255, 100); 
            return 
                Color.FromArgb(alpha, 100, 150, 255); 
        } 
    }
    public class VoiceprintPanel : GlassPanel
    {
        private VoiceSignature _signature; 
        public string Title
        {
            get; 
            set; 
        } public void UpdateSignature(VoiceSignature signature)
        {
            _signature = signature; 
            Invalidate(); 
        } public void Clear()
        { 
            _signature = default;
            Invalidate(); 
        } protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); 
            var g = e.Graphics; 
            g.SmoothingMode = SmoothingMode.AntiAlias; 
            if (Title != null) 
            { 
                TextRenderer.DrawText(g, Title, new Font("Consolas", 10f), new Point(5, 5), Color.White); 
            } 
            if (_signature.AverageRms <= 0) 
                return;
            var rect = ClientRectangle; rect.Inflate(-15, -15); 
            rect.Y += 15; 
            var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float rmsRadius = (float)(_signature.AverageRms * 1500);
            rmsRadius = Math.Max(5, Math.Min(rmsRadius, rect.Width / 2f)); 
            float pitchAngle = (float)(_signature.AverageFrequency % 360); 
            float timbrePoints = (float)Math.Min(12, 3 + (_signature.AverageTimbre / 500));
            var path = new GraphicsPath();
            for (int i = 0; 
                i < timbrePoints;
                i++) 
            {
                float angle = (float)(2 * Math.PI * i / timbrePoints) + (pitchAngle * 
                    (float)Math.PI / 180f);
                float x = center.X + rmsRadius * (float)Math.Cos(angle);
                float y = center.Y + rmsRadius * (float)Math.Sin(angle);
                if (i == 0) path.AddLine(x, y, x, y);
                else 
                    path.AddLine(path.GetLastPoint(), new PointF(x, y)); 
            }
            path.CloseFigure(); 
            using (var brush = new SolidBrush(Color.FromArgb(150, 0, 255, 255))) 
                g.FillPath(brush, path); 
            using (var pen = new Pen(Color.Cyan, 2)) 
                g.DrawPath(pen, path);
        } 
    }

    // EmotionalStatePanel draws a radar/spider chart.
    public class EmotionalStatePanel : GlassPanel
    {
        private Dictionary<string, float> _emotionalState; 
        public void UpdateState(Dictionary<string, float> state) 
        {
            _emotionalState = state; Invalidate();
        }
        public void Clear() 
        {
            _emotionalState = null; 
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); 
            if (_emotionalState == null || 
                _emotionalState.Count == 0)
                return;
            var g = e.Graphics; 
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            var center = new PointF(rect.Width / 2f, rect.Height / 2f);
            float radius = Math.Min(rect.Width, rect.Height) / 2f - 20;

            var labels = _emotionalState.Keys.ToList();
            int numAxes = labels.Count;
            float angleStep = (float)(2 * Math.PI / numAxes);

            // Draw axes and grid lines
            using (var gridPen = new Pen(Color.FromArgb(50, 0, 255, 255)))
            using (var labelFont = new Font("Consolas", 8f))
            using (var labelBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
            {
                for (int i = 0; i < numAxes; i++)
                {
                    float angle = i * angleStep;
                    var p2 = new PointF(center.X + radius *
                        (float)Math.Cos(angle), center.Y + radius *
                        (float)Math.Sin(angle));
                    g.DrawLine(gridPen, center, p2);

                    for (int j = 1; j <= 4; j++)
                    {
                        float gridRadius = radius * (j / 4f);
                        g.DrawEllipse(gridPen, center.X - gridRadius, center.Y - gridRadius, gridRadius * 2, gridRadius * 2);
                    }
                    var labelPos = new PointF(center.X + (radius + 5) * 
                        (float)Math.Cos(angle), center.Y + (radius + 5) * 
                        (float)Math.Sin(angle));
                    var textSize = g.MeasureString(labels[i], labelFont);
                    if (labelPos.X < center.X) labelPos.X -= textSize.Width;
                    if (labelPos.Y < center.Y) labelPos.Y -= textSize.Height;
                    g.DrawString(labels[i], labelFont, labelBrush, labelPos);
                }
            }

            // Draw the data shape
            var points = new PointF[numAxes];
            for (int i = 0; 
                i < numAxes; i++)
            {
                float value = _emotionalState[labels[i]];
                float angle = i * angleStep;
                points[i] = new PointF(center.X + radius * value * 
                    (float)Math.Cos(angle), center.Y + radius * value * 
                    (float)Math.Sin(angle));
            }

            if (points.Length > 2)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(points);
                    using (var brush = new SolidBrush(Color.FromArgb(120, 255, 100, 100))) 
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(255, 50, 50), 2f)) 
                        g.DrawPath(pen, path);
                }
            }
        }
    }
    public class SoundPlayer : IDisposable 
    { 
        private WaveOutEvent _waveOut;
        private WaveStream _waveStream;
        public void PlayAudio(byte[] audioData, WaveFormat format) 
        { 
            Stop();
            try 
            {
                _waveStream = new RawSourceWaveStream(audioData, 0, audioData.Length, format); 
                _waveOut = new WaveOutEvent(); 
                _waveOut.Init(_waveStream); 
                _waveOut.Play(); 
            } 
            catch 
            { /* ignore playback errors */ } 
        } public void Stop() 
        { 
            _waveOut?.Stop(); 
            _waveOut?.Dispose();
            _waveOut = null; 
            _waveStream?.Dispose(); 
            _waveStream = null;
        }
        public void Dispose() 
        {
            Stop(); 
        }
    }
    #endregion

    #region Data Structures & Definitions
    public class SessionRecord
    {
        public DateTime SessionDate { get; set; }
        public VoiceSignature QuestionerSignature { get; set; }
        public VoiceSignature SubjectSignature { get; set; }
        public List<QuestionLog> QuestionLogs { get; set; } = new List<QuestionLog>();
        public List<EventLogItem> EventLog { get; set; } = new List<EventLogItem>();
        public TestResult FinalResult { get; set; }
        public string ResultSummary { get; set; }
        public int MicroExpressionCount { get; set; }
        public double AverageStress { get; set; }
    }

    public class QuestionLog
    {
        public string QuestionText { get; set; }
        public byte[] AnswerAudio { get; set; }
        public AnswerAnalysisResult AnalysisResult { get; set; }
    }

    public class EventLogItem
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public string Details { get; set; }
        public string ColorHtml { get; set; }
        [JsonIgnore]
        public object Tag { get; set; } // Used to associate log with QuestionLog in UI

        public EventLogItem() { } // For JSON Deserializer
        public EventLogItem(string type, string details, Color? color = null)
        {
            Timestamp = DateTime.Now;
            EventType = type;
            Details = details;
            ColorHtml = ColorTranslator.ToHtml(color ?? Color.Cyan);
        }
    }

    public struct VocalAnalysisData
    {
        public float VocalStressLevel;
        public Complex[] LatestFft;
        public Dictionary<string, float> EmotionalState;
    }

    public struct DataPoint
    {
        public float Value;
        public bool IsStressed;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public struct VoiceSignature
    {
        [JsonProperty] public double AverageRms;
        [JsonProperty] public double AverageFrequency;
        [JsonProperty] public double AverageTimbre;
    }

    public class AnswerAnalysisResult
    {
        public double AverageRms { get; set; }
        public double AveragePitch { get; set; }
        public double AverageTimbre { get; set; }
        public float StressLevel { get; set; }
        public bool IsDeceptive { get; set; }
        [JsonIgnore]
        public QuestionLog ParentQuestion { get; set; }
        public Dictionary<string, float> EmotionalState { get; set; }
    }

    public enum SessionState
    { Idle,
        CalibratingQuestioner,
        CalibratingSubject, 
        InProgress,
        Finished
    }
    public enum SpeakerType
    { 
        Unknown,
        Questioner,
        Subject 
    }
    public enum TestResult 
    { 
        TRUTHFUL, 
        DECEPTIVE, 
        INCONCLUSIVE 
    }
    #endregion

    #region Core Logic: Vocal Stress Test
    public class VocalStressTest
    {
        #region Enums & Structs
        private enum ExpectedAnswer 
        { 
            Yes, 
            No 
        }
        private struct Question 
        {
            public string Text; 
            public bool IsKeyQuestion;
            public ExpectedAnswer Answer; }
        public struct TestResultData 
        {
            public TestResult Result; 
            public string Summary; 
        }
        public struct SpeakerIdentification 
        { 
            public SpeakerType Type;
            public double Confidence; 
            public double[] RmsConfidence; 
            public double[] PitchConfidence; 
            public double[] TimbreConfidence; 
        }
        #endregion

        #region Fields and Properties
        private readonly List<Question> _questions; 
        private int _currentQuestionIndex = -1;
        private readonly Random _random = new Random();
        private int _deceptionSpikes = 0;
        private int _microExpressionCount = 0;
        private const int CALIBRATION_SAMPLES_NEEDED = 50; 
        private int _calibrationSampleCount = 0;
        private readonly List<double> _rmsReadings = new List<double>();
        private readonly List<double> _freqReadings = new List<double>();
        private readonly List<double> _timbreReadings = new List<double>();
        private const int ADAPTIVE_BASELINE_WINDOW = 5;
        private readonly Queue<VoiceSignature> _adaptiveBaselineSamples = new Queue<VoiceSignature>();
        public SessionRecord CurrentSession { get; private set; }
        public Queue<DataPoint> StressHistory { get; } = new Queue<DataPoint>();
        public Queue<DataPoint> PitchHistory { get; } = new Queue<DataPoint>();
        public Queue<DataPoint> TimbreHistory { get; } = new Queue<DataPoint>();
        public VocalAnalysisData CurrentData { get; private set; }
        public float PeakStressLevel { get; private set; }
        public SpeakerIdentification LastIdentification { get; private set; }
        private const int MAX_HISTORY_POINTS = 150;
        public SessionState CurrentState 
        { get; private set; } = SessionState.Idle;
        public bool IsCalibrating => CurrentState == SessionState.CalibratingQuestioner || 
            CurrentState == SessionState.CalibratingSubject;
        private readonly AppSettings _settings;
        public delegate void CalibrationCompleteHandler(SpeakerType speaker, VoiceSignature signature);
        public delegate void MicroExpressionHandler(string description);
        public event DataUpdateHandler OnDataUpdate;
        public event StateChangeHandler OnStateChange;
        public event CalibrationCompleteHandler OnCalibrationComplete;
        public event MicroExpressionHandler OnMicroExpressionDetected;
        public delegate void DataUpdateHandler(VocalAnalysisData data);
        public delegate void StateChangeHandler(string message, Color color);
        #endregion

        public VocalStressTest(AppSettings settings)
        {
            _settings = settings;
            _questions = new List<Question> 
            { 
                new Question 
                { 
                    Text = "Is your name recorded as John Smith?", 
                    IsKeyQuestion = false, Answer = ExpectedAnswer.Yes 
                }, 
                new Question 
                { 
                    Text = "Is today " + DateTime.Now.DayOfWeek.ToString() + "?", 
                    IsKeyQuestion = false, Answer = ExpectedAnswer.Yes 
                }, 
                new Question
                { 
                    Text = "Have you ever told a lie?",
                    IsKeyQuestion = true, Answer = ExpectedAnswer.Yes 
                },
                new Question
                { 
                    Text = "Regarding the missing file, were you involved?", 
                    IsKeyQuestion = true, Answer = ExpectedAnswer.No 
                }, 
                new Question 
                { 
                    Text = "Are you in Wilton, Maine?",
                    IsKeyQuestion = false, Answer = ExpectedAnswer.Yes
                }, 
                new Question 
                {
                    Text = "Did you access the file without authorization?", 
                    IsKeyQuestion = true, Answer = ExpectedAnswer.No 
                }, 
                new Question
                { 
                    Text = "Have you answered all questions truthfully?", 
                    IsKeyQuestion = true, Answer = ExpectedAnswer.Yes 
                } 
            };
            LastIdentification = new SpeakerIdentification 
            {
                RmsConfidence = new double[2], 
                PitchConfidence = new double[2], 
                TimbreConfidence = new double[2] 
            };
        }

        #region Public Methods (Session & Analysis)
        public void StartSession() 
        {
            ResetState(); 
            CurrentSession = new SessionRecord 
        {
                SessionDate = DateTime.Now 
            }; 
            LogEvent("Session Start", "New session initiated.", Color.White); 
            CurrentState = SessionState.CalibratingQuestioner; 
            OnStateChange?.Invoke("QUESTIONER, please state your name and role for voice calibration.", Color.Orange); 
        }
        public string AskNextQuestion() 
        {
            if (CurrentState != SessionState.InProgress)
                return null; 
            _currentQuestionIndex++; 
            PeakStressLevel = 0; 
            return 
                _currentQuestionIndex < _questions.Count ?
                _questions[_currentQuestionIndex].Text : null;
        }
        public async Task<AnswerAnalysisResult> ProcessAnswerAsync(string verbalResponse, byte[] audioBuffer)
        {
            return await Task.Run(() =>
            {
                if (_currentQuestionIndex < 0 || _currentQuestionIndex >= _questions.Count ||
                CurrentSession.SubjectSignature.AverageRms == 0)
                    return new AnswerAnalysisResult();
                var analysis = AnalyzeFullBuffer(audioBuffer);
                var question = _questions[_currentQuestionIndex];
                var baseline = GetCurrentBaseline();
                double stressFactor = baseline.AverageRms > 0 ? analysis.AverageRms / baseline.AverageRms : 1.0;
                bool isDeceptiveResponse = (question.Answer == ExpectedAnswer.Yes && verbalResponse != "yes") || 
                (question.Answer == ExpectedAnswer.No && verbalResponse != "no");
                var result = new AnswerAnalysisResult
                {
                    AverageRms = analysis.AverageRms,
                    AveragePitch = analysis.AverageFrequency,
                    AverageTimbre = analysis.AverageTimbre
                };

                if (question.IsKeyQuestion && (stressFactor > GetStressThreshold() || isDeceptiveResponse)) 
                {
                    _deceptionSpikes++; 
                    result.IsDeceptive = true; 
                }
                result.StressLevel = (float)(stressFactor * 2.5);
                PeakStressLevel = Math.Max(PeakStressLevel, result.StressLevel);
                result.EmotionalState = CalculateEmotionalState(analysis);
                if (!question.IsKeyQuestion)
                { 
                    UpdateAdaptiveBaseline(analysis); 
                }
                var questionLog = new QuestionLog
                {
                    QuestionText = question.Text,
                    AnswerAudio = audioBuffer,
                    AnalysisResult = result
                };
                CurrentSession.QuestionLogs.Add(questionLog);
                result.ParentQuestion = questionLog;

                DetectMicroExpressions(audioBuffer);
                return result;
            });
        }
        public async Task<SpeakerIdentification> IdentifySpeakerAsync(byte[] audioBuffer)
        {
            return await Task.Run(() =>
            {
                if (audioBuffer == null || audioBuffer.Length == 0 ||
                CurrentSession?.QuestionerSignature.AverageRms == 0)
                    return new SpeakerIdentification
                    {
                        Type = SpeakerType.Unknown,
                        Confidence = 0,
                        RmsConfidence = new double[2],
                        PitchConfidence = new double[2],
                        TimbreConfidence = new double[2]
                    };
                var analysis = AnalyzeFullBuffer(audioBuffer);
                double qRms = GetSingleDifference(analysis.AverageRms,
                    CurrentSession.QuestionerSignature.AverageRms);
                double sRms = GetSingleDifference(analysis.AverageRms,
                    CurrentSession.SubjectSignature.AverageRms);
                double qPitch = GetSingleDifference(analysis.AverageFrequency,
                    CurrentSession.QuestionerSignature.AverageFrequency);
                double sPitch = GetSingleDifference(analysis.AverageFrequency,
                    CurrentSession.SubjectSignature.AverageFrequency);
                double qTimbre = GetSingleDifference(analysis.AverageTimbre,
                    CurrentSession.QuestionerSignature.AverageTimbre);
                double sTimbre = GetSingleDifference(analysis.AverageTimbre,
                    CurrentSession.SubjectSignature.AverageTimbre);
                double qScore = (qRms * 0.2) + (qPitch * 0.4) + (qTimbre * 0.4);
                double sScore = (sRms * 0.2) + (sPitch * 0.4) + (sTimbre * 0.4);
                var result = new SpeakerIdentification
                {
                    Type = sScore < qScore ? SpeakerType.Subject : SpeakerType.Questioner,
                    RmsConfidence = NormalizeConf(qRms, sRms),
                    PitchConfidence = NormalizeConf(qPitch, sPitch),
                    TimbreConfidence = NormalizeConf(qTimbre, sTimbre)
                };
                double totalScore = qScore + sScore;
                result.Confidence = totalScore > 0 ? 1.0 - (Math.Min(qScore, sScore) / totalScore) : 1.0;
                LastIdentification = result;
                return result;
            });
        }

        public void Calibrate(byte[] audioBuffer)
        {
            if (!IsCalibrating || audioBuffer.Length == 0)
                return;
            var fftResults = PerformFFT(audioBuffer);
            if (fftResults == null)
                return;
            _rmsReadings.Add(CalculateRMS(audioBuffer));
            _freqReadings.Add(CalculateFundamentalFrequency(fftResults, _settings.SampleRate));
            _timbreReadings.Add(CalculateSpectralCentroid(fftResults, _settings.SampleRate));
            _calibrationSampleCount++;
            OnStateChange?.Invoke($"Calibrating... {(_calibrationSampleCount * 100 / CALIBRATION_SAMPLES_NEEDED)}%", Color.Orange);
            if (_calibrationSampleCount >= CALIBRATION_SAMPLES_NEEDED)
            {
                var signature = new VoiceSignature
                {
                    AverageRms = _rmsReadings.DefaultIfEmpty(0.01).Average(),
                    AverageFrequency = _freqReadings.DefaultIfEmpty(150).Average(),
                    AverageTimbre = _timbreReadings.DefaultIfEmpty(1500).Average()
                };
                if (CurrentState == SessionState.CalibratingQuestioner)
                {
                    CurrentSession.QuestionerSignature = signature;
                    OnCalibrationComplete?.Invoke(SpeakerType.Questioner, signature);
                    ResetCalibrationCounters();
                    CurrentState = SessionState.CalibratingSubject;
                    OnStateChange?.Invoke("SUBJECT, please state your name for voice calibration.", Color.Cyan);
                }
                else if (CurrentState == SessionState.CalibratingSubject)
                {
                    CurrentSession.SubjectSignature = signature;
                    _adaptiveBaselineSamples.Clear();
                    _adaptiveBaselineSamples.Enqueue(signature);
                    OnCalibrationComplete?.Invoke(SpeakerType.Subject, signature);
                    ResetCalibrationCounters();
                    CurrentState = SessionState.InProgress;
                }
            }
        }
        public bool IsTestFinished() => _currentQuestionIndex >= _questions.Count - 1;
        public double GetStressThreshold() => _settings.StressThreshold;
        public async Task<TestResultData> EndSessionAsync()
        {
            return await Task.Run(() =>
            {
                CurrentState = SessionState.Finished;
                int keyQuestions = _questions.Count(q => q.IsKeyQuestion);
                var result = new TestResultData();

                if (_deceptionSpikes > keyQuestions / 2)
                {
                    result.Result = TestResult.DECEPTIVE;
                }
                else if (_deceptionSpikes > 0 || _microExpressionCount > keyQuestions)
                {
                    result.Result = TestResult.INCONCLUSIVE;
                }
                else
                {
                    result.Result = TestResult.TRUTHFUL;
                }

                double avgStress = CurrentSession.QuestionLogs.Any() ?
                CurrentSession.QuestionLogs.Average(q => q.AnalysisResult.StressLevel) : 0;
                CurrentSession.AverageStress = avgStress;
                CurrentSession.MicroExpressionCount = _microExpressionCount;

                result.Summary = $"Analysis complete.\n" +
                                 $"{_deceptionSpikes} stress events across {keyQuestions} key questions.\n" +
                                 $"{_microExpressionCount} vocal micro-expressions detected.\n" +
                                 $"Average subject stress: {avgStress:F2} µt.";

                CurrentSession.FinalResult = result.Result;
                CurrentSession.ResultSummary = result.Summary;

                return result;
            });
        }
        public void Update()
        {
            if (CurrentState == SessionState.Idle || CurrentState == SessionState.Finished)
                return;
            UpdateHistory();
            OnDataUpdate?.Invoke(CurrentData);
        }
        public void ProcessLiveAudio(byte[] audioBuffer)
        {
            if (CurrentState != SessionState.InProgress)
                return;
            var fft = PerformFFT(audioBuffer);
            var analysis = AnalyzeFullBuffer(audioBuffer);
            var emotionalState = CalculateEmotionalState(analysis);
            CurrentData = new VocalAnalysisData
            {
                LatestFft = fft,
                EmotionalState = emotionalState,
                VocalStressLevel = (float)(analysis.AverageRms / GetCurrentBaseline().AverageRms) * 2.5f
            };
        }
        public void LoadSession(SessionRecord session)
        {
            ResetState();
            CurrentSession = session; CurrentState = SessionState.Finished;
        }
        public void LoadQuestionDataForReview(QuestionLog log)
        {
            StressHistory.Clear();
            PitchHistory.Clear();
            TimbreHistory.Clear();
            var result = log.AnalysisResult;
            for (int i = 0; i < MAX_HISTORY_POINTS; 
                i++)
            {
                float progress = (float)i / MAX_HISTORY_POINTS;
                StressHistory.Enqueue(new DataPoint
                {
                    Value = (float)(result.StressLevel * progress),
                    IsStressed = result.IsDeceptive
                });
                PitchHistory.Enqueue(new DataPoint 
                { 
                    Value = (float)result.AveragePitch 
                });
                TimbreHistory.Enqueue(new DataPoint 
                {
                    Value = (float)result.AverageTimbre
                });
            }
        }
        #endregion

        #region Internal Logic & Signal Processing
        private Complex[] PerformFFT(byte[] audioBuffer)
        {
            int bytesPerSample = 2;
            int sampleCount = audioBuffer.Length / bytesPerSample;
            if (sampleCount == 0)
                return null;
            int fftSize = 2;
            while (fftSize * 2 <= sampleCount) fftSize *= 2;
            if (fftSize < 2)
                return null;
            var complexBuffer = new Complex[fftSize];
            var window = HanningWindow(fftSize);
            for (int i = 0; 
                i < fftSize; i++)
            {
                if ((i * 2) + 1 >= audioBuffer.Length)
                    break;
                short sample = (short)(audioBuffer[i * 2] | audioBuffer[i * 2 + 1] << 8);
                complexBuffer[i].X = (float)(sample / 32768.0 * window[i]);
                complexBuffer[i].Y = 0;
            }
            FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2.0), complexBuffer);
            return complexBuffer;
        }
        private static float[] HanningWindow(int size)
        {
            float[] window = new float[size];
            for (int i = 0;
                i < size; i++)
            {
                window[i] = 0.5f * (1 - (float)Math.Cos((2 * Math.PI * i) / (size - 1)));
            }
            return window;
        }
        private VoiceSignature AnalyzeFullBuffer(byte[] buffer)
        {
            var fft = PerformFFT(buffer);
            if (fft == null) return new VoiceSignature();
            return new VoiceSignature
            {
                AverageRms = CalculateRMS(buffer),
                AverageFrequency = CalculateFundamentalFrequency(fft, _settings.SampleRate),
                AverageTimbre = CalculateSpectralCentroid(fft, _settings.SampleRate)
            };
        }
        private double CalculateFundamentalFrequency(Complex[] fftResults, int sampleRate)
        {
            int maxMagnitudeIndex = 0;
            double maxMagnitude = 0;
            for (int i = 1;
                i < fftResults.Length / 2; 
                i++)
            {
                double magnitude = Math.Sqrt(fftResults[i].X * fftResults[i].X + fftResults[i].Y * fftResults[i].Y);
                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                    maxMagnitudeIndex = i;
                }
            }
            return (double)maxMagnitudeIndex * sampleRate / fftResults.Length;
        }
        private double CalculateSpectralCentroid(Complex[] fftResults, int sampleRate)
        {
            double weightedSum = 0;
            double magnitudeSum = 0;
            for (int i = 1; 
                i < fftResults.Length / 2; i++)
            {
                double magnitude = Math.Sqrt(fftResults[i].X * fftResults[i].X + fftResults[i].Y * fftResults[i].Y);
                double frequency = (double)i * sampleRate / fftResults.Length;
                weightedSum += frequency * magnitude;
                magnitudeSum += magnitude;
            }
            return (magnitudeSum > 0) ? weightedSum / magnitudeSum : 0;
        }
        private double CalculateRMS(byte[] audioBuffer)
        {
            if (audioBuffer == null || audioBuffer.Length < 2) return 0.0;
            double sumOfSquares = 0; 
            int sampleCount = audioBuffer.Length / 2;
            for (int i = 0;
                i < audioBuffer.Length - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(audioBuffer, i);
                double scaledSample = sample / 32768.0;
                sumOfSquares += scaledSample * scaledSample;
            }
            return Math.Sqrt(sumOfSquares / sampleCount);
        }
        private Dictionary<string, float> CalculateEmotionalState(VoiceSignature sig)
        {
            var baseline = GetCurrentBaseline();
            float rmsRatio = baseline.AverageRms > 0 ? (float)(sig.AverageRms / baseline.AverageRms) : 1f;
            float pitchRatio = baseline.AverageFrequency > 0 ? (float)(sig.AverageFrequency / baseline.AverageFrequency) : 1f;
            float agitation = Math.Max(0, Math.Min(1f, (rmsRatio - 1f) * 2f + (pitchRatio - 1f)));
            float cognitiveLoad = Math.Min(1f, (float)(Math.Abs(1 - pitchRatio) * 1.5));
            return new Dictionary<string, float> { { "Agitation", agitation }, { "Cognitive Load", cognitiveLoad },
                {
                    "Hesitation", Math.Min(1, (float)(_random.NextDouble() * agitation * 0.5)) },
                {
                    "Confidence", Math.Max(0, 1f - cognitiveLoad * 0.8f) } };
        }
        private void UpdateAdaptiveBaseline(VoiceSignature newSample)
        {
            _adaptiveBaselineSamples.Enqueue(newSample);
            while (_adaptiveBaselineSamples.Count > ADAPTIVE_BASELINE_WINDOW) _adaptiveBaselineSamples.Dequeue();
        }
        private VoiceSignature GetCurrentBaseline()
        {
            if (_adaptiveBaselineSamples.Count == 0 && CurrentSession != null)
                return CurrentSession.SubjectSignature;
            if (_adaptiveBaselineSamples.Count == 0)
                return new VoiceSignature(); 
            return new VoiceSignature
                {
                    AverageRms = _adaptiveBaselineSamples.Average(s => s.AverageRms),
                    AverageFrequency = _adaptiveBaselineSamples.Average(s => s.AverageFrequency),
                    AverageTimbre = _adaptiveBaselineSamples.Average(s => s.AverageTimbre)
                };
        }
        private void LogEvent(string type, string details, Color color)
        {
            CurrentSession?.EventLog.Add(new EventLogItem(type, details, color));
        }
        private void ResetState()
        {
            _currentQuestionIndex = -1;
            _deceptionSpikes = 0;
            PeakStressLevel = 0;
            _microExpressionCount = 0;
            ResetCalibrationCounters();
            StressHistory.Clear();
            PitchHistory.Clear();
            TimbreHistory.Clear();
            _adaptiveBaselineSamples.Clear();
            LastIdentification = new SpeakerIdentification
            {
                RmsConfidence = new double[2],
                PitchConfidence = new double[2],
                TimbreConfidence = new double[2]
            };
        }
        private void ResetCalibrationCounters()
        {
            _calibrationSampleCount = 0;
            _rmsReadings.Clear();
            _freqReadings.Clear();
            _timbreReadings.Clear();
        }
        private void UpdateHistory()
        {
            if (CurrentSession == null) return;
            var baseline = GetCurrentBaseline();
            var analysis = new VoiceSignature();
            if (CurrentData.LatestFft != null) analysis = AnalyzeFullBuffer(new byte[0]); // Dummy call to get structure

            StressHistory.Enqueue(new DataPoint
            {
                Value = PeakStressLevel,
                IsStressed = PeakStressLevel > GetStressThreshold() * 1.5
            });
            PitchHistory.Enqueue(new DataPoint
            {
                Value = (float)(baseline.AverageFrequency + (_random.NextDouble() - 0.5) * 10)
            });
            TimbreHistory.Enqueue(new DataPoint
            {
                Value = (float)(baseline.AverageTimbre + (_random.NextDouble() - 0.5) * 50)
            });

            while (StressHistory.Count > MAX_HISTORY_POINTS) StressHistory.Dequeue();
            while (PitchHistory.Count > MAX_HISTORY_POINTS) PitchHistory.Dequeue();
            while (TimbreHistory.Count > MAX_HISTORY_POINTS) TimbreHistory.Dequeue();
        }
        private void DetectMicroExpressions(byte[] audioBuffer)
        {
            const int chunkSize = 1024;
            var pitchValues = new List<double>();
            for (int i = 0; i < audioBuffer.Length; i += chunkSize)
            {
                int remaining = Math.Min(chunkSize, audioBuffer.Length - i);
                if (remaining < 512) break;
                var chunk = new byte[remaining];
                Array.Copy(audioBuffer, i, chunk, 0, remaining);
                var fft = PerformFFT(chunk);
                if (fft != null)
                {
                    pitchValues.Add(CalculateFundamentalFrequency(fft, _settings.SampleRate));
                }
            }

            if (pitchValues.Count < 3) return;
            for (int i = 1; i < pitchValues.Count - 1; i++)
            {
                double prev = pitchValues[i - 1];
                double current = pitchValues[i];
                double next = pitchValues[i + 1];

                if (Math.Abs(current - prev) > 25 && Math.Sign(current - prev) != Math.Sign(next - current))
                {
                    _microExpressionCount++;
                    OnMicroExpressionDetected?.Invoke($"Sudden pitch instability detected at {i * (chunkSize / (float)_settings.SampleRate):F2}s");
                    return; // Only detect one per answer to avoid spam
                }
            }
        }
        private double GetSingleDifference(double val, double target) => target > 0 ? Math.Abs(val - target) / target : 0;
        private double[] NormalizeConf(double q, double s)
        {
            double total = q + s;
            return total > 0 ? new[] { 1.0 - (q / total), 1.0 - (s / total) } : new[] { 0.5, 0.5 };
        }
        #endregion
    }
    #endregion
}