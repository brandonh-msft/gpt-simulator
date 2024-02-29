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

var userKernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(deployment, endpoint, creds)
    .Build();

var botKernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(deployment, endpoint, creds)
    .Build();

Console.WriteLine("Generating first question (you may be prompted for login)...");
Console.WriteLine(string.Empty);
try
{
    var promptToUser = userKernel.InvokePromptStreamingAsync("You are a user who is chatting to a representative at Global Bank, where you are a customer. You may request information on your accounts, ask for financial advice, and inquire about credit card offers. Your questions should be short and to the point, as this is simply a chat message interface. Make up your own name and create an initial question to the representative that makes sense for them to answer.", cancellationToken: ct);

    var userQuestion = await AppendHistoryAsync("User", promptToUser);
    bool firstQuestion = true;

    // Your code here
    do
    {
        var botResponse = botKernel.InvokePromptStreamingAsync(firstQuestion ? $@"You are the representative for Global Bank, in a chat with a current customer of the bank. Your job it is to answer questions from the user about their accounts and transactions, as well as provide them financial advice and explain the bank's current product offerings (i.e. credit cards, loans, etc.). You should make up numbers to answer questions about balances, fees, etc. ensuring they are plausible for the question asked by the user.

Make up your own name, and answer this first question a user has asked you: ""{userQuestion}""

Answer their question in a friendly manner but be short and to the point, as this is simply a chat message interface. Keep the conversation going until the user tells you all their concerns have been resolved." :
$@"You are the representative for Global Bank, in a chat with a current customer of the bank. Your job it is to answer questions from the user about their accounts and transactions, as well as provide them financial advice and explain the bank's current product offerings (i.e. credit cards, loans, etc.). You should make up numbers to answer questions about balances, fees, etc. ensuring they are plausible for the question asked by the user. The conversation with this user so far has gone like this (you are 'Bank Representative' and the user is 'User'):

---
{dialogSoFar}
---

Now the user asks: ""{userQuestion}""

Answer their question in a friendly manner but be short and to the point, as this is simply a chat message interface. Keep the conversation going until the user tells you all their concerns have been resolved. Don't include any persona prefixes in your responses.", cancellationToken: ct);

        await AppendHistoryAsync("Bank Representative", botResponse);

        promptToUser = userKernel.InvokePromptStreamingAsync($@"You are a user who is chatting to a representative at Global Bank, where you are a customer. You may request information on your accounts, ask for financial advice, and inquire about credit card offers. Your questions should be short and to the point, as this is simply a chat message interface. The dialog between you and the bank representative so far has gone like this (you are 'User' and the representative is 'Bank Representative'):
---
{dialogSoFar}
---

What would you like to ask next? Don't include any persona prefixes in your question.", cancellationToken: ct);
        userQuestion = await AppendHistoryAsync("User", promptToUser);

        firstQuestion = false;
    } while (!cts.IsCancellationRequested);
}
catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
{
    Console.WriteLine("Exiting...");
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
