using System.Text;

using Azure.Identity;

using Microsoft.SemanticKernel;

string endpoint = args[0], deployment = args[1];

using CancellationTokenSource cts = new();
var ct = cts.Token;

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

StringBuilder dialogSoFar = new();

var creds = new DefaultAzureCredential(includeInteractiveCredentials: true);

var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(deployment, endpoint, creds)
    .Build();
kernel.ImportPluginFromPromptDirectory(Path.Combine(Environment.CurrentDirectory, "sk", "user"));
kernel.ImportPluginFromPromptDirectory(Path.Combine(Environment.CurrentDirectory, "sk", "bot"));
kernel.ImportPluginFromPromptDirectory(Path.Combine(Environment.CurrentDirectory, "sk", "proctor"));

Console.WriteLine("Generating first question (you may be prompted for login)...");
Console.WriteLine(string.Empty);
try
{
    var streamingResponse = kernel.InvokeStreamingAsync(kernel.Plugins["user"]["initial"], cancellationToken: ct);

    var userQuestion = await AppendHistoryAsync("User", streamingResponse);
    bool firstQuestion = true;

    do
    {
        streamingResponse = kernel.InvokeStreamingAsync("bot", firstQuestion ? "first" : "followup", new() { ["userQuestion"] = userQuestion, ["dialogSoFar"] = dialogSoFar }, cancellationToken: ct);

        var botResponse = await AppendHistoryAsync("Bank Representative", streamingResponse);

        await GradeResponseAsync(kernel, userQuestion, botResponse);

        streamingResponse = kernel.InvokeStreamingAsync("user", "followup", new() { ["dialogSoFar"] = dialogSoFar }, cancellationToken: ct);
        userQuestion = await AppendHistoryAsync("User", streamingResponse);

        firstQuestion = false;
    } while (!cts.IsCancellationRequested);
}
catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
{
    Console.WriteLine("Exiting...");
}

async Task GradeResponseAsync(Kernel kernel, string userQuestion, string botResponseToGrade)
{
    var grade = await kernel.InvokeAsync("proctor", "grade", new() { ["userQuestion"] = userQuestion, ["botResponse"] = botResponseToGrade }, cancellationToken: ct);
    Console.WriteLine("Grade: " + grade.GetValue<string>());
    Console.WriteLine();
}

async Task<string> AppendHistoryAsync(string speaker, IAsyncEnumerable<StreamingKernelContent> stream)
{
    StringBuilder fullResponse = new();
    Console.Write($"{speaker}: ");
    await foreach (var s in stream)
    {
        if (s is null)
        {
            continue;
        }

        var token = s.ToString();
        Console.Write(token);
        fullResponse.Append(token);
    }

    string line = $"{speaker}: {fullResponse}";
    dialogSoFar.AppendLine(line);

    Console.WriteLine();
    Console.WriteLine();

    return fullResponse.ToString();
}
