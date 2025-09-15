using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Vorbis;

namespace NoteblockPitchShifter
{
    public partial class Form1 : Form
    {
        private IWavePlayer waveOut;
        private WaveStream audioReader;
        private VarispeedSampleProvider pitchProvider;

        private string[] audioFiles = new string[0];
        private int currentIndex = 0;

        public Form1()
        {
            InitializeComponent();

            button1.Click += Button1_Click;
            button2.Click += Button2_Click;
            button3.Click += Button3_Click;
            button4.Click += Button4_Click;
            button5.Click += Button5_Click;

            trackBar1.Minimum = -1200;
            trackBar1.Maximum = 1200;
            trackBar1.Value = 0;
            trackBar1.TickFrequency = 100;
            trackBar1.Scroll += TrackBar1_Scroll;

            comboBox1.SelectedIndexChanged += ComboBox1_SelectedIndexChanged;

            textBox3.ReadOnly = false;
            textBox3.KeyDown += TextBox3_KeyDown;
            textBox3.Leave += TextBox3_Leave;

            textBox1.KeyDown += TextBox1_KeyDown;
            textBox1.Leave += TextBox1_Leave;

            checkBox1.Checked = false;

            UpdatePitchTextBox(trackBar1.Value);
        }

        #region VarispeedSampleProvider

        public class VarispeedSampleProvider : ISampleProvider, IDisposable
        {
            private readonly WaveStream sourceStream;
            private readonly ISampleProvider source;
            private float playbackRate;
            private readonly WaveFormat waveFormat;
            private double sourcePosition;
            private readonly int channels;
            private readonly int bytesPerSample;
            private readonly object lockObj = new object();
            private readonly float[] tempBuffer;
            private bool disposed;

            public VarispeedSampleProvider(WaveStream sourceStream, float initialPlaybackRate)
            {
                if (sourceStream == null) throw new ArgumentNullException(nameof(sourceStream));
                if (initialPlaybackRate <= 0)
                    throw new ArgumentOutOfRangeException(nameof(initialPlaybackRate), "must be > 0");

                this.sourceStream = sourceStream;
                this.source = sourceStream.ToSampleProvider();
                this.waveFormat = source.WaveFormat;
                this.playbackRate = initialPlaybackRate;
                this.sourcePosition = 0.0;
                this.channels = waveFormat.Channels;
                this.bytesPerSample = waveFormat.BitsPerSample / 8;
                this.tempBuffer = new float[channels * 2];
                this.disposed = false;
            }

            public WaveFormat WaveFormat => waveFormat;

            public float PlaybackRate
            {
                get => playbackRate;
                set
                {
                    if (value <= 0)
                        throw new ArgumentOutOfRangeException("PlaybackRate must be > 0");
                    playbackRate = value;
                }
            }

            public void ResetPosition()
            {
                lock (lockObj)
                {
                    sourcePosition = 0.0;
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int samplesWritten = 0;
                long maxSampleIndex = sourceStream.Length / (bytesPerSample * channels);

                lock (lockObj)
                {
                    while (samplesWritten < count)
                    {
                        int baseSampleIndex = (int)Math.Floor(sourcePosition);
                        float frac = (float)(sourcePosition - baseSampleIndex);

                        if (baseSampleIndex > maxSampleIndex - 2)
                        {
                            baseSampleIndex = (int)(maxSampleIndex - 2);
                            if (baseSampleIndex < 0) baseSampleIndex = 0;
                            frac = 0f;
                            if (samplesWritten == 0)
                                break;
                        }

                        long bytePosition = baseSampleIndex * bytesPerSample * channels;

                        if (sourceStream.CanSeek && sourceStream.Position != bytePosition)
                        {
                            sourceStream.Position = bytePosition;
                        }

                        int neededSamples = channels * 2;
                        int read = source.Read(tempBuffer, 0, neededSamples);
                        if (read == 0)
                            break;

                        for (int ch = 0; ch < channels; ch++)
                        {
                            float s0 = tempBuffer[ch];
                            float s1 = (read > channels + ch) ? tempBuffer[channels + ch] : s0;
                            float sampleVal = s0 * (1 - frac) + s1 * frac;

                            if ((offset + samplesWritten) < buffer.Length)
                                buffer[offset + samplesWritten] = sampleVal;

                            samplesWritten++;
                            if (samplesWritten >= count)
                                break;
                        }

                        sourcePosition += playbackRate;
                    }
                }

                return samplesWritten;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        sourceStream?.Dispose();
                    }
                    disposed = true;
                }
            }

            ~VarispeedSampleProvider()
            {
                Dispose(false);
            }
        }

        #endregion

        private void UpdatePitchTextBox(int cent)
        {
            textBox3.Text = cent.ToString();
        }

        private void TrackBar1_Scroll(object sender, EventArgs e)
        {
            int cent = trackBar1.Value;
            UpdatePitchTextBox(cent);
        }

        private void TextBox3_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyCentFromTextBox();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void TextBox3_Leave(object sender, EventArgs e)
        {
            ApplyCentFromTextBox();
        }

        private void ApplyCentFromTextBox()
        {
            if (!int.TryParse(textBox3.Text.Trim(), out int cent))
            {
                UpdatePitchTextBox(trackBar1.Value);
                return;
            }
            cent = Clamp(cent, trackBar1.Minimum, trackBar1.Maximum);
            trackBar1.Value = cent;
            UpdatePitchTextBox(cent);
        }

        private int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void TextBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyFolderPathFromTextBox();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void TextBox1_Leave(object sender, EventArgs e)
        {
            ApplyFolderPathFromTextBox();
        }

        private void ApplyFolderPathFromTextBox()
        {
            string path = textBox1.Text;
            if (string.IsNullOrEmpty(path))
                return;

            if (!Directory.Exists(path))
            {
                MessageBox.Show("指定されたフォルダが存在しません。");
                return;
            }

            string[] files = Directory.GetFiles(path, "*.ogg", SearchOption.TopDirectoryOnly);

            if (files.Length == 0)
            {
                MessageBox.Show("指定フォルダにOGG音声ファイルが見つかりませんでした。");
                return;
            }

            audioFiles = files;
            currentIndex = 0;

            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(audioFiles.Select(f => (object)Path.GetFileName(f)).ToArray());
            comboBox1.SelectedIndex = 0;

            textBox1.Text = path;

            LoadAudioForPlayback(audioFiles[currentIndex]);
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "OGGファイルが入ったフォルダを選択してください";
                if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    textBox1.Text = dlg.SelectedPath;
                    ApplyFolderPathFromTextBox();
                }
            }
        }

        private void ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = comboBox1.SelectedIndex;
            if (idx < 0 || idx >= audioFiles.Length)
                return;

            currentIndex = idx;
            LoadAudioForPlayback(audioFiles[currentIndex]);
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "出力先フォルダを指定してください";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    textBox2.Text = dlg.SelectedPath;
                }
            }
        }

        private void Button3_Click(object sender, EventArgs e)
        {
            if (audioReader == null || waveOut == null)
                return;

            try
            {
                int cent = trackBar1.Value;
                float pitchFactor = (float)Math.Pow(2.0, cent / 1200.0);

                Debug.WriteLine($"再生開始 pitchFactor={pitchFactor}, cent={cent}, ファイル拡張子={Path.GetExtension(audioFiles[currentIndex]).ToLowerInvariant()}");

                if (pitchProvider == null)
                {
                    pitchProvider = new VarispeedSampleProvider(audioReader, pitchFactor);
                    waveOut.Init(pitchProvider);
                }
                else
                {
                    pitchProvider.PlaybackRate = pitchFactor;
                }

                if (audioReader.CanSeek)
                    audioReader.CurrentTime = TimeSpan.Zero;

                pitchProvider.ResetPosition();

                waveOut.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show("再生開始時にエラーが発生しました: " + ex.Message);
            }
        }

        private void Button4_Click(object sender, EventArgs e)
        {
            waveOut?.Stop();
        }

        private void Button5_Click(object sender, EventArgs e)
        {
            string outputFolder = textBox2.Text;
            if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
            {
                MessageBox.Show("正しい出力先フォルダを指定してください。");
                return;
            }

            if (audioFiles == null || audioFiles.Length == 0)
            {
                MessageBox.Show("先にOGGファイルを読み込んでください。");
                return;
            }

            // UIロック
            button5.Enabled = false;
            comboBox1.Enabled = false;

            string[] targets = checkBox1.Checked ? (string[])audioFiles.Clone() : new string[] { audioFiles[currentIndex] };

            int cent = trackBar1.Value;

            try
            {
                foreach (var file in targets)
                {
                    Debug.WriteLine($"[変換開始] {file}");
                    ProcessFile(file, outputFolder, cent);
                    Debug.WriteLine($"[変換終了] {file}");
                }
                MessageBox.Show("変換処理が完了しました。");
            }
            catch (Exception ex)
            {
                MessageBox.Show("処理中にエラーが発生しました: " + ex.ToString());
            }
            finally
            {
                button5.Enabled = true;
                comboBox1.Enabled = true;
            }
        }

        private void ProcessFile(string inputFile, string baseOutputFolder, int cent)
        {
            Debug.WriteLine($"ProcessFile が呼ばれました。入力ファイル: {inputFile}");

            const string subfolder = "pitch_shifted";
            string outDir = Path.Combine(baseOutputFolder, subfolder);
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            string fileNameNoExt = Path.GetFileNameWithoutExtension(inputFile);
            string outputWav = Path.Combine(outDir, fileNameNoExt + ".wav");

            string ext = Path.GetExtension(inputFile).ToLowerInvariant();

            WaveStream sourceStream = null;

            try
            {
                if (ext == ".ogg")
                {
                    sourceStream = CreateWaveStreamForPreview(inputFile);
                    if (sourceStream == null)
                        throw new InvalidOperationException("音声ファイルの読み込みに失敗しました。");
                }
                else if (ext == ".wav")
                {
                    sourceStream = new WaveFileReader(inputFile);
                }
                else
                {
                    throw new InvalidOperationException("対応していないファイル形式です。");
                }

                float pitchFactor = (float)Math.Pow(2.0, cent / 1200.0);
                pitchFactor = Clamp(pitchFactor, 0.05f, 20f);
                int channels = sourceStream.WaveFormat.Channels;

                using (var varispeed = new VarispeedSampleProvider(sourceStream, pitchFactor))
                using (var writer = new WaveFileWriter(outputWav,
                    WaveFormat.CreateIeeeFloatWaveFormat(varispeed.WaveFormat.SampleRate, channels)))
                {
                    int floatSamplesPerBuffer = 32768; 

                    float[] buffer = new float[floatSamplesPerBuffer * channels];

                    int samplesRead;
                    int totalSamples = 0;
                    while ((samplesRead = varispeed.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        totalSamples += samplesRead;
                        writer.WriteSamples(buffer, 0, samplesRead);

                        if (totalSamples > 100_000_000)
                        {
                            Debug.WriteLine($"[{fileNameNoExt}] 処理が長すぎるため途中停止します。");
                            break;
                        }
                    }
                }

                string outputOgg = Path.Combine(outDir, fileNameNoExt + ".ogg");
                bool ffmpegSucceeded = ConvertWavToOgg(outputWav, outputOgg);
                if (ffmpegSucceeded)
                {
                    try
                    {
                        File.Delete(outputWav);
                        Debug.WriteLine($"WAVファイルを削除しました: {outputWav}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"WAVファイル削除に失敗しました: {ex}");
                    }
                }
                else
                {
                    MessageBox.Show("ffmpegがインストールされていないか、PATHが正しく設定されていません。\n"
                        + "WAVファイルとしては書き出されています。", "ffmpeg 実行エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessFileで例外: {ex}");
                throw;
            }
            finally
            {
                sourceStream?.Dispose();
            }
        }

        private bool ConvertWavToOgg(string wavFilePath, string oggFilePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{wavFilePath}\" -c:a libvorbis -qscale:a 10 \"{oggFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var proc = Process.Start(psi))
                {
                    string stdErr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode == 0)
                        return true;
                    else
                    {
                        Debug.WriteLine($"ffmpeg error:\n{stdErr}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ffmpeg実行失敗: {ex}");
                return false;
            }
        }

        private WaveStream CreateWaveStreamForPreview(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (ext == ".ogg")
                {
                    var reader = new VorbisWaveReader(filePath);
                    var memStream = new MemoryStream();
                    WaveFileWriter.WriteWavFileToStream(memStream, reader);
                    memStream.Position = 0;
                    return new WaveFileReader(memStream);
                }
                else
                {
                    MessageBox.Show("OGGファイル以外はサポートしていません。");
                    return null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("音声ファイルの読み込みに失敗しました: " + ex.Message);
                return null;
            }
        }

        private WaveStream CreateWaveStreamForProcess(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (ext == ".ogg")
                {
                    return new VorbisWaveReader(filePath);
                }
                else
                {
                    throw new InvalidOperationException("対応していないファイル形式です。");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("音声ファイルの読み込みに失敗しました: " + ex.Message);
                return null;
            }
        }

        private void LoadAudioForPlayback(string filePath)
        {
            DisposePlayback();

            try
            {
                audioReader = CreateWaveStreamForPreview(filePath);

                if (audioReader == null)
                {
                    MessageBox.Show("対応していないファイル形式です。");
                    return;
                }

                waveOut = new WaveOutEvent();
            }
            catch (Exception ex)
            {
                MessageBox.Show("音声ファイルの読み込みに失敗しました: " + ex.Message);
            }
        }

        private void DisposePlayback()
        {
            try
            {
                waveOut?.Stop();
                waveOut?.Dispose();
                waveOut = null;
            }
            catch { }

            try
            {
                audioReader?.Dispose();
                audioReader = null;
            }
            catch { }

            pitchProvider = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisposePlayback();
            base.OnFormClosing(e);
        }
    }
}
