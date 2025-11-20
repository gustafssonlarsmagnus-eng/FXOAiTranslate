using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FXOptionsSimulator
{
    public class ToggleSwitch : Control
    {
        private bool _checked = false;
        private string _leftText = "USD";
        private string _rightText = "EUR";
        private readonly Color _onColor = Color.FromArgb(0, 120, 215); // Windows blue
        private readonly Color _offColor = Color.FromArgb(200, 200, 200); // Gray
        private readonly System.Windows.Forms.Timer _animationTimer;
        private float _thumbPosition = 0; // 0 = left (off), 1 = right (on)
        private const float AnimationSpeed = 0.15f;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    _animationTimer.Start();
                    CheckedChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public string LeftText
        {
            get => _leftText;
            set { _leftText = value; Invalidate(); }
        }

        public string RightText
        {
            get => _rightText;
            set { _rightText = value; Invalidate(); }
        }

        public ToggleSwitch()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint, true);

            this.Size = new Size(200, 30);
            this.Cursor = Cursors.Hand;

            _animationTimer = new System.Windows.Forms.Timer { Interval = 10 };
            _animationTimer.Tick += (s, e) =>
            {
                float target = _checked ? 1f : 0f;
                if (Math.Abs(_thumbPosition - target) < 0.01f)
                {
                    _thumbPosition = target;
                    _animationTimer.Stop();
                }
                else
                {
                    _thumbPosition += (target - _thumbPosition) * AnimationSpeed;
                }
                Invalidate();
            };
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Calculate dimensions
            int switchWidth = 50;
            int switchHeight = 24;
            int thumbSize = 20;
            int padding = 2;

            // Calculate positions
            int switchX = this.Width - switchWidth;
            int switchY = (this.Height - switchHeight) / 2;
            int thumbX = (int)(switchX + padding + (switchWidth - thumbSize - padding * 2) * _thumbPosition);
            int thumbY = switchY + padding;

            // Draw background track
            Color trackColor = _checked ? _onColor : _offColor;
            using (var brush = new SolidBrush(trackColor))
            using (var path = GetRoundedRect(switchX, switchY, switchWidth, switchHeight, switchHeight / 2))
            {
                e.Graphics.FillPath(brush, path);
            }

            // Draw thumb
            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, thumbX, thumbY, thumbSize, thumbSize);
            }

            // Draw text labels
            using (var font = new Font("Segoe UI", 9F))
            using (var brush = new SolidBrush(this.ForeColor))
            {
                // Left text (unchecked state)
                string leftLabel = _leftText;
                SizeF leftSize = e.Graphics.MeasureString(leftLabel, font);
                float leftX = switchX - leftSize.Width - 10;
                float leftY = (this.Height - leftSize.Height) / 2;

                using (var leftBrush = new SolidBrush(_checked ? Color.Gray : this.ForeColor))
                {
                    e.Graphics.DrawString(leftLabel, font, leftBrush, leftX, leftY);
                }

                // Right text (checked state)
                string rightLabel = _rightText;
                SizeF rightSize = e.Graphics.MeasureString(rightLabel, font);
                float rightX = switchX + switchWidth + 10;
                float rightY = (this.Height - rightSize.Height) / 2;

                using (var rightBrush = new SolidBrush(_checked ? this.ForeColor : Color.Gray))
                {
                    e.Graphics.DrawString(rightLabel, font, rightBrush, rightX, rightY);
                }
            }
        }

        private GraphicsPath GetRoundedRect(float x, float y, float width, float height, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animationTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
