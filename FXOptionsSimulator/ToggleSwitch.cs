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
                    _animationTimer.Start(); // Start sliding animation
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
            int padding = 2;

            // Define colors
            Color borderColor = Color.FromArgb(200, 200, 200);
            Color blueBackground = Color.FromArgb(0, 120, 215);
            Color whiteBackground = Color.White;
            Color blueText = Color.White;
            Color grayText = Color.FromArgb(100, 100, 100);

            // Draw white background for entire control
            using (var bgBrush = new SolidBrush(whiteBackground))
            using (var bgPath = GetRoundedRect(0, 0, width, height, cornerRadius))
            {
                e.Graphics.FillPath(bgBrush, bgPath);
            }

            // Draw outer border
            using (var borderPath = GetRoundedRect(0, 0, width, height, cornerRadius))
            using (var borderPen = new Pen(borderColor, 1))
            {
                e.Graphics.DrawPath(borderPen, borderPath);
            }

            // Draw center divider line
            using (var dividerPen = new Pen(borderColor, 1))
            {
                e.Graphics.DrawLine(dividerPen, halfWidth, 1, halfWidth, height - 1);
            }

            // Calculate sliding selector position
            int selectorWidth = halfWidth - padding * 2;
            int selectorX = (int)(padding + (_thumbPosition * halfWidth));
            int selectorY = padding;
            int selectorHeight = height - padding * 2;

            // Draw sliding blue selector
            using (var selectorBrush = new SolidBrush(blueBackground))
            using (var selectorPath = GetRoundedRect(selectorX, selectorY, selectorWidth, selectorHeight, cornerRadius - 1))
            {
                e.Graphics.FillPath(selectorBrush, selectorPath);
            }

            // Draw text
            using (var font = new Font("Segoe UI", 9F, FontStyle.Regular))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Determine text colors based on selector position
                // When selector is on left (position 0-0.5), left text is white
                // When selector is on right (position 0.5-1), right text is white
                Color leftTextColor = _thumbPosition < 0.5f ? blueText : grayText;
                Color rightTextColor = _thumbPosition >= 0.5f ? blueText : grayText;

                // Left text
                using (var leftTextBrush = new SolidBrush(leftTextColor))
                {
                    RectangleF leftRect = new RectangleF(0, 0, halfWidth, height);
                    e.Graphics.DrawString(_leftText, font, leftTextBrush, leftRect, sf);
                }

                // Right text
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
