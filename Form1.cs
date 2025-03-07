using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SoloviinaP5Updater
{
    public partial class Form1 : Form
    {
        private IConfiguration configuration;
        private ProgressBar progressBar;
        private Label progressLable;
        private Label progressLabel;

        public Form1()
        {
            InitializeComponent();
            this.Opacity = 0;
            progressLabel = new Label();
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            configuration = builder.Build();
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            string? localVersion = GetLocalVersion();
            string? githubVersion = await GetGithubVersionAsync();

            Debug.WriteLine($"Local Version: {localVersion}");
            Debug.WriteLine($"GitHub Version: {githubVersion}");

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

            Version localVerObj, githubVerObj;
            try
            {
                localVerObj = new Version(localVersion);
                githubVerObj = new Version(githubVersion);
            }
            catch (FormatException ex)
            {
                MessageBox.Show("Некоректний формат версії: " + ex.Message,
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

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
                    this.Opacity = 1;
                    await StartUpdateProcessAsync();
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

        private string? GetLocalVersion()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string? relativePath = configuration["LocalVersionFilePath"];
                if (relativePath == null)
                {
                    throw new InvalidOperationException("LocalVersionFilePath is not configured.");
                }
                string fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

                if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Invalid path detected.");
                }

                if (File.Exists(fullPath))
                {
                    return File.ReadAllText(fullPath, Encoding.UTF8).Trim();
                }
                else
                {
                    return null;
                }
            }
            catch (IOException)
            {
                return null;
            }
        }

        private async Task<string?> GetGithubVersionAsync()
        {
            string? githubApiUrl = configuration["GitHubApiUrl"];
            if (githubApiUrl == null)
            {
                return null;
            }
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SoloviinaP5Updater");
                    HttpResponseMessage response = await client.GetAsync(githubApiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"GitHub API response content: {content}");
                        dynamic? releases = JsonConvert.DeserializeObject(content);

                        if (releases == null || releases.Count == 0)
                        {
                            Debug.WriteLine("No releases found in the GitHub repository.");
                            return null;
                        }

                        Version? latestVersion = null;
                        foreach (var release in releases)
                        {
                            string tagName = release.tag_name;
                            Debug.WriteLine($"Found release tag: {tagName}");
                            var match = System.Text.RegularExpressions.Regex.Match(tagName, @"v(\d+\.\d+\.\d+)");
                            if (match.Success)
                            {
                                Version releaseVersion = new Version(match.Groups[1].Value);
                                Debug.WriteLine($"Parsed release version: {releaseVersion}");
                                if (latestVersion == null || releaseVersion > latestVersion)
                                {
                                    latestVersion = releaseVersion;
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"Tag name '{tagName}' does not match the expected version format.");
                            }
                        }
                        return latestVersion?.ToString();
                    }
                    else
                    {
                        Debug.WriteLine($"GitHub API response status code: {response.StatusCode}");
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"GitHub API response content: {errorContent}");
                        return null;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Exception in GetGithubVersionAsync: {ex.Message}");
                return null;
            }
        }

        private async Task StartUpdateProcessAsync()
        {
            Debug.WriteLine("StartUpdateProcessAsync started.");
            string? githubApiUrl = configuration["GitHubApiUrl"];
            if (githubApiUrl == null)
            {
                MessageBox.Show("GitHub API URL is not configured.",
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Debug.WriteLine("GitHub API URL is not configured.");
                this.Close();
                return;
            }
            string downloadDirectory = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SoloviinaP5Updater");
                    HttpResponseMessage response = await client.GetAsync(githubApiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        dynamic? releases = JsonConvert.DeserializeObject(content);
                        Debug.WriteLine($"GitHub API response content: {content}");

                        dynamic? latestRelease = null;
                        Version? latestVersion = null;
                        if (releases != null)
                        {
                            foreach (var release in releases)
                            {
                                string tagName = release.tag_name;
                                var match = System.Text.RegularExpressions.Regex.Match(tagName, @"v(\d+\.\d+\.\d+)");
                                if (match.Success)
                                {
                                    Version releaseVersion = new Version(match.Groups[1].Value);
                                    if (latestVersion == null || releaseVersion > latestVersion)
                                    {
                                        latestVersion = releaseVersion;
                                        latestRelease = release;
                                    }
                                }
                            }
                        }

                        if (latestRelease != null)
                        {
                            string downloadUrl = latestRelease.assets[0].browser_download_url;
                            string assetName = latestRelease.assets[0].name;
                            string filePath = Path.Combine(downloadDirectory, assetName);
                            Debug.WriteLine($"Download URL: {downloadUrl}, Asset name: {assetName}, File path: {filePath}");

                            // Extract checksum from release notes
                            string releaseNotes = latestRelease.body;
                            string? expectedChecksum = ExtractChecksumFromReleaseNotes(releaseNotes);
                            Debug.WriteLine($"Expected checksum: {expectedChecksum}");

                            if (string.IsNullOrEmpty(expectedChecksum))
                            {
                                MessageBox.Show("Не вдалося отримати контрольну суму з GitHub.",
                                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                Debug.WriteLine("Failed to get checksum from GitHub.");
                                this.Close();
                                return;
                            }

                            if (!await TryToDeleteFileAsync(filePath))
                            {
                                this.Close();
                                return;
                            }

                            bool downloadSuccess = await DownloadFileAsync(downloadUrl, filePath);

                            if (downloadSuccess)
                            {
                                await Task.Delay(2000);

                                // Calculate the checksum of the downloaded file
                                string actualChecksum = CalculateSHA256Checksum(filePath);
                                Debug.WriteLine($"Actual checksum: {actualChecksum}");

                                // Log the expected and actual checksums
                                Debug.WriteLine($"Expected Checksum: {expectedChecksum}");
                                Debug.WriteLine($"Actual Checksum: {actualChecksum}");

                                if (expectedChecksum != actualChecksum)
                                {
                                    Debug.WriteLine("Checksum mismatch. Download failed.");
                                    MessageBox.Show("Контрольна сума не збігається. Завантаження не вдалося.",
                                                    "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    this.Close();
                                    return;
                                }

                                try
                                {
                                    await Task.Run(() =>
                                    {
                                        using (var archive = RarArchive.Open(filePath))
                                        {
                                            var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
                                            int totalEntries = entries.Count;
                                            int processedEntries = 0;
                                            Debug.WriteLine($"Total entries to extract: {totalEntries}");

                                            foreach (var entry in entries)
                                            {
                                                string entryPath = Path.GetFullPath(Path.Combine(downloadDirectory, entry.Key));
                                                if (!entryPath.StartsWith(downloadDirectory, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    throw new UnauthorizedAccessException("Invalid path detected.");
                                                }

                                                entry.WriteToDirectory(downloadDirectory, new ExtractionOptions()
                                                {
                                                    ExtractFullPath = true,
                                                    Overwrite = true
                                                });

                                                processedEntries++;
                                                int progress = (int)((processedEntries * 100) / totalEntries);
                                                Invoke(new Action(() =>
                                                {
                                                    progressBar.Value = progress;
                                                    progressLable.Text = $"Розпакування: {progress}%";
                                                }));
                                                Debug.WriteLine($"Processed entries: {processedEntries}, Progress: {progress}%");
                                            }
                                        }
                                    });

                                    MessageBox.Show($"Українізатор успішно розпаковано та оновлено до версії {latestVersion}.", "Успіх!", MessageBoxButtons.OK, MessageBoxIcon.Information);

                                    if (File.Exists(filePath))
                                    {
                                        File.Delete(filePath);
                                        Debug.WriteLine($"Файл {filePath} видалено після розпакування.");
                                    }

                                    // Update the local version file
                                    UpdateLocalVersion(latestVersion?.ToString() ?? string.Empty);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Не вдалося розпакувати файл: " + ex.Message,
                                                    "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    Debug.WriteLine($"Exception during extraction: {ex.Message}");
                                }
                                Application.Exit();
                            }
                        }
                        else
                        {
                            MessageBox.Show("Не вдалося знайти останній реліз з GitHub.",
                                            "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Debug.WriteLine("Failed to find the latest release on GitHub.");
                            this.Close();
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"GitHub API response status code: {response.StatusCode}");
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"GitHub API response content: {errorContent}");
                        MessageBox.Show("Не вдалося отримати інформацію про останній реліз з GitHub.",
                                        "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Exception in StartUpdateProcess: {ex.Message}");
                MessageBox.Show("Помилка при отриманні інформації про останній реліз: " + ex.Message,
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        private string? ExtractChecksumFromReleaseNotes(string releaseNotes)
        {
            var match = System.Text.RegularExpressions.Regex.Match(releaseNotes, @"SHA256 Checksum\s*`([a-fA-F0-9]{64})`");
            return match.Success ? match.Groups[1].Value : null;
        }

        private string CalculateSHA256Checksum(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            {
                SHA256 sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
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
                    await Task.Delay(delayMilliseconds);
                }
                catch (UnauthorizedAccessException ex)
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
            Debug.WriteLine("DownloadFileAsync started.");
            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        long? contentLength = response.Content.Headers.ContentLength;
                        Debug.WriteLine($"Content length: {contentLength}");
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            long totalBytesRead = 0;
                            int bytesRead;
                            progressBar.Value = 0;
                            progressBar.Visible = true;
                            progressLable.Visible = true;
                            var stopwatch = Stopwatch.StartNew();

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;
                                if (contentLength.HasValue)
                                {
                                    int progress = (int)((totalBytesRead * 100) / contentLength.Value);
                                    progressBar.Value = progress;
                                    double speed = totalBytesRead / stopwatch.Elapsed.TotalSeconds / 1024; // Speed in KB/s
                                    string speedText = speed > 1000
                                        ? $"{speed / 1024:F2} Mb/s"
                                        : $"{speed:F2} KB/s";
                                    progressLable.Text = $"Завантаження: {progress}% | Швидкість: {speedText}";
                                    Debug.WriteLine($"Total bytes read: {totalBytesRead}, Progress: {progress}%, Speed: {speedText}");
                                }
                            }
                        }
                        return true;
                    }
                    else
                    {
                        MessageBox.Show("Статус код: " + (int)response.StatusCode,
                                        "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Debug.WriteLine($"Response status code: {(int)response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                MessageBox.Show("Не вдалося завантажити файл: " + ex.Message,
                                "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Debug.WriteLine($"HttpRequestException: {ex.Message}");
                return false;
            }
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            progressLable = new Label();
            progressBar = new ProgressBar();
            SuspendLayout();
            // 
            // progressLable
            // 
            progressLable.AutoSize = true;
            progressLable.BackColor = Color.Transparent;
            progressLable.Location = new Point(12, 50);
            progressLable.Name = "progressLable";
            progressLable.Size = new Size(200, 15);
            progressLable.TabIndex = 1;
            progressLable.Visible = true; // Ensure the label is visible
                                          // 
                                          // progressBar
                                          // 
            progressBar.Location = new Point(12, 12);
            progressBar.MarqueeAnimationSpeed = 30;
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(673, 35);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.TabIndex = 2;
            // 
            // Form1
            // 
            ClientSize = new Size(697, 80);
            Controls.Add(progressBar);
            Controls.Add(progressLable);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Updater";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        private void UpdateLocalVersion(string newVersion)
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string? relativePath = configuration["LocalVersionFilePath"];
                if (relativePath == null)
                {
                    throw new InvalidOperationException("LocalVersionFilePath is not configured.");
                }
                string fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));

                if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Invalid path detected.");
                }

                File.WriteAllText(fullPath, newVersion, Encoding.UTF8);
                Debug.WriteLine($"Local version updated to: {newVersion}");
            }
            catch (IOException ex)
            {
                MessageBox.Show("Не вдалося оновити локальну версію: " + ex.Message,
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("Не вдалося оновити локальну версію: " + ex.Message,
                                "Помилка!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
