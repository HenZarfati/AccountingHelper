using System;
using System.Drawing;
using System.Windows.Forms;
using AccountingHelper.Core.Services;

namespace AccountingHelper.UI
{
    public partial class Form1 : Form
    {
        private string _selectedFilePath;
        private readonly ExcelProcessorService _processor = new ExcelProcessorService();

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
                btnProcess.Enabled = true;
                lblStatus.Text = "";
            }
        }

        private async void btnProcess_Click(object sender, EventArgs e)
        {
            btnProcess.Enabled = false;
            btnUpload.Enabled = false;
            lblStatus.ForeColor = Color.DarkBlue;
            lblStatus.Text = "Processing... fetching live USD rate and calculating...";

            try
            {
                string outputPath = await _processor.ProcessAsync(_selectedFilePath);
                lblStatus.ForeColor = Color.DarkGreen;
                lblStatus.Text = $"Done! Saved to: {outputPath}";
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.Red;
                lblStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                btnProcess.Enabled = true;
                btnUpload.Enabled = true;
            }
        }
    }
}
