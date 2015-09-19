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
        private int streamLength = 30000;   //初期値29650
        private IWavePlayer WaveOutDevice = null;
        private WaveStream wavStream = null;
        private FilteredWaveStream outputStream;
        public double[] inputSampleStream;
        public Complex[] spectrum;
        public double[] power;
        bool isPlay;
        int melFiltNum = 20;
        int[][] mfbIndices;
        double[][] tBank;
        public double[] melSpectrum;
        public double[] MFCC;

        double log2 = Math.Log(2);



        Graphics g1;
        Graphics g2;
        Graphics g3;

        public Form1()
        {
            InitializeComponent();
            voice = "C:\\Workspace\\GlottalSource2.wav";

            df = 10;
            sampleRate = 44100;
            f1 = sampleRate / 2;

            inputSampleStream = new double[streamLength];
            spectrum = new Complex[(int)f1 / df * 2];
            power = new double[(int)f1 / df * 2];
            //filteredSampleStream = new double[streamLength];
            //postprocessStream = new double[streamLength - faderWidth];
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
                readOffset = 3160; //////////読み出し開始位置 3100


                for (int i = 0; i < out_Array.Length; i++)　///////////////////////////////////// ゼロクロス位置強制
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

                    buffer[outIndex++] = (float)((float)(out_Array[readIndex]) / 32767 / 1000 * Gain);

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

            byte[] byteArray = new byte[streamLength * 2];
            wavStream.Read(byteArray, 0, streamLength * 2);

            for (int i = 0; i < byteArray.Length / 2; i++)
            {
                inputSampleStream[i] = BitConverter.ToInt16(byteArray, i * 2);
                
            }
            int j;
        }


        private void calcSpectrum(double[] data)  //インパルス応答計算
        {


            for (int i = 0; i < spectrum.Length / 2 ; i++)  //順対称
            {
                spectrum[i] = data[i];
                spectrum[spectrum.Length - 1 - i] = data[i];                
            }

            Fourier.Forward(spectrum);

            for(int i = 0; i < power.Length; i++)
            {
                power[i] = spectrum[i].MagnitudeSquared();
            }

            //遅延あり
            //Complex[] wk_array = new Complex[data.Length];
            //Array.Copy(spectrum, 0, wk_array, 0, data.Length);
            //Array.Copy(wk_array, 0, spectrum, data.Length, data.Length);
            //Array.Reverse(wk_array);
            //Array.Copy(wk_array, 0, spectrum, 0, data.Length);

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

                //tBank[i] = new double[(int)sampleRate / 2 / df];
                //for (int j = 0; j < tBank[i].Length; j++)
                //{
                //    if (j >= fai && j < fbi)
                //    {
                //        tBank[i][j] = bartlett(j - fai, fbi - fai);
                //    }
                //    else
                //    {
                //        tBank[i][j] = 0;
                //    }
                //}

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

            openFileDialog1.Dispose();
        }
            private   void waveformDraw(Graphics g, double[] data)
    {

     Pen pen1 = new Pen(Color.Black);
            for (int i = 2; i < 800; i++)
            {
                g.DrawLine(
                    pen1,
                    (int)(i - 1),
                    (int)(200 - data[i * 30 + 1000] / 100),
                    (int)(i),
                    (int)(200 - data[(i + 1) * 30 + 1000] / 100)
                    );
            }
    }

            //TODO スペクトラム表示スケールの最適化
            private void spectrumDraw(Graphics g, double[] data)
            {
                Pen pen1 = new Pen(Color.Crimson);

                for (int i = 2; i < data.GetLength(0) / 15; i++)
                {
                    g.DrawLine(
                        pen1,
                        (int)((i - 1) * 2),
                        (int)(Math.Log10(data[(i - 1) *15]) * -10 + 200),
                        (int)(i * 2),
                        (int)(Math.Log10(data[i *15]) * -10 + 200)
                        );
                }

                Pen pen2 = new Pen(Color.LightGray);
                g.DrawLine(pen2, 0, 200, pictureBox1.Width, 200);
                for (int i = 1; i < 100; i += 10)
                {
                    g.DrawLine(pen2, 0, (int)(Math.Log10(i) * -100 + 200), pictureBox1.Width, (int)(Math.Log10(i) * -100 + 200));
                }

                for (int i = 1; i < 14; i++)
                {
                    g.DrawLine(pen2, i * 100, 0, i * 100, pictureBox1.Height);
                }

                Font fnt = new Font("Arial", 10);
                g.DrawString("1000", fnt, Brushes.Gray, 185, 210);
                g.DrawString("2000", fnt, Brushes.Gray, 385, 210);
                g.DrawString("3000", fnt, Brushes.Gray, 585, 210);
                g.DrawString("4000", fnt, Brushes.Gray, 785, 210);
                g.DrawString("5000", fnt, Brushes.Gray, 985, 210);
                g.DrawString("6000", fnt, Brushes.Gray, 1185, 210);

            }



        private void SystemUpdate()
        {

            calcSpectrum(inputSampleStream);
            g1.Clear(pictureBox1.BackColor);
            waveformDraw(g1, inputSampleStream);
            pictureBox1.Refresh();
            spectrumDraw(g2, power);
            melFilter(power);
            MFCC = dct_ii(melSpectrum);


        }

    }


}
