namespace Seneschal.Mcp;

/// <summary>What the assistant reads to make an extension. The texts are the person's own when they changed them (Options, Assistant), and the defaults otherwise.</summary>
internal static class AuthoringGuide
{
    public static string Text(InstructionStore instructions, bool inRepo, string root) => instructions.Get("guide")
        .Replace("{{build}}", inRepo
            ? "In this checkout the build loads the extension live into the running app, with its services, and no restart is needed."
            : "An assistant without a terminal uses seneschal_extension_build, which asks the user first because it runs the code, packs the extension and imports it, with its services, and no restart is needed.")
        .Replace("{{root}}", root);
}
