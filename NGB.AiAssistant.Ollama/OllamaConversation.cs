using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Chat;
using NativeRole = OllamaSharp.Models.Chat.ChatRole;

namespace NGB.AiAssistant.Ollama;

/// <summary>Preserves native thinking and named tool results across model turns.</summary>
internal static class OllamaConversation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Configure(ChatOptions options, IEnumerable<ChatMessage> messages)
    {
        var transcript = Convert(messages).ToArray();
        var instructions = options.Instructions;
        var factory = options.RawRepresentationFactory;
        options.Instructions = null;
        options.RawRepresentationFactory = client =>
        {
            var request = factory?.Invoke(client) as ChatRequest ?? new ChatRequest();
            var native = new List<Message>(request.Messages ?? []);
            if (!string.IsNullOrWhiteSpace(instructions))
                native.Add(new Message(NativeRole.System, instructions));

            native.AddRange(transcript);
            request.Messages = native;
            return request;
        };
    }

    private static IEnumerable<Message> Convert(IEnumerable<ChatMessage> messages)
    {
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var calls = message.Contents.OfType<FunctionCallContent>().ToArray();
            foreach (var call in calls)
            {
                toolNames[call.CallId] = call.Name;
            }

            var thinking = string.Concat(message.Contents.OfType<TextReasoningContent>().Select(content => content.Text));
            var images = message.Contents.OfType<DataContent>()
                .Where(content => content.HasTopLevelMediaType("image"))
                .Select(content => content.Base64Data.ToString())
                .ToArray();

            if (message.Text.Length > 0 || thinking.Length > 0 || calls.Length > 0 || images.Length > 0)
            {
                yield return new Message(new NativeRole(message.Role.Value), message.Text)
                {
                    Thinking = thinking.Length > 0 ? thinking : null,
                    Images = images.Length > 0 ? images : null,
                    ToolCalls = calls.Length > 0 ? calls.Select(call => new Message.ToolCall
                    {
                        Id = call.CallId,
                        Function = new Message.Function { Name = call.Name, Arguments = call.Arguments }
                    }).ToArray() : null
                };
            }

            foreach (var result in message.Contents.OfType<FunctionResultContent>())
            {
                if (!toolNames.TryGetValue(result.CallId, out var name))
                    throw new InvalidOperationException("A tool result must follow its named function call.");

                yield return new Message(NativeRole.Tool, JsonSerializer.Serialize(result.Result, Json))
                {
                    ToolName = name
                };
            }
        }
    }
}
