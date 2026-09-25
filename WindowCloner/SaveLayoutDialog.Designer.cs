namespace WindowCloner
{
    partial class SaveLayoutDialog
    {
                                private System.ComponentModel.IContainer components = null;

                                        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

                                        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SaveLayoutDialog));
            inputField = new System.Windows.Forms.TextBox();
            saveBtn = new System.Windows.Forms.Button();
            Label = new System.Windows.Forms.Label();
            SuspendLayout();
                                                inputField.Location = new System.Drawing.Point(22, 55);
            inputField.Name = "inputField";
            inputField.Size = new System.Drawing.Size(521, 39);
            inputField.TabIndex = 0;
            inputField.KeyDown += inputField_KeyDown;
                                                saveBtn.Location = new System.Drawing.Point(212, 113);
            saveBtn.Name = "saveBtn";
            saveBtn.Size = new System.Drawing.Size(150, 46);
            saveBtn.TabIndex = 1;
            saveBtn.Text = "Save";
            saveBtn.UseVisualStyleBackColor = true;
            saveBtn.Click += saveBtn_Click;
                                                Label.AutoSize = true;
            Label.Location = new System.Drawing.Point(183, 9);
            Label.Name = "Label";
            Label.Size = new System.Drawing.Size(209, 32);
            Label.TabIndex = 2;
            Label.Text = "Enter layout name";
                                                AutoScaleDimensions = new System.Drawing.SizeF(13F, 32F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(562, 171);
            Controls.Add(Label);
            Controls.Add(saveBtn);
            Controls.Add(inputField);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Name = "SaveLayoutDialog";
            Text = "Save Layout";
            Load += SaveLayoutDialog_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TextBox inputField;
        private System.Windows.Forms.Button saveBtn;
        private System.Windows.Forms.Label Label;
    }
}