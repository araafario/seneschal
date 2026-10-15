namespace Seneschal.Mcp;

/// <summary>What the assistant reads to make an extension. The texts are the person's own when they changed them (Options, Assistant), and the defaults otherwise.</summary>
internal static class AuthoringGuide
{
    public static string Text(InstructionStore instructions, string root) => instructions.Get("guide")
        .Replace("{{build}}", "seneschal_extension_build builds the project in the projects folder and loads it live into the running app, with its services, and no restart is needed. It asks the user first because it runs the code. With a terminal, dotnet build in the project folder does the same")
        .Replace("{{root}}", root);
}
