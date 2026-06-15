namespace AccountingHelper.UI
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.btnUpload = new System.Windows.Forms.Button();
            this.lblFilePath = new System.Windows.Forms.Label();
            this.btnMonthlyFix = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.SuspendLayout();

            // lblTitle
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(30, 22);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Text = "Excel Processor";

            // btnUpload
            this.btnUpload.Location = new System.Drawing.Point(30, 75);
            this.btnUpload.Name = "btnUpload";
            this.btnUpload.Size = new System.Drawing.Size(150, 34);
            this.btnUpload.Text = "Upload Excel File";
            this.btnUpload.UseVisualStyleBackColor = true;
            this.btnUpload.Click += new System.EventHandler(this.btnUpload_Click);

            // lblFilePath
            this.lblFilePath.AutoSize = false;
            this.lblFilePath.ForeColor = System.Drawing.Color.Gray;
            this.lblFilePath.Location = new System.Drawing.Point(195, 83);
            this.lblFilePath.Name = "lblFilePath";
            this.lblFilePath.Size = new System.Drawing.Size(555, 20);
            this.lblFilePath.Text = "No file selected";

            // btnMonthlyFix
            this.btnMonthlyFix.Enabled = false;
            this.btnMonthlyFix.Location = new System.Drawing.Point(30, 130);
            this.btnMonthlyFix.Name = "btnMonthlyFix";
            this.btnMonthlyFix.Size = new System.Drawing.Size(180, 34);
            this.btnMonthlyFix.Text = "עיבוד חודשי";
            this.btnMonthlyFix.UseVisualStyleBackColor = true;
            this.btnMonthlyFix.Click += new System.EventHandler(this.btnMonthlyFix_Click);

            // lblStatus
            this.lblStatus.AutoSize = false;
            this.lblStatus.Location = new System.Drawing.Point(30, 185);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(740, 40);
            this.lblStatus.Text = "";

            // Form1
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 260);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.btnUpload);
            this.Controls.Add(this.lblFilePath);
            this.Controls.Add(this.btnMonthlyFix);
            this.Controls.Add(this.lblStatus);
            this.Name = "Form1";
            this.Text = "Accounting Helper";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Button btnUpload;
        private System.Windows.Forms.Label lblFilePath;
        private System.Windows.Forms.Button btnMonthlyFix;
        private System.Windows.Forms.Label lblStatus;
    }
}
