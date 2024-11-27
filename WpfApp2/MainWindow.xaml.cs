using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfApp2
{
    public partial class MainWindow : Window
    {
        private Dictionary<string, Dictionary<string, double>> exchangeRates;
        private DateTime lastUpdateTime;
        private DateTime apiLastUpdateTime;
        private const string ApiUrl = "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies.json";

        public MainWindow()
        {
            InitializeComponent();
            InitializeUI();
            LoadCurrencies();
        }
        
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void AmountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow only digits and one decimal point
            if (!char.IsDigit(e.Text, 0))
            {
                if (e.Text == "." && !AmountTextBox.Text.Contains("."))
                {
                    // Allow decimal point if it's not already present
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
            else
            {
                // Check if we're exceeding the decimal place limit
                if (AmountTextBox.Text.Contains("."))
                {
                    int decimalPos = AmountTextBox.Text.IndexOf(".");
                    if (AmountTextBox.SelectionStart > decimalPos &&
                        AmountTextBox.Text.Substring(decimalPos).Length >= 3)
                    {
                        e.Handled = true;
                    }
                }
            }
        }

        private void InitializeUI()
        {
            // Add placeholder handling
            FromCurrency.DropDownClosed += (s, e) => UpdatePlaceholder(FromCurrency, FromCurrencyPlaceholder);
            ToCurrency.DropDownClosed += (s, e) => UpdatePlaceholder(ToCurrency, ToCurrencyPlaceholder);

            // Amount TextBox handling
            AmountTextBox.TextChanged += (s, e) =>
            {
                AmountPlaceholder.Visibility = string.IsNullOrEmpty(AmountTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };

            // Handle paste events
            DataObject.AddPastingHandler(AmountTextBox, new DataObjectPastingEventHandler((s, e) =>
            {
                if (e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    string text = (string)e.DataObject.GetData(DataFormats.Text);
                    if (!decimal.TryParse(text, out _))
                    {
                        e.CancelCommand();
                    }
                }
            }));

            // Add keyboard shortcuts
            AmountTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                    ConvertButton_Click(s, e);
            };
        }

        private async Task<List<string>> FetchCurrencies(string url)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    client.Timeout = TimeSpan.FromSeconds(10); // Set timeout
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    var currencies = new List<string>();

                    using (JsonDocument doc = JsonDocument.Parse(responseBody))
                    {
                        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                        {
                            currencies.Add(property.Name.ToUpper());
                        }
                    }

                    return currencies;
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception($"Network error: {ex.Message}");
                }
                catch (JsonException ex)
                {
                    throw new Exception($"Invalid data received: {ex.Message}");
                }
                catch (TaskCanceledException)
                {
                    throw new Exception("Request timed out");
                }
            }
        }

        private async Task<Dictionary<string, Dictionary<string, double>>> FetchExchangeRates(string baseCurrency)
        {
            string url = $"https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/{baseCurrency.ToLower()}.json";

            using (var client = new HttpClient())
            {
                try
                {
                    client.Timeout = TimeSpan.FromSeconds(10); // Set timeout
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    var result = new Dictionary<string, Dictionary<string, double>>();

                    using (JsonDocument doc = JsonDocument.Parse(responseBody))
                    {
                        var rates = new Dictionary<string, double>();
                        foreach (JsonProperty property in doc.RootElement.GetProperty(baseCurrency.ToLower()).EnumerateObject())
                        {
                            rates[property.Name.ToUpper()] = property.Value.GetDouble();
                        }
                        result[baseCurrency.ToUpper()] = rates;

                        // Extract the last updated date and time from the API response
                        if (doc.RootElement.TryGetProperty("date", out JsonElement dateElement))
                        {
                            apiLastUpdateTime = DateTime.Parse(dateElement.GetString());
                        }
                    }

                    return result;
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception($"Network error: {ex.Message}");
                }
                catch (JsonException ex)
                {
                    throw new Exception($"Invalid data received: {ex.Message}");
                }
                catch (TaskCanceledException)
                {
                    throw new Exception("Request timed out");
                }
            }
        }

        private async void LoadCurrencies()
        {
            try
            {
                ShowLoading(true);
                var currencies = await FetchCurrencies(ApiUrl);

                if (currencies != null && currencies.Count > 0)
                {
                    currencies.Sort(); // Sort alphabetically
                    FromCurrency.ItemsSource = currencies;
                    ToCurrency.ItemsSource = currencies;
                }
                else
                {
                    ShowError("No currencies found.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load currencies: {ex.Message}");
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (FromCurrency.SelectedItem == null || ToCurrency.SelectedItem == null ||
                string.IsNullOrWhiteSpace(AmountTextBox.Text))
            {
                ShowError("Please select currencies and enter an amount.");
                return;
            }

            string fromCurrency = FromCurrency.SelectedItem.ToString();
            string toCurrency = ToCurrency.SelectedItem.ToString();

            if (double.TryParse(AmountTextBox.Text, out double amount))
            {
                try
                {
                    ShowLoading(true);

                    // Refresh rates if they're older than 1 hour
                    if (exchangeRates == null || !exchangeRates.ContainsKey(fromCurrency) ||
                        DateTime.Now - lastUpdateTime > TimeSpan.FromHours(1))
                    {
                        exchangeRates = await FetchExchangeRates(fromCurrency);
                        lastUpdateTime = DateTime.Now;
                    }

                    if (exchangeRates[fromCurrency].TryGetValue(toCurrency, out double toRate))
                    {
                        double convertedAmount = amount * toRate;
                        ResultTextBlock.Text = $"{amount:N2} {fromCurrency} = {convertedAmount:N2} {toCurrency}";
                        LastUpdatedTextBlock.Text = $"Last updated: {apiLastUpdateTime:g}";
                        
                        this.Height = convertedAmount.ToString("N2").Length > 14 ? 748 : (convertedAmount.ToString("N2").Length > 6 ? 688 : 678);
                    }
                    else
                    {
                        ShowError("Conversion rate not available.");
                    }
                }
                catch (Exception ex)
                {
                    ShowError($"Error during conversion: {ex.Message}");
                }
                finally
                {
                    ShowLoading(false);
                }
            }
            else
            {
                ShowError("Please enter a valid numeric amount.");
            }
        }

        private void ShowLoading(bool show)
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            IsEnabled = !show;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void UpdatePlaceholder(ComboBox comboBox, TextBlock placeholder)
        {
            placeholder.Visibility = comboBox.SelectedItem == null ?
                Visibility.Visible : Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
    }
}