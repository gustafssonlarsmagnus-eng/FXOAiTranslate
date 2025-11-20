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
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // The switch takes up the full control size
            int trackHeight = this.Height;
            int trackWidth = this.Width;
            int thumbWidth = trackWidth / 2 - 2; // Half width minus padding
            int thumbHeight = trackHeight - 4;
            int padding = 2;

            // Calculate thumb position
            int thumbX = (int)(padding + (_thumbPosition * (trackWidth - thumbWidth - padding * 2)));
            int thumbY = padding;

            // Draw background track
            Color trackColor = _checked ? _onColor : _offColor;
            using (var brush = new SolidBrush(trackColor))
            using (var path = GetRoundedRect(0, 0, trackWidth, trackHeight, trackHeight / 2))
            {
                e.Graphics.FillPath(brush, path);
            }

            // Draw thumb
            using (var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            {
                e.Graphics.FillEllipse(shadowBrush, thumbX + 1, thumbY + 2, thumbWidth, thumbHeight);
            }
            using (var thumbBrush = new SolidBrush(Color.White))
            using (var thumbPath = GetRoundedRect(thumbX, thumbY, thumbWidth, thumbHeight, thumbHeight / 2))
            {
                e.Graphics.FillPath(thumbBrush, thumbPath);
            }

            // Draw text INSIDE the track
            using (var font = new Font("Segoe UI", 9F, FontStyle.Bold))
            {
                // Left text (USD or term currency)
                string leftLabel = _leftText;
                SizeF leftSize = e.Graphics.MeasureString(leftLabel, font);
                float leftX = (trackWidth / 4) - (leftSize.Width / 2);
                float leftY = (trackHeight - leftSize.Height) / 2;

                // Right text (EUR or base currency)
                string rightLabel = _rightText;
                SizeF rightSize = e.Graphics.MeasureString(rightLabel, font);
                float rightX = (trackWidth * 3 / 4) - (rightSize.Width / 2);
                float rightY = (trackHeight - rightSize.Height) / 2;

                // Draw text based on state
                if (_checked)
                {
                    // When checked (EUR), show USD grayed on left, EUR in white on thumb
                    using (var leftBrush = new SolidBrush(Color.FromArgb(150, 150, 150)))
                    {
                        e.Graphics.DrawString(leftLabel, font, leftBrush, leftX, leftY);
                    }
                    using (var rightBrush = new SolidBrush(Color.FromArgb(0, 100, 180)))
                    {
                        e.Graphics.DrawString(rightLabel, font, rightBrush, rightX, rightY);
                    }
                }
                else
                {
                    // When unchecked (USD), show USD in white on thumb, EUR grayed on right
                    using (var leftBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                    {
                        e.Graphics.DrawString(leftLabel, font, leftBrush, leftX, leftY);
                    }
                    using (var rightBrush = new SolidBrush(Color.White))
                    {
                        e.Graphics.DrawString(rightLabel, font, rightBrush, rightX, rightY);
                    }
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
