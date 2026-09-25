namespace WindowCloner
{
    public partial class SaveLayoutDialog : Form
    {
        public string windowTitle;
        public string processName;
        public Form1 form;
        public SaveLayoutDialog(string title, string process, Form1 form)
        {
            InitializeComponent();
            windowTitle = title;
            processName = process;
            this.form = form;
            if(form.WindowLayoutObject != null && !string.IsNullOrEmpty(form.WindowLayoutObject.name))
            {
                inputField.Text = form.WindowLayoutObject.name;
                inputField.Focus();
                inputField.SelectAll();
            }
        }
        public SaveLayoutDialog()
        {
            InitializeComponent();
        }
        private static WindowLayoutObject CreateCurrentObject(string name, SaveLayoutDialog instance)
        {
            int bestW = -1;
            int bestH = -1;
            if (NativeMethods.GetWindowRect(instance.form._targetHwnd, out var rect))
            {
                bestW = rect.Width;
                bestH = rect.Height;
            }

            var f = instance.form;
            var cap = f.CaptureRect;

            return new WindowLayoutObject
            {
                name = name,
                windowName = instance.windowTitle,
                processName = instance.processName,

                                locationX = f.Location.X,
                locationY = f.Location.Y,
                width = f.Size.Width,
                height = f.Size.Height,

                                rectX = cap.X,
                rectY = cap.Y,
                rectW = Math.Max(1, cap.Width),
                rectH = Math.Max(1, cap.Height),

                                scaleX = f.CaptureScaleX,
                scaleY = f.CaptureScaleY,

                bestSuitedForWidth = bestW,
                bestSuitedForHeight = bestH
            };
        }
        private void saveBtn_Click(object sender, EventArgs e)
        {
            if(string.IsNullOrEmpty(inputField.Text.Trim()))
            {
                return;
            }
            WindowLayoutObject newWlo = CreateCurrentObject(inputField.Text.Trim(), this);
            if (Program.HasLayoutOfName(inputField.Text.Trim(), out WindowLayoutObject wlo))
            {
                DialogResult result = MessageBox.Show("A layout with that name already exists! Do you wish to replace it?", "Hold on!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (result == DialogResult.OK)
                {
                    Program.windowLayouts.Remove(wlo);
                    Program.windowLayouts.Add(newWlo);
                }
            }
            else
            {
                Program.windowLayouts.Add(newWlo);
            }
            DialogResult = DialogResult.OK;
            Program.SaveLayoutFile();
            form._wlo = newWlo;
        }

        private void inputField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                saveBtn_Click(null, null);
            }
        }

        private void SaveLayoutDialog_Load(object sender, EventArgs e)
        {
            Program.CenterToForm(this, form);
            TopMost = true;
        }
    }
}