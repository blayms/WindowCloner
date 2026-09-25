namespace WindowCloner
{
    partial class RectangleSelector
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button btnConfirm;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblInfo;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblLayout;
        private System.Windows.Forms.ComboBox cmbLayout;
        private System.Windows.Forms.CheckBox chkOnlyWindowName;
        private System.Windows.Forms.Button btnRemoveLayout;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RectangleSelector));
            pictureBox1 = new PictureBox();
            btnConfirm = new Button();
            btnCancel = new Button();
            lblInfo = new Label();
            panel1 = new Panel();
            lblLayout = new Label();
            cmbLayout = new ComboBox();
            chkOnlyWindowName = new CheckBox();
            btnRemoveLayout = new Button();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(784, 517);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // btnConfirm
            // 
            btnConfirm.BackColor = Color.FromArgb(0, 120, 215);
            btnConfirm.FlatStyle = FlatStyle.Flat;
            btnConfirm.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnConfirm.ForeColor = Color.White;
            btnConfirm.Location = new Point(12, 15);
            btnConfirm.Name = "btnConfirm";
            btnConfirm.Size = new Size(100, 33);
            btnConfirm.TabIndex = 0;
            btnConfirm.Text = "✓ Confirm";
            btnConfirm.UseVisualStyleBackColor = false;
            // 
            // btnCancel
            // 
            btnCancel.BackColor = Color.FromArgb(60, 60, 60);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnCancel.ForeColor = Color.White;
            btnCancel.Location = new Point(118, 15);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(100, 33);
            btnCancel.TabIndex = 1;
            btnCancel.Text = "✗ Cancel";
            btnCancel.UseVisualStyleBackColor = false;
            // 
            // lblInfo
            // 
            lblInfo.AutoSize = true;
            lblInfo.Font = new Font("Segoe UI", 10F);
            lblInfo.ForeColor = Color.White;
            lblInfo.Location = new Point(230, 12);
            lblInfo.Name = "lblInfo";
            lblInfo.Size = new Size(283, 37);
            lblInfo.TabIndex = 2;
            lblInfo.Text = "Selection: full window ";
            // 
            // panel1
            // 
            panel1.BackColor = Color.FromArgb(30, 30, 30);
            panel1.Controls.Add(btnConfirm);
            panel1.Controls.Add(btnCancel);
            panel1.Controls.Add(lblInfo);
            panel1.Controls.Add(lblLayout);
            panel1.Controls.Add(cmbLayout);
            panel1.Controls.Add(chkOnlyWindowName);
            panel1.Controls.Add(btnRemoveLayout);
            panel1.Dock = DockStyle.Bottom;
            panel1.Location = new Point(0, 517);
            panel1.Name = "panel1";
            panel1.Size = new Size(784, 65);
            panel1.TabIndex = 6;
            // 
            // lblLayout
            // 
            lblLayout.AutoSize = true;
            lblLayout.Font = new Font("Segoe UI", 10F);
            lblLayout.ForeColor = Color.White;
            lblLayout.Location = new Point(580, 12);
            lblLayout.Name = "lblLayout";
            lblLayout.Size = new Size(103, 37);
            lblLayout.TabIndex = 3;
            lblLayout.Text = "Layout:";
            // 
            // cmbLayout
            // 
            cmbLayout.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLayout.Font = new Font("Segoe UI", 10F);
            cmbLayout.FormattingEnabled = true;
            cmbLayout.Location = new Point(700, 10);
            cmbLayout.Name = "cmbLayout";
            cmbLayout.Size = new Size(200, 45);
            cmbLayout.TabIndex = 4;
            // 
            // chkOnlyWindowName
            // 
            chkOnlyWindowName.AutoSize = true;
            chkOnlyWindowName.Font = new Font("Segoe UI", 10F);
            chkOnlyWindowName.ForeColor = Color.White;
            chkOnlyWindowName.Location = new Point(950, 10);
            chkOnlyWindowName.Name = "chkOnlyWindowName";
            chkOnlyWindowName.Size = new Size(318, 41);
            chkOnlyWindowName.TabIndex = 5;
            chkOnlyWindowName.Text = "Only for window name";
            chkOnlyWindowName.UseVisualStyleBackColor = true;
            // 
            // btnRemoveLayout
            // 
            btnRemoveLayout.BackColor = Color.FromArgb(150, 40, 40);
            btnRemoveLayout.FlatStyle = FlatStyle.Flat;
            btnRemoveLayout.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnRemoveLayout.ForeColor = Color.White;
            btnRemoveLayout.Location = new Point(1300, 7);
            btnRemoveLayout.Name = "btnRemoveLayout";
            btnRemoveLayout.Size = new Size(140, 50);
            btnRemoveLayout.TabIndex = 6;
            btnRemoveLayout.Text = "Remove Layout";
            btnRemoveLayout.UseVisualStyleBackColor = false;
            btnRemoveLayout.Visible = false;
            // 
            // RectangleSelector
            // 
            BackColor = Color.Black;
            ClientSize = new Size(784, 582);
            Controls.Add(pictureBox1);
            Controls.Add(panel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "RectangleSelector";
            Text = "Select Region";
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            ResumeLayout(false);
        }
    }
}