using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HtmlAgilityPack;
using ReverseMarkdown;

namespace WebsiteScraperUI
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void browseButton_Click(object sender, EventArgs e)
        {
            saveFileDialog.Filter = "Text Files (*.txt)|*.txt";
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                filePathTextBox.Text = saveFileDialog.FileName;
            }
        }

        private async void startButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(urlTextBox.Text) || string.IsNullOrWhiteSpace(filePathTextBox.Text))
            {
                MessageBox.Show("Please enter a valid URL and select a file path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            startButton.Enabled = false;
            browseButton.Enabled = false;

            var progress = new Progress<ProgressReport>();
            progress.ProgressChanged += (s, report) =>
            {
                progressBar.Value = report.PercentComplete;
                statusLabel.Text = report.StatusMessage;
            };

            try
            {
                var scraper = new Scraper(urlTextBox.Text);
                var content = await scraper.ScrapeAsync(progress);
                if (filePathTextBox.Text != null) {
                    await File.WriteAllTextAsync(filePathTextBox.Text, content);
                }

                MessageBox.Show("Scraping completed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                startButton.Enabled = true;
                browseButton.Enabled = true;
            }
        }
    }

    public class Scraper
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Converter _converter;

        public Scraper(string baseUrl)
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
            _converter = new Converter();
        }

        public async Task<string> ScrapeAsync(IProgress<ProgressReport> progress)
        {
            var stringBuilder = new StringBuilder();
            var allLinks = new HashSet<string>();
            await GetLinksAsync(_baseUrl, allLinks, progress);

            int i = 0;
            foreach (var link in allLinks)
            {
                try
                {
                    var html = await _httpClient.GetStringAsync(link);
                    var document = new HtmlAgilityPack.HtmlDocument();
                    document.LoadHtml(html);

                    var contentNode = document.DocumentNode.SelectSingleNode("//body"); // Adjust this selector to target the main content
                    if (contentNode != null)
                    {
                        var markdown = _converter.Convert(contentNode.InnerHtml);
                        stringBuilder.AppendLine(markdown);
                        stringBuilder.AppendLine("\n---\n");
                    }

                    i++;
                    progress.Report(new ProgressReport { PercentComplete = (int)((double)i / allLinks.Count * 100), StatusMessage = $"Scraping: {link}" });
                }
                catch (Exception ex)
                {
                    // Log or handle the error for individual page scraping
                    Console.WriteLine($"Error scraping {link}: {ex.Message}");
                }
            }

            return stringBuilder.ToString();
        }

        private async Task GetLinksAsync(string url, HashSet<string> allLinks, IProgress<ProgressReport> progress)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var document = new HtmlAgilityPack.HtmlDocument();
                document.LoadHtml(html);

                var links = document.DocumentNode.SelectNodes("//a[@href]");
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        var href = link.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            var absoluteUrl = new Uri(new Uri(_baseUrl), href).ToString();
                            if (absoluteUrl.StartsWith(_baseUrl) && !allLinks.Contains(absoluteUrl))
                            {
                                allLinks.Add(absoluteUrl);
                                progress.Report(new ProgressReport { PercentComplete = 0, StatusMessage = $"Found {allLinks.Count} links..." });
                                await GetLinksAsync(absoluteUrl, allLinks, progress);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log or handle the error for link discovery
                Console.WriteLine($"Error discovering links at {url}: {ex.Message}");
            }
        }
    }

    public class ProgressReport
    {
        public int PercentComplete { get; set; }
        public string? StatusMessage { get; set; }
    }
}