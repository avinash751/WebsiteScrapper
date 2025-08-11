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
        private readonly HashSet<string> _visitedLinks;
        private IProgress<ProgressReport>? _progress;
        private StringBuilder _stringBuilder;
        private int _scrapedCount;

        public Scraper(string baseUrl)
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
            _converter = new Converter();
            _visitedLinks = new HashSet<string>();
            _stringBuilder = new StringBuilder();
            _scrapedCount = 0;
        }

        public async Task<string> ScrapeAsync(IProgress<ProgressReport> progress)
        {
            _progress = progress;
            await ScrapePageAndFindLinks(_baseUrl);
            return _stringBuilder.ToString();
        }

        private async Task ScrapePageAndFindLinks(string url)
        {
            if (_visitedLinks.Contains(url)) return;

            _visitedLinks.Add(url);
            _scrapedCount++;
            _progress?.Report(new ProgressReport { PercentComplete = (int)((double)_scrapedCount / _visitedLinks.Count * 100), StatusMessage = $"Scraping: {url}" });

            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var document = new HtmlAgilityPack.HtmlDocument();
                document.LoadHtml(html);

                // Extract content
                var contentNode = document.DocumentNode.SelectSingleNode("//div[@class='doc-content']"); // Targeted selector
                if (contentNode != null)
                {
                    var markdown = _converter.Convert(contentNode.InnerText);
                    _stringBuilder.AppendLine($"# {document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "No Title"}");
                    _stringBuilder.AppendLine(markdown);
                    _stringBuilder.AppendLine("\n---\n");
                }

                // Find and follow links
                var links = document.DocumentNode.SelectNodes("//a[@href]");
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        var href = link.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            var absoluteUrl = new Uri(new Uri(_baseUrl), href).ToString();
                            if (absoluteUrl.StartsWith(_baseUrl) && !_visitedLinks.Contains(absoluteUrl))
                            {
                                await ScrapePageAndFindLinks(absoluteUrl);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scraping {url}: {ex.Message}");
            }
            _progress?.Report(new ProgressReport { PercentComplete = (int)((double)_scrapedCount / _visitedLinks.Count * 100), StatusMessage = $"Scraped {_scrapedCount} of {_visitedLinks.Count} pages." });
        }
    }

    public class ProgressReport
    {
        public int PercentComplete { get; set; }
        public string? StatusMessage { get; set; }
    }
}