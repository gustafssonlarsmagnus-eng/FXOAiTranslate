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
                    Invalidate(); // Instant redraw, no animation needed for segmented control
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

            int width = this.Width;
            int height = this.Height;
            int halfWidth = width / 2;
            int cornerRadius = 4;

            // Define colors
            Color borderColor = Color.FromArgb(200, 200, 200);
            Color blueBackground = Color.FromArgb(0, 120, 215);
            Color whiteBackground = Color.White;
            Color blueText = Color.White;
            Color grayText = Color.FromArgb(100, 100, 100);

            // Draw outer border
            using (var borderPath = GetRoundedRect(0, 0, width, height, cornerRadius))
            using (var borderPen = new Pen(borderColor, 1))
            {
                e.Graphics.DrawPath(borderPen, borderPath);
            }

            // Draw left section background
            Color leftBgColor = !_checked ? blueBackground : whiteBackground;
            using (var leftBrush = new SolidBrush(leftBgColor))
            {
                // Left half with rounded corners on left side only
                GraphicsPath leftPath = new GraphicsPath();
                leftPath.AddArc(0, 0, cornerRadius * 2, cornerRadius * 2, 180, 90);
                leftPath.AddLine(cornerRadius, 0, halfWidth, 0);
                leftPath.AddLine(halfWidth, 0, halfWidth, height);
                leftPath.AddLine(halfWidth, height, cornerRadius, height);
                leftPath.AddArc(0, height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);
                leftPath.CloseFigure();
                e.Graphics.FillPath(leftBrush, leftPath);
            }

            // Draw right section background
            Color rightBgColor = _checked ? blueBackground : whiteBackground;
            using (var rightBrush = new SolidBrush(rightBgColor))
            {
                // Right half with rounded corners on right side only
                GraphicsPath rightPath = new GraphicsPath();
                rightPath.AddLine(halfWidth, 0, width - cornerRadius, 0);
                rightPath.AddArc(width - cornerRadius * 2, 0, cornerRadius * 2, cornerRadius * 2, 270, 90);
                rightPath.AddArc(width - cornerRadius * 2, height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
                rightPath.AddLine(width - cornerRadius, height, halfWidth, height);
                rightPath.AddLine(halfWidth, height, halfWidth, 0);
                rightPath.CloseFigure();
                e.Graphics.FillPath(rightBrush, rightPath);
            }

            // Draw center divider line
            using (var dividerPen = new Pen(borderColor, 1))
            {
                e.Graphics.DrawLine(dividerPen, halfWidth, 1, halfWidth, height - 1);
            }

            // Draw text
            using (var font = new Font("Segoe UI", 9F, FontStyle.Regular))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Left text
                Color leftTextColor = !_checked ? blueText : grayText;
                using (var leftTextBrush = new SolidBrush(leftTextColor))
                {
                    RectangleF leftRect = new RectangleF(0, 0, halfWidth, height);
                    e.Graphics.DrawString(_leftText, font, leftTextBrush, leftRect, sf);
                }

                // Right text
                Color rightTextColor = _checked ? blueText : grayText;
                using (var rightTextBrush = new SolidBrush(rightTextColor))
                {
                    RectangleF rightRect = new RectangleF(halfWidth, 0, halfWidth, height);
                    e.Graphics.DrawString(_rightText, font, rightTextBrush, rightRect, sf);
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
