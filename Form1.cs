using System;
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SoloviinaP5Updater
{
    public partial class Form1 : Form
    {
        private ProgressBar updateProgressBar;

        public Form1()
        {
            InitializeComponent();
            // Set the form’s opacity to 0 so it is effectively invisible.
            this.Opacity = 0;
            // Note: The Load event is subscribed in InitializeComponent.
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            // Retrieve versions.
            string localVersion = GetLocalVersion();
            string githubVersion = await GetGithubVersionAsync();

            // Check for errors in retrieving versions.
            if (localVersion == null)
            {
                MessageBox.Show("Файл ua.txt з локальною версією українізатора відсутній.",
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }
            if (githubVersion == null)
            {
                MessageBox.Show("Не вдалося отримати актуальну версію з GitHub.",
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Parse versions.
            Version localVerObj, githubVerObj;
            try
            {
                localVerObj = new Version(localVersion);
                githubVerObj = new Version(githubVersion);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Некоректний формат версії: " + ex.Message,
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Compare versions; if the GitHub version is newer, offer update.
            if (githubVerObj > localVerObj)
            {
                string updateMessage =
                    $"Переклад було оновлено до версії {githubVersion}!\n" +
                    $"Встановлена версія: {localVersion}. Бажаєте оновитися?";

                DialogResult result = MessageBox.Show(updateMessage,
                                        "Оновлення українізатора для Persona 5 Royal",
                                        MessageBoxButtons.YesNo,
                                        MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    // Make the form visible now.
                    this.Opacity = 1;
                    // Start the update process (with progress tracking).
                    StartUpdateProcess();
                }
                else
                {
                    this.Close();
                }
            }
            else
            {
                MessageBox.Show("У вас встановлена остання версія українізатора.",
                                "Оновлення", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
        }

        private string GetLocalVersion()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ua.txt");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path, Encoding.UTF8).Trim();
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetGithubVersionAsync()
        {
            string githubUrl = "https://raw.githubusercontent.com/BIDLOV/P5R_UA/main/version.txt";
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync(githubUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        return content.Trim();
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private async void StartUpdateProcess()
        {
            // Define the URL for the update file hosted on Dropbox.
            string dropboxUrl = "https://www.dropbox.com/scl/fi/t28h4p0g5xz5e3xceavk4/Florence.exe?rlkey=fqldea5qzrpet622qlvba7oat&st=xububr9b&dl=1";

            // Define the local file path for the updated file.
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Українізатор для Persona 5 Royal.exe");

            // Attempt to delete any existing file using a retry mechanism.
            if (!await TryToDeleteFileAsync(filePath))
            {
                this.Close();
                return;
            }

            // Prepare and show the progress bar.
            updateProgressBar.Visible = true;
            updateProgressBar.Value = 0;
            updateProgressBar.Minimum = 0;
            updateProgressBar.Maximum = 100;
            updateProgressBar.Style = ProgressBarStyle.Continuous;

            // Download the file asynchronously with progress tracking.
            bool downloadSuccess = await DownloadFileAsync(dropboxUrl, filePath);

            // Hide the progress bar.
            updateProgressBar.Visible = false;

            if (downloadSuccess)
            {
                // Wait briefly to ensure the file is released.
                await Task.Delay(2000);

                // Try launching the updated file.
                try
                {
                    Process.Start(filePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не вдалося запустити файл: " + ex.Message,
                                    "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Application.Exit();
            }
        }

        private async Task<bool> TryToDeleteFileAsync(string filePath, int retryCount = 10, int delayMilliseconds = 500)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Debug.WriteLine($"Старий файл {filePath} видалено.");
                    }
                    return true;
                }
                catch (IOException)
                {
                    // Wait before trying again if the file is locked.
                    await Task.Delay(delayMilliseconds);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не вдалося видалити старий файл: " + ex.Message,
                                    "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            return false;
        }

        private async Task<bool> DownloadFileAsync(string url, string path)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        // Acquire the total content length if available.
                        long? contentLength = response.Content.Headers.ContentLength;

                        // If contentLength is provided, ensure that the progress bar is in continuous mode.
                        if (contentLength.HasValue && contentLength.Value > 0)
                        {
                            updateProgressBar.Style = ProgressBarStyle.Continuous;
                        }
                        else
                        {
                            // For unknown length, use marquee style.
                            updateProgressBar.Style = ProgressBarStyle.Marquee;
                        }

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920]; // 80 KB buffer
                            long totalBytesRead = 0;
                            int bytesRead;

                            // Read the stream chunk-by-chunk.
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;

                                // Update progress if content length is known.
                                if (contentLength.HasValue && contentLength.Value > 0)
                                {
                                    int progress = (int)((totalBytesRead * 100) / contentLength.Value);
                                    updateProgressBar.Value = Math.Min(progress, 100);
                                }
                            }
                        }
                        return true;
                    }
                    else
                    {
                        MessageBox.Show("Статус код: " + (int)response.StatusCode,
                                        "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не вдалося завантажити файл: " + ex.Message,
                                "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            updateProgressBar = new ProgressBar();
            SuspendLayout();
            // 
            // updateProgressBar
            // 
            updateProgressBar.Location = new System.Drawing.Point(12, 12);
            updateProgressBar.MarqueeAnimationSpeed = 30;
            updateProgressBar.Name = "updateProgressBar";
            updateProgressBar.Size = new System.Drawing.Size(673, 88);
            // Initially set to continuous so we can update its Value.
            updateProgressBar.Style = ProgressBarStyle.Continuous;
            updateProgressBar.TabIndex = 0;
            // 
            // Form1
            // 
            ClientSize = new System.Drawing.Size(697, 111);
            Controls.Add(updateProgressBar);
            Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Updater";
            // Subscribe to the Load event only once.
            Load += Form1_Load;
            ResumeLayout(false);
        }
    }
}
