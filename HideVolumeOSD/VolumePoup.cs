using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace HideVolumeOSD
{
    public partial class VolumePoup : Form
    {
        Volume volumeControl = new Volume();
        Thread threadVolume = new Thread(new ThreadStart(VolumeProc));

        private static VolumePoup popup;
        private static string volume;

        ManualResetEvent threadEvent = new ManualResetEvent(false);

        public VolumePoup()
        {
            InitializeComponent();

            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.UserPaint, true);

            popup = this;
            threadVolume.IsBackground = true;   // must never keep the process alive (e.g. in -hide / -show mode)
            threadVolume.Start();
        }
        public void Stop()
        {
            threadVolume.Abort();
        }
        public void updateValueSafe()
        {
            if (this.InvokeRequired)
            {
                Action safeWrite = delegate { updateValueSafe(); };
                this.Invoke(safeWrite);                
            }
            else
            {
                volume = ((int)(getVolume() * 100)).ToString();
                Refresh();
            }            
        }
        private float lastVolume = 0;

        public float getVolume()
        {
            try
            {
                lastVolume = volumeControl.GetMasterVolume();
            }
            catch (Exception ex)
            {
                // e.g. audio service not ready / no audio device: don't crash the app
                Log.Write("getVolume failed", ex);
            }

            return lastVolume;
        }
      
        protected override void OnVisibleChanged(EventArgs e)
        {
            if (this.Visible)
            {
                threadEvent.Set();                
            }
            else 
            {
                threadEvent.Reset();
            }
        }
		
        public static void VolumeProc()
        {
            while (popup.threadEvent.WaitOne())
            {
                while (popup.Visible)
                {
                    Thread.Sleep(20);

                    try
                    {
                        popup.updateValueSafe();
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(200);
                    }
                }
            }
        }    

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                Font font = new Font(Font.FontFamily, (float)(Height / 2), FontStyle.Regular);

                StringFormat stringFormat = new StringFormat();
                stringFormat.Alignment = StringAlignment.Center;
                stringFormat.LineAlignment = StringAlignment.Center;

                Rectangle rect = ClientRectangle;

                rect.Width -= 1;
                rect.Height -= 1;

                Color backColor;
                Color foreColor;

                if (Settings.Default.VolumeDisplayLight)
                {
                    backColor = Color.White;
                    foreColor = Color.Black;
                }
                else
                {
                    backColor = Color.Black;
                    foreColor = Color.White;
                }

                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                e.Graphics.FillRectangle(new SolidBrush(backColor), rect);
                e.Graphics.DrawRectangle(new Pen(Color.SteelBlue, 1), rect);
                e.Graphics.DrawString(volume, font, new SolidBrush(foreColor), rect, stringFormat);
            }
        }
    }
}
