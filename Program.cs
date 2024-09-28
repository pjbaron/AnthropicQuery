using Newtonsoft.Json.Linq;


public class AnthropicApiClient(string apiKey)
{
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly string _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));

    private async Task<string> CallAnthropicApi(JObject requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(requestBody.ToString(), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"API request failed with status code {response.StatusCode}. Response content: {responseContent}");
        }

        return responseContent;
    }

    public async Task<string> RetryAnthropicApi(JObject requestBody, int maxAttempts = 10, int initialDelay = 1, int maxDelay = 60)
    {
        int attempt = 0;
        int delay = initialDelay;
        Random random = new Random();

        while (true)
        {
            attempt++;
            try
            {
                return await CallAnthropicApi(requestBody);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Attempt {attempt} failed: {e.Message}");

                if (attempt >= maxAttempts)
                {
                    throw new Exception($"Max attempts ({maxAttempts}) reached. Giving up.");
                }

                delay = Math.Min(maxDelay, delay * 2);
                double jitter = random.NextDouble() * 0.1 * delay;
                double sleepTime = delay + jitter;

                Console.WriteLine($"Retrying in {sleepTime:F2} seconds...");
                await Task.Delay(TimeSpan.FromSeconds(sleepTime));
            }
        }
    }

    public static string? GetTextContent(JObject message)
    {
        var content = message["content"] as JArray;
        if (content == null) return null;

        foreach (var item in content)
        {
            if (item is JObject contentItem &&
                contentItem["type"]?.ToString() == "text" &&
                contentItem["text"] != null)
            {
                return contentItem["text"]?.ToString();
            }
        }
        return null;
    }

    public async Task<string> PerformQuery(string prompt)
    {
        var apiParams = new JObject
        {
            ["model"] = "claude-3-5-sonnet-20240620",
            ["max_tokens"] = 8192,
            ["temperature"] = 0.1,
            ["system"] = "You are a helpful assistant who is excellent at planning tasks and executing the plan in the correct order. If you don't know how to do something, answer \"I don't know how to do that.\"",
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"\nAnswer this question completely, but as concisely as possible. Do not include additional text except for the answer to the question (e.g. 'Certainly ...').\n{prompt}"
                        }
                    }
                }
            }
        };

        var message = await RetryAnthropicApi(apiParams);
        var jsonMessage = JObject.Parse(message);
        return GetTextContent(jsonMessage) ?? throw new InvalidOperationException("Failed to get text content from API response.");
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("The ANTHROPIC_API_KEY environment variable is not set.");
        }

        var client = new AnthropicApiClient(apiKey);

        try
        {
            string prompt = "What is the capital of Peru?";
            Console.WriteLine($"Asking: {prompt}");
            string response = await client.PerformQuery(prompt);
            await File.WriteAllTextAsync("answer.txt", response);
            Console.WriteLine($"Answer: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
