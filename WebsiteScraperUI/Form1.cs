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
        private readonly Uri _baseUri; // Store the base URI object
        private readonly string _baseHost; // Store the base host for comparison
        private readonly Converter _converter;
        private readonly HashSet<string> _visitedLinks; // Stores normalized URLs (without fragments)
        private readonly Queue<string> _linksToScrape; // Queue for links to be scraped
        private IProgress<ProgressReport>? _progress;
        private StringBuilder _stringBuilder;
        private int _scrapedCount;
        private int _totalLinksDiscovered;

        public Scraper(string baseUrl)
        {
            _httpClient = new HttpClient();
            _baseUri = new Uri(baseUrl);
            _baseHost = _baseUri.Host; // Only store the host for comparison

            _converter = new Converter();
            _visitedLinks = new HashSet<string>();
            _linksToScrape = new Queue<string>();
            _stringBuilder = new StringBuilder();
            _scrapedCount = 0;
            _totalLinksDiscovered = 0;
        }

        public async Task<string> ScrapeAsync(IProgress<ProgressReport> progress)
        {
            _progress = progress;
            _linksToScrape.Enqueue(_baseUri.ToString()); // Start with the initial URL
            _visitedLinks.Add(NormalizeUrl(_baseUri.ToString())); // Add initial URL to visited links

            while (_linksToScrape.Any())
            {
                var currentUrl = _linksToScrape.Dequeue();
                await ScrapePageAndFindLinks(currentUrl);
            }

            return _stringBuilder.ToString();
        }

        private async Task ScrapePageAndFindLinks(string url)
        {
            Console.WriteLine($"Attempting to scrape: {url}");

            _scrapedCount++;
            _progress?.Report(new ProgressReport { PercentComplete = (int)((double)_scrapedCount / _totalLinksDiscovered * 100), StatusMessage = $"Scraping: {url}" });

            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var document = new HtmlAgilityPack.HtmlDocument();
                document.LoadHtml(html);

                // Refined Content Extraction
                HtmlNode? contentNode = document.DocumentNode.SelectSingleNode("//main") ??
                                       document.DocumentNode.SelectSingleNode("//article");

                if (contentNode == null)
                {
                    // Fallback to common ID-based divs
                    contentNode = document.DocumentNode.SelectSingleNode("//div[@id='main-content']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@id='content']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@id='page-content']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@id='wrapper']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@id='container']");
                }

                if (contentNode == null)
                {
                    // Fallback to common Class-based divs
                    contentNode = document.DocumentNode.SelectSingleNode("//div[@class='main-content']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@class='content']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@class='page-content']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@class='wrapper']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@class='container']") ??
                                  document.DocumentNode.SelectSingleNode("//div[@class='doc-content']");
                }

                if (contentNode == null)
                {
                    // Aggressive Fallback: Try to find the largest div in the body, excluding known non-content elements
                    var body = document.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        var candidateDivs = body.SelectNodes(".//div[not(contains(@class, 'nav')) and not(contains(@class, 'header')) and not(contains(@class, 'footer')) and not(contains(@class, 'sidebar')) and not(contains(@id, 'nav')) and not(contains(@id, 'header')) and not(contains(@id, 'footer')) and not(contains(@id, 'sidebar'))]");
                        if (candidateDivs != null && candidateDivs.Any())
                        {
                            contentNode = candidateDivs.OrderByDescending(n => n.InnerText.Length).FirstOrDefault();
                            Console.WriteLine($"Using aggressive fallback for {url}. Selected div with text length: {contentNode?.InnerText.Length ?? 0}");
                        }
                    }
                }

                if (contentNode != null)
                {
                    var markdown = _converter.Convert(contentNode.InnerText);
                    _stringBuilder.AppendLine($"# {document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "No Title"}");
                    _stringBuilder.AppendLine(markdown);
                    _stringBuilder.AppendLine("\n---\n");
                    Console.WriteLine($"Content extracted from {url}. Markdown length: {markdown.Length}");
                }
                else
                {
                    Console.WriteLine($"No suitable content node found for {url}");
                }

                // Find and follow links
                var links = document.DocumentNode.SelectNodes("//a[@href]");
                Console.WriteLine($"Found {links?.Count ?? 0} links on {url}");
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        var href = link.GetAttributeValue("href", string.Empty);
                        Console.WriteLine($"  Processing link href: {href}");
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            Uri absoluteUri;
                            try
                            {
                                absoluteUri = new Uri(new Uri(url), href);
                            }
                            catch (UriFormatException ex)
                            {
                                Console.WriteLine($"    Invalid URI format for href '{href}': {ex.Message}");
                                continue;
                            }

                            var absoluteUrl = absoluteUri.ToString();
                            Console.WriteLine($"    Constructed absolute URL: {absoluteUrl}");

                            // Normalize URL for comparison and visited links check
                            var normalizedAbsoluteUrl = NormalizeUrl(absoluteUrl);

                            // Ignore anchor links that point to the same page (after normalization)
                            if (absoluteUri.Fragment.Length > 0 && normalizedAbsoluteUrl == NormalizeUrl(url))
                            {
                                Console.WriteLine($"    Ignoring anchor link: {absoluteUrl}");
                                continue;
                            }

                            // Compare hosts to ensure it's within the same domain
                            if (absoluteUri.Host != _baseHost)
                            {
                                Console.WriteLine($"    Not following (outside base domain): {absoluteUrl}");
                                continue;
                            }

                            if (!_visitedLinks.Contains(normalizedAbsoluteUrl))
                            {
                                _visitedLinks.Add(normalizedAbsoluteUrl);
                                _linksToScrape.Enqueue(absoluteUrl); // Enqueue the original URL for scraping
                                _totalLinksDiscovered++;
                                Console.WriteLine($"Following link: {absoluteUrl}");
                            }
                            else
                            {
                                Console.WriteLine($"    Not following (already visited): {absoluteUrl}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  Skipping empty or whitespace href");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scraping {url}: {ex.Message}");
            }
            _progress?.Report(new ProgressReport { PercentComplete = (int)((double)_scrapedCount / _totalLinksDiscovered * 100), StatusMessage = $"Scraped {_scrapedCount} of {_totalLinksDiscovered} pages." });
        }

        private string NormalizeUrl(string url)
        {
            var uri = new Uri(url);
            var normalizedUrl = uri.GetLeftPart(UriPartial.Path);
            if (!normalizedUrl.EndsWith("/") && !string.IsNullOrEmpty(uri.AbsolutePath))
            {
                normalizedUrl += "/";
            }
            return normalizedUrl;
        }
    }
}