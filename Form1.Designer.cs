namespace FXOAiTranslator
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support
        /// </summary>
        private void InitializeComponent()
        {
            this.txtInput = new System.Windows.Forms.TextBox();
            this.btnParse = new System.Windows.Forms.Button();
            this.lstBlotter = new System.Windows.Forms.ListBox();
            this.txtDebug = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // txtInput
            // 
            this.txtInput.Location = new System.Drawing.Point(12, 12);
            this.txtInput.Multiline = true;
            this.txtInput.Name = "txtInput";
            this.txtInput.Size = new System.Drawing.Size(500, 60);
            this.txtInput.TabIndex = 0;
            this.txtInput.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtInput_KeyDown);
            // 
            // btnParse
            // 
            this.btnParse.Location = new System.Drawing.Point(520, 12);
            this.btnParse.Name = "btnParse";
            this.btnParse.Size = new System.Drawing.Size(100, 60);
            this.btnParse.TabIndex = 1;
            this.btnParse.Text = "Parse Trade";
            this.btnParse.UseVisualStyleBackColor = true;
            this.btnParse.Click += new System.EventHandler(this.btnParse_Click);
            // 
            // lstBlotter
            // 
            this.lstBlotter.FormattingEnabled = true;
            this.lstBlotter.ItemHeight = 15;
            this.lstBlotter.Location = new System.Drawing.Point(12, 85);
            this.lstBlotter.Name = "lstBlotter";
            this.lstBlotter.Size = new System.Drawing.Size(760, 150);
            this.lstBlotter.TabIndex = 2;
            // 
            // txtDebug
            // 
            this.txtDebug.Location = new System.Drawing.Point(12, 250);
            this.txtDebug.Multiline = true;
            this.txtDebug.Name = "txtDebug";
            this.txtDebug.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDebug.Size = new System.Drawing.Size(760, 180);
            this.txtDebug.TabIndex = 3;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 461);
            this.Controls.Add(this.txtDebug);
            this.Controls.Add(this.lstBlotter);
            this.Controls.Add(this.btnParse);
            this.Controls.Add(this.txtInput);
            this.Name = "Form1";
            this.Text = "FXO AI Translator";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TextBox txtInput;
        private System.Windows.Forms.Button btnParse;
        private System.Windows.Forms.ListBox lstBlotter;
        private System.Windows.Forms.TextBox txtDebug;
    }
}
