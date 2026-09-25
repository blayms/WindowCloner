using System.Drawing;
using System.Windows.Forms;

namespace WindowCloner
{
    partial class Welcome
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.ListBox listBoxWindows;
        private System.Windows.Forms.Button btnSelect;
        private System.Windows.Forms.Label lblTitle;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Welcome));
            listBoxWindows = new ListBox();
            btnSelect = new Button();
            lblTitle = new Label();
            SuspendLayout();
                                                listBoxWindows.FormattingEnabled = true;
            listBoxWindows.Location = new Point(26, 98);
            listBoxWindows.Margin = new Padding(6, 7, 6, 7);
            listBoxWindows.Name = "listBoxWindows";
            listBoxWindows.Size = new Size(512, 420);
            listBoxWindows.TabIndex = 0;
                                                btnSelect.Enabled = false;
            btnSelect.Location = new Point(376, 532);
            btnSelect.Margin = new Padding(6, 7, 6, 7);
            btnSelect.Name = "btnSelect";
            btnSelect.Size = new Size(162, 57);
            btnSelect.TabIndex = 1;
            btnSelect.Text = "Select";
            btnSelect.UseVisualStyleBackColor = true;
                                                lblTitle.AutoSize = true;
            lblTitle.Location = new Point(26, 33);
            lblTitle.Margin = new Padding(6, 0, 6, 0);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(307, 32);
            lblTitle.TabIndex = 4;
            lblTitle.Text = "Select a window to capture:";
                                                AutoScaleDimensions = new SizeF(13F, 32F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(553, 611);
            Controls.Add(lblTitle);
            Controls.Add(btnSelect);
            Controls.Add(listBoxWindows);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(6, 7, 6, 7);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Welcome";
            Text = "Window Cloner";
            ResumeLayout(false);
            PerformLayout();
        }
    }
}