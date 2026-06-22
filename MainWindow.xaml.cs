using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShapeRectangle = System.Windows.Shapes.Rectangle;

namespace ClipForgeWpf;

public partial class MainWindow : Window
{
    readonly ObservableCollection<FrameItem> _frames = new();
    readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _previewTimer = new();
    readonly LibVLC _libVlc;
    VlcMediaPlayer _player;
    VlcMedia? _media;
    string? _videoPath;
    readonly string _tempRoot = Path.Combine(TempBase, "session_" + Guid.NewGuid().ToString("N"));
    double _duration;
    double _fps;
    int _width;
    int _height;
    double _start;
    double _end;
    int _previewIndex;
    bool _updatingTimeline;
    bool _extracting;
    bool _muted = true;
    int _loadSerial;
    int _stillSerial;
    CancellationTokenSource? _extractCancel;
    Rect? _recordRect;
    bool _recording;
    CancellationTokenSource? _recordCancel;
    string? _recordDir;
    int _recordFrameIndex;
    readonly int _recordFps = 30;
    WindowState _beforeAreaRecordState = WindowState.Normal;
    Rect? _cropRect;
    readonly ObservableCollection<OverlaySpec> _overlays = new();

    static string BaseDir => AppContext.BaseDirectory;
    static string TempBase => Path.Combine(BaseDir, "tmp");
    static string FfmpegPath => Path.Combine(BaseDir, "ffmpeg.exe");

    public MainWindow()
    {
        Core.Initialize();
        InitializeComponent();
        CleanupOldTempSessions();
        Directory.CreateDirectory(_tempRoot);
        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
        _player = new VlcMediaPlayer(_libVlc);
        _player.Mute = _muted;
        VideoView.MediaPlayer = _player;
        VideoView.Visibility = Visibility.Collapsed;
        UpdateMuteButton();
        FrameList.ItemsSource = _frames;
        _uiTimer.Tick += (_, _) => SyncPositionFromPlayer();
        _previewTimer.Tick += (_, _) => TickPreview();
        Closing += (_, _) =>
        {
            try { _player.Stop(); } catch { }
            _ = StopRecordingAsync(addFrames: false);
            try { VideoView.MediaPlayer = null; } catch { }
            _media?.Dispose();
            _player.Dispose();
            _libVlc.Dispose();
            ClearTempRoot(deleteRoot: true);
        };
        LocationChanged += (_, _) => RefreshVideoDropGuard();
        SizeChanged += (_, _) => RefreshVideoDropGuard();
    }

    void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => SetStatus(text)));
            return;
        }
        StatusText.Text = text;
    }
    void SetProgress(int current, int total, string label)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => SetProgress(current, total, label)));
            return;
        }
        if (total <= 0)
        {
            ExtractProgress.IsIndeterminate = true;
            ProgressText.Text = label;
            return;
        }
        ExtractProgress.IsIndeterminate = false;
        ExtractProgress.Maximum = total;
        ExtractProgress.Value = Math.Clamp(current, 0, total);
        var percent = Math.Clamp(current * 100.0 / total, 0, 100);
        ProgressText.Text = $"{current:N0}/{total:N0} ({percent:0}%)";
    }
    void ResetProgress(string text = "-")
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => ResetProgress(text)));
            return;
        }
        ExtractProgress.IsIndeterminate = false;
        ExtractProgress.Maximum = 100;
        ExtractProgress.Value = 0;
        ProgressText.Text = text;
    }
    static string TimeText(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    double CurrentSeconds => _player.Time > 0 ? _player.Time / 1000.0 : Math.Clamp(Timeline.Value, 0, Math.Max(Timeline.Maximum, 0));

    void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void Window_PreviewDragOver(object sender, DragEventArgs e) => Window_DragOver(sender, e);

    void Window_PreviewDrop(object sender, DragEventArgs e) => LoadDropped(e);

    void Window_Drop(object sender, DragEventArgs e) => LoadDropped(e);
    void Video_Drop(object sender, DragEventArgs e) => LoadDropped(e);

    void RefreshVideoDropGuard()
    {
        if (VideoDropPopup == null) return;
        var wasOpen = VideoDropPopup.IsOpen;
        VideoDropPopup.IsOpen = false;
        VideoDropPopup.IsOpen = wasOpen;
    }

    void LoadDropped(DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var path = files.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(path)) return;
        LoadVideo(path);
    }

    void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Video|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv|All|*.*" };
        if (dialog.ShowDialog() == true) LoadVideo(dialog.FileName);
    }

    async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        StopExtract();
        await StopRecordingAsync(addFrames: false);
        _loadSerial++;
        _stillSerial++;
        StopSourceForNewVideo();
        _player = new VlcMediaPlayer(_libVlc) { Mute = _muted };
        VideoView.MediaPlayer = _player;
        VideoView.Visibility = Visibility.Collapsed;
        _frames.Clear();
        FrameList.Items.Refresh();
        ResultImage.Source = null;
        SourceStillImage.Source = null;
        SourceStillImage.Visibility = Visibility.Visible;
        VideoView.Visibility = Visibility.Collapsed;
        EmptyVideoText.Visibility = Visibility.Visible;
        _videoPath = null;
        _duration = 0;
        _fps = 0;
        _width = 0;
        _height = 0;
        _start = 0;
        _end = 0;
        _cropRect = null;
        Timeline.Maximum = 1;
        Timeline.Value = 0;
        MetaText.Text = "-";
        UpdateTexts();
        ResetProgress();
        SetStatus("Status: clearing cache");

        await Task.Delay(150);
        await Task.Run(() => ForceDeleteDirectory(TempBase));
        Directory.CreateDirectory(_tempRoot);

        SetStatus("Status: cache cleared");
    }

    async void LoadVideo(string path)
    {
        if (!File.Exists(path)) return;
        var loadId = ++_loadSerial;
        try
        {
            _videoPath = path;
            SetStatus("Status: reading original info");
            ResetProgress();
            EmptyVideoText.Visibility = Visibility.Collapsed;
            SourceStillImage.Source = null;
            SourceStillImage.Visibility = Visibility.Visible;
            VideoView.Visibility = Visibility.Collapsed;
            StopSourceForNewVideo();
            await Task.Delay(40);
            if (loadId != _loadSerial) return;

            _player = new VlcMediaPlayer(_libVlc) { Mute = _muted };
            VideoView.MediaPlayer = _player;
            VideoView.Visibility = Visibility.Collapsed;
            _media = new VlcMedia(_libVlc, path, FromType.FromPath);
            _player.Media = _media;
            _player.Mute = _muted;
            _player.Pause();
            _player.Time = 0;
            _uiTimer.Stop();
            _player.Mute = _muted;

            var meta = await Task.Run(() => Probe(path));
            if (loadId != _loadSerial) return;
            _duration = meta.duration > 0 ? meta.duration : Math.Max(0, _player.Length / 1000.0);
            _fps = meta.fps;
            _width = meta.width;
            _height = meta.height;
            _start = 0;
            _end = _duration;
            _cropRect = null;
            Timeline.Maximum = Math.Max(_duration, 0.001);
            Timeline.Value = 0;
            MetaText.Text = $"{_width}x{_height} | {_fps:0.###} FPS | {TimeText(_duration)}";
            await ShowSourceStillAsync(path, 0, loadId);
            SetStatus(_fps > 0 ? "Status: idle" : "Status: failed to read source FPS");
            UpdateTexts();
        }
        catch (Exception ex)
        {
            SetStatus("Status: load failed " + Short(ex.Message));
        }
    }

    void StopSourceForNewVideo()
    {
        _uiTimer.Stop();
        _previewTimer.Stop();
        try { _player.Pause(); } catch { }
        try { _player.Stop(); } catch { }
        try { VideoView.Visibility = Visibility.Collapsed; } catch { }
        try { VideoView.MediaPlayer = null; } catch { }
        try { _player.Media = null; } catch { }
        try { _media?.Dispose(); } catch { }
        _media = null;
        try { _player.Dispose(); } catch { }
    }

    async Task ShowSourceStillAsync(string? videoPath, double seconds, int loadId)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return;
        var stillId = ++_stillSerial;
        var dir = Path.Combine(_tempRoot, "still");
        Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, "source_still_" + stillId.ToString("000000", CultureInfo.InvariantCulture) + ".jpg");
        try
        {
            await Task.Run(() => RunFfmpeg($"-ss {Q(Math.Max(0, seconds))} -i {Q(videoPath)} -frames:v 1 -q:v 3 {Q(output)}"));
            if (loadId != _loadSerial || stillId != _stillSerial || !File.Exists(output)) return;
            var image = LoadBitmapSource(output);
            if (loadId != _loadSerial || stillId != _stillSerial) return;
            SourceStillImage.Source = image;
            SourceStillImage.Visibility = Visibility.Visible;
            VideoView.Visibility = Visibility.Collapsed;
            try { if (_player.IsPlaying) _player.Pause(); } catch { }
        }
        catch
        {
            if (loadId == _loadSerial) SourceStillImage.Visibility = Visibility.Visible;
        }
    }

    void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayback();

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        try { _player.Mute = _muted; } catch { }
        UpdateMuteButton();
    }

    void UpdateMuteButton()
    {
        if (MuteSlash == null || MuteButton == null) return;
        MuteSlash.Visibility = _muted ? Visibility.Visible : Visibility.Collapsed;
        MuteButton.ToolTip = _muted ? "Muted" : "Sound";
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (Keyboard.FocusedElement is TextBox) return;
        TogglePlayback();
        e.Handled = true;
    }

    void TogglePlayback()
    {
        if (_videoPath == null) return;
        if (_player.IsPlaying)
        {
            var seconds = CurrentSeconds;
            _player.Pause();
            _uiTimer.Stop();
            _ = ShowSourceStillAsync(_videoPath, seconds, _loadSerial);
        }
        else
        {
            _stillSerial++;
            SourceStillImage.Visibility = Visibility.Collapsed;
            VideoView.Visibility = Visibility.Visible;
            _player.Mute = _muted;
            _player.Play();
            _uiTimer.Start();
        }
    }

    void Back5_Click(object sender, RoutedEventArgs e) => SeekBy(-5);
    void Back1_Click(object sender, RoutedEventArgs e) => SeekBy(-1);
    void Forward1_Click(object sender, RoutedEventArgs e) => SeekBy(1);
    void Forward5_Click(object sender, RoutedEventArgs e) => SeekBy(5);
    void PrevFrame_Click(object sender, RoutedEventArgs e) => SeekBy(-1 / Math.Max(_fps, 1));
    void NextFrame_Click(object sender, RoutedEventArgs e) => SeekBy(1 / Math.Max(_fps, 1));

    void SeekBy(double delta)
    {
        var baseTime = Math.Max(Timeline.Value, CurrentSeconds);
        var next = Math.Clamp(baseTime + delta, 0, Math.Max(_duration, 0));
        _player.Time = (long)Math.Round(next * 1000);
        _updatingTimeline = true;
        Timeline.Value = next;
        _updatingTimeline = false;
        if (!_player.IsPlaying) _ = ShowSourceStillAsync(_videoPath, next, _loadSerial);
        UpdateTexts();
    }

    void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingTimeline || _videoPath == null) return;
        var next = Math.Clamp(Timeline.Value, 0, Math.Max(_duration, 0));
        _player.Time = (long)Math.Round(next * 1000);
        if (!_player.IsPlaying) _ = ShowSourceStillAsync(_videoPath, next, _loadSerial);
        UpdateTexts();
    }

    void SyncPositionFromPlayer()
    {
        _updatingTimeline = true;
        Timeline.Value = Math.Clamp(_player.Time / 1000.0, 0, Timeline.Maximum);
        _updatingTimeline = false;
        UpdateTexts();
    }

    void SetStart_Click(object sender, RoutedEventArgs e)
    {
        _start = Math.Min(CurrentSeconds, Math.Max(0, _end - 0.001));
        UpdateTexts();
    }

    void SetEnd_Click(object sender, RoutedEventArgs e)
    {
        _end = Math.Max(CurrentSeconds, _start + 0.001);
        UpdateTexts();
    }

    void UpdateTexts()
    {
        StartText.Text = TimeText(_start);
        CurrentText.Text = TimeText(CurrentSeconds);
        EndText.Text = TimeText(_end);
        SegmentText.Text = $"Segment: {TimeText(_start)} ~ {TimeText(_end)}";
    }

    async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath == null) return;
        var sourcePath = _videoPath;
        if (_extracting)
        {
            SetStatus("Status: extraction is already running");
            return;
        }
        if (_fps <= 0)
        {
            SetStatus("Status: no source FPS info");
            return;
        }
        var outDir = Path.Combine(_tempRoot, "frames_" + DateTime.Now.Ticks);
        Directory.CreateDirectory(outDir);
        var expectedFrames = EstimateFrameCount(_start, _end);
        SetStatus("Status: extracting original frames");
        SetProgress(0, expectedFrames, "0/" + expectedFrames.ToString("N0"));
        _extracting = true;
        _extractCancel?.Dispose();
        _extractCancel = new CancellationTokenSource();
        var token = _extractCancel.Token;
        try
        {
            var files = await Task.Run(() => ExtractOriginalFrames(sourcePath, outDir, _start, _end, expectedFrames, current =>
            {
                SetStatus($"Status: extracting {current:N0}/{expectedFrames:N0} frames");
                SetProgress(current, expectedFrames, "");
            }, token), token);
            AddFrames(files);
            SetProgress(files.Length, Math.Max(expectedFrames, files.Length), "done");
            SetStatus($"Status: done {files.Length} frames");
        }
        catch (OperationCanceledException)
        {
            ResetProgress("stopped");
            SetStatus("Status: extraction stopped");
        }
        catch (Exception ex)
        {
            ResetProgress("failed");
            SetStatus("Status: extract failed " + Short(ex.Message));
        }
        finally
        {
            _extracting = false;
            _extractCancel?.Dispose();
            _extractCancel = null;
        }
    }

    void StopExtract_Click(object sender, RoutedEventArgs e) => StopExtract();

    void StopExtract()
    {
        if (!_extracting) return;
        try { _extractCancel?.Cancel(); } catch { }
        SetStatus("Status: stopping extraction");
    }

    void RecordArea_Click(object sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            SetStatus("Status: stop recording before changing area");
            return;
        }
        BeginAreaSelection();
    }

    void BeginAreaSelection()
    {
        _beforeAreaRecordState = WindowState;
        WindowState = WindowState.Minimized;
        var dialog = new ScreenRegionWindow(
            startRecording: rect =>
            {
                _recordRect = rect;
                StartRecording(rect);
            },
            stopRecording: async () => await StopRecordingAsync(addFrames: true),
            cancelRecording: async () => await StopRecordingAsync(addFrames: false));
        var ok = dialog.ShowDialog() == true;
        if (!_recording)
        {
            WindowState = _beforeAreaRecordState;
            Activate();
        }
        if (!ok) return;
        _recordRect = dialog.SelectedRect;
        SetStatus($"Status: record area {Math.Round(_recordRect.Value.Width):0}x{Math.Round(_recordRect.Value.Height):0}");
    }

    async void RecordToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_recording) StartRecording(null);
        else await StopRecordingAsync(addFrames: true);
    }

    void StartRecording(Rect? rectOverride)
    {
        if (_recording) return;
        var rect = rectOverride ?? new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var x = (int)Math.Round(rect.X);
        var y = (int)Math.Round(rect.Y);
        var w = Math.Max(2, (int)Math.Round(rect.Width));
        var h = Math.Max(2, (int)Math.Round(rect.Height));
        if (w % 2 == 1) w--;
        if (h % 2 == 1) h--;

        _recordDir = Path.Combine(_tempRoot, "record_" + DateTime.Now.Ticks);
        Directory.CreateDirectory(_recordDir);
        _recordFrameIndex = 0;
        _recordCancel?.Dispose();
        _recordCancel = new CancellationTokenSource();
        _recording = true;
        _ = Task.Run(() => RecordScreenLoop(_recordDir, x, y, w, h, _recordCancel.Token));
        SetRecordingUi(true);
        SetStatus($"Status: recording {w}x{h}");
    }

    async Task StopRecordingAsync(bool addFrames)
    {
        var dir = _recordDir;
        if (!_recording) return;
        _recording = false;
        _recordDir = null;
        try { _recordCancel?.Cancel(); } catch { }
        await Task.Delay(120);
        _recordCancel?.Dispose();
        _recordCancel = null;
        SetRecordingUi(false);

        if (!addFrames || string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            SetStatus("Status: recording stopped");
            return;
        }
        var files = Directory.GetFiles(dir, "record_*.png").OrderBy(x => x).ToArray();
        if (files.Length == 0)
        {
            SetStatus("Status: recording stopped, no frames");
            return;
        }
        AddRecordedFrames(files);
        SetStatus($"Status: recording added {files.Length:N0} frames");
    }

    void RecordScreenLoop(string dir, int x, int y, int w, int h, CancellationToken token)
    {
        var frameMs = Math.Max(1, 1000 / _recordFps);
        var watch = Stopwatch.StartNew();
        var next = 0L;
        while (!token.IsCancellationRequested)
        {
            var index = Interlocked.Increment(ref _recordFrameIndex);
            var path = Path.Combine(dir, $"record_{index:000000}.png");
            try
            {
                using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), System.Drawing.CopyPixelOperation.SourceCopy);
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                if (index % _recordFps == 0) SetStatus($"Status: recording {index:N0} frames");
            }
            catch (Exception ex)
            {
                SetStatus("Status: recording failed " + Short(ex.Message));
                break;
            }
            next += frameMs;
            var delay = (int)Math.Max(1, next - watch.ElapsedMilliseconds);
            if (token.WaitHandle.WaitOne(delay)) break;
        }
    }

    void SetRecordingUi(bool recording)
    {
        RecordButton.Content = recording ? "\u25A0 Stop" : "\u25CF Full Record";
        RecordButton.Foreground = recording ? Brushes.Black : new SolidColorBrush(Color.FromRgb(225, 29, 72));
        RecordButton.Background = recording ? new SolidColorBrush(Color.FromRgb(255, 232, 232)) : SystemColors.ControlBrush;
    }

    int EstimateFrameCount(double start, double end)
    {
        var dur = Math.Max(0.001, end - start);
        return Math.Max(1, (int)Math.Ceiling(dur * Math.Max(_fps, 1)));
    }

    string[] ExtractOriginalFrames(string video, string outDir, double start, double end, int expectedFrames, Action<int> progress, CancellationToken token)
    {
        var dur = Math.Max(0.001, end - start);
        var pattern = Path.Combine(outDir, "frame_%06d.png");
        RunFfmpegWithProgress($"-ss {Q(start)} -t {Q(dur)} -i {Q(video)} -map 0:v:0 -fps_mode passthrough {Q(pattern)}", expectedFrames, progress, token);
        var files = Directory.GetFiles(outDir, "frame_*.png").OrderBy(x => x).ToArray();
        token.ThrowIfCancellationRequested();
        if (files.Length == 0) throw new Exception("no frames extracted");
        return files;
    }

    void AddFrames(string[] files)
    {
        var first = _frames.Count;
        var duration = (int)Math.Round(1000 / Math.Max(_fps, 1));
        for (var i = 0; i < files.Length; i++)
        {
            _frames.Add(new FrameItem
            {
                Index = _frames.Count + 1,
                Path = files[i],
                Time = _start + i / Math.Max(_fps, 1),
                Duration = duration,
                Thumbnail = LoadBitmap(files[i], 90)
            });
        }
        if (_frames.Count > first) FrameList.SelectedIndex = first;
    }

    void AddRecordedFrames(string[] files)
    {
        var first = _frames.Count;
        var duration = (int)Math.Round(1000.0 / _recordFps);
        for (var i = 0; i < files.Length; i++)
        {
            _frames.Add(new FrameItem
            {
                Index = _frames.Count + 1,
                Path = files[i],
                Time = i / (double)_recordFps,
                Duration = duration,
                Thumbnail = LoadBitmap(files[i], 90)
            });
        }
        _fps = _recordFps;
        if (_frames.Count > first) FrameList.SelectedIndex = first;
    }

    void FrameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshResultPreview();
    }

    void OutputSettings_Changed(object sender, SelectionChangedEventArgs e) => RefreshResultPreview();

    void RefreshResultPreview()
    {
        if (FrameList.SelectedItem is not FrameItem item) return;
        try { ResultImage.Source = RenderOutputFrame(item.Path); }
        catch (Exception ex) { SetStatus("Status: preview failed " + Short(ex.Message)); }
    }

    void ToggleSelect_Click(object sender, RoutedEventArgs e)
    {
        if (FrameList.SelectedItems.Count == _frames.Count) FrameList.SelectedItems.Clear();
        else
        {
            FrameList.SelectedItems.Clear();
            foreach (var f in _frames) FrameList.SelectedItems.Add(f);
        }
    }

    void Duration_Click(object sender, RoutedEventArgs e)
    {
        var selected = FrameList.SelectedItems.Cast<FrameItem>().ToList();
        if (selected.Count == 0 && FrameList.SelectedItem is FrameItem cur) selected.Add(cur);
        if (selected.Count == 0) return;
        var input = Microsoft.VisualBasic.Interaction.InputBox("Duration (ms)", "Change Duration", selected[0].Duration.ToString());
        if (!int.TryParse(input, out var ms) || ms < 1) return;
        foreach (var f in selected) f.Duration = ms;
        Renumber();
    }

    void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = FrameList.SelectedItems.Cast<FrameItem>().ToList();
        foreach (var f in selected)
        {
            _frames.Remove(f);
            try { if (File.Exists(f.Path)) File.Delete(f.Path); } catch { }
        }
        Renumber();
    }

    void ClearFrames_Click(object sender, RoutedEventArgs e)
    {
        _frames.Clear();
        ResultImage.Source = null;
        ClearTempRoot(deleteRoot: false);
        SetStatus("Status: idle");
    }

    void ClearTempRoot(bool deleteRoot)
    {
        try
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
            if (!deleteRoot) Directory.CreateDirectory(_tempRoot);
        }
        catch { }
    }

    static void CleanupOldTempSessions()
    {
        try
        {
            if (!Directory.Exists(TempBase)) return;
            var cutoff = DateTime.UtcNow.AddHours(-12);
            foreach (var dir in Directory.GetDirectories(TempBase, "session_*"))
            {
                var info = new DirectoryInfo(dir);
                if (info.LastWriteTimeUtc < cutoff) info.Delete(true);
            }
        }
        catch { }
    }

    void Renumber()
    {
        for (var i = 0; i < _frames.Count; i++) _frames[i].Index = i + 1;
        FrameList.Items.Refresh();
    }

    void PreviewPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_previewTimer.IsEnabled) { _previewTimer.Stop(); return; }
        if (_frames.Count == 0) return;
        _previewIndex = Math.Max(FrameList.SelectedIndex, 0);
        _previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _frames[_previewIndex].Duration));
        _previewTimer.Start();
    }

    void TickPreview()
    {
        if (_frames.Count == 0) { _previewTimer.Stop(); return; }
        FrameList.SelectedIndex = _previewIndex;
        FrameList.ScrollIntoView(_frames[_previewIndex]);
        _previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _frames[_previewIndex].Duration));
        _previewIndex = (_previewIndex + 1) % _frames.Count;
    }

    void SaveZip_Click(object sender, RoutedEventArgs e)
    {
        if (_frames.Count == 0) return;
        var dialog = new SaveFileDialog { Filter = "ZIP|*.zip", FileName = "frames.zip" };
        if (dialog.ShowDialog() != true) return;
        using var zip = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);
        for (var i = 0; i < _frames.Count; i++) zip.CreateEntryFromFile(_frames[i].Path, $"frame_{i + 1:000000}.png");
        SetStatus("Status: ZIP saved");
    }

    void SaveGif_Click(object sender, RoutedEventArgs e) => SaveAnimation("GIF|*.gif", "output.gif", ExportFormat.Gif);
    void SaveWebp_Click(object sender, RoutedEventArgs e) => SaveAnimation("WebP|*.webp", "output.webp", ExportFormat.Webp);

    async void SaveAnimation(string filter, string name, ExportFormat format)
    {
        if (_frames.Count == 0) return;
        var dialog = new SaveFileDialog { Filter = filter, FileName = name };
        if (dialog.ShowDialog() != true) return;
        var plan = CreateExportPlan();
        SetStatus("Status: preparing export");
        SetProgress(0, _frames.Count, "0/" + _frames.Count.ToString("N0"));
        try
        {
            await Task.Run(() => EncodeAnimation(dialog.FileName, format, plan));
            SetProgress(_frames.Count, _frames.Count, "done");
            SetStatus("Status: saved");
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(dialog.FileName) && new FileInfo(dialog.FileName).Length == 0)
                    File.Delete(dialog.FileName);
            }
            catch { }
            SetStatus("Status: save failed " + Short(ex.Message));
        }
    }

    ExportPlan CreateExportPlan()
    {
        var first = _frames.First();
        var source = LoadBitmapSource(first.Path);
        var crop = GetCropRect(source.PixelWidth, source.PixelHeight);
        var output = GetOutputSize(crop.Width, crop.Height);
        return new ExportPlan(crop.X, crop.Y, crop.Width, crop.Height, output.Width, output.Height, _overlays.Count > 0, LoopValue(), QualityValue());
    }

    void EncodeAnimation(string output, ExportFormat format, ExportPlan plan)
    {
        var dir = Path.Combine(_tempRoot, "render_" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir);
        var concat = Path.Combine(dir, "concat.txt");
        var inputFiles = new string[_frames.Count];

        if (plan.HasOverlays)
        {
            SetStatus("Status: rendering frames");
            for (var i = 0; i < _frames.Count; i++)
            {
                inputFiles[i] = Path.Combine(dir, $"render_{i + 1:000000}.png");
                SaveBitmap(RenderOutputFrame(_frames[i].Path), inputFiles[i]);
                if (i % 10 == 0 || i == _frames.Count - 1)
                {
                    SetStatus($"Status: rendering {i + 1:N0}/{_frames.Count:N0} frames");
                    SetProgress(i + 1, _frames.Count, "");
                }
            }
        }
        else
        {
            for (var i = 0; i < _frames.Count; i++) inputFiles[i] = _frames[i].Path;
        }

        using (var sw = new StreamWriter(concat, false, new UTF8Encoding(false)))
        {
            for (var i = 0; i < _frames.Count; i++)
            {
                sw.WriteLine($"file '{inputFiles[i].Replace("\\", "/").Replace("'", "'\\''")}'");
                sw.WriteLine($"duration {Math.Max(_frames[i].Duration, 1) / 1000.0:0.000000}");
            }
            sw.WriteLine($"file '{inputFiles[^1].Replace("\\", "/").Replace("'", "'\\''")}'");
        }

        SetStatus("Status: encoding 0/" + _frames.Count.ToString("N0") + " frames");
        SetProgress(0, _frames.Count, "0/" + _frames.Count.ToString("N0"));
        var tempOutput = Path.Combine(dir, "encoded" + Path.GetExtension(output));
        var filter = $"crop={plan.CropWidth}:{plan.CropHeight}:{plan.CropX}:{plan.CropY},scale={plan.OutputWidth}:{plan.OutputHeight}:flags=lanczos";
        var args = format == ExportFormat.Gif
            ? $"-f concat -safe 0 -i {Q(concat)} -filter_complex {Q(filter + ",split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=sierra2_4a")} -fps_mode vfr -loop {plan.Loop} {Q(tempOutput)}"
            : $"-f concat -safe 0 -i {Q(concat)} -vf {Q(filter)} -fps_mode vfr -an -c:v libwebp_anim -lossless 0 -quality {plan.Quality} -loop {plan.Loop} {Q(tempOutput)}";
        RunFfmpegWithProgress(args, _frames.Count, current =>
        {
            SetStatus($"Status: encoding {current:N0}/{_frames.Count:N0} frames");
            SetProgress(current, _frames.Count, "");
        });
        if (!File.Exists(tempOutput) || new FileInfo(tempOutput).Length == 0)
            throw new Exception("encoded file is empty");
        File.Copy(tempOutput, output, true);
    }

    void Crop_Click(object sender, RoutedEventArgs e)
    {
        var frame = FrameList.SelectedItem as FrameItem ?? _frames.FirstOrDefault();
        if (frame == null) return;
        var dialog = new CropWindow(frame.Path, _cropRect) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _cropRect = dialog.CropRect;
            RefreshResultPreview();
        }
    }

    void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        _cropRect = null;
        RefreshResultPreview();
        SetStatus("Status: crop reset");
    }

    void Insert_Click(object sender, RoutedEventArgs e)
    {
        var frame = FrameList.SelectedItem as FrameItem ?? _frames.FirstOrDefault();
        if (frame == null) return;
        var baseImage = RenderOutputFrame(frame.Path);
        var dialog = new OverlayWindow(baseImage, _overlays) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _overlays.Clear();
            foreach (var overlay in dialog.Overlays) _overlays.Add(overlay);
            RefreshResultPreview();
        }
    }

    void ResetInsert_Click(object sender, RoutedEventArgs e)
    {
        _overlays.Clear();
        RefreshResultPreview();
        SetStatus("Status: insert overlays reset");
    }

    BitmapSource RenderOutputFrame(string path)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => RenderOutputFrame(path));

        var source = LoadBitmapSource(path);
        var crop = GetCropRect(source.PixelWidth, source.PixelHeight);
        var cropped = new CroppedBitmap(source, crop);
        var output = GetOutputSize(crop.Width, crop.Height);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(cropped, new Rect(0, 0, output.Width, output.Height));
            foreach (var overlay in _overlays)
            {
                var rect = new Rect(
                    overlay.X * output.Width,
                    overlay.Y * output.Height,
                    overlay.Width * output.Width,
                    overlay.Height * output.Height);
                dc.PushOpacity(Math.Clamp(overlay.Opacity, 0, 1));
                if (overlay.Kind == "image" && File.Exists(overlay.Path))
                {
                    dc.DrawImage(LoadBitmapSource(overlay.Path), rect);
                }
                else if (overlay.Kind == "text")
                {
                    var color = (Color)ColorConverter.ConvertFromString(overlay.Color);
                    var text = new FormattedText(
                        overlay.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily(overlay.Font), overlay.Italic ? FontStyles.Italic : FontStyles.Normal, overlay.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                        Math.Clamp(overlay.FontSize, 8, Math.Max(8, rect.Height)),
                        new SolidColorBrush(color),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    var decorations = new TextDecorationCollection();
                    if (overlay.Underline) decorations.Add(TextDecorations.Underline[0]);
                    if (overlay.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
                    if (decorations.Count > 0) text.SetTextDecorations(decorations);
                    text.MaxTextWidth = Math.Max(1, rect.Width);
                    text.MaxTextHeight = Math.Max(1, rect.Height);
                    dc.DrawText(text, rect.TopLeft);
                }
                dc.Pop();
            }
        }

        var bmp = new RenderTargetBitmap(output.Width, output.Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    Int32Rect GetCropRect(int sourceWidth, int sourceHeight)
    {
        var rect = _cropRect ?? new Rect(0, 0, sourceWidth, sourceHeight);
        rect.Intersect(new Rect(0, 0, sourceWidth, sourceHeight));
        if (rect.Width < 1 || rect.Height < 1) rect = new Rect(0, 0, sourceWidth, sourceHeight);

        return new Int32Rect(
            Math.Max(0, (int)Math.Round(rect.X)),
            Math.Max(0, (int)Math.Round(rect.Y)),
            Math.Max(1, Math.Min(sourceWidth, (int)Math.Round(rect.Width))),
            Math.Max(1, Math.Min(sourceHeight, (int)Math.Round(rect.Height))));
    }

    (int Width, int Height) GetOutputSize(int cropWidth, int cropHeight)
    {
        var text = ((ComboBoxItem?)ResolutionBox.SelectedItem)?.Content?.ToString() ?? "Source";
        if (!int.TryParse(text, out var basis)) return (cropWidth, cropHeight);
        var ratio = cropWidth / (double)Math.Max(1, cropHeight);
        return ratio >= 1
            ? (basis, Math.Max(1, (int)Math.Round(basis / ratio)))
            : (Math.Max(1, (int)Math.Round(basis * ratio)), basis);
    }

    static BitmapSource LoadBitmapSource(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    static void SaveBitmap(BitmapSource source, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

    void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityText != null) QualityText.Text = ((int)e.NewValue).ToString();
    }

    int LoopValue() => int.TryParse(LoopText.Text, out var v) ? Math.Max(0, v) : 0;
    int QualityValue() => int.TryParse(QualityText.Text, out var v) ? Math.Clamp(v, 1, 100) : 100;

    enum ExportFormat { Gif, Webp }

    sealed record ExportPlan(int CropX, int CropY, int CropWidth, int CropHeight, int OutputWidth, int OutputHeight, bool HasOverlays, int Loop, int Quality);

    static (double duration, double fps, int width, int height) Probe(string path)
    {
        var text = RunFfmpegCapture($"-i {Q(path)}", allowFail: true);
        double duration = 0;
        var dm = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (dm.Success) duration = int.Parse(dm.Groups[1].Value) * 3600 + int.Parse(dm.Groups[2].Value) * 60 + double.Parse(dm.Groups[3].Value, CultureInfo.InvariantCulture);
        double fps = 0;
        var fm = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*fps");
        if (fm.Success) fps = double.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture);
        var sm = Regex.Match(text, @"(\d{3,5})x(\d{3,5})");
        var w = sm.Success ? int.Parse(sm.Groups[1].Value) : 0;
        var h = sm.Success ? int.Parse(sm.Groups[2].Value) : 0;
        return (duration, fps, w, h);
    }

    static string Q(object value) => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("\"", "\\\"") + "\"";

    static void RunFfmpeg(string args) => _ = RunFfmpegCapture(args);

    static void RunFfmpegWithProgress(string args, int totalFrames, Action<int> progress, CancellationToken token = default)
    {
        var psi = new ProcessStartInfo(FfmpegPath, "-hide_banner -y -nostats -progress pipe:2 " + args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var p = Process.Start(psi)!;
        var output = new StringBuilder();
        var lastSeenFrame = 0;
        var lastReportedFrame = 0;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        using var cancel = token.Register(() =>
        {
            try
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
            }
            catch { }
        });

        string? line;
        while ((line = p.StandardError.ReadLine()) != null)
        {
            token.ThrowIfCancellationRequested();
            output.AppendLine(line);
            var match = Regex.Match(line, @"^frame=(\d+)");
            if (!match.Success) continue;

            var frame = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (frame <= lastSeenFrame) continue;
            lastSeenFrame = frame;
            if (frame - lastReportedFrame < 10 && frame < totalFrames) continue;
            lastReportedFrame = frame;
            progress(Math.Min(frame, Math.Max(totalFrames, frame)));
        }

        p.WaitForExit();
        token.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) throw new Exception(output.ToString());
    }

    static string RunFfmpegCapture(string args, bool allowFail = false)
    {
        var psi = new ProcessStartInfo(FfmpegPath, "-hide_banner -y " + args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!allowFail && p.ExitCode != 0) throw new Exception(output);
        return output;
    }

    static BitmapImage LoadBitmap(string path, int decodeWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    static string Short(string text) => text.Length <= 180 ? text : text[^180..];

    static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, true);
                return;
            }
            catch
            {
                Thread.Sleep(120);
            }
        }
    }
}

public class FrameItem : INotifyPropertyChanged
{
    int _index;
    int _duration;
    public int Index { get => _index; set { _index = value; Changed(nameof(Index)); } }
    public required string Path { get; set; }
    public double Time { get; set; }
    public int Duration { get => _duration; set { _duration = value; Changed(nameof(Duration)); Changed(nameof(DurationText)); } }
    public BitmapImage? Thumbnail { get; set; }
    public string TimeText => TimeSpan.FromSeconds(Time).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    public string DurationText => Duration + "ms";
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class OverlaySpec
{
    public string Kind { get; set; } = "text";
    public string Path { get; set; } = "";
    public string Text { get; set; } = "Text";
    public string Font { get; set; } = "Segoe UI";
    public string Color { get; set; } = "#FFFFFF";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public double FontSize { get; set; } = 32;
    public double X { get; set; } = 0.08;
    public double Y { get; set; } = 0.08;
    public double Width { get; set; } = 0.25;
    public double Height { get; set; } = 0.12;
    public double Opacity { get; set; } = 1.0;
    public OverlaySpec Clone() => (OverlaySpec)MemberwiseClone();
}

public class ScreenRegionWindow : Window
{
    readonly Canvas _canvas = new();
    readonly ShapeRectangle _rect = new()
    {
        Stroke = Brushes.DeepSkyBlue,
        StrokeThickness = 3,
        Fill = new SolidColorBrush(Color.FromArgb(72, 0, 180, 255))
    };
    readonly StackPanel _panel = new();
    readonly Button _recordButton = MakeButton("\u25CF Record");
    readonly Button _reselectButton = MakeButton("Reselect");
    readonly Button _cancelButton = MakeButton("Cancel");
    readonly Action<Rect> _startRecording;
    readonly Func<Task> _stopRecording;
    readonly Func<Task> _cancelRecording;
    Point _start;
    bool _dragging;
    bool _hasSelection;
    bool _recording;
    Rect _selectedDip;
    public Rect SelectedRect { get; private set; }

    public ScreenRegionWindow(Action<Rect> startRecording, Func<Task> stopRecording, Func<Task> cancelRecording)
    {
        _startRecording = startRecording;
        _stopRecording = stopRecording;
        _cancelRecording = cancelRecording;
        Title = "Record Area";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Cursor = Cursors.Cross;

        var root = new Grid();
        Content = root;
        _canvas.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        root.Children.Add(_canvas);
        _canvas.Children.Add(_rect);
        _rect.Visibility = Visibility.Collapsed;

        _panel.Orientation = Orientation.Horizontal;
        _panel.HorizontalAlignment = HorizontalAlignment.Center;
        _panel.VerticalAlignment = VerticalAlignment.Top;
        _panel.Margin = new Thickness(0, 18, 0, 0);
        _panel.Background = Brushes.Transparent;
        root.Children.Add(_panel);

        var area = MakeButton("Area Select");
        area.Click += (_, _) => ArmSelect();
        _cancelButton.Click += async (_, _) => await CancelOrClose();
        _recordButton.Click += async (_, _) => await ToggleAreaRecording();
        _reselectButton.Click += async (_, _) =>
        {
            if (_recording)
            {
                await _stopRecording();
                _recording = false;
            }
            ResetForReselect();
        };
        _panel.Children.Add(area);
        _panel.Children.Add(_cancelButton);

        _canvas.MouseLeftButtonDown += CanvasDown;
        _canvas.MouseMove += CanvasMove;
        _canvas.MouseLeftButtonUp += CanvasUp;
        _canvas.MouseRightButtonDown += async (_, _) => await CancelOrClose();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) await CancelOrClose();
        };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            _canvas.Focus();
        };
    }

    static Button MakeButton(string text) => new()
    {
        Content = text,
        Width = 192,
        Height = 62,
        Margin = new Thickness(10, 6, 10, 6),
        Padding = new Thickness(12, 0, 12, 0),
        FontSize = 18
    };

    void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (_recording) return;
        if (_panel.IsMouseOver) return;
        _dragging = true;
        _hasSelection = false;
        _panel.Visibility = Visibility.Collapsed;
        _start = e.GetPosition(_canvas);
        _rect.Visibility = Visibility.Visible;
        SetRect(new Rect(_start, new Size(1, 1)));
        _canvas.CaptureMouse();
    }

    void CanvasMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        SetRect(new Rect(_start, e.GetPosition(_canvas)));
    }

    void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _canvas.ReleaseMouseCapture();
        var rect = new Rect(Canvas.GetLeft(_rect), Canvas.GetTop(_rect), _rect.Width, _rect.Height);
        if (rect.Width < 8 || rect.Height < 8)
        {
            ResetForReselect();
            return;
        }
        _selectedDip = rect;
        SelectedRect = ToPhysicalRect(rect);
        _hasSelection = true;
        ShowSelectionControls();
    }

    void ArmSelect()
    {
        if (_recording) return;
        Cursor = Cursors.Cross;
        _panel.Visibility = Visibility.Visible;
    }

    void ResetForReselect()
    {
        if (_recording) return;
        SetSelectionOverlayMode();
        _hasSelection = false;
        _dragging = false;
        _rect.Visibility = Visibility.Collapsed;
        _panel.Children.Clear();
        var area = MakeButton("Area Select");
        area.Click += (_, _) => ArmSelect();
        _panel.Children.Add(area);
        _panel.Children.Add(_cancelButton);
        _panel.HorizontalAlignment = HorizontalAlignment.Center;
        _panel.VerticalAlignment = VerticalAlignment.Top;
        _panel.Margin = new Thickness(0, 18, 0, 0);
        _panel.Visibility = Visibility.Visible;
    }

    void ShowSelectionControls()
    {
        _panel.Children.Clear();
        _panel.Children.Add(_recordButton);
        _panel.Children.Add(_reselectButton);
        _panel.Children.Add(_cancelButton);
        _recordButton.Content = _recording ? "\u25A0 Stop" : "\u25CF Record";
        _recordButton.Foreground = _recording ? Brushes.Black : new SolidColorBrush(Color.FromRgb(225, 29, 72));
        _recordButton.FontWeight = FontWeights.Bold;
        _panel.Visibility = Visibility.Visible;
        PositionPanelOutsideSelection();
    }

    void SetSelectionOverlayMode()
    {
        Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        _canvas.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        _rect.Fill = new SolidColorBrush(Color.FromArgb(72, 0, 180, 255));
        _rect.Stroke = Brushes.DeepSkyBlue;
    }

    void SetRecordingOverlayMode()
    {
        Background = Brushes.Transparent;
        _canvas.Background = Brushes.Transparent;
        _rect.Fill = Brushes.Transparent;
        _rect.Stroke = Brushes.DeepSkyBlue;
    }

    void PositionPanelOutsideSelection()
    {
        var top = _selectedDip.Top - 84;
        if (top < 8) top = _selectedDip.Bottom + 12;
        if (top + 84 > Height) top = Math.Max(8, _selectedDip.Top - 84);
        var left = Math.Clamp(_selectedDip.Left, 8, Math.Max(8, Width - 660));
        _panel.HorizontalAlignment = HorizontalAlignment.Left;
        _panel.VerticalAlignment = VerticalAlignment.Top;
        _panel.Margin = new Thickness(left, top, 0, 0);
    }

    async Task ToggleAreaRecording()
    {
        if (!_recording)
        {
            if (!_hasSelection) return;
            SetRecordingOverlayMode();
            _startRecording(SelectedRect);
            _recording = true;
            ShowSelectionControls();
            return;
        }

        await _stopRecording();
        _recording = false;
        DialogResult = true;
    }

    async Task CancelOrClose()
    {
        if (_recording)
        {
            await _cancelRecording();
            _recording = false;
        }
        DialogResult = false;
    }

    Rect ToPhysicalRect(Rect rect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            (Left + rect.X) * dpi.DpiScaleX,
            (Top + rect.Y) * dpi.DpiScaleY,
            rect.Width * dpi.DpiScaleX,
            rect.Height * dpi.DpiScaleY);
    }

    void SetRect(Rect r)
    {
        r = new Rect(Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom), Math.Abs(r.Width), Math.Abs(r.Height));
        Canvas.SetLeft(_rect, r.X);
        Canvas.SetTop(_rect, r.Y);
        _rect.Width = Math.Max(1, r.Width);
        _rect.Height = Math.Max(1, r.Height);
    }
}

public class CropWindow : Window
{
    readonly Canvas _canvas = new();
    readonly Image _image = new();
    readonly ShapeRectangle _rect = new() { Stroke = Brushes.OrangeRed, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(48, 255, 135, 8)) };
    readonly Border _resizeHandle = new()
    {
        Width = 14,
        Height = 14,
        Background = Brushes.DeepSkyBlue,
        BorderBrush = Brushes.White,
        BorderThickness = new Thickness(1),
        Cursor = Cursors.SizeNWSE
    };
    readonly BitmapSource _source;
    Point _start;
    Point _last;
    bool _dragging;
    bool _moving;
    bool _resizing;
    double _ratio;
    public Rect CropRect { get; private set; }

    public CropWindow(string imagePath, Rect? initial)
    {
        Title = "Crop Selection";
        Width = 960;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _source = MainWindowLoadBitmap(imagePath);
        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        foreach (var (label, ratio) in new[] { ("Free", 0.0), ("1:1", 1.0), ("16:9", 16.0 / 9.0), ("9:16", 9.0 / 16.0), ("3:2", 3.0 / 2.0), ("2:3", 2.0 / 3.0) })
        {
            var b = new Button { Content = label, MinWidth = 72, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (_, _) => SetRatioMode(ratio);
            buttons.Children.Add(b);
        }
        var apply = new Button { Content = "Apply", MinWidth = 96, Height = 36, FontWeight = FontWeights.Bold };
        apply.Click += (_, _) => { CommitCrop(); DialogResult = true; };
        buttons.Children.Add(apply);

        var view = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(view);
        var scale = Math.Min(850.0 / _source.PixelWidth, 560.0 / _source.PixelHeight);
        scale = Math.Min(scale, 1.0);
        _image.Source = _source;
        _image.Width = _source.PixelWidth * scale;
        _image.Height = _source.PixelHeight * scale;
        _canvas.Width = _image.Width;
        _canvas.Height = _image.Height;
        _canvas.Children.Add(_image);
        _canvas.Children.Add(_rect);
        _canvas.Children.Add(_resizeHandle);
        _resizeHandle.MouseLeftButtonDown += HandleDown;
        _resizeHandle.MouseMove += CanvasMove;
        _resizeHandle.MouseLeftButtonUp += CanvasUp;

        var init = initial ?? new Rect(0, 0, _source.PixelWidth, _source.PixelHeight);
        SetRect(new Rect(init.X * scale, init.Y * scale, init.Width * scale, init.Height * scale));
        _canvas.MouseLeftButtonDown += CanvasDown;
        _canvas.MouseMove += CanvasMove;
        _canvas.MouseLeftButtonUp += CanvasUp;
    }

    void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        var current = CurrentRect();
        _start = p;
        _last = p;
        _moving = current.Contains(p);
        _dragging = !_moving;
        _resizing = false;
        if (_dragging) SetRect(new Rect(_start, new Size(1, 1)));
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    void CanvasMove(object sender, MouseEventArgs e)
    {
        if (!_dragging && !_moving && !_resizing) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        if (_moving)
        {
            var dx = p.X - _last.X;
            var dy = p.Y - _last.Y;
            var r = CurrentRect();
            r.X = Math.Clamp(r.X + dx, 0, Math.Max(0, _canvas.Width - r.Width));
            r.Y = Math.Clamp(r.Y + dy, 0, Math.Max(0, _canvas.Height - r.Height));
            SetRect(r);
            _last = p;
            return;
        }
        if (_resizing)
        {
            ResizeFromHandle(p);
            return;
        }
        SetRect(FitRectToCanvas(MakeDragRect(_start, p)));
    }

    void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _moving = false;
        _resizing = false;
        _canvas.ReleaseMouseCapture();
        _resizeHandle.ReleaseMouseCapture();
        e.Handled = true;
    }

    void HandleDown(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        _dragging = false;
        _moving = false;
        _last = e.GetPosition(_canvas);
        _resizeHandle.CaptureMouse();
        e.Handled = true;
    }

    void SetRatioMode(double ratio)
    {
        _ratio = ratio;
        if (_ratio <= 0) return;
        var maxW = _canvas.Width * 0.72;
        var maxH = _canvas.Height * 0.72;
        var w = maxW;
        var h = w / _ratio;
        if (h > maxH)
        {
            h = maxH;
            w = h * _ratio;
        }
        var x = (_canvas.Width - w) / 2;
        var y = (_canvas.Height - h) / 2;
        SetRect(new Rect(x, y, w, h));
    }

    Rect MakeDragRect(Point a, Point b)
    {
        var w = b.X - a.X;
        var h = b.Y - a.Y;
        if (_ratio > 0)
        {
            var signX = Math.Sign(w == 0 ? 1 : w);
            var signY = Math.Sign(h == 0 ? 1 : h);
            if (Math.Abs(w / Math.Max(1, h)) > _ratio) h = signY * Math.Abs(w) / _ratio;
            else w = signX * Math.Abs(h) * _ratio;
            b = new Point(a.X + w, a.Y + h);
        }
        return new Rect(a, b);
    }

    void ResizeFromHandle(Point p)
    {
        var r = CurrentRect();
        var newW = Math.Max(8, p.X - r.X);
        var newH = Math.Max(8, p.Y - r.Y);
        if (_ratio > 0)
        {
            newH = newW / _ratio;
            if (r.Y + newH > _canvas.Height)
            {
                newH = _canvas.Height - r.Y;
                newW = newH * _ratio;
            }
        }
        newW = Math.Min(newW, _canvas.Width - r.X);
        newH = Math.Min(newH, _canvas.Height - r.Y);
        if (_ratio > 0)
        {
            if (newW / Math.Max(1, newH) > _ratio) newW = newH * _ratio;
            else newH = newW / _ratio;
        }
        SetRect(new Rect(r.X, r.Y, Math.Max(8, newW), Math.Max(8, newH)));
    }

    void SetRect(Rect r)
    {
        r = FitRectToCanvas(r);
        Canvas.SetLeft(_rect, r.X);
        Canvas.SetTop(_rect, r.Y);
        _rect.Width = Math.Max(1, r.Width);
        _rect.Height = Math.Max(1, r.Height);
        Canvas.SetLeft(_resizeHandle, r.Right - _resizeHandle.Width / 2);
        Canvas.SetTop(_resizeHandle, r.Bottom - _resizeHandle.Height / 2);
    }

    Rect FitRectToCanvas(Rect r)
    {
        r = new Rect(Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom), Math.Abs(r.Width), Math.Abs(r.Height));
        if (_ratio > 0 && r.Width > 1 && r.Height > 1)
        {
            if (r.Width / r.Height > _ratio) r.Width = r.Height * _ratio;
            else r.Height = r.Width / _ratio;
        }
        if (r.Width > _canvas.Width)
        {
            r.Width = _canvas.Width;
            if (_ratio > 0) r.Height = r.Width / _ratio;
        }
        if (r.Height > _canvas.Height)
        {
            r.Height = _canvas.Height;
            if (_ratio > 0) r.Width = r.Height * _ratio;
        }
        r.X = Math.Clamp(r.X, 0, Math.Max(0, _canvas.Width - r.Width));
        r.Y = Math.Clamp(r.Y, 0, Math.Max(0, _canvas.Height - r.Height));
        return r;
    }

    Rect CurrentRect()
    {
        return new Rect(Canvas.GetLeft(_rect), Canvas.GetTop(_rect), _rect.Width, _rect.Height);
    }

    void CommitCrop()
    {
        var sx = _source.PixelWidth / _canvas.Width;
        var sy = _source.PixelHeight / _canvas.Height;
        CropRect = new Rect(Canvas.GetLeft(_rect) * sx, Canvas.GetTop(_rect) * sy, _rect.Width * sx, _rect.Height * sy);
    }

    static BitmapSource MainWindowLoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

public class OverlayWindow : Window
{
    readonly Canvas _canvas = new();
    readonly BitmapSource _baseImage;
    readonly Slider _opacity = new() { Minimum = 0, Maximum = 100, Value = 100, Width = 150 };
    readonly TextBox _opacityNumber = new() { Text = "100", Width = 52 };
    readonly StackPanel _textTools = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    readonly ComboBox _fontBox = new() { Width = 150, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
    readonly TextBox _fontSizeBox = new() { Width = 48, Height = 30, Text = "32", Margin = new Thickness(0, 0, 6, 0) };
    readonly ComboBox _colorBox = new() { Width = 96, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
    readonly System.Windows.Controls.Primitives.ToggleButton _boldButton = new() { Content = "B", Width = 34, Height = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 4, 0) };
    readonly System.Windows.Controls.Primitives.ToggleButton _italicButton = new() { Content = "I", Width = 34, Height = 30, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 4, 0) };
    readonly System.Windows.Controls.Primitives.ToggleButton _underlineButton = new() { Content = "U", Width = 34, Height = 30, Margin = new Thickness(0, 0, 4, 0) };
    readonly System.Windows.Controls.Primitives.ToggleButton _strikeButton = new() { Content = "S", Width = 34, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
    readonly System.Collections.Generic.List<FrameworkElement> _items = new();
    FrameworkElement? _selected;
    TextBox? _editingTextBox;
    Point _dragStart;
    bool _dragging;
    bool _resizing;
    bool _updatingTextTools;
    public ObservableCollection<OverlaySpec> Overlays { get; } = new();

    public OverlayWindow(BitmapSource baseImage, ObservableCollection<OverlaySpec> overlays)
    {
        Title = "Insert Image/Text";
        Width = 980;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;
        Focusable = true;
        _baseImage = baseImage;
        foreach (var o in overlays) Overlays.Add(o.Clone());

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;
        BuildTextTools();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(tools, Dock.Bottom);
        root.Children.Add(tools);

        var addImage = new Button { Content = "Add Image", Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        addImage.Click += (_, _) => AddImage();
        var addText = new Button { Content = "Add Text", Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        addText.Click += (_, _) => AddText();
        var delete = new Button { Content = "Delete Selected", Height = 36, Margin = new Thickness(8, 0, 8, 0) };
        delete.Click += (_, _) => DeleteSelected();
        var apply = new Button { Content = "Apply", Height = 36, MinWidth = 92, FontWeight = FontWeights.Bold };
        apply.Click += (_, _) => { SyncBack(); DialogResult = true; };
        _opacity.ValueChanged += (_, _) => ApplyOpacity();
        _opacityNumber.TextChanged += (_, _) => { if (double.TryParse(_opacityNumber.Text, out var v)) _opacity.Value = Math.Clamp(v, 0, 100); };
        foreach (var c in new UIElement[] { addImage, addText, new TextBlock { Text = "Opacity", VerticalAlignment = VerticalAlignment.Center }, _opacity, _opacityNumber, delete, apply }) tools.Children.Add(c);

        var scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var scale = Math.Min(850.0 / _baseImage.PixelWidth, 560.0 / _baseImage.PixelHeight);
        scale = Math.Min(scale, 1.0);
        _canvas.Width = _baseImage.PixelWidth * scale;
        _canvas.Height = _baseImage.PixelHeight * scale;
        _canvas.Background = new ImageBrush(_baseImage) { Stretch = Stretch.Fill };
        _canvas.AllowDrop = true;
        _canvas.Drop += CanvasDrop;
        _canvas.DragOver += CanvasDragOver;
        _canvas.MouseLeftButtonDown += CanvasDown;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && _editingTextBox?.IsKeyboardFocusWithin != true) DeleteSelected();
        };
        Loaded += (_, _) => Focus();
        Rebuild();
    }

    void BuildTextTools()
    {
        foreach (var font in new[] { "Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana", "Malgun Gothic", "Noto Sans KR", "Times New Roman", "Consolas" })
            _fontBox.Items.Add(font);
        _fontBox.SelectedItem = "Segoe UI";

        foreach (var color in new[] { "#FFFFFF", "#000000", "#FF0000", "#00AEEF", "#FFD400", "#00C853", "#FF7A00", "#E11D48" })
            _colorBox.Items.Add(color);
        _colorBox.SelectedItem = "#FFFFFF";

        _textTools.Children.Add(new TextBlock { Text = "Font", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _textTools.Children.Add(_fontBox);
        _textTools.Children.Add(new TextBlock { Text = "Size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _textTools.Children.Add(_fontSizeBox);
        _textTools.Children.Add(new TextBlock { Text = "Color", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _textTools.Children.Add(_colorBox);
        _textTools.Children.Add(_boldButton);
        _textTools.Children.Add(_italicButton);
        _textTools.Children.Add(_underlineButton);
        _textTools.Children.Add(_strikeButton);

        _fontBox.SelectionChanged += (_, _) => ApplyTextTools();
        _colorBox.SelectionChanged += (_, _) => ApplyTextTools();
        _fontSizeBox.TextChanged += (_, _) => ApplyTextTools();
        _boldButton.Checked += (_, _) => ApplyTextTools();
        _boldButton.Unchecked += (_, _) => ApplyTextTools();
        _italicButton.Checked += (_, _) => ApplyTextTools();
        _italicButton.Unchecked += (_, _) => ApplyTextTools();
        _underlineButton.Checked += (_, _) => ApplyTextTools();
        _underlineButton.Unchecked += (_, _) => ApplyTextTools();
        _strikeButton.Checked += (_, _) => ApplyTextTools();
        _strikeButton.Unchecked += (_, _) => ApplyTextTools();
    }

    void AddImage()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All|*.*" };
        if (dialog.ShowDialog() != true) return;
        AddImagePath(dialog.FileName, new Point(_canvas.Width * 0.08, _canvas.Height * 0.08));
    }

    void AddImagePath(string path, Point point)
    {
        if (!File.Exists(path)) return;
        SyncBack();
        var width = Math.Min(_canvas.Width * 0.24, 180);
        var ratio = ImageRatio(path);
        var height = width / ratio;
        Overlays.Add(new OverlaySpec
        {
            Kind = "image",
            Path = path,
            X = Math.Clamp(point.X / _canvas.Width, 0, 0.95),
            Y = Math.Clamp(point.Y / _canvas.Height, 0, 0.95),
            Width = Math.Clamp(width / _canvas.Width, 0.03, 1),
            Height = Math.Clamp(height / _canvas.Height, 0.03, 1)
        });
        Rebuild();
    }

    void AddText() => AddTextAt(new Point(_canvas.Width * 0.08, _canvas.Height * 0.08));

    void AddTextAt(Point point)
    {
        SyncBack();
        Overlays.Add(new OverlaySpec
        {
            Kind = "text",
            Text = "Double click text",
            X = Math.Clamp(point.X / _canvas.Width, 0, 0.88),
            Y = Math.Clamp(point.Y / _canvas.Height, 0, 0.88),
            Width = 0.36,
            Height = 0.12
        });
        Rebuild();
    }

    void DeleteSelected()
    {
        SyncBack();
        if (_selected?.Tag is OverlaySpec spec) Overlays.Remove(spec);
        _selected = null;
        _editingTextBox = null;
        _textTools.Visibility = Visibility.Collapsed;
        Rebuild();
    }

    void Rebuild()
    {
        _canvas.Children.Clear();
        _items.Clear();
        foreach (var overlay in Overlays)
        {
            var item = new Border
            {
                Tag = overlay,
                BorderBrush = Brushes.OrangeRed,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                Opacity = overlay.Opacity,
                MinWidth = 24,
                MinHeight = 20
            };
            var grid = new Grid { ClipToBounds = false };
            item.Child = grid;

            if (overlay.Kind == "image" && File.Exists(overlay.Path))
            {
                grid.Children.Add(new Image { Source = LoadForOverlay(overlay.Path), Stretch = Stretch.Fill, IsHitTestVisible = false });
            }
            else
            {
                var text = new TextBox
                {
                    Text = overlay.Text,
                    FontFamily = new FontFamily(overlay.Font),
                    FontSize = overlay.FontSize,
                    Foreground = (Brush)new BrushConverter().ConvertFromString(overlay.Color)!,
                    Background = Brushes.Transparent,
                    FontWeight = overlay.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = overlay.Italic ? FontStyles.Italic : FontStyles.Normal,
                    TextDecorations = TextDecorationsFor(overlay),
                    BorderThickness = new Thickness(0),
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    IsHitTestVisible = false
                };
                text.TextChanged += (_, _) => overlay.Text = text.Text;
                text.LostKeyboardFocus += (_, e) =>
                {
                    if (e.NewFocus is DependencyObject next && (_textTools == next || _textTools.IsAncestorOf(next))) return;
                    text.IsHitTestVisible = false;
                    if (_editingTextBox == text)
                    {
                        _editingTextBox = null;
                        _textTools.Visibility = Visibility.Collapsed;
                    }
                };
                grid.Children.Add(text);
            }

            var handle = new Border
            {
                Width = 13,
                Height = 13,
                Background = Brushes.DeepSkyBlue,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                Visibility = Visibility.Collapsed,
                Tag = item
            };
            handle.MouseLeftButtonDown += ResizeHandleDown;
            grid.Children.Add(handle);

            item.Width = Math.Max(24, overlay.Width * _canvas.Width);
            item.Height = Math.Max(20, overlay.Height * _canvas.Height);
            Canvas.SetLeft(item, overlay.X * _canvas.Width);
            Canvas.SetTop(item, overlay.Y * _canvas.Height);
            item.MouseLeftButtonDown += ItemDown;
            item.MouseMove += ItemMove;
            item.MouseLeftButtonUp += ItemUp;
            item.MouseWheel += ItemWheel;
            _canvas.Children.Add(item);
            _items.Add(item);
        }
        _textTools.Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255));
        _textTools.Effect = null;
        Panel.SetZIndex(_textTools, 1000);
        _canvas.Children.Add(_textTools);
        UpdateSelectionVisuals();
    }

    void Select(FrameworkElement item)
    {
        _selected = item;
        _opacity.Value = item.Opacity * 100;
        _opacityNumber.Text = ((int)_opacity.Value).ToString();
        if (item.Tag is not OverlaySpec { Kind: "text" })
        {
            _editingTextBox = null;
            _textTools.Visibility = Visibility.Collapsed;
        }
        UpdateSelectionVisuals();
    }

    void ItemDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement item) return;
        Select(item);
        if (e.ClickCount >= 2 && item.Tag is OverlaySpec { Kind: "text" })
        {
            BeginTextEdit(item);
            e.Handled = true;
            return;
        }
        _dragging = true;
        _resizing = false;
        _dragStart = e.GetPosition(_canvas);
        item.CaptureMouse();
        Focus();
        e.Handled = true;
    }

    void ItemMove(object sender, MouseEventArgs e)
    {
        if ((!_dragging && !_resizing) || sender is not FrameworkElement item || _selected != item) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        if (_resizing)
        {
            ResizeItem(item, dx, dy);
        }
        else
        {
            Canvas.SetLeft(item, Math.Clamp(Canvas.GetLeft(item) + dx, 0, Math.Max(0, _canvas.Width - item.Width)));
            Canvas.SetTop(item, Math.Clamp(Canvas.GetTop(item) + dy, 0, Math.Max(0, _canvas.Height - item.Height)));
        }
        _dragStart = p;
    }

    void ItemUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _resizing = false;
        if (sender is FrameworkElement item) item.ReleaseMouseCapture();
    }

    void ItemWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement item) return;
        Select(item);
        var factor = e.Delta > 0 ? 1.08 : 0.92;
        item.Width = Math.Clamp(item.Width * factor, 20, _canvas.Width);
        item.Height = Math.Clamp(item.Height * factor, 18, _canvas.Height);
        e.Handled = true;
    }

    void ResizeHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: FrameworkElement item }) return;
        Select(item);
        _dragging = false;
        _resizing = true;
        _dragStart = e.GetPosition(_canvas);
        item.CaptureMouse();
        e.Handled = true;
    }

    void ResizeItem(FrameworkElement item, double dx, double dy)
    {
        var maxW = Math.Max(24, _canvas.Width - Canvas.GetLeft(item));
        var maxH = Math.Max(20, _canvas.Height - Canvas.GetTop(item));
        var newW = Math.Clamp(item.Width + dx, 24, maxW);
        var newH = Math.Clamp(item.Height + dy, 20, maxH);
        if (item.Tag is OverlaySpec { Kind: "image" })
        {
            var ratio = Math.Max(0.01, item.Width / Math.Max(1, item.Height));
            newH = Math.Clamp(newW / ratio, 20, maxH);
        }
        item.Width = newW;
        item.Height = newH;
    }

    void BeginTextEdit(FrameworkElement item)
    {
        var tb = FindChild<TextBox>(item);
        if (tb == null || item.Tag is not OverlaySpec overlay) return;
        _editingTextBox = tb;
        LoadTextTools(overlay);
        _textTools.Visibility = Visibility.Visible;
        PositionTextTools(item);
        tb.IsHitTestVisible = true;
        tb.Focus();
        tb.SelectAll();
    }

    void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != _canvas) return;
        if (e.ClickCount >= 2)
        {
            AddTextAt(e.GetPosition(_canvas));
            e.Handled = true;
            return;
        }
        _selected = null;
        _editingTextBox = null;
        _textTools.Visibility = Visibility.Collapsed;
        UpdateSelectionVisuals();
        Focus();
    }

    void CanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasImageDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void CanvasDrop(object sender, DragEventArgs e)
    {
        if (!HasImageDrop(e)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var path = files.FirstOrDefault(IsImageFile);
        if (path != null) AddImagePath(path, e.GetPosition(_canvas));
        e.Handled = true;
    }

    void UpdateSelectionVisuals()
    {
        foreach (var item in _items)
        {
            var selected = item == _selected;
            if (item is Border border)
            {
                border.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                border.BorderBrush = selected ? Brushes.DeepSkyBlue : Brushes.OrangeRed;
                border.Background = selected ? new SolidColorBrush(Color.FromArgb(32, 0, 180, 255)) : new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
                if (border.Child is Grid grid)
                {
                    foreach (var handle in grid.Children.OfType<Border>().Where(x => x.Tag == item))
                        handle.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        if (_selected != null && _textTools.Visibility == Visibility.Visible) PositionTextTools(_selected);
    }

    void PositionTextTools(FrameworkElement item)
    {
        _textTools.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Min(_textTools.DesiredSize.Width, _canvas.Width);
        var height = _textTools.DesiredSize.Height;
        var left = Canvas.GetLeft(item);
        var top = Canvas.GetTop(item) - height - 8;
        if (top < 6) top = Canvas.GetTop(item) + item.Height + 8;
        if (top + height > _canvas.Height) top = Math.Max(6, _canvas.Height - height - 6);
        left = Math.Clamp(left, 6, Math.Max(6, _canvas.Width - width - 6));
        Canvas.SetLeft(_textTools, left);
        Canvas.SetTop(_textTools, top);
    }

    void ApplyOpacity()
    {
        if (_selected == null) return;
        _selected.Opacity = _opacity.Value / 100.0;
        _opacityNumber.Text = ((int)_opacity.Value).ToString();
    }

    void SyncBack()
    {
        foreach (var item in _items)
        {
            if (item.Tag is not OverlaySpec overlay) continue;
            overlay.X = Canvas.GetLeft(item) / _canvas.Width;
            overlay.Y = Canvas.GetTop(item) / _canvas.Height;
            overlay.Width = item.Width / _canvas.Width;
            overlay.Height = item.Height / _canvas.Height;
            overlay.Opacity = item.Opacity;
            if (FindChild<TextBox>(item) is TextBox tb)
            {
                overlay.Text = tb.Text;
                overlay.Font = tb.FontFamily.Source;
                overlay.FontSize = tb.FontSize;
                overlay.Color = tb.Foreground is SolidColorBrush brush ? brush.Color.ToString(CultureInfo.InvariantCulture) : overlay.Color;
                overlay.Bold = tb.FontWeight == FontWeights.Bold;
                overlay.Italic = tb.FontStyle == FontStyles.Italic;
                overlay.Underline = tb.TextDecorations?.Contains(TextDecorations.Underline[0]) == true;
                overlay.Strike = tb.TextDecorations?.Contains(TextDecorations.Strikethrough[0]) == true;
            }
        }
    }

    void LoadTextTools(OverlaySpec overlay)
    {
        _updatingTextTools = true;
        _fontBox.SelectedItem = _fontBox.Items.Contains(overlay.Font) ? overlay.Font : "Segoe UI";
        _fontSizeBox.Text = Math.Round(Math.Clamp(overlay.FontSize, 8, 240)).ToString(CultureInfo.InvariantCulture);
        _colorBox.SelectedItem = _colorBox.Items.Contains(overlay.Color) ? overlay.Color : "#FFFFFF";
        _boldButton.IsChecked = overlay.Bold;
        _italicButton.IsChecked = overlay.Italic;
        _underlineButton.IsChecked = overlay.Underline;
        _strikeButton.IsChecked = overlay.Strike;
        _updatingTextTools = false;
    }

    void ApplyTextTools()
    {
        if (_updatingTextTools || _editingTextBox == null || _selected?.Tag is not OverlaySpec overlay) return;
        var font = _fontBox.SelectedItem?.ToString() ?? "Segoe UI";
        var color = _colorBox.SelectedItem?.ToString() ?? "#FFFFFF";
        var size = double.TryParse(_fontSizeBox.Text, out var parsed) ? Math.Clamp(parsed, 8, 240) : overlay.FontSize;
        overlay.Font = font;
        overlay.FontSize = size;
        overlay.Color = color;
        overlay.Bold = _boldButton.IsChecked == true;
        overlay.Italic = _italicButton.IsChecked == true;
        overlay.Underline = _underlineButton.IsChecked == true;
        overlay.Strike = _strikeButton.IsChecked == true;

        _editingTextBox.FontFamily = new FontFamily(overlay.Font);
        _editingTextBox.FontSize = overlay.FontSize;
        _editingTextBox.Foreground = (Brush)new BrushConverter().ConvertFromString(overlay.Color)!;
        _editingTextBox.FontWeight = overlay.Bold ? FontWeights.Bold : FontWeights.Normal;
        _editingTextBox.FontStyle = overlay.Italic ? FontStyles.Italic : FontStyles.Normal;
        _editingTextBox.TextDecorations = TextDecorationsFor(overlay);
    }

    static TextDecorationCollection? TextDecorationsFor(OverlaySpec overlay)
    {
        var decorations = new TextDecorationCollection();
        if (overlay.Underline) decorations.Add(TextDecorations.Underline[0]);
        if (overlay.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
        return decorations.Count == 0 ? null : decorations;
    }

    static bool HasImageDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        return files.Any(IsImageFile);
    }

    static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
    }

    static double ImageRatio(string path)
    {
        try
        {
            var bmp = LoadForOverlay(path);
            return Math.Max(0.01, bmp.PixelWidth / (double)Math.Max(1, bmp.PixelHeight));
        }
        catch
        {
            return 1.0;
        }
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var found = FindChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    static BitmapSource LoadForOverlay(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
