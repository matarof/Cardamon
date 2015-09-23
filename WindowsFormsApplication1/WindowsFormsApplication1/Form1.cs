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
            
            spectrum = new Complex[(int) sampleRate / df];
            nPower = new double[(int)sampleRate / df / 2];
            //filteredSampleStream = new double[streamLength];
            //postprocessStream = new double[streamLength - faderWidth];

            window = new double[(int)sampleRate / df];
            offset = 0;
            setWindow(window.Length);
            initTBank(melFiltNum, 6000);
            melSpectrum = new double[melFiltNum];
            MFCC = new double[melFiltNum];
            
            Bitmap bmp1 = new Bitmap(pictureBox1.Size.Width, pictureBox1.Size.Height);
            pictureBox1.Image = bmp1;
            g1 = Graphics.FromImage(pictureBox1.Image);

            Bitmap bmp2 = new Bitmap(pictureBox2.Size.Width, pictureBox2.Size.Height);
            pictureBox2.Image = bmp2;
            g2 = Graphics.FromImage(pictureBox2.Image);

            Bitmap bmp3 = new Bitmap(pictureBox3.Size.Width, pictureBox3.Size.Height);
            pictureBox3.Image = bmp3;
            g3 = Graphics.FromImage(pictureBox3.Image);

            setWaveFile1();
            SystemUpdate();

            melFiltNum = 20;
            isPlay = false;

            
        }

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
                readOffset = 0; //////////読み出し開始位置 3100


                for (int i = 0; i < out_Array.Length-1; i++)　///////////////////////////////////// ゼロクロス位置強制
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


        private void setWaveFile1()
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


            //for (int i = 0; i < spectrum.Length / 2; i++)  //順対称
            //{
            //    spectrum[i] = data[i];
            //    spectrum[spectrum.Length - 1 - i] = data[i];
            //}

            for (int i = 0; i < window.Length; i++)  
            {
                spectrum[i] = data[i + offset] * window[i];
            }

            Fourier.Forward(spectrum, FourierOptions.NoScaling);   //defaultはsart(n)で正規化のでここでは正規化しない

            for(int i = 0; i < nPower.Length; i++)
            {
                nPower[i] = Math.Pow(spectrum[i].Magnitude / (spectrum.Length/2), 2) / powerCorrectionFactor;
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

            Parallel.For(0, melFiltNum, i =>
            {
                melSpectrum[i] = 0;

                for (int j = 0; j < mfbIndices[i][0]; j++)
                {
                    melSpectrum[i] += data[mfbIndices[i][1] + j] * tBank[i][j];

                }

            });
        }


        public double[] dct_ii(double[] data)  //////////////////////////////////////////DCT-II bruteforce
        {
            double[] c = new double[data.Length];

            for (int i = 1; i < data.Length; i++)
            {
                double a = Math.PI * i / data.Length;

                Parallel.For(1, data.Length, j =>
                {
                    c[i] += data[j] * Math.Cos(a * (j + 0.5));
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
                //window[i] = 0.54 - 0.46 * Math.Cos(2 * (Math.PI) * d * i);  // hamming window
                window[i] = 0.54 - 0.46 * Math.Cos(d * i);   //折り返して最適化
                window[ww - 1 - i] = window[i];
            }

            foreach(double n in window)
            {
                powerCorrectionFactor += n;
            }
            powerCorrectionFactor /= window.Length;
        }



/// ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////// 描画メソッド


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
                    (int)(dh - data[i * dw ] * dh),
                    (int)(i),
                    (int)(dh - data[(i + 1) * dw ] * dh)
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
            for (double i = 0.1 ; i > 0.01; i -= 0.02)
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
            SolidBrush br1 = new SolidBrush(Color.MediumBlue);
            SolidBrush br2 = new SolidBrush(Color.Black); ;

            for (int i = 0; i < data1.Length; i++)
            {
                g.FillRectangle(br1, 25 + i * 20, (int)(pictureBox3.Height - 10 - data1[i] * 10000), 20, (int)(data1[i] * 10000));
                g.FillRectangle(br2, 25 + i * 20, (int)((pictureBox3.Height - 10 - data2[i] * 5000) / 5), 20, 3);

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
            setWaveFile1();
            offset = 0;
            SystemUpdate();

            openFileDialog1.Dispose();
        }

        private void play()
        {
            if (isPlay == false)
            {
                ClearAudioDevice();
                WaveOutDevice = CreateDevice(comboBoxAudioIF.SelectedIndex);
                //outputStream = new FilteredWaveStream(sampleRate, filteredSampleStream);
                outputStream = new FilteredWaveStream(sampleRate,inputSampleStream);
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

            if (checkBox1.Checked == true) { od.isHotPotato = 1; }
            else od.isHotPotato = 0;
            od.name = voice;


            System.Xml.Serialization.XmlSerializer serializer1 = new System.Xml.Serialization.XmlSerializer(typeof(FilterbankOutput));
            System.IO.StreamWriter sw = new System.IO.StreamWriter(String.Format(@"{0}_FB.xml", voice), false, new System.Text.UTF8Encoding(false));
            serializer1.Serialize(sw, od);
            sw.Close();

        }
    }

    public class FilterbankOutput
    {
        public string name { get; set; }
        public int isHotPotato { get; set; }

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
    }

}
