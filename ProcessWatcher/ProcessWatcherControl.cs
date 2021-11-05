using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcessWatcher
{
    public partial class ProcessWatcherControl : UserControl
    {
        public ProcessWatcherControl()
        {
            InitializeComponent();
            DoubleBuffered = true; // to prevent flickering
        }

        public event ControlClosedEventHandler? ControlClosed;
        public event AlertSetEventHandler? AlertSet;

        private ProcessDiagnostics previousValues = null;
        private List<ProcessDiagnostics> historyValues = new List<ProcessDiagnostics>();
        private ProcessDiagnostics diagnostics = new ProcessDiagnostics(0, 0, 0);
        public ProcessDiagnostics Diagnostics { set { Store(diagnostics); diagnostics = value; Refresh(); } }
        internal Settings.Process? Process { get; set; }

        private void ProcessWatcherControl_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            
            e.Graphics.DrawString(diagnostics.Count.ToString(), Font, new SolidBrush(ForeColor), new PointF(Width * 0.1f, 10));
            e.Graphics.DrawString(diagnostics.CpuUsage.ToString(), Font, new SolidBrush(ForeColor), new PointF(Width / 2 - e.Graphics.MeasureString(diagnostics.CpuUsage.ToString(), Font).Width / 2, 10));
            e.Graphics.DrawString(diagnostics.MemoryUsage.ToString(), Font, new SolidBrush(ForeColor), new PointF(Width * 0.9f - e.Graphics.MeasureString(diagnostics.MemoryUsage.ToString(), Font).Width, 10));

            if (historyValues.Any())
            {
                var top = e.Graphics.MeasureString("X", Font);

                e.Graphics.DrawRectangle(Pens.Gray, new RectangleF(Width * 0.1f, top.Height + 10, Width * 0.8f, Height * 0.2f).Round());
                DrawSeries(historyValues.Select(x => (float)x.Count), historyValues.Max(x => x.Count), Color.Red, new RectangleF(Width * 0.1f, top.Height + 10, Width * 0.8f, Height * 0.2f), e.Graphics);
                
                e.Graphics.DrawRectangle(Pens.Gray, new RectangleF(Width * 0.1f, top.Height + 10 + Height * 0.25f, Width * 0.8f, Height * 0.2f).Round().Move(-1, -1).Resize(2, 2));
                DrawSeries(historyValues.Select(x => x.CpuUsage), 100f, Color.Green, new RectangleF(Width * 0.1f, top.Height + 10 + Height * 0.25f, Width * 0.8f, Height * 0.2f), e.Graphics);

                e.Graphics.DrawRectangle(Pens.Gray, new RectangleF(Width * 0.1f, top.Height + 10 + Height * 0.5f, Width * 0.8f, Height * 0.2f).Round().Move(-1, -1).Resize(2, 2));
                DrawSeries(historyValues.Select(x => x.MemoryUsage), 100f, Color.Blue, new RectangleF(Width * 0.1f, top.Height + 10 + Height * 0.5f, Width * 0.8f, Height * 0.2f), e.Graphics);
            }
        }

        private void DrawSeries(IEnumerable<float> series, float max, Color color, RectangleF area, Graphics graphics)
        {
            float left = area.Left;

            foreach (var value in series)
            {
                DrawBar(value, max, color, left, area.Top, area.Width / 60, area.Height, graphics);
                left += area.Width / 60;
            }
        }

        private void DrawBar(float value, float max, Color color, float left, float top, float width, float height, Graphics graphics)
        {
            graphics.FillRectangle(new SolidBrush(color), new RectangleF(left, top + height - height / max * value, width, height / max * value));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ControlClosed?.Invoke(this, EventArgs.Empty);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(Process!.Name))
            {
                process.Kill();
            }

            diagnostics = new ProcessDiagnostics(0, 0, 0);
        }
        
        private void OnSetAlert()
        {
            int count;
            float cpu;
            float mem;

            if (!int.TryParse(toolStripTextBox3.Text, out count))
            {
                toolStripTextBox3.Text = "";
                count = -1;
            }

            if (!float.TryParse(toolStripTextBox1.Text, out cpu))
            {
                toolStripTextBox1.Text = "";
                cpu = -1;
            }

            if (!float.TryParse(toolStripTextBox2.Text, out mem))
            {
                toolStripTextBox2.Text = "";
                mem = -1;
            }

            AlertSet?.Invoke(this, new AlertSetEventArgs { Count = count == -1 ? null : count, CpuThreshold = cpu == -1 ? null : cpu, MemoryThreshold = mem == -1 ? null : mem });
        }

        private void contextMenuStrip1_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            OnSetAlert();
        }

        internal bool Alert()
        {
            ForeColor = Color.Red;
            Refresh();

            // Some hysteresis shit.
            if (previousValues == null)
            {
                previousValues = new ProcessDiagnostics(diagnostics.Count, diagnostics.CpuUsage, diagnostics.MemoryUsage);
                return true;
            }
            else if (previousValues.Count != diagnostics.Count || previousValues.CpuUsage != diagnostics.CpuUsage || previousValues.MemoryUsage != diagnostics.MemoryUsage)
            {
                previousValues = new ProcessDiagnostics(diagnostics.Count, diagnostics.CpuUsage, diagnostics.MemoryUsage);
                return true;
            }

            return false;
        }

        private void Store(ProcessDiagnostics diagnostics)
        {
            historyValues.Add(diagnostics);
            if (historyValues.Count > 60)
            {
                historyValues.RemoveAt(0);
            }
        }
    }

    public record ProcessDiagnostics(int Count, float CpuUsage, float MemoryUsage);
    public delegate void ControlClosedEventHandler(object sender, EventArgs e);
    public delegate void AlertSetEventHandler(object sender, AlertSetEventArgs e);

    public class AlertSetEventArgs : EventArgs
    {
        public int? Count { get; set; }
        public float? CpuThreshold { get; set; }
        public float? MemoryThreshold { get; set; }
    }

    public static class Extension
    {
        public static Rectangle Round(this RectangleF rect) => new Rectangle((int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height);
        public static Rectangle Move(this Rectangle rect, int x, int y) => new Rectangle(rect.Left + x, rect.Top + y, rect.Width, rect.Height);
        public static Rectangle Resize(this Rectangle rect, int w, int h) => new Rectangle(rect.Left, rect.Top, rect.Width + w, rect.Height + h);
    }
}
