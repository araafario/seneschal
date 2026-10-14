using Seneschal.Sdk;
using MudBlazor;

namespace Calculator;

// Metadata, plus what the AI assistant can do with the tool (IToolMcp).
public sealed class CalculatorTool : IToolModule, IToolMcp
{
    public ToolMetadata Metadata { get; } = new(
        Id: "calculator",
        Title: "Calculator",
        Description: "Evaluate math expressions",
        Group: "Math",
        Icon: Icons.Material.Filled.Calculate,
        ComponentType: typeof(CalculatorView),
        SearchTerms: ["calculator", "math", "calculate", "expression"])
    {
        // Keeps the typed expression and the history while the user visits other pages
        Persistent = true,
        Prompts = ["Calculate 15% of 2480", "What is sqrt(144) + 2^10"],
    };

    public IEnumerable<McpTool> McpTools(IServiceProvider services)
    {
        yield return new McpTool("calculator_evaluate", "Evaluate a math expression and return the result",
            McpEffect.Read,
            McpSchema.Object().Text("expression", "For example (3 + 4) * 2, 2^10 or sqrt(16). Supports + - * / % ^, parentheses, pi, e, sqrt, abs, sin, cos, tan, ln, log, round, floor, ceil", required: true),
            args =>
            {
                var expression = args.Text("expression");
                if (!CalculatorEngine.TryEvaluate(expression, out var result, out var error))
                    throw new McpException(error);
                return CalculatorEngine.Format(result);
            });
    }
}