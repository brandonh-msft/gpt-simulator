using System.Text;

using LangChain.Chains.HelperChains;
using LangChain.Providers.Azure;

using static LangChain.Chains.Chain;

string endpoint = args[0], deployment = args[1];

using CancellationTokenSource cts = new();
var ct = cts.Token;

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

StringBuilder dialogSoFar = new();

var provider = new AzureOpenAiProvider(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!, endpoint);
var model = new AzureOpenAiChatModel(provider, deployment);

static string MakeInitialBotPrompt(string userQuestion) => $@"You are the representative for Global Bank, in a chat with a current customer of the bank. Your job it is to answer questions from the user about their accounts and transactions, as well as provide them financial advice and explain the bank's current product offerings (i.e. credit cards, loans, etc.). You should make up numbers to answer questions about balances, fees, etc. ensuring they are plausible for the question asked by the user.

Make up your own name, and answer this first question a user has asked you: ""{userQuestion}""

Answer their question in a friendly manner but be short and to the point, as this is simply a chat message interface. Keep the conversation going until the user tells you all their concerns have been resolved.";

const string InitialUserPrompt = @"You are a user who is chatting to a representative at Global Bank, where you are a customer. You may request information on your accounts, ask for financial advice, and inquire about credit card offers. Your questions should be short and to the point, as this is simply a chat message interface. Make up your own name and create an initial question to the representative that makes sense for them to answer.";

static string MakeFollowOnBotPrompt(string userQuestion, string dialogSoFar) => $@"You are the representative for Global Bank, in a chat with a current customer of the bank. Your job it is to answer questions from the user about their accounts and transactions, as well as provide them financial advice and explain the bank's current product offerings (i.e. credit cards, loans, etc.). You should make up numbers to answer questions about balances, fees, etc. ensuring they are plausible for the question asked by the user. The conversation with this user so far has gone like this (you are 'Bank Representative' and the user is 'User'):

---
{dialogSoFar}
---

Now the user asks: ""{userQuestion}""

Answer their question in a friendly manner but be short and to the point, as this is simply a chat message interface. Keep the conversation going until the user tells you all their concerns have been resolved. Don't include any persona prefixes in your responses.";

static string MakeFollowOnUserPrompt(string dialogSoFar) => $@"You are a user who is chatting to a representative at Global Bank, where you are a customer. You may request information on your accounts, ask for financial advice, and inquire about credit card offers. Your questions should be short and to the point, as this is simply a chat message interface. The dialog between you and the bank representative so far has gone like this (you are 'User' and the representative is 'Bank Representative'):
---
{dialogSoFar}
---

What would you like to ask next? Don't include any persona prefixes in your question.";

try
{
    var promptToUser = Set(InitialUserPrompt) | LLM(model);

    var userQuestion = await AppendHistoryAsync("User", promptToUser);
    bool firstQuestion = true;

    do
    {
        var botResponse = Set(firstQuestion ? MakeInitialBotPrompt(userQuestion) : MakeFollowOnBotPrompt(userQuestion, dialogSoFar.ToString())) | LLM(model);
        await AppendHistoryAsync("Bank Representative", botResponse);

        promptToUser = Set(MakeFollowOnUserPrompt(dialogSoFar.ToString())) | LLM(model);
        userQuestion = await AppendHistoryAsync("User", promptToUser);

        firstQuestion = false;
    } while (!cts.IsCancellationRequested);
}
catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
{
    Console.WriteLine("Exiting...");
}

async Task<string> AppendHistoryAsync(string speaker, StackChain stream)
{
    var fullResponse = await stream.Run();

    string line = $"{speaker}: {fullResponse.Value["text"]}";
    Console.WriteLine(line);
    dialogSoFar.AppendLine(line);

    Console.WriteLine();

    return fullResponse.Value["text"].ToString()!;
}
