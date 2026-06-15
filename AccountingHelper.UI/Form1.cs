using System;
using System.Drawing;
using System.Windows.Forms;
using AccountingHelper.Core.Services;

namespace AccountingHelper.UI
{
    public partial class Form1 : Form
    {
        private string _selectedFilePath;
        private readonly MonthlyFixProcessorService _monthlyProcessor = new MonthlyFixProcessorService();

        public Form1()
        {
            InitializeComponent();
        }

        private void btnUpload_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                Title = "Select Excel File"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _selectedFilePath = dialog.FileName;
                lblFilePath.Text = _selectedFilePath;
                lblFilePath.ForeColor = Color.Black;
                btnMonthlyFix.Enabled = true;
                lblStatus.Text = "";
            }
        }

        private async void btnMonthlyFix_Click(object sender, EventArgs e)
        {
            btnMonthlyFix.Enabled = false;
            btnUpload.Enabled = false;
            lblStatus.ForeColor = Color.DarkBlue;
            lblStatus.Text = "מעבד... מביא שערי מטבע ומדד מחירים...";

            try
            {
                string outputPath = await _monthlyProcessor.ProcessAsync(_selectedFilePath);
                lblStatus.ForeColor = Color.DarkGreen;
                lblStatus.Text = $"הושלם! נשמר ב: {outputPath}";
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.Red;
                lblStatus.Text = $"שגיאה: {ex.Message}";
                // The label truncates long URLs — show the full error so it can be read/screenshotted
                MessageBox.Show(ex.Message, "שגיאה", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnMonthlyFix.Enabled = true;
                btnUpload.Enabled = true;
            }
        }
    }
}
