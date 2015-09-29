using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Complex;
using MathNet.Numerics.IntegralTransforms;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

namespace WindowsFormsApplication1
{
    public partial class Form1 : Form
    {

        private string voice;
        int df;  //周波数応答関数計算ステップ(Hz)
        int f0 = 10;  //スペクトラム計算開始周波数
        int f1;  //スペクトラム計算終了周波数 サンプリング周波数で規定(コンストラクタで設定)

        public int sampleRate;
        int audioLatency = 50;
        private int streamLength;
        private IWavePlayer WaveOutDevice = null;
        private WaveFileReader wavStream = null;
        private FilteredWaveStream outputStream;

        double powerCorrectionFactor;
        public double[] inputSampleStream;
        public Complex[] spectrum;
        public double[] nPower;
        public double[] window;
        bool isPlay;
        int melFiltNum = 20;
        int[][] mfbIndices;
        double[][] tBank;
        public double[] melSpectrum;
        public double[] MFCC;
        public double[] n1MelSpectrum;
        public double[] n1MFCC;
        public double[] n2MelSpectrum;
        public double[] n2MFCC;

        public List<FilterbankOutput> MFCCDataList;

        double log2 = Math.Log(2);

        int dw;
        int offset;

        Graphics g1;
        Graphics g2;
        Graphics g3;

        public Form1()
        {
            InitializeComponent();
            InitializeWasapiControls();
            voice = "D:\\Workspace\\GlottalSource2.wav";

            df = 10;
            sampleRate = 44100;

            spectrum = new Complex[(int)sampleRate / df];
            nPower = new double[(int)sampleRate / df / 2];

            window = new double[(int)sampleRate / df];
            offset = 0;
            setWindow(window.Length);
            initTBank(melFiltNum, 6000);
            melSpectrum = new double[melFiltNum];
            MFCC = new double[melFiltNum];
            n1MelSpectrum = new double[melFiltNum];
            n1MFCC = new double[melFiltNum];
            n2MelSpectrum = new double[melFiltNum];
            n2MFCC = new double[melFiltNum];

            Bitmap bmp1 = new Bitmap(pictureBox1.Size.Width, pictureBox1.Size.Height);
            pictureBox1.Image = bmp1;
            g1 = Graphics.FromImage(pictureBox1.Image);

            Bitmap bmp2 = new Bitmap(pictureBox2.Size.Width, pictureBox2.Size.Height);
            pictureBox2.Image = bmp2;
            g2 = Graphics.FromImage(pictureBox2.Image);

            Bitmap bmp3 = new Bitmap(pictureBox3.Size.Width, pictureBox3.Size.Height);
            pictureBox3.Image = bmp3;
            g3 = Graphics.FromImage(pictureBox3.Image);

            setWaveFile();
            SystemUpdate();

            melFiltNum = 20;
            isPlay = false;

            MFCCDataList = new List<FilterbankOutput> { };
        }

        #region NAUDIO WASAPI
        private void InitializeWasapiControls()
        {
            var enumerator = new MMDeviceEnumerator();
            var endPoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var comboItems = new List<WasapiDeviceComboItem>();
            foreach (var endPoint in endPoints)
            {
                var comboItem = new WasapiDeviceComboItem();
                comboItem.Description = string.Format("{0} ({1})", endPoint.FriendlyName, endPoint.DeviceFriendlyName);
                comboItem.Device = endPoint;
                comboItems.Add(comboItem);
            }
            comboBoxAudioIF.DisplayMember = "Description";
            comboBoxAudioIF.ValueMember = "Device";
            comboBoxAudioIF.DataSource = comboItems;
        }

        private IWavePlayer CreateDevice(int id)
        {
            WasapiOut outputDevice = new WasapiOut(
        (MMDevice)comboBoxAudioIF.SelectedValue, AudioClientShareMode.Shared, false, audioLatency);
            return outputDevice as IWavePlayer;

        }

        private void ClearAudioDevice()
        {
            if (WaveOutDevice != null)
            {
                WaveOutDevice.Stop();
                WaveOutDevice.Dispose();
            }
            if (wavStream != null)
            {
                wavStream.Dispose();
            }
            WaveOutDevice = null;
        }

        class WasapiDeviceComboItem
        {
            public string Description { get; set; }
            public MMDevice Device { get; set; }
        }

        class FilteredWaveStream : ISampleProvider
        {

            private readonly WaveFormat waveFormat;

            private int readIndex;
            private int readOffset;
            private int readStart;
            private int readEnd;
            private double[] out_Array;


            public FilteredWaveStream(int sampleRate, double[] data)
            {
                waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                Gain = 1;
                out_Array = data;
                readOffset = 0;

                for (int i = 0; i < out_Array.Length - 1; i++)　///////////////////////////////////// ゼロクロス位置強制
                {
                    if ((out_Array[i + readOffset] < 0) && (out_Array[i + readOffset + 1] > 0))
                    {
                        readStart = i + readOffset + 1;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }
                for (int i = 0; i < out_Array.Length; i++)
                {
                    if ((out_Array[out_Array.Length - 1 - i] > 0) && (out_Array[out_Array.Length - 2 - i] < 0))
                    {
                        readEnd = out_Array.Length - 2 - i;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }

                readIndex = readStart;
            }

            public WaveFormat WaveFormat
            {
                get { return waveFormat; }
            }

            public double Gain { get; set; }

            public int Read(float[] buffer, int offset, int count)
            {
                int outIndex = offset;

                for (int sampleCount = 0; sampleCount < count; sampleCount++)
                {

                    buffer[outIndex++] = (float)((float)(out_Array[readIndex]) / 1000 * Gain);

                    readIndex++;
                    if (readIndex >= readEnd)
                    {
                        readIndex = readStart;
                    }
                }
                return count;
            }
        }

        private void play()
        {
            if (isPlay == false)
            {
                ClearAudioDevice();
                WaveOutDevice = CreateDevice(comboBoxAudioIF.SelectedIndex);
                outputStream = new FilteredWaveStream(sampleRate, inputSampleStream);
                WaveOutDevice.Init(new SampleToWaveProvider(outputStream as ISampleProvider));

                WaveOutDevice.Play();
                isPlay = true;
            }
            else
            {
                ClearAudioDevice();
                isPlay = false;
            }
        }
        #endregion

        private void setWaveFile()
        {
            FileInfo fi = new FileInfo(this.voice);
            if (!fi.Exists)
            {
                OpenFileDialog openFileDialog1 = new OpenFileDialog();
                openFileDialog1.Filter = "wavファイル|*.wav";
                openFileDialog1.Title = "Select a glottal source File";

                if (openFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    this.voice = openFileDialog1.FileName;
                }
                this.textBoxVoicefile.Text = this.voice;

                openFileDialog1.Dispose();
            }

            string fileExt = fi.Extension;

            if (fileExt.ToLower() == ".wav") wavStream = new WaveFileReader(this.voice);
            else return;

            streamLength = (int)(wavStream.Length / wavStream.WaveFormat.Channels / 2);
            byte[] byteArray = new byte[streamLength * 2 * wavStream.WaveFormat.Channels];
            wavStream.Read(byteArray, 0, streamLength * 2 * wavStream.WaveFormat.Channels);

            inputSampleStream = new double[streamLength];

            for (int i = 0; i < streamLength; i++)
            {
                inputSampleStream[i] = BitConverter.ToInt16(byteArray, i * 2 * wavStream.WaveFormat.Channels);

            }

            short max = (short)Math.Max(Math.Abs(inputSampleStream.Max()), Math.Abs(inputSampleStream.Min()));

            for (int i = 0; i < inputSampleStream.Length; i++)
            {
                inputSampleStream[i] /= max;
            }
        }

        private void calcSpectrum(double[] data)
        {
            var pre_emp = 0.97;

            for (int i = 0; i < window.Length; i++)
            {
                spectrum[i] = (data[i + offset + 1]  - pre_emp * (data[i + offset]))* window[i];  //プリエンファシス+窓掛け
            }

            Fourier.Forward(spectrum, FourierOptions.NoScaling);   //defaultはsart(n)で正規化のでここでは正規化しない

            for (int i = 0; i < nPower.Length; i++)
            {
                nPower[i] = Math.Pow(spectrum[i].Magnitude / (spectrum.Length / 2), 2) / powerCorrectionFactor;
            }

        }

        private int iMel(double m)
        {
            int f = (int)(1000 * (Math.Exp(m * log2 / 1000) - 1));
            return f;
        }

        private double fMel(int f)
        {
            double m = (1000 / log2) * Math.Log(f / 1000 + 1);
            return m;
        }

        private void initTBank(int mn, int maxf)  //mnフィルタバンク数　maxf フィルタバンク最大周波数
        {
            double maxm = fMel(maxf);
            double melFiltWidth = maxm / (melFiltNum / 2 + 0.5);

            mfbIndices = new int[mn][];
            tBank = new double[mn][];

            for (int i = 0; i < melFiltNum; i++)
            {
                double ma = melFiltWidth * i * 0.5;
                double mb = melFiltWidth * (i * 0.5 + 1);

                int fai = (int)(iMel(ma) / df);
                int fbi = (int)(iMel(mb) / df);

                mfbIndices[i] = new int[] { fbi - fai + 1, fai, fbi };

                tBank[i] = new double[mfbIndices[i][0]];
                for (int j = 0; j < tBank[i].Length; j++)
                {
                    tBank[i][j] = bartlett(j, mfbIndices[i][0]);

                }
            }
        }

        private double bartlett(int n, int l)
        {
            double i;

            if (n <= l / 2)
            {
                i = 2.0 * n / l;
            }
            else
            {
                i = 2.0 - 2.0 * n / l;
            }
            return i;

        }

        public void melFilter(double[] data)
        {
            double[] a = new double[melFiltNum];

            Parallel.For(0, melFiltNum, i =>
            {
                a[i] = 0;

                for (int j = 0; j < mfbIndices[i][0]; j++)
                {
                    a[i] += data[mfbIndices[i][1] + j] * tBank[i][j];

                }
            });

            double b = a.Sum();
            double c = a.Max();
            Parallel.For(0, melFiltNum, i =>
            {
                melSpectrum[i] = Math.Log10(a[i]);
                n1MelSpectrum[i] = Math.Log10(a[i] / b); //全体のパワーで正規化
                n2MelSpectrum[i] = Math.Log10(a[i] / c); //最大値を1として正規化
            });

        }

        //public double[] dct_ii(double[] data)  //////////////////////////////////////////DCT-II bruteforce
        //{
        //    double[] c = new double[data.Length];

        //    for (int i = 1; i < data.Length; i++)
        //    {
        //        double a = Math.PI * i / data.Length;

        //        Parallel.For(1, data.Length, j =>
        //        {
        //            c[i] += data[j] * Math.Cos(a * (j + 0.5));
        //        });
        //    }

        //    return c;
        //}
        public double[] dct_ii(double[] data)  //////////////////////////////////////////DCT-II bruteforce
        {
            double[] c = new double[data.Length];
            double b = Math.Sqrt(2.0 / data.Length);
            double b0 = Math.Sqrt(data.Length);

            for (int i = 0; i < data.Length; i++)
            {
                c[0] += data[i] / b0;
            }

            for (int i = 1; i < data.Length; i++)
            {
                double a = Math.PI * i / data.Length;


                Parallel.For(1, data.Length, j =>
                {
                    c[i] += data[j] * b * Math.Cos(a * (j + 0.5));
                });
            }

            return c;
        }
        private void setWindow(int ww)
        {
            Array.Clear(this.window, 0, window.Length);
            double d = (2 * (Math.PI)) / (double)ww;

            for (int i = 0; i < ww / 2; i++)
            {
                window[i] = 0.54 - 0.46 * Math.Cos(d * i);   //// hamming window 折り返して最適化
                window[ww - 1 - i] = window[i];
            }

            foreach (double n in window)
            {
                powerCorrectionFactor += n;
            }
            powerCorrectionFactor /= window.Length;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////// 描画メソッド

        private void waveformDraw(Graphics g, double[] data)
        {
            dw = (int)(streamLength / 800);

            g.FillRectangle(Brushes.Aqua, offset / dw, 0, spectrum.Length / dw, pictureBox1.Height - 1);

            int dh = (pictureBox1.Height - 1) / 2;
            Pen pen1 = new Pen(Color.Black);

            for (int i = 2; i < 800; i++)
            {
                g.DrawLine(
                    pen1,
                    (int)(i - 1),
                    (int)(dh - data[i * dw] * dh),
                    (int)(i),
                    (int)(dh - data[(i + 1) * dw] * dh)
                    );
            }
        }
        private void spectrumDraw(Graphics g, double[] data)   ///パワースペクトル描画
        {
            int mul = -30;

            Pen pen1 = new Pen(Color.Crimson);
            for (int i = 2; i < data.GetLength(0); i++)
            {
                g.DrawLine(
                    pen1,
                    (int)((i - 1) * 2),
                    (int)(Math.Log10(data[(i - 1)]) * mul + 50),
                    (int)(i * 2),
                    (int)(Math.Log10(data[i]) * mul + 50)
                    );
            }

            Pen pen2 = new Pen(Color.LightGray);
            for (double i = 1; i > 0.1; i -= 0.2)
            {
                g.DrawLine(pen2, 0, (int)(Math.Log10(i) * mul + 50), pictureBox2.Width, (int)(Math.Log10(i) * mul + 50));
            }
            for (double i = 0.1; i > 0.01; i -= 0.02)
            {
                g.DrawLine(pen2, 0, (int)(Math.Log10(i) * mul + 50), pictureBox2.Width, (int)(Math.Log10(i) * mul + 50));
            }
            for (double i = 0.01; i >= 0.001; i -= 0.002)
            {
                g.DrawLine(pen2, 0, (int)(Math.Log10(i) * mul + 50), pictureBox2.Width, (int)(Math.Log10(i) * mul + 50));
            }
            Pen pen3 = new Pen(Color.Blue);
            g.DrawLine(pen3, 0, 50, pictureBox2.Width, 50);

            for (int i = 1; i < 14; i++)
            {
                g.DrawLine(pen2, i * 100, 0, i * 100, pictureBox2.Height);
            }

            Font fnt = new Font("Arial", 10);
            g.DrawString("1000", fnt, Brushes.Gray, 185, 210);
            g.DrawString("2000", fnt, Brushes.Gray, 385, 210);
            g.DrawString("3000", fnt, Brushes.Gray, 585, 210);
            g.DrawString("4000", fnt, Brushes.Gray, 785, 210);
            g.DrawString("5000", fnt, Brushes.Gray, 985, 210);
            g.DrawString("6000", fnt, Brushes.Gray, 1185, 210);
        }

        private void fbParamDraw(Graphics g, double[] data1, double[] data2)
        {
            SolidBrush br1 = new SolidBrush(Color.Crimson);
            SolidBrush br2 = new SolidBrush(Color.Black); ;

            for (int i = 0; i < data1.Length; i++)
            {
                g.FillRectangle(br1, 25 + i * 20, (int)(pictureBox3.Height - 300 - data1[i] * 50), 20, 3);
                g.FillRectangle(br2, 25 + i * 20, (int)(pictureBox3.Height - 100 - data2[i] * 20), 20, 3);

            }
        }

        private void filterBankDraw(Graphics g, int[][] indices, double[][] data)  //インデックスあり
        {
            Pen pen1 = new Pen(Color.Gray);

            for (int j = 0; j < data.Length; j++)
            {
                for (int i = 0; i < indices[j][0] - 1; i++)
                {
                    g.DrawLine(
                        pen1,
                        (int)((indices[j][1] + i) * 2),
                        (int)((data[j][i]) * -100 + 200),
                        (int)((indices[j][1] + i + 1) * 2),
                        (int)((data[j][i + 1]) * -100 + 200)
                        );
                }
            }
        }

        private void SystemUpdate()
        {
            g1.Clear(pictureBox1.BackColor);
            waveformDraw(g1, inputSampleStream);
            pictureBox1.Refresh();

            calcSpectrum(inputSampleStream);
            g2.Clear(pictureBox2.BackColor);
            spectrumDraw(g2, nPower);
            filterBankDraw(g2, mfbIndices, tBank);
            pictureBox2.Refresh();

            melFilter(nPower);
            MFCC = dct_ii(melSpectrum);
            n1MFCC = dct_ii(n1MelSpectrum);
            n2MFCC = dct_ii(n2MelSpectrum);
            g3.Clear(pictureBox3.BackColor);
            fbParamDraw(g3, melSpectrum, MFCC);
            pictureBox3.Refresh();
        }

        #region User Interface

        private void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            offset = (int)(e.X * dw);
            SystemUpdate();
            play();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Filter = "wavファイル|*.wav";
            openFileDialog1.Title = "Select a glottal source File";

            if (openFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.voice = openFileDialog1.FileName;
            }
            this.textBoxVoicefile.Text = this.voice;
            setWaveFile();
            offset = 0;
            SystemUpdate();

            openFileDialog1.Dispose();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.textBoxVoicefile.Text = this.voice;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            FilterbankOutput od = new FilterbankOutput();

            od.m1 = MFCC[1];
            od.m2 = MFCC[2];
            od.m3 = MFCC[3];
            od.m4 = MFCC[4];
            od.m5 = MFCC[5];
            od.m6 = MFCC[6];
            od.m7 = MFCC[7];
            od.m8 = MFCC[8];
            od.m9 = MFCC[9];
            od.m10 = MFCC[10];
            od.m11 = MFCC[11];
            od.m12 = MFCC[12];
            od.m13 = MFCC[13];
            od.m14 = MFCC[14];
            od.m15 = MFCC[15];
            od.m16 = MFCC[16];
            od.m17 = MFCC[17];
            od.m18 = MFCC[18];
            od.m19 = MFCC[19];

            od.fb1 = melSpectrum[0];
            od.fb2 = melSpectrum[1];
            od.fb3 = melSpectrum[2];
            od.fb4 = melSpectrum[3];
            od.fb5 = melSpectrum[4];
            od.fb6 = melSpectrum[5];
            od.fb7 = melSpectrum[6];
            od.fb8 = melSpectrum[7];
            od.fb9 = melSpectrum[8];
            od.fb10 = melSpectrum[9];
            od.fb11 = melSpectrum[10];
            od.fb12 = melSpectrum[11];
            od.fb13 = melSpectrum[12];
            od.fb14 = melSpectrum[13];
            od.fb15 = melSpectrum[14];
            od.fb16 = melSpectrum[15];
            od.fb17 = melSpectrum[16];
            od.fb18 = melSpectrum[17];
            od.fb19 = melSpectrum[18];
            od.fb20 = melSpectrum[19];

            od.n1m1 = n1MFCC[1];
            od.n1m2 = n1MFCC[2];
            od.n1m3 = n1MFCC[3];
            od.n1m4 = n1MFCC[4];
            od.n1m5 = n1MFCC[5];
            od.n1m6 = n1MFCC[6];
            od.n1m7 = n1MFCC[7];
            od.n1m8 = n1MFCC[8];
            od.n1m9 = n1MFCC[9];
            od.n1m10 = n1MFCC[10];
            od.n1m11 = n1MFCC[11];
            od.n1m12 = n1MFCC[12];
            od.n1m13 = n1MFCC[13];
            od.n1m14 = n1MFCC[14];
            od.n1m15 = n1MFCC[15];
            od.n1m16 = n1MFCC[16];
            od.n1m17 = n1MFCC[17];
            od.n1m18 = n1MFCC[18];
            od.n1m19 = n1MFCC[19];

            od.n1fb1 = n1MelSpectrum[0];
            od.n1fb2 = n1MelSpectrum[1];
            od.n1fb3 = n1MelSpectrum[2];
            od.n1fb4 = n1MelSpectrum[3];
            od.n1fb5 = n1MelSpectrum[4];
            od.n1fb6 = n1MelSpectrum[5];
            od.n1fb7 = n1MelSpectrum[6];
            od.n1fb8 = n1MelSpectrum[7];
            od.n1fb9 = n1MelSpectrum[8];
            od.n1fb10 = n1MelSpectrum[9];
            od.n1fb11 = n1MelSpectrum[10];
            od.n1fb12 = n1MelSpectrum[11];
            od.n1fb13 = n1MelSpectrum[12];
            od.n1fb14 = n1MelSpectrum[13];
            od.n1fb15 = n1MelSpectrum[14];
            od.n1fb16 = n1MelSpectrum[15];
            od.n1fb17 = n1MelSpectrum[16];
            od.n1fb18 = n1MelSpectrum[17];
            od.n1fb19 = n1MelSpectrum[18];
            od.n1fb20 = n1MelSpectrum[19];

            od.n2m1 = n2MFCC[1];
            od.n2m2 = n2MFCC[2];
            od.n2m3 = n2MFCC[3];
            od.n2m4 = n2MFCC[4];
            od.n2m5 = n2MFCC[5];
            od.n2m6 = n2MFCC[6];
            od.n2m7 = n2MFCC[7];
            od.n2m8 = n2MFCC[8];
            od.n2m9 = n2MFCC[9];
            od.n2m10 = n2MFCC[10];
            od.n2m11 = n2MFCC[11];
            od.n2m12 = n2MFCC[12];
            od.n2m13 = n2MFCC[13];
            od.n2m14 = n2MFCC[14];
            od.n2m15 = n2MFCC[15];
            od.n2m16 = n2MFCC[16];
            od.n2m17 = n2MFCC[17];
            od.n2m18 = n2MFCC[18];
            od.n2m19 = n2MFCC[19];

            od.n2fb1 = n2MelSpectrum[0];
            od.n2fb2 = n2MelSpectrum[1];
            od.n2fb3 = n2MelSpectrum[2];
            od.n2fb4 = n2MelSpectrum[3];
            od.n2fb5 = n2MelSpectrum[4];
            od.n2fb6 = n2MelSpectrum[5];
            od.n2fb7 = n2MelSpectrum[6];
            od.n2fb8 = n2MelSpectrum[7];
            od.n2fb9 = n2MelSpectrum[8];
            od.n2fb10 = n2MelSpectrum[9];
            od.n2fb11 = n2MelSpectrum[10];
            od.n2fb12 = n2MelSpectrum[11];
            od.n2fb13 = n2MelSpectrum[12];
            od.n2fb14 = n2MelSpectrum[13];
            od.n2fb15 = n2MelSpectrum[14];
            od.n2fb16 = n2MelSpectrum[15];
            od.n2fb17 = n2MelSpectrum[16];
            od.n2fb18 = n2MelSpectrum[17];
            od.n2fb19 = n2MelSpectrum[18];
            od.n2fb20 = n2MelSpectrum[19];

            if (checkBox1.Checked == true) { od.isHotPotato = 1; }
            else od.isHotPotato = 0;
            od.name = voice;

            System.Xml.Serialization.XmlSerializer serializer1 = new System.Xml.Serialization.XmlSerializer(typeof(FilterbankOutput));
            System.IO.StreamWriter sw = new System.IO.StreamWriter(String.Format(@"{0}_FB.xml", voice), false, new System.Text.UTF8Encoding(false));
            serializer1.Serialize(sw, od);
            sw.Close();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string dataFile;
            FilterbankOutput ad;

            OpenFileDialog openFileDialog2 = new OpenFileDialog();
            openFileDialog2.Filter = "xmlファイル|*.xml";
            openFileDialog2.Title = "Select a MFCC Data File";

            if (openFileDialog2.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                dataFile = openFileDialog2.FileName;
            }
            else return;

            openFileDialog2.Dispose();

            System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(FilterbankOutput));
            System.IO.StreamReader sr = new System.IO.StreamReader(dataFile, new System.Text.UTF8Encoding(false));
            ad = (FilterbankOutput)serializer.Deserialize(sr);
            sr.Close();

            MFCCDataList.Add(ad);

            System.Xml.Serialization.XmlSerializer serializer1 = new System.Xml.Serialization.XmlSerializer(typeof(List<FilterbankOutput>));
            System.IO.StreamWriter sw = new System.IO.StreamWriter(String.Format(@"D:\Workspace\{0}.xml", this.textBoxCombindFile.Text), false, new System.Text.UTF8Encoding(false));
            serializer1.Serialize(sw, MFCCDataList);
            sw.Close();
        }
    }
    #endregion

    public class FilterbankOutput
    {
        public string name { get; set; }
        public int isHotPotato { get; set; }

        public double fb1 { get; set; }
        public double fb2 { get; set; }
        public double fb3 { get; set; }
        public double fb4 { get; set; }
        public double fb5 { get; set; }
        public double fb6 { get; set; }
        public double fb7 { get; set; }
        public double fb8 { get; set; }
        public double fb9 { get; set; }
        public double fb10 { get; set; }
        public double fb11 { get; set; }
        public double fb12 { get; set; }
        public double fb13 { get; set; }
        public double fb14 { get; set; }
        public double fb15 { get; set; }
        public double fb16 { get; set; }
        public double fb17 { get; set; }
        public double fb18 { get; set; }
        public double fb19 { get; set; }
        public double fb20 { get; set; }

        public double m1 { get; set; }
        public double m2 { get; set; }
        public double m3 { get; set; }
        public double m4 { get; set; }
        public double m5 { get; set; }
        public double m6 { get; set; }
        public double m7 { get; set; }
        public double m8 { get; set; }
        public double m9 { get; set; }
        public double m10 { get; set; }
        public double m11 { get; set; }
        public double m12 { get; set; }
        public double m13 { get; set; }
        public double m14 { get; set; }
        public double m15 { get; set; }
        public double m16 { get; set; }
        public double m17 { get; set; }
        public double m18 { get; set; }
        public double m19 { get; set; }

        public double n1fb1 { get; set; }
        public double n1fb2 { get; set; }
        public double n1fb3 { get; set; }
        public double n1fb4 { get; set; }
        public double n1fb5 { get; set; }
        public double n1fb6 { get; set; }
        public double n1fb7 { get; set; }
        public double n1fb8 { get; set; }
        public double n1fb9 { get; set; }
        public double n1fb10 { get; set; }
        public double n1fb11 { get; set; }
        public double n1fb12 { get; set; }
        public double n1fb13 { get; set; }
        public double n1fb14 { get; set; }
        public double n1fb15 { get; set; }
        public double n1fb16 { get; set; }
        public double n1fb17 { get; set; }
        public double n1fb18 { get; set; }
        public double n1fb19 { get; set; }
        public double n1fb20 { get; set; }

        public double n1m1 { get; set; }
        public double n1m2 { get; set; }
        public double n1m3 { get; set; }
        public double n1m4 { get; set; }
        public double n1m5 { get; set; }
        public double n1m6 { get; set; }
        public double n1m7 { get; set; }
        public double n1m8 { get; set; }
        public double n1m9 { get; set; }
        public double n1m10 { get; set; }
        public double n1m11 { get; set; }
        public double n1m12 { get; set; }
        public double n1m13 { get; set; }
        public double n1m14 { get; set; }
        public double n1m15 { get; set; }
        public double n1m16 { get; set; }
        public double n1m17 { get; set; }
        public double n1m18 { get; set; }
        public double n1m19 { get; set; }

        public double n2fb1 { get; set; }
        public double n2fb2 { get; set; }
        public double n2fb3 { get; set; }
        public double n2fb4 { get; set; }
        public double n2fb5 { get; set; }
        public double n2fb6 { get; set; }
        public double n2fb7 { get; set; }
        public double n2fb8 { get; set; }
        public double n2fb9 { get; set; }
        public double n2fb10 { get; set; }
        public double n2fb11 { get; set; }
        public double n2fb12 { get; set; }
        public double n2fb13 { get; set; }
        public double n2fb14 { get; set; }
        public double n2fb15 { get; set; }
        public double n2fb16 { get; set; }
        public double n2fb17 { get; set; }
        public double n2fb18 { get; set; }
        public double n2fb19 { get; set; }
        public double n2fb20 { get; set; }

        public double n2m1 { get; set; }
        public double n2m2 { get; set; }
        public double n2m3 { get; set; }
        public double n2m4 { get; set; }
        public double n2m5 { get; set; }
        public double n2m6 { get; set; }
        public double n2m7 { get; set; }
        public double n2m8 { get; set; }
        public double n2m9 { get; set; }
        public double n2m10 { get; set; }
        public double n2m11 { get; set; }
        public double n2m12 { get; set; }
        public double n2m13 { get; set; }
        public double n2m14 { get; set; }
        public double n2m15 { get; set; }
        public double n2m16 { get; set; }
        public double n2m17 { get; set; }
        public double n2m18 { get; set; }
        public double n2m19 { get; set; }

    }

}
