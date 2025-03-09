// VSPilot.UI/Dialogs/SettingsDialog.cs
using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shell;
using VSPilot.Core.Services;
using System.Threading.Tasks;

namespace VSPilot.UI.Dialogs
{
    public class SettingsDialog : Form
    {
        private TextBox openAIKeyTextBox;
        private TextBox anthropicKeyTextBox;
        private CheckBox useGitHubCopilotCheckBox;
        private Button saveButton;
        private Button cancelButton;
        private Label statusLabel;
        private Label copilotStatusLabel;
        private Button loginButton;
        private readonly GitHubCopilotService _copilotService;

        public SettingsDialog(GitHubCopilotService copilotService = null)
        {
            _copilotService = copilotService;
            InitializeComponent();
            LoadSettings();

            // Start the async check but don't await it here
            _ = Task.Run(async () =>
            {
                await CheckCopilotStatusAsync();
            });
        }

        private void InitializeComponent()
        {
            this.Text = "VSPilot Settings";
            this.Width = 500;
            this.Height = 350; // Increased height to accommodate new controls
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // OpenAI API Key
            Label openAILabel = new Label
            {
                Text = "OpenAI API Key:",
                Left = 20,
                Top = 20,
                Width = 120
            };
            this.Controls.Add(openAILabel);

            openAIKeyTextBox = new TextBox
            {
                Left = 150,
                Top = 20,
                Width = 300,
                PasswordChar = '*'
            };
            this.Controls.Add(openAIKeyTextBox);

            // Anthropic API Key
            Label anthropicLabel = new Label
            {
                Text = "Anthropic API Key:",
                Left = 20,
                Top = 60,
                Width = 120
            };
            this.Controls.Add(anthropicLabel);

            anthropicKeyTextBox = new TextBox
            {
                Left = 150,
                Top = 60,
                Width = 300,
                PasswordChar = '*'
            };
            this.Controls.Add(anthropicKeyTextBox);

            // GitHub Copilot
            useGitHubCopilotCheckBox = new CheckBox
            {
                Text = "Use GitHub Copilot (requires Visual Studio authentication)",
                Left = 20,
                Top = 100,
                Width = 430,
                Checked = false
            };
            this.Controls.Add(useGitHubCopilotCheckBox);

            // GitHub Copilot Status
            copilotStatusLabel = new Label
            {
                Text = "GitHub Copilot status: Checking...",
                Left = 20,
                Top = 140,
                Width = 300,
                ForeColor = System.Drawing.Color.Gray
            };
            this.Controls.Add(copilotStatusLabel);

            // Login Button
            loginButton = new Button
            {
                Text = "Log In",
                Left = 370,
                Top = 135,
                Width = 80,
                Enabled = false
            };
            loginButton.Click += LoginButton_Click;
            this.Controls.Add(loginButton);

            // Status Label
            statusLabel = new Label
            {
                Text = "",
                Left = 20,
                Top = 220,
                Width = 430,
                ForeColor = System.Drawing.Color.Red
            };
            this.Controls.Add(statusLabel);

            // Save Button
            saveButton = new Button
            {
                Text = "Save",
                Left = 300,
                Top = 270,
                Width = 80
            };
            saveButton.Click += SaveButton_Click;
            this.Controls.Add(saveButton);

            // Cancel Button
            cancelButton = new Button
            {
                Text = "Cancel",
                Left = 390,
                Top = 270,
                Width = 80
            };
            cancelButton.Click += CancelButton_Click;
            this.Controls.Add(cancelButton);

            // Add a link to get API keys
            LinkLabel getKeysLink = new LinkLabel
            {
                Text = "How to get API keys",
                Left = 20,
                Top = 180,
                Width = 150
            };
            getKeysLink.LinkClicked += GetKeysLink_LinkClicked;
            this.Controls.Add(getKeysLink);
        }

        private void GetKeysLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start("https://platform.openai.com/api-keys");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening link: {ex.Message}");
            }
        }

        private async Task CheckCopilotStatusAsync()
        {
            if (_copilotService == null)
            {
                copilotStatusLabel.Text = "GitHub Copilot status: Unknown (service not available)";
                copilotStatusLabel.ForeColor = System.Drawing.Color.Gray;
                loginButton.Enabled = false;
                return;
            }

            try
            {
                bool isInstalled = await _copilotService.IsCopilotInstalledAsync();
                if (!isInstalled)
                {
                    copilotStatusLabel.Text = "GitHub Copilot status: Not installed";
                    copilotStatusLabel.ForeColor = System.Drawing.Color.Red;
                    loginButton.Enabled = true;
                    loginButton.Text = "Install";
                    return;
                }

                bool isLoggedIn = await _copilotService.IsCopilotLoggedInAsync();
                if (isLoggedIn)
                {
                    copilotStatusLabel.Text = "GitHub Copilot status: Logged in";
                    copilotStatusLabel.ForeColor = System.Drawing.Color.Green;
                    loginButton.Text = "Refresh Status";
                }
                else
                {
                    copilotStatusLabel.Text = "GitHub Copilot status: Not logged in";
                    copilotStatusLabel.ForeColor = System.Drawing.Color.Red;
                    loginButton.Text = "Log In";
                }

                loginButton.Enabled = true;
            }
            catch (Exception ex)
            {
                copilotStatusLabel.Text = $"GitHub Copilot status: Error checking";
                copilotStatusLabel.ForeColor = System.Drawing.Color.Red;
                Debug.WriteLine($"Error checking Copilot status: {ex.Message}");
                loginButton.Enabled = false;
            }
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            if (_copilotService == null)
            {
                return;
            }

            try
            {
                loginButton.Enabled = false;
                loginButton.Text = "Opening...";

                if (loginButton.Text == "Install")
                {
                    // Open the GitHub Copilot installation page
                    Process.Start("https://marketplace.visualstudio.com/items?itemName=GitHub.copilot");
                }
                else
                {
                    // Open the GitHub Copilot login page or trigger the login flow
                    _copilotService.OpenCopilotLoginPage();
                }

                _copilotService.ResetCache();

                // Re-enable the button after a short delay
                var timer = new Timer
                {
                    Interval = 2000
                };
                timer.Tick += (s, args) => {
                    _ = Task.Run(async () => await CheckCopilotStatusAsync());
                    loginButton.Enabled = true;
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error with Copilot login: {ex.Message}");
                statusLabel.Text = $"Error with GitHub Copilot: {ex.Message}";
                statusLabel.ForeColor = System.Drawing.Color.Red;
                loginButton.Enabled = true;
                loginButton.Text = "Log In";
            }
        }

        private void LoadSettings()
        {
            try
            {
                // Load settings from environment variables or settings file
                string openAIKey = Environment.GetEnvironmentVariable("VSPILOT_API_KEY") ?? "";
                string anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
                bool useGitHubCopilot = false;

                // Try to load from settings file if it exists
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VSPilot",
                    "settings.txt");

                if (File.Exists(settingsPath))
                {
                    string[] lines = File.ReadAllLines(settingsPath);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string value = parts[1].Trim();

                            if (key == "OpenAIKey" && string.IsNullOrEmpty(openAIKey))
                            {
                                openAIKey = value;
                            }
                            else if (key == "AnthropicKey" && string.IsNullOrEmpty(anthropicKey))
                            {
                                anthropicKey = value;
                            }
                            else if (key == "UseGitHubCopilot")
                            {
                                useGitHubCopilot = value.ToLower() == "true";
                            }
                        }
                    }
                }

                // Set the values in the UI
                openAIKeyTextBox.Text = openAIKey;
                anthropicKeyTextBox.Text = anthropicKey;
                useGitHubCopilotCheckBox.Checked = useGitHubCopilot;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
                statusLabel.Text = "Error loading settings. Please try again.";
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Save settings to environment variables and settings file
                string openAIKey = openAIKeyTextBox.Text.Trim();
                string anthropicKey = anthropicKeyTextBox.Text.Trim();
                bool useGitHubCopilot = useGitHubCopilotCheckBox.Checked;

                // Set environment variables for the current process
                Environment.SetEnvironmentVariable("VSPILOT_API_KEY", openAIKey, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", anthropicKey, EnvironmentVariableTarget.Process);

                // Save to settings file
                string settingsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VSPilot");

                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                string settingsPath = Path.Combine(settingsDir, "settings.txt");
                using (StreamWriter writer = new StreamWriter(settingsPath))
                {
                    writer.WriteLine($"OpenAIKey={openAIKey}");
                    writer.WriteLine($"AnthropicKey={anthropicKey}");
                    writer.WriteLine($"UseGitHubCopilot={useGitHubCopilot}");
                }

                statusLabel.Text = "Settings saved successfully. Restart VSPilot to apply changes.";
                statusLabel.ForeColor = System.Drawing.Color.Green;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
                statusLabel.Text = "Error saving settings. Please try again.";
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
